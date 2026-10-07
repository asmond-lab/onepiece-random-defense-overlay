"""Strict frozen-wire boundary. Only stdlib; identifiers never contain prose."""
from dataclasses import dataclass
import json
import re
from typing import Final, TypeAlias

Json: TypeAlias = None | bool | int | float | str | list["Json"] | dict[str, "Json"]
DIFFICULTIES: Final = frozenset(("쉬움", "보통", "어려움", "악몽", "지옥", "신",
                                "easy", "normal", "hard", "nightmare", "hell", "god", "god-plus", "unknown"))
WISPS: Final = {"e016", "e017", "e018", "e019", "e0IX", "e01A"}
UNIT: Final = re.compile(r"[A-Za-z0-9_:-]{1,80}\Z")
VERSION: Final = re.compile(r"[A-Za-z0-9_.-]{1,80}\Z")
COMMON: Final = frozenset(("sequence", "recognitionRevision", "elapsedMs", "round", "completedStory", "mode", "guideNumber", "kind", "evidence"))
FIELDS: Final = {
    "observation": ({"inventory", "rewardWisps", "resources"}, {"gambleFailures", "gambleCounters"}),
    "recommendation": ({"action", "goalUnitIds"}, {"targetUnitId", "selection"}),
    "craft": ({"unitId", "count", "consumed"}, set()),
    "selection": ({"wispId", "count", "outputs"}, set()),
    "gamble": ({"gambleType", "count", "cost", "outputs"}, set()),
    "outcome": ({"outcome", "outcomeSource", "inventory", "goalUnitIds"}, set()),
}


class WireError(ValueError):
    """A rejected document; processing must not advance its cursor."""


def require(condition: bool, field: str) -> None:
    if not condition:
        raise WireError(field)


def json_object(value: Json) -> dict[str, Json]:
    if not isinstance(value, dict):
        raise WireError("object required")
    return value


def json_array(value: Json) -> list[Json]:
    if not isinstance(value, list):
        raise WireError("array required")
    return value


def text(value: Json) -> str:
    if not isinstance(value, str):
        raise WireError("string required")
    return value


def integer(value: Json, minimum: int = 0, maximum: int = 2147483647) -> int:
    if not isinstance(value, int) or isinstance(value, bool) or not minimum <= value <= maximum:
        raise WireError("integer")
    return value


def identifier(value: Json) -> str:
    result = text(value)
    require(UNIT.fullmatch(result) is not None, "unit identifier")
    return result


def counts(value: Json, allowed: set[str] | None = None) -> dict[str, int]:
    entries = json_object(value)
    require(len(entries) <= 512, "collection size")
    result = {}
    for key, count in entries.items():
        require(allowed is None or key in allowed, "collection key")
        result[identifier(key)] = integer(count, maximum=9223372036854775807 if allowed == {"gold", "lumber", "trait-points"} else 2147483647)
    return result


def unique_object(pairs: list[tuple[str, Json]]) -> dict[str, Json]:
    result = {}
    for key, value in pairs:
        require(key not in result, "duplicate JSON key")
        result[key] = value
    return result


def load_json(value: str | bytes) -> Json:
    return json.loads(value, object_pairs_hook=unique_object)


@dataclass(frozen=True, slots=True)
class GambleCounter:
    attempts: int
    successes: int
    failures: int


def gamble_counters(value: Json) -> dict[str, GambleCounter]:
    result = {}
    for kind, raw in json_object(value).items():
        require(kind in ("low", "middle", "high", "world", "absalom", "lumberWisp"), "counter kind")
        fields = json_object(raw)
        require(set(fields) == {"attempts", "successes", "failures"}, "counter fields")
        counter = GambleCounter(integer(fields["attempts"]), integer(fields["successes"]), integer(fields["failures"]))
        require(counter.attempts == counter.successes + counter.failures, "counter coherence")
        result[kind] = counter
    return result


@dataclass(frozen=True, slots=True)
class Event:
    sequence: int
    revision: int
    elapsed: int
    round: int
    kind: str
    evidence: str
    inventory: dict[str, int]
    gamble_counters: dict[str, GambleCounter]
    goals: tuple[str, ...]
    target: str | None
    outcome: str | None
    source: str | None
    canonical: str


@dataclass(frozen=True, slots=True)
class Packet:
    packet_id: str
    match_id: str
    chunk: int
    map_hash: str
    difficulty: str
    map_version: str
    events: tuple[Event, ...]
    canonical: str


def parse_event(value: Json) -> Event:
    raw = json_object(value)
    kind = text(raw.get("kind"))
    require(kind in FIELDS, "event kind")
    required, optional = FIELDS[kind]
    require(COMMON | required <= raw.keys() <= COMMON | required | optional, "event fields")
    evidence = text(raw["evidence"])
    require(evidence in ("native-observed", "inventory-matched", "observation-only", "user-confirmed", "recommendation", "unknown"), "evidence")
    require(raw["mode"] in ("Normal", "Manual", "Beginner", "Guide"), "mode")
    round_number = integer(raw["round"], 1, 65)
    integer(raw["completedStory"], 0, 14)
    integer(raw["guideNumber"], 0, 99)
    for field in ("inventory", "consumed", "outputs", "selection", "rewardWisps"):
        if field in raw:
            counts(raw[field], WISPS if field == "rewardWisps" else None)
    for field in ("resources", "cost"):
        if field in raw:
            counts(raw[field], {"gold", "lumber", "trait-points"})
    for field in ("count", "gambleFailures"):
        if field in raw:
            integer(raw[field])
    for field in ("unitId", "targetUnitId", "wispId"):
        if field in raw:
            identifier(raw[field])
    if "wispId" in raw:
        require(text(raw["wispId"]) in WISPS, "wisp")
    goals = json_array(raw.get("goalUnitIds", []))
    require(len(goals) <= 8, "goals")
    parsed_goals = tuple(identifier(g) for g in goals)
    require(len(set(parsed_goals)) == len(parsed_goals), "duplicate goals")
    if "action" in raw:
        require(raw["action"] in ("Waiting", "Recognition", "Craft", "Gather", "Story", "Reward", "Navigation", "Economy", "Upgrade", "Maintain", "Finished", "Item"), "action")
    if "gambleType" in raw:
        require(raw["gambleType"] in ("low", "medium", "high"), "gamble type")
    if "outcome" in raw:
        require(raw["outcome"] in ("clear", "fail", "interrupted"), "outcome")
        require(raw["outcomeSource"] in ("mapSettlement", "clearRound", "unitWipe", "appExit", "unknown"), "outcome source")
    return Event(integer(raw["sequence"], 1, 9223372036854775807),
                 integer(raw["recognitionRevision"], maximum=9223372036854775807),
                 integer(raw["elapsedMs"], maximum=9223372036854775807), round_number, kind, evidence,
                 counts(raw.get("inventory", {})), gamble_counters(raw.get("gambleCounters", {})), parsed_goals,
                 text(raw["targetUnitId"]) if "targetUnitId" in raw else None,
                 text(raw["outcome"]) if "outcome" in raw else None,
                 text(raw["outcomeSource"]) if "outcomeSource" in raw else None,
                 json.dumps(raw, sort_keys=True, allow_nan=False))


def parse_packet(value: Json) -> Packet:
    raw = json_object(value)
    require(set(raw) == {"schemaVersion", "consentVersion", "packetId", "matchId", "chunkIndex", "appVersion", "mapVersion", "mapScriptSha256", "profileVersion", "difficulty", "events"}, "packet fields")
    require(integer(raw["schemaVersion"]) == 3 and integer(raw["consentVersion"]) == 3, "version")
    for field, length in (("packetId", 32), ("matchId", 32), ("mapScriptSha256", 64)):
        require(re.fullmatch(f"[0-9a-f]{{{length}}}", text(raw[field])) is not None, field)
    for field in ("appVersion", "mapVersion", "profileVersion"):
        require(VERSION.fullmatch(text(raw[field])) is not None, field)
    difficulty = text(raw["difficulty"])
    require(difficulty in DIFFICULTIES, "difficulty")
    raw_events = json_array(raw["events"])
    require(1 <= len(raw_events) <= 64, "events")
    events = tuple(parse_event(e) for e in raw_events)
    require(all(a.sequence < b.sequence for a, b in zip(events, events[1:])), "event order")
    canonical = json.dumps(raw, sort_keys=True, allow_nan=False)
    require(len(canonical.encode()) <= 512 * 1024, "body size")
    return Packet(text(raw["packetId"]), text(raw["matchId"]), integer(raw["chunkIndex"]), text(raw["mapScriptSha256"]),
                  difficulty, text(raw["mapVersion"]), events, canonical)
