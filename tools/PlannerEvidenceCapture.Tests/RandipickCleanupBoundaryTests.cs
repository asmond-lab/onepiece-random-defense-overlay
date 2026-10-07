using System.Reflection;
using Xunit;
namespace PlannerEvidenceCapture.Tests;
public sealed class RandipickCleanupBoundaryTests
{
    [Fact]
    public void CandidateHasSeparateFixtureRunnerRatherThanForwardingToOrdinaryMain()
    {
        var program = typeof(CaptureInputContract).Assembly.GetType("PlannerEvidenceCapture.Program")!;
        var method = program.GetMethod("RunRandipickUiCleanup", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var il = method!.GetMethodBody()!.GetILAsByteArray()!;
        var calls = new List<MethodBase>();
        for (var i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] is not (0x28 or 0x6f)) continue;
            try { var target = method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1)); if (target is not null) calls.Add(target); }
            catch (ArgumentException) { }
        }
        Assert.Contains(calls, m => m.DeclaringType?.Name == "RandipickCleanupArguments" && m.Name == "Validate");
        Assert.DoesNotContain(calls, m => m.DeclaringType == program && m.Name == "Main");
        Assert.DoesNotContain(calls, m => m.DeclaringType?.Name == "WarcraftMemoryRecognitionService");
    }

    [Fact]
    public void DedicatedEntryRejectsLiveModesBeforeAnyWpfInitialization()
    {
        var type = typeof(CaptureInputContract).Assembly.GetType("PlannerEvidenceCapture.RandipickCleanupArguments");
        Assert.NotNull(type);
        var validate = type!.GetMethod("Validate", BindingFlags.Public | BindingFlags.Static)!;
        string[] good = ["--randipick-ui-cleanup-fixture", "--output", Path.Combine(Path.GetTempPath(), "randipick-ui-cleanup-review-new"), "--candidate-sha", new string('a', 64)];
        validate.Invoke(null, [good]);
        foreach (var args in new[] { Array.Empty<string>(), good.Concat(new[] { "--native-unit-probe" }).ToArray(),
            new[] { "--bullet-guide-live", good[1], good[2], good[3], good[4] },
            new[] { good[0], good[1], "C:/Users/123/Desktop/output", good[3], good[4] },
            new[] { good[0], good[1], good[2], good[3], "not-a-sha" } })
            Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(() => validate.Invoke(null, [args])).InnerException);
    }
}
