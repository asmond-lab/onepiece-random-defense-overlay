using Xunit;
namespace OrandOverlay.Tests;

public sealed class BulletGoroseiNativeTests
{
    [Fact]
    public void ActualMarkerReaderVerifiesSelfOwnerAndLifeButNotActiveEffects()
    {
        var m = new MarkerMemory();
        var result = m.Reader().Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, [(MarkerMemory.Unit, 0x6f303332)]);
        Assert.Equal(GoroseiMarkerStatus.SelectedIdentity, result.Status);
        Assert.Equal(GoroseiMode.Nasjuro, result.Mode);
        var candidate = Assert.Single(result.Candidates);
        Assert.True(candidate.IdentityVerified); Assert.True(candidate.AliveVerified);
        Assert.Equal(10f, candidate.Life); Assert.Equal(9UL << 32, candidate.EngineHandle);
        Assert.False(result.EffectsActiveVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleDifferentMarkersAreConflictRegardlessOfEnumerationOrder(bool reverse)
    {
        var m = new MarkerMemory(); m.Second(0x6f303245);
        (ulong Address, uint Rawcode)[] candidates = [(MarkerMemory.Unit, 0x6f303332), (0x110000, 0x6f303245)];
        var result = m.Reader().Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, reverse ? candidates.Reverse() : candidates);
        Assert.Equal(GoroseiMarkerStatus.Conflict, result.Status);
        Assert.Equal(GoroseiMode.None, result.Mode);
        Assert.Equal(2, result.Candidates.Length);
    }

    [Fact]
    public void SelectedIdentityDoesNotActivateNasjuroPolicyAndConflictSurvivesSession()
    {
        var m = new MarkerMemory();
        var marker = m.Reader().Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, [(MarkerMemory.Unit, 0x6f303332)]);
        var session = new GoroseiObservationSession(); session.Reset(1); session.Accept(1, 1, marker, true);
        Assert.Equal(GoroseiMode.Nasjuro, session.Current.Mode);
        Assert.Equal(GoroseiMode.None, session.Current.EffectMode);
        Assert.Same(marker, session.Current.Marker);
        m.Second(0x6f303245);
        var conflict = m.Reader().Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256,
            [(MarkerMemory.Unit, 0x6f303332), (0x110000, 0x6f303245)]);
        session.Accept(1, 2, conflict, true);
        Assert.False(session.Current.IsCurrent);
        Assert.Equal(GoroseiMarkerStatus.Conflict, session.Current.Marker!.Status);
        Assert.Equal(GoroseiMode.Nasjuro, session.LastKnown);
        var frame = NasjuroWispAdvicePolicyTests.Frame() with { Gorosei = session.Current, RecognitionRevision = 2 };
        Assert.NotEqual(NasjuroApplicability.Applicable, NasjuroWispAdvicePolicy.Evaluate(frame).Applicability);
    }

    [Fact]
    public void LivingPreAppearanceMarkerEnablesPlanningNotCurrentEffect()
    {
        var m = new MarkerMemory();
        var marker = m.Reader().Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, [(MarkerMemory.Unit, 0x6f303332)]);
        var session = new GoroseiObservationSession(); session.Reset(1); session.Accept(1, 1, marker, true);
        var frame = NasjuroWispAdvicePolicyTests.Frame() with { Gorosei = session.Current, GoalId = BulletGuidePolicy.GoalId };
        frame = frame with { ShipReservations = ShipReservationPolicy.Evaluate(frame, NasjuroWispAdvicePolicyTests.Catalog()) };
        // Latest user correction: observed selection plans from the beginning, effect is separate.
        Assert.Equal(NasjuroApplicability.Applicable, NasjuroWispAdvicePolicy.Evaluate(frame).Applicability);
        Assert.Equal(GoroseiMode.None, frame.CurrentGoroseiEffect);
        Assert.False(frame.Gorosei.EffectsActiveVerified);
        Assert.Equal(BulletGuideAdvice.Operation(frame),
            new BeginnerCoachPlanner(NasjuroWispAdvicePolicyTests.Catalog()).Decide(frame).OperationGuide);
    }

    [Fact]
    public void RecognitionProducerCollectsAllMarkersAndTransmitsDetailsToUiSourceContract()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MainWindow.xaml.cs"))) root = root.Parent;
        Assert.NotNull(root);
        string Source(string file) => File.ReadAllText(Path.Combine(root.FullName, file));
        Assert.DoesNotContain("if (detected != GoroseiMode.None) gorosei = detected;", Source("WarcraftMemoryRecognitionService.cs"));
        Assert.Contains("new WarcraftGoroseiReader", Source("WarcraftMemoryRecognitionService.cs"));
        Assert.Contains("GoroseiMarker =", Source("WarcraftMemoryRecognitionService.cs"));
        Assert.Contains("_goroseiObservation.AcceptRecognition", Source("MainWindow.xaml.cs"));
        Assert.Contains("result.Diagnostics.GoroseiMarker", Source("GoroseiObservationSession.cs"));
        Assert.Contains("_goroseiObservation.Current.EffectMode", Source("MainWindow.xaml.cs"));
        Assert.Contains("frame.CurrentGoroseiEffect", Source("BulletGuideAdvice.cs"));
        Assert.Contains("frame.Gorosei.EffectEvidence", Source("BulletGuideAdvice.cs"));
    }

    internal sealed class MarkerMemory
    {
        internal const ulong Unit = 0x100000, Module = 0x400000;
        private readonly Dictionary<ulong, byte> bytes = new();
        internal MarkerMemory()
        {
            U64(Unit, 0x401000);
            U64(0x401178, Module + 0x1163ad0); U64(0x401278, Module + 0x1163a00); U64(0x401280, Module + 0x1163a20);
            U32(Unit + 0x178, 0x6f303332); Put(Unit + 0x1c0, [7]);
            U64(Unit + 0x18, 9UL << 32); U64(Unit + 0x258, (9UL << 32) | 1);
            U64(Module + 0x2b808c0, 0x120000); U32(0x120030, 2); U64(0x120018, 0x130000);
            for (var i = 0; i < 2; i++)
            {
                var entry = 0x140000UL + (ulong)i * 0x1000;
                U32(0x130000UL + (ulong)i * 16, 0xfffffffe); U64(0x130008UL + (ulong)i * 16, entry);
                U32(entry + 0x24, 9); U64(entry + 0x30, 0);
            }
            U32(0x140018, 0x2b61676c); U64(0x140090, Unit);
            Put(0x1410d0, BitConverter.GetBytes(10f)); Put(0x1410e0, BitConverter.GetBytes(10f));
            U64(Module + 0x2b80848, 0x200000); Put(0x200090, BitConverter.GetBytes(100f));
        }
        internal void Second(uint rawcode)
        {
            Put(0x110000, Read(Unit, 0x600)); U32(0x110178, rawcode); U64(0x110018, (9UL << 32) | 2);
            U32(0x120030, 3); U32(0x130020, 0xfffffffe); U64(0x130028, 0x142000);
            U32(0x142024, 9); U32(0x142018, 0x2b61676c); U64(0x142090, 0x110000);
        }
        internal WarcraftGoroseiReader Reader(Func<ulong, int, byte[]>? read = null) => new(read ?? Read, Module, 0x4000000);
        internal byte[] Read(ulong address, int length) => Enumerable.Range(0, length).Select(i => bytes.GetValueOrDefault(address + (ulong)i)).ToArray();
        internal void Put(ulong address, byte[] value) { for (var i = 0; i < value.Length; i++) bytes[address + (ulong)i] = value[i]; }
        internal void U32(ulong address, uint value) => Put(address, BitConverter.GetBytes(value));
        internal void U64(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
    }
}
