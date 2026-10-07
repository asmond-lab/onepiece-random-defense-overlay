using Xunit;

namespace OrandOverlay.Tests;

public sealed class TelemetryConsentGateTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public void DenialHasNoProductionEffects(bool? answer)
    {
        var root = Root();
        var context = OverlayExecutionContext.Production(root);
        Assert.False(context.EnsureConsent(() => answer));
        Assert.False(context.HasCurrentConsent);
        AssertBlocked(context);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void ExplicitConsentPersistsAndLegacySwitchCannotDisableAutomaticTelemetry()
    {
        var root = Root();
        try
        {
            var context = OverlayExecutionContext.Production(root);
            Assert.True(context.EnsureConsent(() => true));
            var next = OverlayExecutionContext.Production(root);
            Assert.True(next.EnsureConsent(() => throw new Exception("Must not ask again")));
            next.SaveSettings(new AppSettings { TelemetryEnabled = false });
            Assert.True(next.HasCurrentConsent);
            Assert.True(next.LoadSettings().TelemetryEnabled);
            Assert.True(next.CreateTelemetry(false).Enabled);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("{\"TelemetryDisclosureVersion\":2,\"TelemetryEnabled\":true}")]
    [InlineData("{\"TelemetryConsentVersion\":2,\"TelemetryConsentAccepted\":true}")]
    [InlineData("{\"TelemetryConsentVersion\":3,\"TelemetryConsentAccepted\":true}")]
    [InlineData("{\"TelemetryConsentVersion\":5,\"TelemetryConsentAccepted\":true}")]
    [InlineData("{\"TelemetryConsentVersion\":3,\"TelemetryConsentAccepted\":false}")]
    [InlineData("{broken")]
    public void InvalidOrLegacyConsentIsReadOnlyUntilExplicitAgreement(string json)
    {
        var root = Root();
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, json);
        try
        {
            var context = OverlayExecutionContext.Production(root);
            var asked = 0;
            Assert.False(context.EnsureConsent(() => { asked++; return false; }));
            Assert.Equal(1, asked);
            AssertBlocked(context);
            Assert.Equal(json, File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(root));
            Assert.True(context.EnsureConsent(() => true));
            Assert.True(context.HasCurrentConsent);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RevocationIsRecheckedAtFactoryBoundary()
    {
        var root = Root();
        try
        {
            var context = OverlayExecutionContext.Production(root);
            Assert.True(context.EnsureConsent(() => true));
            File.WriteAllText(Path.Combine(root, "settings.json"), "{}");
            AssertBlocked(context);
            Assert.True(context.EnsureConsent(() => true));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FixtureRemainsInertWithSimulatedConsent()
    {
        var context = OverlayExecutionContext.Fixture(new AppSettings
        { TelemetryConsentVersion = 3, TelemetryConsentAccepted = true });
        Assert.False(context.EnsureConsent(() => throw new Exception("Fixture must not ask")));
        Assert.False(context.HasCurrentConsent);
        context.RequireConsent();
        Assert.False(context.CreateTelemetry(true).Enabled);
        Assert.Null(context.CreateGameplayTelemetry(new("1.0", "fixture", new string('a', 64), "fixture"), new DataCatalog()));
        Assert.Null(context.CreateObservedTelemetry(new DataCatalog()));
        Assert.Null(context.CreateGameplayStatsRefreshService());
        Assert.Null(context.CreateCoachJournal());
        Assert.Null(context.CreateUpdateService());
        Assert.Null(context.CreateClearRefreshService());
        await context.RefreshProfilesAsync();
        context.RunRuntime(() => throw new Exception("Fixture runtime"));
    }

    private static string Root() => Path.Combine(Path.GetTempPath(), "orand-consent-" + Guid.NewGuid().ToString("N"));
    private static void AssertBlocked(OverlayExecutionContext context)
    {
        Assert.Throws<InvalidOperationException>(() => context.RequireConsent());
        Assert.Throws<InvalidOperationException>(() => context.CreateCatalog());
        Assert.Throws<InvalidOperationException>(() => context.CreateObservedTelemetry(new DataCatalog()));
        Assert.Throws<InvalidOperationException>(() => context.LoadSettings());
        Assert.Throws<InvalidOperationException>(() => context.SaveSettings(new()));
        Assert.Throws<InvalidOperationException>(() => context.CreateCoachJournal());
        Assert.Throws<InvalidOperationException>(() => context.CreateTelemetry(true));
        Assert.Throws<InvalidOperationException>(() => context.CreateGameplayTelemetry(new("1.0", "fixture", new string('a', 64), "fixture"), new DataCatalog()));
        Assert.Throws<InvalidOperationException>(() => context.CreateGameplayStatsRefreshService());
        Assert.Throws<InvalidOperationException>(() => context.CreateRecognizer(new DataCatalog()));
        Assert.Throws<InvalidOperationException>(() => context.CreateUpdateService());
        Assert.Throws<InvalidOperationException>(() => context.CreateClearRefreshService());
        Assert.Throws<InvalidOperationException>(() => { _ = context.RefreshProfilesAsync(); });
        Assert.Throws<InvalidOperationException>(() => context.RunRuntime(() => throw new Exception("effect")));
        Assert.Throws<InvalidOperationException>(() => context.TryRuntime(() => true));
        Assert.Throws<InvalidOperationException>(() => context.LogUnknownRawcodes(new()));
    }
}
