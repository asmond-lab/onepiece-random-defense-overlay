using System.Text;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Warcraft300MapPathReaderTests
{
    [Fact] public void ExactTypedPathIsObservationNotArchiveApproval()
    {
        var f = new NativeMapFixture(); var r = f.Observe();
        Assert.NotNull(r.Observation); Assert.Equal(f.Path, r.Observation!.Path);
        Assert.Contains("CMapSetupWar3", r.Observation.Provenance);
        Assert.InRange(r.BytesRead, 1, Warcraft300MapPathReader.MaximumReadBytes);
    }
    [Theory]
    [InlineData("root")][InlineData("setup")][InlineData("rep")][InlineData("instance")]
    [InlineData("script")][InlineData("state")][InlineData("world")][InlineData("ui")]
    public void EveryRequiredTypeIsChecked(string name)
    { var f = new NativeMapFixture(); f.Q(f.Nodes[name], 0); Assert.Null(f.Observe().Observation); }
    [Theory][InlineData("version")][InlineData("hash")][InlineData("trust")][InlineData("epoch")]
    public void CallerAndExecutablePinsFailClosed(string which)
    {
        var f = new NativeMapFixture();
        f.Context = which switch { "version" => f.Context with { Version = "wrong" },
            "hash" => f.Context with { ExecutableSha256 = "wrong" },
            "trust" => f.Context with { CallerValidated = false }, _ => f.Context with { Epoch = Guid.Empty } };
        Assert.Null(f.Observe().Observation); Assert.Equal(0, f.Calls);
    }
    [Theory][InlineData("root")][InlineData("setup")][InlineData("rep")][InlineData("text")][InlineData("string")][InlineData("vm")][InlineData("context")]
    public void PointerTextOrEpochChangesDuringSamplingFail(string which)
    {
        var f = new NativeMapFixture(); bool changed = false;
        f.AfterRead = (a, n) => { if (a != f.Text || changed) return; changed = true;
            switch (which) {
            case "root": f.Q(f.Base + 0x2E9AD00, 0); break;
            case "setup": f.Q(f.Root + 0x2638, 0); break;
            case "rep": f.Q(f.Setup + 0x50, 0); break;
            case "text": f.Q(f.Rep + 0x38, f.Text + 8); break;
            case "string": f.Bytes[f.Text + 3] = (byte)'Z'; break;
            case "vm": f.Q(f.Instance + 24, 7); break;
            default: f.Context = f.Context with { Epoch = Guid.NewGuid() }; break;
            }
        };
        Assert.Null(f.Observe().Observation);
    }
    [Theory]
    [InlineData(@"\\server\share\map.w3x")][InlineData(@"\\?\C:\map.w3x")]
    [InlineData("C:map.w3x")][InlineData("C:/map.w3x:secret")][InlineData("C:/map.txt")]
    [InlineData("C:/../map.w3x")][InlineData("C:/NUL.w3x")][InlineData("https://host/map.w3x")]
    public void UnsafePathsAreRejected(string path)
    { var f = new NativeMapFixture(); f.SetText(Encoding.UTF8.GetBytes(path)); Assert.Null(f.Observe().Observation); }
    [Theory][InlineData("C:/Users/123/Documents/Warcraft III/Maps/Download/ORDR_S2_2.320[R].w3x")]
    [InlineData(@"D:\Maps\맵.w3x")]
    public void LocalAbsoluteUtf8PathsAccepted(string path)
    { var f = new NativeMapFixture(); f.SetText(Encoding.UTF8.GetBytes(path)); Assert.NotNull(f.Observe().Observation); }
    [Fact] public void InvalidUtf8Rejected()
    { var f = new NativeMapFixture(); f.SetText(new byte[] { 0xC0, 0xAF }); Assert.Null(f.Observe().Observation); }
    [Fact] public void MissingTerminatorRejectedWithinBudget()
    { var f = new NativeMapFixture(); f.SetText(Enumerable.Repeat((byte)'a', 2048).ToArray(), false); Assert.Equal("UnterminatedPath", f.Observe().Failure); }
    [Fact] public void TruncatedReadsRejected()
    { var f = new NativeMapFixture(); f.Short = true; Assert.Equal("ShortRead", f.Observe().Failure); }
    [Fact] public void ReadsNeverCrossPages()
    { var f = new NativeMapFixture(); f.Text = 0x260FF8; f.Q(f.Rep + 0x38, f.Text); f.SetText(Encoding.UTF8.GetBytes(f.Path)); Assert.NotNull(f.Observe().Observation); }
    [Fact] public void CancelAndTimeBudgetsFailClosed()
    {
        var f = new NativeMapFixture(); using var c = new CancellationTokenSource(); c.Cancel();
        Assert.Equal("Cancelled", f.Observe(c.Token).Failure);
        f.AfterRead = (_, _) => f.Now += 1001;
        Assert.Equal("ReadTimeBudget", f.Observe().Failure);
    }
    [Fact] public void ExactReaderTimeCapIsRejected()
    { var f = new NativeMapFixture(); f.AfterRead = (_, _) => f.Now += Warcraft300MapPathReader.MaximumMilliseconds; Assert.Equal("ReadTimeBudget", f.Observe().Failure); }
    [Fact] public void UntrustedFreshClosureRejected()
    { var f = new NativeMapFixture(); f.Capture = () => f.Context with { CallerValidated = false }; Assert.Null(f.Observe().Observation); }
}

internal sealed class NativeMapFixture
{
    internal readonly Dictionary<ulong, byte> Bytes = new();
    internal ulong Base = 0x140000000, Root = 0x200000, World = 0x210000, Ui = 0x220000,
        Instance = 0x230000, Script = 0x240000, State = 0x245000, Setup = 0x250000, Rep = 0x255000, Text = 0x260000;
    internal readonly Dictionary<string, ulong> Nodes = new();
    internal string Path = "C:/Maps/active.w3x";
    internal long Now = 100; internal int Calls; internal bool Short;
    internal Action<ulong, int>? AfterRead; internal Func<NativeMapContext>? Capture; internal Func<long>? ClockOverride;
    internal NativeMapContext Context;
    internal NativeMapFixture()
    {
        Context = new(50744, new DateTimeOffset(2026, 9, 14, 5, 21, 7, TimeSpan.Zero),
            Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash, Base, Root, World, Ui,
            Instance, Script, Guid.NewGuid(), true);
        ulong x = unchecked(((Root - 0x2D2C27903E7F5D3DUL) ^ 0x3A11C7B7EF67132BUL) - 0x5BE06F37FC9B5B29UL);
        Q(Base + 0x2E9AD00, (x >> 29) | (x << 35));
        var types = new[] { ("root", Root, 0x26C8C70UL), ("world", World, 0x2764A20UL), ("ui", Ui, 0x275ED08UL),
            ("instance", Instance, 0x27ECBC0UL), ("script", Script, 0x27ECC40UL), ("state", State, 0x26CCE00UL),
            ("setup", Setup, 0x26C8AE8UL), ("rep", Rep, 0x2282340UL) };
        foreach (var (name, a, vt) in types) { Nodes[name] = a; Q(a, Base + vt); }
        Q(World + 0x40, Ui); Q(Base + 0x2F5EF00, Ui); Q(Base + 0x2F85360, Ui);
        Q(Root + 0x25D0, Instance); Q(Root + 0x25E0, Script); Q(Root + 0x2620, State);
        Q(Root + 0x2638, Setup); Q(Setup + 0x50, Rep); Q(Rep + 0x38, Text);
        SetText(Encoding.UTF8.GetBytes(Path));
    }
    internal void Q(ulong a, ulong value) { var b = BitConverter.GetBytes(value); for (int i = 0; i < 8; i++) Bytes[a + (ulong)i] = b[i]; }
    internal void SetText(byte[] text, bool terminate = true)
    { for (int i = 0; i < 2048; i++) Bytes[Text + (ulong)i] = 0; for (int i = 0; i < text.Length; i++) Bytes[Text + (ulong)i] = text[i]; if (terminate && text.Length < 2048) Bytes[Text + (ulong)text.Length] = 0; }
    private byte[] Read(ulong a, int n)
    {
        Assert.True((a & 4095) + (ulong)n <= 4096); Calls++;
        var b = Enumerable.Range(0, Short ? n - 1 : n).Select(i => Bytes.GetValueOrDefault(a + (ulong)i)).ToArray();
        AfterRead?.Invoke(a, n); return b;
    }
    internal NativeMapPathResult Observe(CancellationToken token = default)
    { var expected = Context; return Warcraft300MapPathReader.ObserveCore(Read, expected, () => Capture?.Invoke() ?? Context, () => ClockOverride?.Invoke() ?? Now, token); }
}
