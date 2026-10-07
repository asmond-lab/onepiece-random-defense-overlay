using System.Collections.Immutable;
using System.Text.Json;
using OrandOverlay;
using Xunit;

namespace PlannerEvidenceCapture.Tests;

public sealed class BulletGuideRowSerializationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EventRowsSerializeTheCurrentStandardProjection(int index)
    {
        var input = index == 2 ? QueenConversionInput.UserConfirmedMissionsComplete : QueenConversionInput.UserConfirmedStoryTooSlow;
        var frame = new CoachFrame
        {
            MatchGeneration = 17 + index, Revision = 31 + index, RecognitionRevision = 91 + index,
            Round = 60, CompletedStoryStage = 13, IsCurrent = index >= 2,
            Mode = index < 2 ? PlayMode.Manual : PlayMode.Guide,
            Inventory = ImmutableDictionary<string, int>.Empty.Add("fixture-retained", 3),
            GoalId = "fixture-goal-" + index, GuideNumber = 1, Difficulty = "악몽",
            ConfirmedNavigation = "AlliedForces.EmergencyCall",
            Recommendations = [new() { Route = new() { Id = "route-" + index, Name = "fixture route", GoalUnitId = "candidate-" + index } }],
            CraftSteps = [new("step-" + index, "fixture target", "trigger", "fixture trigger", "ABCD", "Q", [])],
            GuidePlan = index < 2 ? null : new BulletGuidePlan(BulletGuideStage.AirFoundation, "fixture-target", false) { QueenInput = input }
        };
        var decision = new CoachDecision(index < 2 ? CoachActionKind.Recognition : CoachActionKind.Maintain,
            "fixture-decision-" + index, "fixture title", "fixture controls", "fixture reason", "fixture confirmation", "fixture milestone");
        object row = index switch
        {
            0 => BulletGuideRowProjection.StopZero(decision, frame),
            1 => BulletGuideRowProjection.StopRetained(decision, frame),
            _ => BulletGuideRowProjection.Queen(index - 2, decision, frame, input)
        };
        // Match capture's heterogeneous List<object> envelope, not a narrowed base-type serializer.
        var envelope = JsonSerializer.SerializeToElement(new { Rows = new List<object> { row } });
        var json = JsonSerializer.Serialize(envelope.GetProperty("Rows")[0], new JsonSerializerOptions { WriteIndented = true });
        var standard = JsonSerializer.SerializeToElement(BulletGuideRowProjection.Observe("observe-fixture", decision, frame));
        var actual = envelope.GetProperty("Rows")[0];
        foreach (var field in standard.EnumerateObject().Where(x => x.Name != "Name"))
            Assert.True(actual.TryGetProperty(field.Name, out var value) && value.GetRawText() == field.Value.GetRawText(),
                "Event differs from Observe field " + field.Name);
        // Fixture-only artifact: not a rendered capture or native observation.
        var evidence = Environment.GetEnvironmentVariable("BULLET_SERIALIZATION_EVIDENCE");
        if (evidence is not null) File.WriteAllText(Path.Combine(evidence, $"event-{index}.json"), json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        foreach (var field in new[] { "Decision", "GoalId", "GuideNumber", "GuidePlan", "ConfirmedNavigation", "Difficulty", "Candidates", "Steps" })
            Assert.True(root.TryGetProperty(field, out _), $"{root.GetProperty("Name")} missing standard field {field}");
        Assert.Equal(JsonSerializer.Serialize(decision), JsonSerializer.Serialize(root.GetProperty("Decision")));
        Assert.Equal((int)decision.Kind, root.GetProperty("Decision").GetProperty("Kind").GetInt32());
        Assert.Equal(frame.GoalId, root.GetProperty("GoalId").GetString());
        Assert.Equal(frame.GuideNumber, root.GetProperty("GuideNumber").GetInt32());
        Assert.Equal(frame.Difficulty, root.GetProperty("Difficulty").GetString());
        Assert.Equal(frame.ConfirmedNavigation, root.GetProperty("ConfirmedNavigation").GetString());
        Assert.Equal(JsonSerializer.Serialize(frame.GuidePlan), JsonSerializer.Serialize(root.GetProperty("GuidePlan")));
        Assert.Equal(JsonValueKind.Array, root.GetProperty("Candidates").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("Steps").ValueKind);
        Assert.Equal(frame.Recommendations.Select(x => x.Route.GoalUnitId), root.GetProperty("Candidates").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(frame.CraftSteps.Select(x => x.TargetUnitId), root.GetProperty("Steps").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(frame.MatchGeneration, root.GetProperty("MatchGeneration").GetInt64());
        Assert.Equal(frame.RecognitionRevision, root.GetProperty("RecognitionRevision").GetInt64());
        if (index == 0)
        {
            Assert.False(root.GetProperty("ProductionUncheckedBodyExecuted").GetBoolean());
            Assert.False(root.GetProperty("IsCurrent").GetBoolean());
            Assert.Equal(JsonSerializer.Serialize(frame.CombatObservations), JsonSerializer.Serialize(root.GetProperty("CombatObservations")));
            Assert.Equal(JsonSerializer.Serialize(frame.NativeNavigation), JsonSerializer.Serialize(root.GetProperty("NativeNavigation")));
        }
        else if (index == 1)
            Assert.Equal(3, root.GetProperty("Inventory").GetProperty("fixture-retained").GetInt32());
        else
        {
            Assert.Equal("user-selection-not-native", root.GetProperty("Provenance").GetString());
            Assert.Equal((int)input, root.GetProperty("Input").GetInt32());
            Assert.True(root.GetProperty("StaleTokensRejected").GetBoolean());
            Assert.Equal(frame.Revision, root.GetProperty("Revision").GetInt64());
        }
    }
}
