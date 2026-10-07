using System.Reflection;
using PlannerEvidenceCapture;
using Xunit;

namespace PlannerEvidenceCapture.Tests;

public sealed class CapturePreflightTests
{
    [Fact]
    public void UnknownButLexicallySafeFixtureIdIsRejectedByBundledAllowlist()
    {
        var method = typeof(CaptureInputContract).GetMethod("ValidateFixtureId");
        Assert.NotNull(method);
        IReadOnlySet<string> allowed = new HashSet<string>(["luffy_common", "rawcode:300h"]);
        method.Invoke(null, ["luffy_common", allowed]);
        Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(() => method.Invoke(null, ["not_a_bundled_unit", allowed])).InnerException);
    }

    [Fact]
    public void ActualBundledMetadataPassesLexicalPreflightWithoutWpf()
    {
        CaptureInputContract.ValidateBundledInputs(Path.Combine(AppContext.BaseDirectory, "Data"));
        CaptureInputContract.InitializeBundledAllowlist();
        var original = new OrandOverlay.AppSettings { TelemetryEnabled = true };
        var factory = typeof(Program).GetMethod("FixtureContext", BindingFlags.Static | BindingFlags.NonPublic)!;
        var context = factory.Invoke(null, [original])!;
        Assert.False((bool)context.GetType().GetProperty("RuntimeEnabled")!.GetValue(context)!);
        var load = context.GetType().GetMethod("LoadSettings")!;
        var cloned = (OrandOverlay.AppSettings)load.Invoke(context, null)!;
        cloned.BeginnerCoachEnabled = false;
        Assert.True(original.BeginnerCoachEnabled);
        context.GetType().GetMethod("SaveSettings")!.Invoke(context, [cloned]);
        Assert.False(((OrandOverlay.AppSettings)load.Invoke(context, null)!).BeginnerCoachEnabled);
        Assert.Throws<ArgumentException>(() => CaptureInputContract.ValidateSettings(new OrandOverlay.AppSettings { GoalUnitId = "not_bundled" }));
        Assert.Throws<ArgumentNullException>(() => CaptureInputContract.ValidateSettings(null!));
        Assert.Throws<ArgumentNullException>(() => CaptureInputContract.ValidateRecognition(null!));
    }

    [Theory]
    [InlineData("--bullet-guide-live")]
    [InlineData("--live-quests")]
    [InlineData("--native-unit-probe")]
    public void LiveAndUnguardedJournalModesAreRejectedBeforeApp(string flag)
    {
        var type = typeof(CaptureOutputScope).Assembly.GetType("PlannerEvidenceCapture.CapturePreflight");
        Assert.NotNull(type);
        var method = type.GetMethod("ValidateArguments")!;
        Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [new[] { "--output", "fake", flag }])).InnerException);
    }

    [Theory]
    [InlineData("--coach-app")]
    [InlineData("--coach-finished-rewards")]
    [InlineData("--four-modes")]
    public void JournalModesAreAcceptedAfterFixtureSeam(string flag)
    {
        var type = typeof(CaptureOutputScope).Assembly.GetType("PlannerEvidenceCapture.CapturePreflight");
        Assert.NotNull(type);
        var method = type.GetMethod("ValidateArguments")!;
        method.Invoke(null, [new[] { "--output", "fake", flag }]);
    }

    [Fact]
    public void FixtureJournalContext_SuppliesMemoryJournalWithoutPath()
    {
        CaptureInputContract.ValidateBundledInputs(Path.Combine(AppContext.BaseDirectory, "Data"));
        CaptureInputContract.InitializeBundledAllowlist();
        var factory = typeof(Program).GetMethod("FixtureJournalContext", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(factory);
        var context = factory!.Invoke(null, [new OrandOverlay.AppSettings()])!;
        Assert.False((bool)context.GetType().GetProperty("RuntimeEnabled")!.GetValue(context)!);
        var journal = context.GetType().GetMethod("CreateCoachJournal")!.Invoke(context, null);
        Assert.NotNull(journal);
        Assert.False((bool)journal!.GetType().GetProperty("IsPersistent")!.GetValue(journal)!);
        Assert.Null(journal.GetType().GetProperty("LatestPath")!.GetValue(journal));
    }

    [Theory]
    [InlineData("../sentinel", "")]
    [InlineData("rawcode:../x", "")]
    [InlineData("\\\\fake-server\\share\\x", "")]
    [InlineData("luffy_common", "file:///C:/fake/sentinel.png")]
    [InlineData("luffy_common", "C:\\fake\\sentinel.png")]
    [InlineData("luffy_common", "\\\\fake-server\\share\\sentinel.png")]
    public void UnsafeImageInputsAreRejectedWithoutImageFactoryOrIo(string id, string fallback)
    {
        var type = typeof(CaptureOutputScope).Assembly.GetType("PlannerEvidenceCapture.CaptureInputContract");
        Assert.NotNull(type);
        var method = type.GetMethod("ValidateImageInput")!;
        Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [id, fallback])).InnerException);
    }
}
