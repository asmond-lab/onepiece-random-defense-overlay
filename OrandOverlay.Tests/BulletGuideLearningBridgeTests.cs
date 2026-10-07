using System.Text.Json.Nodes;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletGuideLearningBridgeTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private readonly DataCatalog _catalog = new();
    public BulletGuideLearningBridgeTests() => _catalog.Load(loadCarryPolicy: false);

    [Theory]
    [InlineData(null, null)]
    [InlineData("Normal", 1)]
    [InlineData("Guide", 2)]
    [InlineData("Guide", null)]
    public void GenericLegacyAndOtherGuidesCannotInfluenceBullet(string? mode, int? guide)
    {
        var json = Payload();
        if (mode is null) json.Remove("mode"); else json["mode"] = mode;
        if (guide is null) json.Remove("guideNumber"); else json["guideNumber"] = guide;
        Assert.False(BulletGuideLearningBridge.TryParse(json.ToJsonString(), Hash, "악몽", out var learning));
        Assert.Null(learning);
    }

    [Fact]
    public void IsolatedGuideEvidenceActuallyChangesGuideOpeningTieBreak()
    {
        Assert.True(BulletGuideLearningBridge.TryParse(Payload().ToJsonString(), Hash, "악몽", out var learning));
        var policy = new BulletGuidePolicy(_catalog);
        var inventory = new Dictionary<string, int>();
        Assert.Equal("rawcode:530h", policy.Plan(10, 4, inventory, "악몽").TargetUnitId);
        Assert.Equal("rawcode:U20h", policy.Plan(10, 4, inventory, "악몽", learning: learning).TargetUnitId);
        Assert.True(learning!.MatchesCohort(Hash, "악몽"));
        Assert.False(learning.MatchesCohort(new string('b', 64), "악몽"));
        Assert.Equal(0, learning.WeightFor("rawcode:U20h", "신"));
    }

    [Fact]
    public void LearnedSupportPreferenceCannotDisplaceMissingDeadlineComponent()
    {
        Assert.True(BulletGuideLearningBridge.TryParse(Payload().ToJsonString(), Hash, "악몽", out var learning));
        var inventory = new[] { "U20h", "930h", "HA0h", "U30h", "MC0h" }
            .ToDictionary(code => "rawcode:" + code, _ => 1);
        var plan = new BulletGuidePolicy(_catalog).Plan(40, 13, inventory, "악몽", learning: learning);
        Assert.Equal(BulletGuideStage.BulletMaterials, plan.Stage);
        Assert.Equal("rawcode:V20h", plan.TargetUnitId);
    }

    [Fact]
    public void IsolatedGuideEvidenceAlsoReachesSupportCandidateSelection()
    {
        Assert.True(BulletGuideLearningBridge.TryParse(Payload().ToJsonString(), Hash, "악몽", out var learning));
        var inventory = new[] { "180h", "U30h", "540h" }.ToDictionary(code => "rawcode:" + code, _ => 1);
        var policy = new BulletGuidePolicy(_catalog);
        Assert.Equal("rawcode:M30h", policy.Plan(55, 13, inventory, "악몽").TargetUnitId);
        var plan = policy.Plan(55, 13, inventory, "악몽", learning: learning);
        Assert.Equal(BulletGuideStage.ControlSupport, plan.Stage);
        Assert.Equal("rawcode:H30h", plan.TargetUnitId);
    }

    [Fact]
    public void InsufficientLabelsAreRejectedRatherThanUsedForGuide()
    {
        var json = Payload();
        json["totalRecords"] = 29;
        json["labeledRecords"] = 29;
        json["goals"]!["rawcode:180h"]!["plays"] = 29;
        json["goals"]!["rawcode:180h"]!["labeled"] = 29;
        json["difficulties"]!["악몽"] = 29;
        Assert.False(BulletGuideLearningBridge.TryParse(json.ToJsonString(), Hash, "악몽", out _));
    }

    private static JsonObject Payload() => JsonNode.Parse("""
        {"schemaVersion":1,"generatedAt":"2026-09-09T00:00:00Z","totalRecords":30,"labeledRecords":30,
         "mode":"Guide","guideNumber":1,
         "goals":{"rawcode:180h":{"plays":30,"labeled":30,"clears":15,"adherenceMean":null,"failHeavyUnits":[]}},
         "weights":{},"goalWeights":{"rawcode:180h":{"rawcode:U20h":0.1,"rawcode:H30h":0.1}},
         "difficulties":{"악몽":30}}
        """)!.AsObject();
}
