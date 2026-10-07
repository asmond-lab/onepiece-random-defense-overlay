using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class LiveStatsTests
{
    [Fact]
    public void WeightFor_PrefersGoalSpecificFailureEvidence()
    {
        var path = Path.Combine(Path.GetTempPath(), $"orand-live-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
                {
                  "schemaVersion": 1,
                  "generatedAt": "2026-09-09T00:00:00Z",
                  "totalRecords": 80,
                  "labeledRecords": 80,
                  "difficulties": { "신": 80 },
                  "goals": {
                    "goal-a": { "plays": 40, "labeled": 40, "clears": 20, "adherenceMean": null, "failHeavyUnits": [] },
                    "goal-b": { "plays": 40, "labeled": 40, "clears": 20, "adherenceMean": null, "failHeavyUnits": [] }
                  },
                  "weights": { "rawcode:200h": -0.02 },
                  "goalWeights": {
                    "goal-a": { "rawcode:200h": -0.08 },
                    "goal-b": { "rawcode:200h": 0.06 }
                  }
                }
                """);

            var stats = LiveStats.Load(path);

            Assert.Equal(-0.08, stats.WeightFor("goal-a", "rawcode:200h"), 3);
            Assert.Equal(0.06, stats.WeightFor("goal-b", "rawcode:200h"), 3);
            Assert.Equal(0, stats.WeightFor("goal-c", "rawcode:200h"), 3);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
