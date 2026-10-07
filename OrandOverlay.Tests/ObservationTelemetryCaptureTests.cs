using Xunit;

namespace OrandOverlay.Tests;

public sealed class ObservationTelemetryCaptureTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly Lazy<DataCatalog> Bundled = new(() => { var c = new DataCatalog(); c.Load(mapVersion: "2.320"); return c; });
    private static DiagnosticBasicInventoryObservation Reference(DateTimeOffset now, int count = 1, char context = 'a') =>
        DiagnosticBasicInventoryObservation.Create(Bundled.Value, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
            new string(context, 64), 1, 0, now.AddMilliseconds(-100), now, TimeSpan.FromMilliseconds(100),
            [new() { UnitId = "rawcode:I10h", Count = count }], new string('b',64), new string(context,64));

    [Fact]
    public void RepeatedHandIsDeduplicatedButChangesAndHeartbeatAreRetained()
    {
        var events = new List<ObservedTelemetryEvent>();
        var capture = new ObservationTelemetryCapture(e => { events.Add(e); return true; }, () => { });
        capture.Observe(Reference(Now), 1, null, Now);
        for (var i = 1; i < 100; i++) capture.Observe(Reference(Now), 1, null, Now.AddMilliseconds(i));
        Assert.Single(events.Where(e => e.Kind == "inventory"));
        capture.Observe(Reference(Now, 2), 1, null, Now.AddSeconds(1));
        capture.Observe(Reference(Now, 2), 1, null, Now.AddSeconds(32));
        Assert.Equal(3, events.Count(e => e.Kind == "inventory"));
        Assert.All(events.Where(e => e.Kind == "inventory"), e => Assert.Null(e.Round));
    }

    [Fact]
    public void FullReadRejectionIsSeparateFromPresentationExpiryAndRecoveryDuration()
    {
        var events = new List<ObservedTelemetryEvent>();
        var capture = new ObservationTelemetryCapture(e => { events.Add(e); return true; }, () => { });
        capture.Observe(Reference(Now), 1, null, Now);
        capture.Read(false, "full", "freshness", TimeSpan.FromSeconds(3.2), 2, Now.AddSeconds(1));
        Assert.DoesNotContain(events, e => e.Lane == "presentation" && e.State == "expired");
        capture.PresentationState("expired", "freshness", Now.AddSeconds(3));
        capture.PresentationState("expired", "freshness", Now.AddSeconds(4));
        capture.Observe(Reference(Now.AddSeconds(7)), 1, null, Now.AddSeconds(7));
        Assert.Single(events.Where(e => e.State == "expired"));
        Assert.Equal(4000, events.Last(e => e.Lane == "presentation").GapMs);
        Assert.Equal("fresh", events.Last(e => e.Lane == "presentation").State);
    }

    [Fact]
    public void ContextChangeCreatesAnonymousFragmentAndDoesNotSerializeBinding()
    {
        var resets = 0;
        var events = new List<ObservedTelemetryEvent>();
        var capture = new ObservationTelemetryCapture(e => { events.Add(e); return true; }, () => resets++);
        capture.Observe(Reference(Now), 1, null, Now);
        capture.Observe(Reference(Now, context: 'c'), 1, null, Now);
        Assert.Equal(1, resets);
        Assert.Equal(2, events.Count(e => e.Kind == "inventory"));
        Assert.DoesNotContain(new string('c',64), System.Text.Json.JsonSerializer.Serialize(events));
    }

    [Fact]
    public void FailedEnqueueDoesNotAdvanceDeduplication()
    {
        var permit = false; var events = new List<ObservedTelemetryEvent>();
        var capture = new ObservationTelemetryCapture(e => { if (permit) events.Add(e); return permit; }, () => { });
        capture.Observe(Reference(Now), 1, null, Now);
        permit = true;
        capture.Observe(Reference(Now), 1, null, Now);
        Assert.Single(events.Where(e => e.Kind == "inventory"));
    }

    [Fact]
    public async Task ProductionFactoryEnables2320OnlyAfterRenewedConsent()
    {
        var root = Path.Combine(Path.GetTempPath(), "randypick-consent4-" + Guid.NewGuid().ToString("N"));
        var context = OverlayExecutionContext.Production(root);
        try
        {
            Assert.Throws<InvalidOperationException>(() => context.CreateObservedTelemetry(Bundled.Value));
            Assert.True(context.EnsureConsent(() => true));
            var client = context.CreateObservedTelemetry(Bundled.Value);
            Assert.NotNull(client);
            await client!.DisposeAsync();
            Assert.Null(context.CreateGameplayTelemetry(new("test", "2.314", new string('a',64), "legacy"), Bundled.Value));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
