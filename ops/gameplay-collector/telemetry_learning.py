"""Complete-session associations, never inferred executions or causal effects."""
from collections import defaultdict
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Iterable

from gameplay_wire import Event, Json, Packet, parse_packet, require


@dataclass(frozen=True, slots=True)
class Session:
    map_hash: str
    difficulty: str
    map_version: str
    goals: tuple[str, ...]
    clear: bool
    owned: frozenset[str]
    adherence: float | None
    history: tuple[Event, ...]


def reconstruct(packets: Iterable[Packet]) -> list[Session]:
    """Deduplicate both immutable packet IDs and (match, sequence) identities."""
    packet_ids: dict[str, str] = {}
    matches: dict[str, list[Packet]] = defaultdict(list)
    for packet in packets:
        prior = packet_ids.get(packet.packet_id)
        require(prior is None or prior == packet.canonical, "conflicting packet identity")
        if prior is None:
            matches[packet.match_id].append(packet)
            packet_ids[packet.packet_id] = packet.canonical
    sessions = []
    for chunks in matches.values():
        first = chunks[0]
        events: dict[int, Event] = {}
        indices: dict[int, tuple[Event, ...]] = {}
        for chunk in chunks:
            require((chunk.map_hash, chunk.difficulty, chunk.map_version) ==
                    (first.map_hash, first.difficulty, first.map_version), "match cohort changed")
            require(chunk.chunk not in indices or indices[chunk.chunk] == chunk.events,
                    "conflicting chunk")
            indices[chunk.chunk] = chunk.events
            for event in chunk.events:
                require(event.sequence not in events or events[event.sequence] == event,
                        "conflicting event identity")
                events[event.sequence] = event
        ordered = sorted(events.values(), key=lambda e: e.sequence)
        if sorted(indices) != list(range(len(indices))):
            continue
        if [e.sequence for e in ordered] != list(range(1, len(ordered) + 1)):
            continue
        if ordered[0].kind != "observation" or ordered[0].round != 1 or first.difficulty == "unknown":
            continue
        if any(a.elapsed > b.elapsed or a.revision > b.revision
               for a, b in zip(ordered, ordered[1:])):
            continue
        terminal = ordered[-1]
        if sum(e.kind == "outcome" for e in ordered) != 1 or terminal.kind != "outcome":
            continue
        observed_wipe = (terminal.evidence == "observation-only"
                         and terminal.outcome == "fail" and terminal.source == "unitWipe"
                         and any(e.kind == "observation" and e.evidence == "native-observed"
                                 and any(e.inventory.values()) for e in ordered[:-1]))
        if terminal.evidence not in ("native-observed", "user-confirmed") and not observed_wipe:
            continue
        if (terminal.outcome, terminal.source) not in (("clear", "mapSettlement"),
                                                       ("fail", "unitWipe"),
                                                       ("fail", "mapSettlement")):
            continue
        # Schema-1 goal keys cannot represent multi-goal combinations without mixing.
        # Until a combination-aware schema exists, learn only unambiguous single goals.
        if len(terminal.goals) != 1 or terminal.goals == ("unknown",):
            continue
        recommendation_goals = {e.goals for e in ordered if e.kind == "recommendation"}
        if any(g != terminal.goals for g in recommendation_goals):
            continue
        roster = terminal.inventory
        if not any(roster.values()):
            roster = next((e.inventory for e in reversed(ordered[:-1])
                           if e.kind == "observation" and e.evidence != "unknown"
                           and any(e.inventory.values())), {})
        owned = frozenset(u for u, count in roster.items() if count > 0)
        # This is inventory-matched adherence correlation, NOT a craft receipt.
        targets = {e.target for e in ordered if e.kind == "recommendation" and e.evidence == "recommendation" and e.target}
        adherence = len(targets & owned) / len(targets) if targets else None
        sessions.append(Session(first.map_hash, first.difficulty, first.map_version,
                                terminal.goals, terminal.outcome == "clear", owned, adherence, tuple(ordered)))
    return sessions


def wilson(hits: int, trials: int) -> tuple[float, float]:
    ratio = hits / trials
    denom = 1 + 1.96 ** 2 / trials
    center = (ratio + 1.96 ** 2 / (2 * trials)) / denom
    margin = 1.96 / denom * (ratio * (1 - ratio) / trials + 1.96 ** 2 / (4 * trials ** 2)) ** .5
    return center - margin, center + margin


def weights_for(records: list[Session]) -> dict[str, float]:
    clears = [r for r in records if r.clear]
    fails = [r for r in records if not r.clear]
    if len(records) < 30 or not clears or not fails:
        return {}
    weights = {}
    for unit in sorted(set().union(*(r.owned for r in records)) - set(records[0].goals)):
        cl, ch = wilson(sum(unit in r.owned for r in clears), len(clears))
        fl, fh = wilson(sum(unit in r.owned for r in fails), len(fails))
        gap = cl - fh if cl > fh else ch - fl if fl > ch else 0
        if gap:
            weights[unit] = max(-.1, min(.1, round(gap, 3)))
    return weights


def aggregate_packets(raw_packets: Iterable[Json]) -> dict[tuple[str, str], dict[str, Json]]:
    records = reconstruct(parse_packet(p) for p in raw_packets)
    cohorts: dict[tuple[str, str], list[Session]] = defaultdict(list)
    for record in records:
        cohorts[(record.map_hash, record.difficulty)].append(record)
    result = {}
    for cohort, sessions in cohorts.items():
        # A map hash must resolve to one map version, never blend ambiguous versions.
        if len({s.map_version for s in sessions}) != 1:
            continue
        goals = {}
        goal_weights = {}
        for goal in sorted({s.goals[0] for s in sessions}):
            group = [s for s in sessions if s.goals == (goal,)]
            weights = weights_for(group)
            adherence = [s.adherence for s in group if s.adherence is not None]
            goals[goal] = dict(plays=len(group), labeled=len(group), clears=sum(s.clear for s in group),
                               adherenceMean=round(sum(adherence) / len(adherence), 3) if adherence else None,
                               failHeavyUnits=sorted(u for u, w in weights.items() if w < 0))
            if weights:
                goal_weights[goal] = weights
        result[cohort] = dict(schemaVersion=1, generatedAt=datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
                              totalRecords=len(sessions), labeledRecords=len(sessions), goals=goals,
                              weights={}, goalWeights=goal_weights, difficulties={cohort[1]: len(sessions)})
    return result
