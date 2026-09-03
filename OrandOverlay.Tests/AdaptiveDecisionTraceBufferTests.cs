using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class AdaptiveDecisionTraceBufferTests
{
    private const string GoldenTelemetryJson = "{\"schemaVersion\":2,\"appVersion\":\"0.6.52\",\"mapVersion\":\"2.314\",\"difficulty\":\"normal\",\"damageLane\":\"physical\",\"goalTierFamily\":\"unknown\",\"surface\":\"top-navigation\",\"urgency\":\"none\",\"outcome\":\"unknown\",\"matchCount\":1,\"observedObjects\":\"0\",\"completedTops\":\"0\",\"readyScans\":\"0\",\"waitingScans\":\"0\",\"transientScans\":\"0\",\"unsupportedScans\":\"0\"}";

    [Fact]
    public void EventCanonicalizationIsCultureIndependentAndOrdinallyOrdered()
    {
        // Given: equivalent decision inputs in different source orders.
        var first = Event(candidateOrder: ["candidate-b", "candidate-a"],
            blockerOrder: [ReasonCode.NoSafeRecommendation, ReasonCode.UnknownInput]);
        var second = Event(candidateOrder: ["candidate-a", "candidate-b"],
            blockerOrder: [ReasonCode.UnknownInput, ReasonCode.NoSafeRecommendation]);
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            // When: each local event is serialized under different cultures.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ko-KR");
            var koreanBytes = first.SerializedBytes;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var englishBytes = second.SerializedBytes;

            // Then: its canonical local payload and dedup key are byte-stable.
            Assert.Equal(Convert.ToHexString(koreanBytes.AsSpan()), Convert.ToHexString(englishBytes.AsSpan()));
            Assert.Equal(first.DecisionFingerprint, second.DecisionFingerprint);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void BufferEvictsOldestEventsForCountBudget()
    {
        // Given: a match buffer sized for two exact event payloads.
        var first = Event(inputFingerprint: "input-1");
        var second = Event(inputFingerprint: "input-2");
        var third = Event(inputFingerprint: "input-3");
        var buffer = new AdaptiveDecisionTraceBuffer(maxEventCount: 2, maxBytes: 32_768);

        // When: three individually-valid events are committed for the same generation.
        Assert.Equal(AdaptiveDecisionAppendResult.Appended,
            buffer.TryAppend(first, first.InputFingerprint, isTransient: false));
        Assert.Equal(AdaptiveDecisionAppendResult.Appended,
            buffer.TryAppend(second, second.InputFingerprint, isTransient: false));
        Assert.Equal(AdaptiveDecisionAppendResult.Appended,
            buffer.TryAppend(third, third.InputFingerprint, isTransient: false));

        // Then: the count remains bounded and the oldest payload was evicted.
        Assert.Equal(2, buffer.Events.Length);
        Assert.Equal([second.DecisionFingerprint, third.DecisionFingerprint],
            buffer.Events.Select(item => item.DecisionFingerprint).ToArray());
    }

    [Fact]
    public void BufferEvictsOldestEventsForByteBudget()
    {
        var first = Event(inputFingerprint: "input-1");
        var second = Event(inputFingerprint: "input-2");
        var third = Event(inputFingerprint: "input-3");
        var maxBytes = first.SerializedBytes.Length + second.SerializedBytes.Length;
        var buffer = new AdaptiveDecisionTraceBuffer(maxEventCount: 3, maxBytes: maxBytes);

        Assert.Equal(AdaptiveDecisionAppendResult.Appended,
            buffer.TryAppend(first, first.InputFingerprint, isTransient: false));
        Assert.Equal(AdaptiveDecisionAppendResult.Appended,
            buffer.TryAppend(second, second.InputFingerprint, isTransient: false));
        Assert.Equal(AdaptiveDecisionAppendResult.Appended,
            buffer.TryAppend(third, third.InputFingerprint, isTransient: false));

        Assert.Equal([second.DecisionFingerprint, third.DecisionFingerprint],
            buffer.Events.Select(item => item.DecisionFingerprint).ToArray());
        Assert.True(buffer.TotalBytes <= maxBytes);
    }

    [Fact]
    public void BufferRejectsDuplicateTransientAndStaleEvents()
    {
        // Given: one committed local decision for a stable input.
        var buffer = new AdaptiveDecisionTraceBuffer(maxEventCount: 3, maxBytes: 32_768);
        var committed = Event();
        Assert.Equal(AdaptiveDecisionAppendResult.Appended,
            buffer.TryAppend(committed, committed.InputFingerprint, isTransient: false));

        // When: the same, transient, and stale decisions attempt to append.
        var duplicate = buffer.TryAppend(committed, committed.InputFingerprint, isTransient: false);
        var transient = buffer.TryAppend(Event(inputFingerprint: "transient"), "transient", isTransient: true);
        var stale = buffer.TryAppend(Event(inputFingerprint: "stale"), "current", isTransient: false);

        // Then: none produce a second local trace payload.
        Assert.Equal(AdaptiveDecisionAppendResult.Duplicate, duplicate);
        Assert.Equal(AdaptiveDecisionAppendResult.Transient, transient);
        Assert.Equal(AdaptiveDecisionAppendResult.Stale, stale);
        Assert.Single(buffer.Events);
    }

    [Fact]
    public void ConfirmedResetClearsMatchTraceAndStartsNextGeneration()
    {
        // Given: an event committed to the initial match generation.
        var buffer = new AdaptiveDecisionTraceBuffer(maxEventCount: 3, maxBytes: 32_768);
        var initial = Event(generation: 0);
        Assert.Equal(AdaptiveDecisionAppendResult.Appended,
            buffer.TryAppend(initial, initial.InputFingerprint, isTransient: false));

        // When: a confirmed match reset occurs.
        buffer.ConfirmedMatchReset();
        var next = Event(generation: 1, inputFingerprint: "next-match");

        // Then: the old trace is gone and only the new generation can append.
        Assert.Empty(buffer.Events);
        Assert.Equal(1, buffer.MatchGeneration);
        Assert.Equal(AdaptiveDecisionAppendResult.Stale,
            buffer.TryAppend(initial, initial.InputFingerprint, isTransient: false));
        Assert.Equal(AdaptiveDecisionAppendResult.Appended,
            buffer.TryAppend(next, next.InputFingerprint, isTransient: false));
    }

    [Fact]
    public void LocalDecisionTraceAndIdentifierFreeTelemetryRemainSeparate()
    {
        // Given: a local adaptive event and a fixed v2 aggregate.
        var localJson = Encoding.UTF8.GetString(Event().SerializedBytes.AsSpan());
        var telemetry = TelemetryFixture();

        // When: the aggregate is serialized.
        var wireBytes = JsonSerializer.SerializeToUtf8Bytes(telemetry);

        // Then: local trace fields stay local and the v2 wire bytes contain no identifiers.
        Assert.DoesNotContain("endpoint", localJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("address", localJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", localJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("actualSelection", localJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Encoding.UTF8.GetBytes(GoldenTelemetryJson), wireBytes);
        using var document = JsonDocument.Parse(wireBytes);
        Assert.False(document.RootElement.TryGetProperty("adaptiveDecision", out _));
        Assert.True(TelemetryPrivacyContract.IsSafe(telemetry));
    }

    [Fact]
    public void UploaderContractTargetsOnlyV2Aggregates()
    {
        Assert.EndsWith("/v2/aggregates", TelemetryUploader.DefaultEndpoint,
            StringComparison.Ordinal);
        Assert.True(TelemetryPrivacyContract.IsSafe(TelemetryFixture()));
    }

    private static AdaptiveDecisionEvent Event(long generation = 0,
        string inputFingerprint = "input-fingerprint", string[]? candidateOrder = null,
        ReasonCode[]? blockerOrder = null) => new(new AdaptiveDecisionEventInput(
            scoringVersion: "scoring-v3",
            profileDataGeneration: "profile-sha-1",
            mapDataGeneration: "map-sha-2",
            matchGeneration: generation,
            inputFingerprint: inputFingerprint,
            phase: PlannerPhase.CommitRound20,
            blockers: (blockerOrder ?? [ReasonCode.UnknownInput, ReasonCode.NoSafeRecommendation]).ToImmutableArray(),
            routeComponents: [new AdaptiveRouteComponent("candidate-a", DamageLane.Physical, 8300, 7900, 8100)],
            candidateIds: (candidateOrder ?? ["candidate-a", "candidate-b"]).ToImmutableArray(),
            confidenceBp: 8200,
            isFallback: false,
            navigationScenarios: [new AdaptiveNavigationScenario("candidate-a", 9100,
                new AdaptiveDecisionInterval(200, 400), new AdaptiveDecisionInterval(500, 700))],
            navigationRegime: AdaptiveNavigationRegime.SecureCore,
            overlayRecommendationLocked: true,
            manualLatches: new ManualLatches(goalOverride: false, navigationOverride: true),
            sourceDefinedForcedExpectationId: "double-benefit"));

    private static TelemetryRecord TelemetryFixture() => new()
    {
        AppVersion = "0.6.52",
        MapVersion = "2.314",
        Difficulty = "normal",
        DamageLane = "physical",
        GoalTierFamily = "unknown",
        Surface = "top-navigation"
    };
}
