using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2322NavigationStoryWiringTests
{
    [Fact]
    public void ApplicationStoryUsesSelected2322SourceAndOnlyGuaranteedBasePayouts()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false, mapVersion: "2.322");
        var profile = MainWindow.LoadApplicationStoryProfile(catalog);
        var source = catalog.Bundle2322!;
        Assert.Equal("2.322", profile.Source.MapVersion);
        Assert.Equal(Map2322SourceContract.ArchiveSha256, profile.Source.Archive.Sha256);
        Assert.Equal(Map2322SourceContract.JassSha256, profile.Source.JassSha256);
        Assert.Equal(14, profile.Stages.Length);
        Assert.Equal(source.Story.Stages.Select(stage => stage.ObjectiveRawcode), profile.Stages.Select(stage => stage.ObjectiveRawcode));
        foreach (var (stage, raw) in profile.Stages.Zip(source.Story.Stages))
        {
            Assert.Equal(5, stage.OwnerId);
            Assert.Same(stage, profile.ResolveActiveStage(raw.ObjectiveRawcode, 5));
            Assert.Equal(raw.BaseGold, Assert.Single(stage.RewardComponents.EveryPlayerBase, reward => reward.Kind == "Gold").Amount);
            Assert.Equal(raw.BaseLumber, stage.RewardComponents.EveryPlayerBase.Where(reward => reward.Kind == "Lumber").Sum(reward => reward.Amount));
            Assert.Equal(raw.BaseUnits.Select(unit => (unit.Id, (long)unit.Count)),
                stage.RewardComponents.EveryPlayerBase.Where(reward => reward.Kind == "Unit").Select(reward => (reward.Id, reward.Amount)));
            Assert.All(stage.RewardComponents.EveryPlayerBase, reward =>
            {
                var pin = Assert.Single(reward.Sources);
                Assert.Equal(raw.CompletionFunction, pin.Function);
                Assert.InRange(pin.EvidenceLine, raw.SourceStartLine, raw.SourceEndLine);
                Assert.Equal("ActivePlayer", reward.Condition);
            });
            Assert.Empty(stage.RewardComponents.ContributionAtLeast25Percent);
            Assert.Empty(stage.RewardComponents.Mvp);
            Assert.Empty(stage.RewardComponents.HiddenOrSideEffect);
        }
        Assert.Contains(profile.Stages[9].RewardComponents.EveryPlayerBase, reward => reward.Id == "e01A" && reward.Amount == 1);
        Assert.DoesNotContain(profile.Stages.SelectMany(stage => stage.RewardComponents.EveryPlayerBase), reward => reward.Id == "e0IA");
    }

    [Fact]
    public void ApplicationNavigationNeverFallsBackToHistoricalProfiles()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false, mapVersion: "2.322");
        var all = MapNavigationCatalog.Options(catalog.Bundle2322!);
        Assert.Equal(15, all.Count);
        Assert.Equal(catalog.Bundle2322!.Navigation.Options.Select(option => option.Id), all.Select(option => option.Id));
        foreach (var category in NavigationProfiles.Categories)
        {
            var menu = MainWindow.VersionNavigationsForCategory(catalog, category.Id);
            Assert.Equal("Unselected", menu[0].Id);
            Assert.Equal(category.Id, menu[0].CategoryId);
            Assert.Equal(all.Where(option => option.CategoryId == category.Id), menu.Skip(1));
            foreach (var option in menu.Skip(1))
                Assert.Equal(option, MainWindow.ResolveVersionNavigation(catalog, option.Id));
        }
        Assert.Equal("Unselected", MainWindow.ResolveVersionNavigation(catalog, "Gambler.ContinuousBetting").Id);
        Assert.Equal("Unselected", MainWindow.ResolveVersionNavigation(catalog, null).Id);
    }
}
