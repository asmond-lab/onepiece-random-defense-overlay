using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class CoachJournalTests
{
    [Fact]
    public async Task MemoryJournal_RecordsAndReadsBackWithoutAPath()
    {
        var journal = CoachJournal.Memory();
        var action = new CoachDecision(CoachActionKind.Craft, "craft:goal", "목표 제작", "", "관측", "", "")
            { TargetUnitId = "goal" };
        Assert.True(await journal.RecordAsync(Frame(1, 1, ("material", 1)), action));
        Assert.False(journal.IsPersistent);
        Assert.Null(journal.LatestPath);
        var review = CoachReview.Read(journal);
        Assert.False(review.Incomplete);
        Assert.Equal(20, review.LastRound);
        Assert.Equal("Craft", review.Entries.Last().Kind);
        Assert.True(await journal.RecordAsync(Frame(1, 4, ("goal", 1)), action));
        review = CoachReview.Read(journal);
        Assert.Equal("goal", review.Entries.Last().AcquiredUnit);
        Assert.True(await journal.RecordAsync(Frame(2, 1), action));
        review = CoachReview.Read(journal);
        Assert.Single(review.Entries);
        Assert.Null(journal.LatestPath);
    }

    [Fact]
    public async Task MemoryJournals_DoNotShareReplayState()
    {
        var left = CoachJournal.Memory();
        var right = CoachJournal.Memory();
        var action = new CoachDecision(CoachActionKind.Craft, "craft:goal", "목표 제작", "", "관측", "", "");
        Assert.True(await left.RecordAsync(Frame(1, 1, ("material", 1)), action));
        Assert.True(left.HasLatest);
        Assert.False(right.HasLatest);
        Assert.Empty(CoachReview.Read(right).Entries);
        Assert.Equal("Craft", Assert.Single(CoachReview.Read(left).Entries).Kind);
        Assert.Null(right.LatestPath);
    }

    [Fact]
    public async Task MemoryJournal_ConcurrentFlushDoesNotDropFinishedReadback()
    {
        var journal = CoachJournal.Memory();
        var finished = new CoachDecision(CoachActionKind.Finished, "end", "종료", "", "관측", "", "");
        var record = journal.RecordAsync(Frame(1, 1) with { Outcome = "fail" }, finished);
        var flushes = Enumerable.Range(0, 8).Select(_ => journal.FlushAsync());
        await Task.WhenAll(flushes.Append(record));
        Assert.True(await record);
        await journal.FlushAsync();
        var review = CoachReview.Read(journal);
        Assert.Equal("fail", review.Outcome);
        Assert.False(review.Incomplete);
        Assert.Equal("Finished", Assert.Single(review.Entries).Kind);
        Assert.Null(journal.LatestPath);
    }

    [Fact]
    public async Task JournalKeepsObservedChangesSeparateFromUnprovenPlayerInputs()
    {
        var directory = Directory.CreateTempSubdirectory("orand-coach-");
        try
        {
            var journal = new CoachJournal(directory.FullName);
            var action = new CoachDecision(CoachActionKind.Craft, "craft:goal", "", "", "", "", "")
                { TargetUnitId = "goal" };
            var frame = Frame(1, 1, ("material", 1));
            Assert.True(await journal.RecordAsync(frame, action));
            Assert.False(await journal.RecordAsync(frame with { Revision = 2 }, action));
            Assert.True(await journal.RecordAsync(frame with { Revision = 3, IsCurrent = false },
                action with { Kind = CoachActionKind.Recognition, Id = "recognition", TargetUnitId = null }));
            Assert.True(await journal.RecordAsync(Frame(1, 4, ("goal", 1)), action));
            Assert.False(await journal.RecordAsync(frame, action));
            var rows = File.ReadAllLines(journal.LatestPath!).Select(line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Equal(3, rows.Length);
                Assert.Equal("goal", rows[2].RootElement.GetProperty("AcquiredRecommendedUnit").GetString());
                Assert.Equal(2, rows[2].RootElement.GetProperty("ObservedInventoryDelta").GetArrayLength());
                Assert.Equal("inventory-increase-not-input-confirmation",
                    rows[2].RootElement.GetProperty("Evidence").GetString());
                var review = CoachReview.Read(journal.LatestPath!);
                Assert.False(review.Incomplete);
                Assert.Equal(20, review.LastRound);
                Assert.Equal("goal", review.Entries.Last().AcquiredUnit);
                Assert.True(await journal.RecordAsync(Frame(2, 1), action));
                Assert.Equal(2, Directory.GetFiles(directory.FullName, "*.jsonl").Length);
            }
            finally { foreach (var row in rows) row.Dispose(); }
        }
        finally { directory.Delete(true); }
    }

    internal static CoachFrame Frame(long generation, long revision, params (string Id, int Count)[] units) => new()
    {
        MatchGeneration = generation, Revision = revision, Round = 20, CompletedStoryStage = 9,
        IsCurrent = true, Difficulty = "악몽",
        Inventory = units.ToImmutableDictionary(unit => unit.Id, unit => unit.Count)
    };
}
