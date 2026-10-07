"""Local synthetic v3 feedback contracts; no network or user data."""
import copy
import importlib
import unittest

HASH = "a" * 64


def packet(index, clear=True, owned=True, goal="goal", map_hash=HASH):
    common = dict(recognitionRevision=1, elapsedMs=0, round=1, completedStory=0,
                  mode="Normal", guideNumber=0)
    inventory = {"support": 1} if owned else {"other": 1}
    return dict(schemaVersion=3, consentVersion=3, packetId=f"{index:032x}",
                matchId=f"{index:032x}", chunkIndex=0, appVersion="1.0",
                mapVersion="1.0", profileVersion="1.0", mapScriptSha256=map_hash,
                difficulty="신", events=[
                    dict(common, sequence=1, kind="observation", evidence="native-observed",
                         inventory=inventory, rewardWisps={}, resources={}),
                    dict(common, sequence=2, kind="recommendation", evidence="recommendation",
                         action="Craft", targetUnitId="support", goalUnitIds=[goal]),
                    dict(common, sequence=3, kind="outcome", evidence="native-observed",
                         outcome="clear" if clear else "fail",
                         outcomeSource="mapSettlement" if clear else "unitWipe",
                         inventory=inventory, goalUnitIds=[goal])])


def population():
    return [packet(i + 1, i < 20, i < 18 or i in (20, 21)) for i in range(40)]


class LearningTests(unittest.TestCase):
    def setUp(self):
        self.learning = importlib.import_module("telemetry_learning")

    def test_complete_deduplicated_cohort_learns_capped_association(self):
        packets = population()
        result = self.learning.aggregate_packets(packets + packets)
        stats = result[(HASH, "신")]
        self.assertEqual(40, stats["totalRecords"])
        self.assertEqual(.1, stats["goalWeights"]["goal"]["support"])
        self.assertEqual({}, stats["weights"])
        self.assertEqual(.5, stats["goals"]["goal"]["adherenceMean"])

    def test_all_observed_wisp_types_survive_collection(self):
        from gameplay_wire import parse_packet
        value = packet(1)
        value["events"][0]["rewardWisps"] = dict.fromkeys(
            ("e016", "e017", "e018", "e019", "e0IX", "e01A"), 1)
        parsed = parse_packet(value)
        self.assertEqual(3, len(parsed.events))

    def test_production_observed_unit_wipe_failures_contribute_without_relabeling(self):
        packets = population()
        for value in packets:
            if value["events"][-1]["outcome"] == "fail":
                value["events"][-1]["evidence"] = "observation-only"
        stats = self.learning.aggregate_packets(packets)[(HASH, "신")]
        self.assertEqual(40, stats["labeledRecords"])
        self.assertEqual(.1, stats["goalWeights"]["goal"]["support"])
        for value in packets:
            value["events"][-1]["evidence"] = "observation-only"
            value["events"][-1]["outcome"] = "clear"
            value["events"][-1]["outcomeSource"] = "clearRound"
        self.assertEqual({}, self.learning.aggregate_packets(packets))

    def test_uncertain_fragments_and_recommendations_are_not_labels_or_ownership(self):
        packets = population()
        for p in packets:
            p["events"][-1]["outcomeSource"] = "clearRound"
        self.assertEqual({}, self.learning.aggregate_packets(packets))

    def test_missing_prefix_is_not_complete(self):
        packets = population()
        for p in packets:
            p["chunkIndex"] = 1
            p["events"] = p["events"][1:]
        self.assertEqual({}, self.learning.aggregate_packets(packets))

    def test_conflicting_event_identity_rejected(self):
        original = packet(1)
        conflict = copy.deepcopy(original)
        conflict["packetId"] = "f" * 32
        conflict["events"][0]["inventory"] = {"other": 1}
        with self.assertRaises(ValueError):
            self.learning.aggregate_packets([original, conflict])

    def test_goal_and_map_cohorts_never_pool_to_reach_gate(self):
        packets = population()
        for i, p in enumerate(packets):
            p["mapScriptSha256"] = HASH if i % 2 else "b" * 64
        self.assertTrue(all(not s["goalWeights"] for s in
                            self.learning.aggregate_packets(packets).values()))

    def test_difficulty_aliases_remain_distinct_cohorts(self):
        packets = population()
        for i, p in enumerate(packets):
            if i % 2:
                p["difficulty"] = "god"
        cohorts = self.learning.aggregate_packets(packets)
        self.assertEqual({(HASH, "신"), (HASH, "god")}, set(cohorts))
        self.assertTrue(all(not stats["goalWeights"] for stats in cohorts.values()))

    def test_minimum_both_groups_and_wilson_gates(self):
        small = population()[:29]
        one_group = population()
        for p in one_group:
            p["events"][-1]["outcome"] = "clear"
            p["events"][-1]["outcomeSource"] = "mapSettlement"
        weak = [packet(i + 1, i < 20, i < 9 or 20 <= i < 31) for i in range(40)]
        for packets in (small, one_group, weak):
            with self.subTest(size=len(packets)):
                stats = self.learning.aggregate_packets(packets)[(HASH, "신")]
                self.assertEqual({}, stats["goalWeights"])

    def test_opposite_goal_associations_remain_separate(self):
        inverse = [packet(i + 100, i < 20, 2 <= i < 20 or i >= 22, goal="other-goal")
                   for i in range(40)]
        # Support is common on failed runs and rare on clear runs in this goal.
        for i, p in enumerate(inverse):
            inventory = {"support": 1} if i < 2 or i >= 22 else {"other": 1}
            p["events"][0]["inventory"] = inventory
            p["events"][-1]["inventory"] = inventory
        stats = self.learning.aggregate_packets(population() + inverse)[(HASH, "신")]
        self.assertEqual(.1, stats["goalWeights"]["goal"]["support"])
        self.assertEqual(-.1, stats["goalWeights"]["other-goal"]["support"])
        self.assertEqual({}, stats["weights"])

    def test_recommendation_does_not_create_owned_unit_or_execution(self):
        packets = population()
        for p in packets:
            p["events"][1]["targetUnitId"] = "never-owned"
        stats = self.learning.aggregate_packets(packets)[(HASH, "신")]
        self.assertEqual(0, stats["goals"]["goal"]["adherenceMean"])
        self.assertNotIn("never-owned", stats["goalWeights"]["goal"])

    def test_shuffled_chunks_with_overlap_deduplicate_event_identity(self):
        from gameplay_wire import parse_packet
        original = packet(1)
        tail = copy.deepcopy(original)
        tail["packetId"] = "f" * 32
        tail["chunkIndex"] = 1
        tail["events"] = tail["events"][1:]
        original["events"] = original["events"][:2]
        sessions = self.learning.reconstruct([parse_packet(tail), parse_packet(original)])
        self.assertEqual([1, 2, 3], [e.sequence for e in sessions[0].history])

    def test_interrupted_unknown_and_midmatch_fragments_do_not_learn(self):
        for mutation in ("interrupted", "unknown", "midmatch", "multi-goal"):
            packets = population()
            for p in packets:
                if mutation == "interrupted":
                    p["events"][-1].update(outcome="interrupted", outcomeSource="appExit")
                elif mutation == "unknown":
                    p["events"][-1]["evidence"] = "unknown"
                elif mutation == "midmatch":
                    p["events"][0]["round"] = 10
                else:
                    p["events"][-1]["goalUnitIds"] = ["goal", "other-goal"]
            with self.subTest(mutation=mutation):
                self.assertEqual({}, self.learning.aggregate_packets(packets))

    def test_prose_extra_fields_rejected(self):
        p = packet(1)
        p["events"][1]["title"] = "private prose"
        with self.assertRaises(ValueError):
            self.learning.aggregate_packets([p])

    def test_native_gamble_totals_preserve_observation_history_without_receipts(self):
        from gameplay_wire import parse_packet
        p = packet(1)
        p["events"][0]["gambleCounters"] = {"middle": {"attempts": 3, "successes": 1, "failures": 2}}
        parsed = parse_packet(p)
        self.assertEqual(3, parsed.events[0].gamble_counters["middle"].attempts)
        self.assertEqual(1, parsed.events[0].round)
        self.assertEqual("observation", parsed.events[0].kind)
        history = self.learning.reconstruct([parsed])[0].history
        self.assertEqual(3, history[0].gamble_counters["middle"].attempts)
        self.assertFalse(any(event.kind == "gamble" for event in history))

    def test_invalid_native_gamble_totals_rejected(self):
        from gameplay_wire import parse_packet
        for counters in ({"middle": {"attempts": 2, "successes": 1, "failures": 2}},
                         {"medium": {"attempts": 3, "successes": 1, "failures": 2}},
                         {"high": {"attempts": True, "successes": 1, "failures": 0}},
                         {"low": {"attempts": 0, "successes": 0, "failures": 0, "cost": 1}}):
            with self.subTest(counters=counters):
                p = packet(1)
                p["events"][0]["gambleCounters"] = counters
                with self.assertRaises(ValueError):
                    parse_packet(p)

    def test_empty_defeat_uses_last_good_observation_not_recommendation(self):
        packets = population()
        for p in packets:
            p["events"][-1]["inventory"] = {}
        stats = self.learning.aggregate_packets(packets)[(HASH, "신")]
        self.assertEqual(.1, stats["goalWeights"]["goal"]["support"])


if __name__ == "__main__":
    unittest.main()
