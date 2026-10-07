using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using OrandOverlay;
using Xunit;
namespace PlannerEvidenceCapture.Tests;

public sealed class BulletSyntheticWiringTests
{
    [Fact]
    public void CompiledScanAndCaptureUseTheExecutedContextAndDetailedProducerSeams()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var scan = typeof(MainWindow).GetMethod("ScanCoreAsync", flags)!;
        var state = scan.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType.GetMethod("MoveNext", flags)!;
        Assert.Contains(Calls(state), m => m?.DeclaringType == typeof(GoroseiObservationSession) && m.Name == "AcceptRecognition");
        var capture = typeof(BulletSyntheticGoroseiProducer).Assembly;
        var methods = capture.GetTypes().SelectMany(t => t.GetMethods(flags | BindingFlags.DeclaredOnly)).ToArray();
        var observe = Assert.Single(methods, m => m.Name.Contains("CaptureBulletGuide") && m.Name.Contains("g__Observe|"));
        Assert.Contains(Calls(observe), m => m?.DeclaringType == typeof(BulletSyntheticGoroseiProducer) && m.Name == "Diagnostics");
        Assert.DoesNotContain(Calls(observe), m => m?.DeclaringType == typeof(RecognitionResult) && m.Name == "set_SyntheticGorosei");
        var root = Assert.Single(methods, m => m.Name == "CaptureBulletGuide");
        Assert.Contains(Calls(root), m => m?.DeclaringType == typeof(OverlayExecutionContext) && m.Name == "Fixture");
        Assert.DoesNotContain(Calls(root), m => m?.DeclaringType == typeof(OverlayExecutionContext) && m.Name == "CreateSyntheticGorosei");
        // Compiled IL connection, not execution of WPF or native handlers.
    }

    [Fact]
    public void CompiledGuideUsesPlanningSelectionAndBothWindowsConsumeSameDecisionFrame()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var refresh = typeof(MainWindow).GetMethod("RefreshAll", flags)!;
        var state = refresh.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType.GetMethod("MoveNext", flags)!;
        Assert.Contains(Calls(state), m => m?.DeclaringType == typeof(MainWindow) && m.Name == "ResolveGuidePlanningSelection");
        Assert.Contains(Calls(state), m => m?.DeclaringType == typeof(BulletGuidePolicy) && m.Name == "Plan");
        Assert.Contains(Calls(state), m => m?.DeclaringType == typeof(MainWindow) && m.Name == "ResolveObservationGorosei");
        var render = typeof(MainWindow).GetMethod("RenderBeginnerCoach", flags)!;
        Assert.Contains(Calls(render), m => m?.DeclaringType == typeof(CoachFrame) && m.Name == "set_UserGoroseiPlan");
        Assert.Contains(Calls(render), m => m?.DeclaringType == typeof(BeginnerCoachView) && m.Name == "Render");
        Assert.Contains(Calls(render), m => m?.DeclaringType == typeof(OverlayWindow) && m.Name == "RenderCoach");
        var selection = typeof(MainWindow).GetMethod("GoroseiCombo_OnSelectionChanged", flags)!;
        Assert.Contains(Calls(selection), m => m?.DeclaringType == typeof(AppSettings) && m.Name == "set_BulletPlanningGoroseiMode");
        var detected = typeof(MainWindow).GetMethod("ApplyDetectedGorosei", flags)!;
        Assert.DoesNotContain(Calls(detected), m => m?.DeclaringType == typeof(AppSettings) && m.Name == "set_BulletPlanningGoroseiMode");
        // Executed pure contracts plus compiled wiring, not rendered WPF evidence.
    }

    private static IEnumerable<MethodBase?> Calls(MethodInfo method)
    {
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => unchecked((ushort)o.Value));
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        for (var offset = 0; offset < il.Length;)
        {
            ushort key = il[offset++];
            if (key == 0xfe) key = (ushort)(0xfe00 | il[offset++]);
            var code = codes[key];
            if (code.OperandType == OperandType.InlineMethod)
                yield return method.Module.ResolveMethod(BitConverter.ToInt32(il, offset));
            offset += code.OperandType switch {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => 4 };
        }
    }
}
