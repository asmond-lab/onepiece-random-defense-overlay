using System.Text;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletNativeGlobalsTests
{
    [Fact]
    public void ConflictReaderAlsoBlocksRecipeRecommendationBoardNotJustCoach()
    {
        var f = new GlobalsMemory(); f.Array("JP", 9, 2); f.Array("SP", 9, 2); f.Array("cE", 13, 1); f.Array("Ly", 13, 1);
        var native = new WarcraftNavigationReader().Read(f.Read, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, f.Nodes["JP"]);
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var inventory = new[] { "HA0h", "I20h", "L00h" }.ToDictionary(c => "rawcode:" + c, _ => 1);
        var plan = new BulletGuidePolicy(catalog).Plan(25, 10, inventory, "악몽", BulletGuidePolicy.NavigationId,
            queenInput: QueenConversionInput.UserConfirmedMissionsComplete);
        var request = new RecommendationPipelineRequest { Engine = new RecommendationEngine(catalog),
            Mode = PlayMode.Guide, GuidePlan = plan, Goal = catalog.Unit(BulletGuidePolicy.GoalId),
            Inventory = inventory.Select(p => new InventoryEntry { UnitId = p.Key, Count = p.Value }).ToArray(),
            InitialSurface = RecommendationSurface.TopAndNavigation, NavigationMode = BulletGuidePolicy.NavigationId,
            Gorosei = GoroseiMode.None, BuildVariant = BuildVariants.AutoId, Difficulty = "악몽", Round = 25, CompletedStoryStage = 10 };
        Assert.NotEmpty(RecommendationPipeline.ComputeCandidates(request).Recommendations);
        Assert.Empty(RecommendationPipeline.ComputeCandidates(request with { NativeNavigation = native }).Recommendations);
    }
    [Theory]
    [InlineData(1, "AlliedForces.DoubleBenefit")]
    [InlineData(2, "AlliedForces.EmergencyCall")]
    [InlineData(3, "AlliedForces.TraitEngineering")]
    public void AlliedSelectionRequiresLySetByCallerAndDoesNotInventRemainingCharges(int selected, string option)
    {
        var f = new GlobalsMemory(); f.Array("JP", 9, 0); f.Array("SP", 9, selected);
        f.Array("cE", 13, 1); f.Array("Ly", 13, 1);
        var result = new WarcraftNavigationReader().Read(f.Read, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, f.Nodes["JP"]);
        Assert.Equal(NativeNavigationStatus.Selected, result.Status);
        Assert.Equal(option, result.OptionId);
        Assert.Contains("별도 관측", result.Detail);
    }

    [Fact]
    public void ObservedFailuresFlowThroughPolicyAndCoachAsFailuresNotAttempts()
    {
        var f = new GlobalsMemory(); f.Array("OE", 9, 2); f.Array("cr", 13, 1);
        var quests = RouteQuestSnapshot.FromVerifiedSlots([new(0, "Q006", false), new(1, "Q001", true), new(2, "Q002", true)]);
        var observed = new WarcraftHighGambleReader().Read(f.Read, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, f.Nodes["JP"], quests);
        quests = quests with { HighGamble = observed };
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var inventory = System.Collections.Immutable.ImmutableDictionary<string, int>.Empty.Add(BulletGuidePolicy.GoalId, 1);
        var plan = new BulletGuidePolicy(catalog).Plan(50, 13, inventory, "악몽", BulletGuidePolicy.NavigationId, routeQuests: quests);
        var frame = BeginnerCoachPlannerTests.ReadyFrame() with { Mode = PlayMode.Guide, GuideNumber = 1,
            Inventory = inventory, Round = 50, GuidePlan = plan, CraftSteps = [] };
        var text = new BeginnerCoachPlanner(catalog).Decide(frame).OperationGuide;
        Assert.True(observed.IsVerified);
        Assert.True(observed.Active);
        Assert.Equal(2, observed.Failures);
        Assert.Equal(observed, plan.HighGamble);
        Assert.True(plan.ActiveHighGambleQuest);
        Assert.Contains(HighGamblePresentation.Describe(observed), text);
        Assert.Equal(2000, HighGamblePresentation.GoldCost);
        Assert.Equal(4, HighGamblePresentation.LumberCost);
        Assert.Equal("R00G", HighGamblePresentation.RequiredTech);
    }
    [Fact]
    public void NativeBountyFlowsThroughActualReaderIntoCoachInsteadOfRequestingSelection()
    {
        var fixture = new GlobalsMemory();
        fixture.Array("JP", 9, 2); fixture.Array("SP", 9, 0);
        fixture.Array("cE", 13, 1); fixture.Array("Ly", 13, 1);
        var observed = new WarcraftNavigationReader().Read(fixture.Read, "2.0.4.23745",
            RouteQuestCatalog.MapScriptSha256, 0, fixture.Nodes["JP"]);
        var frame = BeginnerCoachPlannerTests.ReadyFrame() with { Round = 22,
            ConfirmedNavigation = observed.Resolve(null) };
        Assert.Equal("PathOfKings.BountyHunter", frame.ConfirmedNavigation);
        Assert.NotEqual(CoachActionKind.Navigation, new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame).Kind);
    }

    [Fact]
    public void ConflictFromReaderStopsSpendingDespiteManualConfirmation()
    {
        var f = new GlobalsMemory(); f.Array("JP", 9, 2); f.Array("SP", 9, 2);
        f.Array("cE", 13, 1); f.Array("Ly", 13, 1);
        var native = new WarcraftNavigationReader().Read(f.Read, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, f.Nodes["JP"]);
        Assert.Equal(NativeNavigationStatus.Conflict, native.Status);
        var frame = BeginnerCoachPlannerTests.ReadyFrame() with { NativeNavigation = native };
        var decision = new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame);
        Assert.Equal(CoachActionKind.Waiting, decision.Kind);
        Assert.Equal("navigation-conflict", decision.Id);
        Assert.Null(decision.ConsumedUnitId);
    }

    [Fact]
    public void NativeProducerAndBothUiConsumersAreWiredSourceContract()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MainWindow.xaml.cs"))) root = root.Parent;
        Assert.NotNull(root);
        string Source(string file) => File.ReadAllText(Path.Combine(root.FullName, file));
        Assert.Contains("new WarcraftNavigationReader().Read", Source("WarcraftMemoryRecognitionService.cs"));
        Assert.Contains("NativeNavigation = _mapSignals.NativeNavigation", Source("MainWindow.Coach.cs"));
        Assert.Contains("EffectiveNavigation", Source("MainWindow.xaml.cs"));
        Assert.Contains("frame.NativeNavigation", Source("BeginnerCoachView.xaml.cs"));
        Assert.Contains("_mapSignals.NativeNavigation", Source("MainWindow.NavigationContext.cs"));
    }

    [Fact]
    public void ArrayRejectsNodePointerReplacementEvenWhenValuesAgree()
    {
        var fixture = new GlobalsMemory();
        fixture.Array("JP", 9, 2);
        var changed = false;
        var memory = new RouteQuestMemory((address, size) =>
        {
            var result = fixture.Read(address, size);
            if (!changed && address == fixture.Data("JP"))
            { changed = true; fixture.Array("JP", 9, 2); }
            return result;
        });
        Assert.Throws<InvalidDataException>(() => memory.Array(fixture.Nodes["JP"], "JP", 9, 4));
    }

    internal sealed class GlobalsMemory
    {
        internal const ulong Base = 0x100000;
        private readonly byte[] bytes = new byte[0x20000];
        internal readonly Dictionary<string, ulong> Nodes = new();
        private int next = 0x8000;
        internal GlobalsMemory()
        {
            var names = new[] { "JP", "SP", "cE", "Ly", "OE", "cr" };
            for (var i = 0; i < names.Length; i++)
            {
                var node = Base + 0x100UL + (ulong)i * 72;
                Nodes[names[i]] = node;
                Put(node + 32, i == names.Length - 1 ? 0UL : node + 72);
                Put(node + 40, Base + 0x3000UL + (ulong)i * 32);
                Encoding.ASCII.GetBytes(names[i] + "\0").CopyTo(bytes, 0x3000 + i * 32);
            }
        }
        internal void Array(string name, int type, params int[] values)
        {
            var node = Nodes[name]; var header = Base + (ulong)next; next += 128;
            Put(node + 48, type); Put(node + 52, type); Put(node + 56, header);
            Put(header + 8, values.Length); Put(header + 24, values.Length); Put(header + 16, header + 32);
            for (var i = 0; i < values.Length; i++) Put(header + 32 + (ulong)i * 4, values[i]);
        }
        internal ulong Data(string name) => BitConverter.ToUInt64(Read(BitConverter.ToUInt64(Read(Nodes[name] + 56, 8)) + 16, 8));
        internal byte[] Read(ulong address, int size) => address >= Base && address + (ulong)size <= Base + (ulong)bytes.Length
            ? bytes.AsSpan((int)(address - Base), size).ToArray() : [];
        internal void Put(ulong address, int value) => BitConverter.GetBytes(value).CopyTo(bytes, (int)(address - Base));
        internal void Put(ulong address, ulong value) => BitConverter.GetBytes(value).CopyTo(bytes, (int)(address - Base));
    }
}
