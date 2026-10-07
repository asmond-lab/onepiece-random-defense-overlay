using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ManualSelectionReplayTests
{
    private readonly DataCatalog _catalog = new();
    public ManualSelectionReplayTests() => _catalog.Load(loadCarryPolicy: false);

    [Fact]
    public void ManualOutputFirstReceiptSurvivesCraftOfferAndMatchesOnlyOnce()
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var frame = Frame(1, 5, 1);
        Observe(gate, frame);
        Observe(gate, frame = Frame(2, 5, 6), craft: true);
        Assert.Equal(0, Observe(gate, Frame(3, 0, 6)).PendingSelectionOutputs);
        Observe(gate, Frame(4, 1, 6));
        Assert.Equal(1, Observe(gate, Frame(5, 0, 6)).PendingSelectionOutputs);
    }

    [Theory]
    [InlineData("lost")]
    [InlineData("grant")]
    [InlineData("match")]
    [InlineData("offered")]
    public void ManualCreditsCannotEscapeTheirObservedReceipt(string boundary)
    {
        var gate = new FirstLegendRewardGate(_catalog);
        Observe(gate, Frame(1, 5, 1));
        Observe(gate, Frame(2, 5, 6));
        var next = Frame(3, boundary == "grant" ? 6 : 5, boundary == "lost" ? 1 : 6);
        if (boundary == "match") next = next with { MatchGeneration = 1 };
        Observe(gate, next, offered: boundary == "offered");
        Assert.Equal(5, Observe(gate, next with { RecognitionRevision = 4,
            RewardWisps = next.RewardWisps.SetItem("e018", next.RewardWisps["e018"] - 5) }).PendingSelectionOutputs);
    }

    [Fact]
    public void UnknownManualSpendStillWaitsAndNonurgentHeldWispsStayConserved()
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var frame = Frame(1, 5, 1);
        Assert.Null(BulletGuideSelectionPolicy.Decide(frame, _catalog));
        Observe(gate, frame);
        var spent = Frame(2, 0, 1);
        var plan = Observe(gate, spent);
        Assert.Equal(5, plan.PendingSelectionOutputs);
        Assert.True(plan.AwaitingRewardHand);
        Assert.Equal("guide1:selection-pending", BulletGuideSelectionPolicy.Decide(
            spent with { GuidePlan = plan }, _catalog)!.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualReceiptRejectsUnacceptedOrWrongPoolGains(bool wrongPool)
    {
        var gate = new FirstLegendRewardGate(_catalog);
        Observe(gate, Frame(1, 5, 1));
        var gain = Frame(2, 5, wrongPool ? 1 : 6) with { IsCurrent = wrongPool };
        if (wrongPool) gain = gain with { Inventory = gain.Inventory.Add("rawcode:N00h", 5) };
        Observe(gate, gain);
        Assert.Equal(5, Observe(gate, Frame(3, 0, 1)).PendingSelectionOutputs);
    }

    // Actual journal seq64-98, match-20260909-165528-437c4fb3444a4c1bb4c229edf3fcf209.
    // Raw wisps/revisions/GuidePlan were not serialized. Reconstruct the observed five-spend
    // at seq69 as 5->0 (minimum held count); use sequence as recognition order and BossSupport.
    [Fact]
    public void RecordedManualFiveBuggyReceiptDoesNotBlockRedForcePipeline()
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var session = new BeginnerCoachSession(_catalog);
        foreach (var line in Recorded.Split((char)10, StringSplitOptions.RemoveEmptyEntries))
        {
            using var document = System.Text.Json.JsonDocument.Parse(line);
            var row = document.RootElement;
            var sequence = row.GetProperty("Sequence").GetInt32();
            var frame = Frame(sequence, sequence < 69 ? 5 : 0, 0) with
            {
                Round = row.GetProperty("Round").GetInt32(),
                Inventory = row.GetProperty("Inventory").EnumerateObject()
                    .ToImmutableDictionary(p => p.Name, p => p.Value.GetInt32())
            };
            Assert.Equal(0, Observe(gate, frame, craft: sequence is >= 65 and < 69).PendingSelectionOutputs);
            Assert.NotEqual("guide1:selection-pending", session.Update(frame).Id);
            if (sequence == 98) Assert.Equal(1, frame.Inventory["rawcode:U30h"]);
        }
    }

    private const string Recorded = """
        {"Sequence":64,"Round":38,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":2,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":1,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":3,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":65,"Round":38,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":2,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":6,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":3,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":66,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":2,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":6,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":3,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":67,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":2,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":6,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":3,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":68,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":6,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":69,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":6,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":70,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":6,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":71,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":6,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":72,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":6,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":73,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":6,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":74,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":6,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":75,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":6,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":76,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":6,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":77,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":6,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":78,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":6,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":79,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":3,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:510h":1,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":80,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":3,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:510h":1,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":81,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":3,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:510h":1,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":82,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":3,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:510h":1,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":83,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":3,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:510h":1,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":84,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":3,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:510h":1,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":85,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":3,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:510h":1,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":86,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":3,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:510h":1,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":87,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":3,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:510h":1,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":88,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":3,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:510h":1,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":89,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:U00h":1,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"rawcode:Z00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:920h":1,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":3,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:510h":1,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":90,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:920h":1,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":3,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":91,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:920h":1,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":3,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":92,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:530h":1,"rawcode:S10h":1,"rawcode:060h":2,"rawcode:920h":1,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":3,"rawcode:X00h":2,"rawcode:I20h":1,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":93,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:530h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":3,"rawcode:X00h":2,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":94,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:530h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:500h":3,"rawcode:X00h":2,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:510h":1,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":95,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:530h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:X00h":2,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:510h":1,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":96,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:U30h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:530h":1,"rawcode:060h":2,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:X00h":2,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:510h":1,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":97,"Round":39,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:U30h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:060h":1,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:X00h":2,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        {"Sequence":98,"Round":40,"CompletedStoryStage":13,"Inventory":{"rawcode:MC0h":1,"rawcode:O00h":3,"rawcode:100h":4,"rawcode:I10h":2,"rawcode:N10h":1,"rawcode:930h":1,"rawcode:610h":1,"rawcode:L00h":1,"rawcode:U30h":1,"rawcode:200h":11,"rawcode:600h":3,"rawcode:N00h":1,"rawcode:I00h":1,"rawcode:E00h":1,"luffy_common":6,"rawcode:J00h":2,"rawcode:900h":6,"rawcode:060h":1,"rawcode:K00h":1,"rawcode:M00h":5,"rawcode:Y50h":1,"rawcode:F00h":1,"rawcode:X50h":1,"rawcode:D00h":1,"rawcode:HA0h":1,"rawcode:X00h":2,"rawcode:800h":2,"rawcode:400h":4,"rawcode:U20h":1,"rawcode:G00h":5,"rawcode:010h":1,"rawcode:P10h":1}}
        """;

    private static CoachFrame Frame(long revision, int wisps, int buggy) => new()
    {
        Mode = PlayMode.Guide, GuideNumber = 1, MatchGeneration = 0, Revision = revision, RecognitionRevision = revision,
        Round = 39, CompletedStoryStage = 13, IsCurrent = true, GuideVisible = true, Difficulty = "악몽",
        Inventory = ImmutableDictionary<string, int>.Empty.Add("rawcode:500h", buggy),
        RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e018", wisps),
        GuidePlan = new(BulletGuideStage.BossSupport, "rawcode:U30h", false)
    };

    private static BulletGuidePlan Observe(FirstLegendRewardGate gate, CoachFrame frame,
        bool craft = false, bool offered = false)
    {
        var plan = gate.Prepare(frame.GuidePlan!, frame);
        gate.Record(frame with { GuidePlan = plan }, new CoachDecision(
            offered ? CoachActionKind.Reward : craft ? CoachActionKind.Craft : CoachActionKind.Waiting,
            "replay", "", "", "", "", "")
        {
            RewardWispId = offered ? "e018" : null,
            SelectionBatch = offered ? new("rawcode:V10h", [new("luffy_common", 5)]) : null
        });
        return plan;
    }
}
