using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using Xunit;

namespace PlannerEvidenceCapture.Tests;

public sealed class BulletGuideProjectionWiringTests
{
    [Fact]
    public void CompiledCaptureLocalFunctionsCallTheTestedProjection()
    {
        // IL connection only, not execution of WPF, event handlers or native observation.
        var assembly = typeof(BulletGuideRowProjection).Assembly;
        var methods = assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)).ToArray();
        var expected = new[] { ("g__Observe|", "Observe"), ("g__StopEventProbe|", "StopZero"),
            ("g__StopEventProbe|", "StopRetained"), ("g__QueenEventChecks|", "Queen") };
        var evidence = new List<object>();
        foreach (var (local, factory) in expected)
        {
            var caller = Assert.Single(methods.Where(m => m.Name.Contains(local) && m.Name.Contains("CaptureBulletGuide")));
            var callee = typeof(BulletGuideRowProjection).GetMethod(factory)!;
            Assert.Contains(Calls(caller), m => m == callee);
            evidence.Add(new { Caller = caller.DeclaringType!.FullName + "." + caller.Name,
                Callee = callee.DeclaringType!.FullName + "." + callee.Name });
        }
        var directory = Environment.GetEnvironmentVariable("BULLET_SERIALIZATION_EVIDENCE");
        if (directory is not null) File.WriteAllText(Path.Combine(directory, "compiled-projection-calls.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
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
            offset += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => 4
            };
        }
    }
}
