using System.Reflection;
using Xunit;
namespace PlannerEvidenceCapture.Tests;

public sealed class FastUniqueUiWiringTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void RecordsCompiledEntryPointWithoutInvokingIt()
    {
        var assembly = typeof(FastUniqueUiFixture).Assembly;
        var entry = assembly.EntryPoint!;
        var cleanup = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Any(attribute =>
            attribute.Key == "CaptureEntryContract" && attribute.Value == "PlannerEvidenceCapture.RandipickUiCleanupEntry");
        var expected = cleanup ? "PlannerEvidenceCapture.RandipickUiCleanupEntry" :
            AppContext.BaseDirectory.Contains("focused-artifacts", StringComparison.Ordinal)
                ? "PlannerEvidenceCapture.FastUniqueUiEntry" : "PlannerEvidenceCapture.Program";
        Assert.Equal(expected, entry.DeclaringType!.FullName);
        output.WriteLine($"ASSEMBLY={assembly.Location}; ENTRY={entry.DeclaringType.FullName}.{entry.Name}; NOT_INVOKED");
    }

    [Fact]
    public void CompiledDedicatedCaptureUsesExistingIsolationAndAcceptedObservationSeams()
    {
        // Reuse the existing IL decoder read-only. This invokes no WPF body or runtime service.
        var decoder = typeof(BulletGuideProjectionWiringTests).GetMethod("Calls", BindingFlags.Static | BindingFlags.NonPublic)!;
        var program = typeof(FastUniqueUiFixture).Assembly.GetType("PlannerEvidenceCapture.Program")!;
        string[] Calls(string name)
        {
            var method = program.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
            return ((IEnumerable<MethodBase?>)decoder.Invoke(null, [method])!).Select(m => m?.Name ?? "").ToArray();
        }
        var entry = Calls("RunFastUniqueUi");
        Assert.Contains("Validate", entry);
        Assert.Contains("Create", entry);
        Assert.Contains("ValidateBundledInputs", entry);
        Assert.Contains("FixtureContext", entry);
        Assert.Contains("CaptureFastUniqueUi", entry);
        var capture = Calls("CaptureFastUniqueUi");
        Assert.Contains("Request", capture);
        Assert.Contains("WaitCoach", capture);
        Assert.Contains("Observe", capture);
        Assert.Contains("SaveCoachWindow", capture);
        Assert.DoesNotContain("CaptureBulletGuide", entry);
        Assert.DoesNotContain("ResetMatchSession", capture);
        Assert.DoesNotContain("Render", capture);
        Assert.DoesNotContain("RecognizeAsync", capture);
        var methods = program.Assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Static |
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        var lambdas = methods.Where(m => m.Name.Contains("CaptureFastUniqueUi") && m.Name.Contains("b__")).ToArray();
        var lambdaCalls = lambdas.SelectMany(m => (IEnumerable<MethodBase?>)decoder.Invoke(null, [m])!).ToArray();
        Assert.Contains(lambdaCalls, m => m?.Name == "ScanFixtureAsync");
        Assert.Contains(lambdaCalls, m => m?.Name == "MatchesObservation");
    }
}
