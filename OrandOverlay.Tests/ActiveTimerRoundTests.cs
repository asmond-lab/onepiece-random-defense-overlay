using System.Text;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ActiveTimerRoundTests
{
    [Fact]
    public void ActualRound35Story13ReplayWithOwnedEggheadDialogCompletesBeforeNextSpawn()
    {
        // Replay match-20260909-163941-aba1a17affac4b0da176ee6e333f9cc0,
        // sequence 58, 2026-09-09T16:43:10.2901788Z: round=35, completed=12,
        // decision=story:13. Live PID 20404 has native dialog 0xD70C80000824A
        // with this exact owned title; pinned JASS kDw:63010 creates it only on clear.
        var image = new TimerImage(35);
        image.SetTitle("|cffFF0000에그헤드|r 등장");
        var reader = new WarcraftCurrentRoundReader(MapSignalRecognitionProfile.ParseStoryCompletionTitle);
        var profile = MapSignalRecognitionProfile.FromStory(MapStoryProfileLoader.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Data")));
        var tracker = new MapSignalSnapshotTracker(profile);
        RawcodeCodec.TryParse("n00B", out var target);
        var before = new MapSignalRawSnapshot([target], []);
        tracker.Observe(before);
        Assert.Equal(12, tracker.Observe(before).CompletedStoryStageOrdinal);
        var cleared = before with { Story13CompletionObserved = image.Round(reader) == 13 };
        Assert.True(cleared.Story13CompletionObserved);
        Assert.Equal(13, tracker.Observe(cleared).CompletedStoryStageOrdinal);
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var planner = new BeginnerCoachPlanner(catalog);
        var replay = new CoachFrame { MatchGeneration = 0, Revision = 941,
            Round = 35, CompletedStoryStage = 12, IsCurrent = true,
            Difficulty = "악몽", Inventory = System.Text.Json.JsonSerializer.Deserialize<
                System.Collections.Immutable.ImmutableDictionary<string, int>>(
                """{"rawcode:910h":1,"rawcode:N10h":1,"rawcode:Y50h":1,"rawcode:700h":4,"rawcode:D10h":1,"rawcode:500h":4,"rawcode:G00h":4,"rawcode:N00h":2,"rawcode:J00h":3,"rawcode:R00h":1,"rawcode:X00h":2,"rawcode:U00h":1,"rawcode:O00h":4,"rawcode:K00h":2,"rawcode:HA0h":1,"rawcode:060h":2,"rawcode:800h":2,"rawcode:010h":1,"rawcode:J10h":1,"rawcode:I00h":1,"rawcode:E00h":2,"rawcode:U20h":1,"rawcode:Q00h":1,"rawcode:L00h":1,"rawcode:F00h":1,"rawcode:420h":1,"rawcode:MC0h":1,"luffy_common":6,"rawcode:M00h":4,"rawcode:600h":2,"rawcode:900h":3,"rawcode:P10h":1,"rawcode:A00h":1,"rawcode:200h":10,"rawcode:610h":1,"rawcode:C00h":1,"rawcode:X50h":1,"rawcode:100h":3,"rawcode:I10h":2,"rawcode:400h":4}""")! };
        replay = replay with { Mode = PlayMode.Guide, GuideNumber = 1, GuidePlan = new BulletGuidePolicy(catalog)
            .Plan(replay.Round, replay.CompletedStoryStage, replay.Inventory, replay.Difficulty) };
        Assert.Equal("story:13", planner.Decide(replay).Id);
        Assert.NotEqual("story:13", planner.Decide(replay with
            { CompletedStoryStage = tracker.LastGood.CompletedStoryStageOrdinal }).Id);
        Assert.Equal(13, tracker.Observe(before).CompletedStoryStageOrdinal);
        Assert.Equal(13, tracker.Observe(before).CompletedStoryStageOrdinal);
        Assert.False(tracker.LastObservationConfirmedReset);
        RawcodeCodec.TryParse("n009", out var finalTarget);
        tracker.Observe(new([finalTarget], []));
        var finalStage = tracker.Observe(new([finalTarget], []));
        Assert.Equal(13, finalStage.CompletedStoryStageOrdinal);
        Assert.Equal(14, finalStage.ActiveObjectiveOrdinal);
        tracker.Observe(before);
        Assert.Equal(13, tracker.Observe(before).CompletedStoryStageOrdinal);
        Assert.False(tracker.LastObservationConfirmedReset);
        RawcodeCodec.TryParse("n000", out var firstTarget);
        tracker.Observe(new([firstTarget], []));
        Assert.Equal(0, tracker.Observe(new([firstTarget], [])).CompletedStoryStageOrdinal);
        Assert.True(tracker.LastObservationConfirmedReset);
        tracker.Reset();
        Assert.Equal(0, tracker.Observe(new([], [])).CompletedStoryStageOrdinal);
        Assert.Equal(0, tracker.Observe(before).CompletedStoryStageOrdinal);
        Assert.Equal(12, tracker.Observe(before).CompletedStoryStageOrdinal);
        tracker.Reset();
        var lateAttach = tracker.Observe(new([], [], Story13CompletionObserved: true));
        Assert.Equal(13, lateAttach.CompletedStoryStageOrdinal);
        Assert.Null(lateAttach.ActiveObjectiveOrdinal);
    }

    [Fact]
    public void PausedActiveTimer8WinsOverRetainedHeap48()
    {
        var image = new TimerImage(8);
        var reader = new WarcraftCurrentRoundReader();
        var heap = MapStateReader.ScanBuffer(Encoding.UTF8.GetBytes("현재 라운드|r : 48|r\0현재 라운드|r : 2|r"));
        Assert.Equal(48, heap.MaxRound);
        Assert.Equal(8, MapStateReader.SelectCurrentRound(heap, image.Round(reader)).MaxRound);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(8)] [InlineData(10)] [InlineData(20)]
    [InlineData(30)] [InlineData(40)] [InlineData(50)] [InlineData(55)] [InlineData(60)] [InlineData(65)]
    public void ExactSourceAuthoredNormalAndBossTimerNeedsNoOffset(int round)
        => Assert.Equal(round, new TimerImage(round).Round(new WarcraftCurrentRoundReader()));

    [Fact]
    public void SameProcessNewRootRejectsIntactOld48AndAcceptsCurrent2()
    {
        var old = new TimerImage(48);
        var next = new TimerImage(2, 0x10000000);
        var reader = new WarcraftCurrentRoundReader();
        Assert.Equal(48, old.Round(reader));
        byte[] BothMatches(ulong address, int length)
        {
            var current = next.Read(address, length);
            return current.Length > 0 ? current : old.Read(address, length);
        }
        Assert.Equal(48, MapStateReader.ScanBuffer(old.Read(TimerImage.Title, 96)).MaxRound);
        Assert.Equal(2, next.Round(reader, BothMatches));
        Assert.True(reader.SessionChanged);
        var boundary = WarcraftMemoryRecognitionService.ReadyDiagnostics(
            new RecognitionDiagnostics { MapState = new(2, 0, "악몽") }, reader.SessionChanged);
        Assert.Null(boundary.MapState);
        reader.Reset();
        Assert.Equal(2, next.Round(reader, BothMatches));
        Assert.False(reader.SessionChanged);
    }

    [Fact]
    public void ReusedRootAndHandleAddressesStillDetectAuthoritativeTitleRollback()
    {
        var image = new TimerImage(48);
        var reader = new WarcraftCurrentRoundReader();
        Assert.Equal(48, image.Round(reader));
        image.SetRound(49);
        Assert.Equal(49, image.Round(reader));
        Assert.False(reader.SessionChanged);
        image.SetRound(2);
        Assert.Equal(2, image.Round(reader));
        Assert.True(reader.SessionChanged);
    }

    [Theory]
    [InlineData(TimerImage.Slots, 0UL)]
    [InlineData(TimerImage.Entry + 0x24, 9UL)]
    [InlineData(TimerImage.Agent + 0x18, 9UL)]
    [InlineData(TimerImage.TimerEntry + 0x30, 1UL)]
    [InlineData(TimerImage.Frame + 0x40, TimerImage.GameUi)]
    [InlineData(TimerImage.Ui + 0x2d8, TimerImage.Agent)]
    [InlineData(TimerImage.Module + 0x2b84770, TimerImage.Ui)]
    public void DetachedOrReusedNativeObjectsCannotPublishCachedRound(ulong address, ulong value)
    {
        var image = new TimerImage(8);
        var reader = new WarcraftCurrentRoundReader();
        Assert.Equal(8, image.Round(reader));
        image.U64(address, value);
        Assert.Null(image.Round(reader));
    }

    [Fact]
    public void ChangedOwnedTitlePointerIsRejectedEvenWhenBothTextsSay8()
    {
        var image = new TimerImage(8);
        image.SetTitle("|cffFF0000현재 라운드|r : 8|r", 128);
        var pointers = 0;
        byte[] ChangePointer(ulong address, int length)
        {
            if (address == TimerImage.Frame + 0x350 && ++pointers == 2)
                image.U64(address, TimerImage.Title + 128);
            return image.Read(address, length);
        }
        var reader = new WarcraftCurrentRoundReader();
        Assert.Null(image.Round(reader, ChangePointer));
        Assert.Equal(8, image.Round(reader));
    }

    [Fact]
    public void ChangedTitleBytesAndRootDuringSnapshotAreRejected()
    {
        var image = new TimerImage(8);
        var titles = 0;
        byte[] ChangeText(ulong address, int length)
        {
            if (address == TimerImage.Title && ++titles == 2) image.SetRound(9);
            return image.Read(address, length);
        }
        Assert.Null(image.Round(new WarcraftCurrentRoundReader(), ChangeText));
        byte[] DetachRoot(ulong address, int length)
        {
            var bytes = image.Read(address, length);
            if (address == TimerImage.Title) image.U64(TimerImage.Module + 0x2b808c0, 0);
            return bytes;
        }
        Assert.Null(image.Round(new WarcraftCurrentRoundReader(), DetachRoot));
    }

    [Theory]
    [InlineData("현재 라운드|r : 48|r")]
    [InlineData("|cffFF0000현재 라운드 : |r")]
    [InlineData("|cffFF0000현재 라운드|r : 0|r")]
    [InlineData("|cffFF0000현재 라운드|r : -1|r")]
    [InlineData("|cffFF0000현재 라운드|r : 66|r")]
    [InlineData("|cffFF0000현재 라운드|r : 8|r garbage")]
    public void PartialOrUnsupportedOwnedTitlesRemainUnknown(string title)
    {
        var image = new TimerImage(8);
        image.SetTitle(title);
        Assert.Null(image.Round(new WarcraftCurrentRoundReader()));
    }

    [Fact]
    public void SourceGuardsRunBeforeAnyNativeRead()
    {
        byte[] NoRead(ulong address, int length) => throw new Exception("Untrusted native read");
        var reader = new WarcraftCurrentRoundReader();
        Assert.Null(reader.Read(NoRead, TimerImage.Module, TimerImage.ModuleSize, "other", RouteQuestCatalog.MapScriptSha256, 0, CancellationToken.None));
        Assert.Null(reader.Read(NoRead, TimerImage.Module, TimerImage.ModuleSize, "2.0.4.23745", "other", 0, CancellationToken.None));
        Assert.Null(reader.Read(NoRead, TimerImage.Module, 0, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, CancellationToken.None));
        foreach (byte? owner in new byte?[] { null, 4 })
            Assert.Null(reader.Read(NoRead, TimerImage.Module, TimerImage.ModuleSize, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, owner, CancellationToken.None));
        Assert.Throws<OperationCanceledException>(() => reader.Read(NoRead, TimerImage.Module, TimerImage.ModuleSize,
            "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, new CancellationToken(true)));
    }

    [Fact]
    public void StablePausedTimerUsesValidatedCacheWithoutRediscoveringTheTable()
    {
        var image = new TimerImage(8);
        var scans = 0;
        byte[] Count(ulong address, int length)
        {
            if (address == TimerImage.Slots && length == 32) scans++;
            return image.Read(address, length);
        }
        var reader = new WarcraftCurrentRoundReader();
        Assert.Equal(8, image.Round(reader, Count));
        Assert.Equal(8, image.Round(reader, Count));
        Assert.Equal(1, scans);
        Assert.False(reader.SessionChanged);
    }

    [Theory]
    [InlineData(TimerImage.Slots, 0UL)]
    [InlineData(TimerImage.Entry + 0x24, 9UL)]
    [InlineData(TimerImage.Frame + 0x40, TimerImage.GameUi)]
    [InlineData(TimerImage.Ui + 0x2d8, TimerImage.Agent)]
    public void DetachedEggheadDialogDoesNotProveCompletion(ulong address, ulong value)
    {
        var image = new TimerImage(35);
        image.SetTitle("|cffFF0000에그헤드|r 등장");
        var reader = new WarcraftCurrentRoundReader(MapSignalRecognitionProfile.ParseStoryCompletionTitle);
        Assert.Equal(13, image.Round(reader));
        image.U64(address, value);
        Assert.Null(image.Round(reader));
    }

    [Fact]
    public void OldEggheadDialogCannotLeakIntoSameProcessNewMatch()
    {
        var old = new TimerImage(35);
        old.SetTitle("|cffFF0000에그헤드|r 등장");
        var next = new TimerImage(2, 0x10000000);
        var reader = new WarcraftCurrentRoundReader(MapSignalRecognitionProfile.ParseStoryCompletionTitle);
        Assert.Equal(13, old.Round(reader));
        byte[] Both(ulong address, int length)
        {
            var current = next.Read(address, length);
            return current.Length > 0 ? current : old.Read(address, length);
        }
        Assert.Null(next.Round(reader, Both));
    }

    private sealed class TimerImage
    {
        internal const ulong Module = 0x10000000;
        internal const int ModuleSize = 0x3000000;
        internal const ulong Root = 0x20000000, Slots = 0x21000000, Entry = 0x22000000,
            Agent = 0x23000000, Ui = 0x24000000, Frame = 0x25000000, Title = 0x26000000,
            GameUi = 0x27000000, TimerEntry = 0x28000000, Timer = 0x29000000;
        internal const ulong DialogHandle = 0x100000000, TimerHandle = 0x200000001;
        private readonly Dictionary<ulong, byte[]> blocks = new();
        private readonly ulong shift;
        private ulong At(ulong address) => address + shift;

        internal TimerImage(int round, ulong shift = 0)
        {
            this.shift = shift;
            foreach (var address in new[] { Module + 0x2b808c0, Module + 0x2b84770 }) blocks[address] = new byte[8];
            foreach (var address in new[] { Root, Entry, TimerEntry }) blocks[At(address)] = new byte[0x100];
            blocks[At(Slots)] = new byte[32];
            foreach (var address in new[] { Agent, Ui, Frame, GameUi, Timer }) blocks[At(address)] = new byte[0x400];
            blocks[At(Title)] = new byte[256];
            U64(Module + 0x2b808c0, At(Root)); U64(Module + 0x2b84770, At(GameUi));
            U64(At(Root) + 0x18, At(Slots)); U32(At(Root) + 0x30, 2);
            U32(At(Slots), 0xfffffffe); U64(At(Slots) + 8, At(Entry));
            U32(At(Slots) + 16, 0xfffffffe); U64(At(Slots) + 24, At(TimerEntry));
            U32(At(Entry) + 0x24, 1); U64(At(Entry) + 0x90, At(Agent));
            U32(At(TimerEntry) + 0x24, 2); U64(At(TimerEntry) + 0x90, At(Timer));
            U64(At(Agent), Module + 0x23dbcf0); U64(At(Agent) + 0x18, DialogHandle);
            U64(At(Agent) + 0x58, At(Ui)); U64(At(Agent) + 0x60, TimerHandle);
            U64(At(Ui), Module + 0x24046a8); U64(At(Ui) + 0x40, At(GameUi));
            U64(At(Ui) + 0x298, At(Frame)); U64(At(Ui) + 0x2d8, At(Timer));
            U64(At(Frame), Module + 0x21ea5f0); U64(At(Frame) + 0x40, At(Ui)); U64(At(Frame) + 0x350, At(Title));
            U64(At(GameUi), Module + 0x23fd7d0); U64(At(Timer), Module + 0x23d7aa0);
            U64(At(Timer) + 0x18, TimerHandle);
            SetRound(round);
        }

        internal void SetRound(int round)
        {
            var marker = round > 50 ? "현재 라운드 : |r" : round % 10 == 0 ? "보스 라운드|r : " : "현재 라운드|r : ";
            SetTitle($"|cffFF0000{marker}{round}|r");
        }

        internal void SetTitle(string title, int offset = 0)
        {
            blocks[At(Title)].AsSpan(offset).Clear();
            Encoding.UTF8.GetBytes(title).CopyTo(blocks[At(Title)], offset);
        }

        internal byte[] Read(ulong address, int length)
        {
            foreach (var (start, bytes) in blocks)
                if (address >= start && address - start + (ulong)length <= (ulong)bytes.Length)
                    return bytes.AsSpan((int)(address - start), length).ToArray();
            return [];
        }
        internal void U64(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
        internal void U32(ulong address, uint value) => Put(address, BitConverter.GetBytes(value));
        private void Put(ulong address, byte[] value)
        {
            var start = blocks.Keys.Single(start => address >= start && address - start + (ulong)value.Length <= (ulong)blocks[start].Length);
            value.CopyTo(blocks[start], (int)(address - start));
        }
        internal int? Round(WarcraftCurrentRoundReader reader, Func<ulong, int, byte[]>? read = null) =>
            reader.Read(read ?? Read, Module, ModuleSize, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, CancellationToken.None);
    }
}
