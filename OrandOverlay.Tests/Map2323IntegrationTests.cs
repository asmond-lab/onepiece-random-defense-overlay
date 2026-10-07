using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2323IntegrationTests
{
[Fact(Skip = "Requires local ORDR map source checkout")]
    public void SelectedBundleUsesIndependentArchiveAndCompleteVersionedConsumers()
    {
        var old = Map2322DataBundle.LoadBundled();
        var current = Map2322DataBundle.LoadBundled("2.323");
        Assert.NotEqual(old.Fingerprint, current.Fingerprint);
        Assert.Equal("2.323", current.MapVersion);
        Assert.Equal(Map2323SourceContract.ArchiveSha256, current.Source.Archive.Sha256);
        Assert.Equal(Map2323SourceContract.JassSha256, current.Source.JassSha256);
        Assert.Equal(265, current.Recipes.ProjectAll().Count);
        Assert.Equal(old.Recipes.ProjectAll().Keys.Order(), current.Recipes.ProjectAll().Keys.Order());
        Assert.Equal(81, current.Commands.Count);
        Assert.Equal(156, current.Hotkeys.Count);
        Assert.Equal(14, current.Story.Stages.Count);
        Assert.Equal(15, current.Navigation.Options.Count);
        Assert.Equal(7, current.Navigation.Exchanges.Count);
        Assert.Equal(1, current.Navigation.DoubleWispFailureGrant);
        Assert.Equal(2, current.Navigation.SelectionWispsForDoubleWispRoll(35));
        Assert.Equal(1, current.Navigation.SelectionWispsForDoubleWispRoll(36));
        Assert.Null(old.Navigation.SelectionWispsForDoubleWispRoll(36));
        Assert.True(current.Story.AdditionalBerryRequiresUnobservedCondition);
        Assert.Null(current.Story.PoneglyphImmediateBerry(true));
        Assert.Null(current.Story.TreasureBerryForRoll(25));
        Assert.Equal(0, current.Story.TreasureBerryForRoll(26));
        Assert.Equal(1, old.Story.PoneglyphImmediateBerry(true));
        var growth = Map2320GrowthSource.LoadBundled("2.323");
        Assert.Equal(3925, growth.Globals.Count);
        Assert.Equal(12, growth.Globals[growth.GrowthName]);
        Assert.Equal(("kU", "Av", "Ov", "hp"), (growth.GrowthName, growth.PreviousName, growth.NextName, growth.TimerName));
        var activity = Map2321ActivityRules.LoadBundled("2.323");
        Assert.Equal(18, activity.IntegerArrays.Count);
        Assert.Equal(190, activity.Recipes.Length);
        Assert.All(activity.IntegerArrays, name => Assert.Equal(9, growth.Globals[name]));
    }

[Fact(Skip = "Requires local ORDR map source checkout")]
    public void ActualScriptAndAllExtractedMemberBytesMatch2323PinnedProvenance()
    {
        var extracted = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "artifacts", "ordr-2323", "map-audit", "extracted"));
        using var receipt = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(extracted, "extraction-manifest.json")));
        Assert.Equal(Map2323SourceContract.ArchiveLengthBytes, receipt.RootElement.GetProperty("archiveBytes").GetInt64());
        Assert.Equal(Map2323SourceContract.ArchiveSha256, receipt.RootElement.GetProperty("archiveSha256").GetString());
        foreach (var member in receipt.RootElement.GetProperty("members").EnumerateArray())
        {
            var bytes = File.ReadAllBytes(Path.Combine(extracted, member.GetProperty("name").GetString()!));
            Assert.Equal(member.GetProperty("bytes").GetInt64(), bytes.LongLength);
            Assert.Equal(member.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        var script = File.ReadAllBytes(Path.Combine(extracted, "war3map.j"));
        Assert.Equal(Map2323SourceContract.JassSha256, Convert.ToHexString(SHA256.HashData(script)).ToLowerInvariant());
        var lines = Encoding.UTF8.GetString(script).Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        using var recipes = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "map-recipes-2323.json")));
        foreach (var row in recipes.RootElement.GetProperty("recipes").EnumerateArray())
        {
            var pin = row.GetProperty("source");
            var excerpt = string.Join("\n", lines.Skip(pin.GetProperty("startLine").GetInt32() - 1)
                .Take(pin.GetProperty("endLine").GetInt32() - pin.GetProperty("startLine").GetInt32() + 1));
            Assert.Equal(pin.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(excerpt))).ToLowerInvariant());
        }
        using var nav = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "navigation-mechanics-2323.json")));
        // Every emitted 2.323 source span must point into the actual new script, not
        // retain an old line while carrying a new function body and top-level SHA.
        void CheckPins(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("startLine", out var first) &&
                    element.TryGetProperty("endLine", out var last) &&
                    element.TryGetProperty("sha256", out var digest))
                {
                    var excerpt = string.Join("\n", lines.Skip(first.GetInt32() - 1).Take(last.GetInt32() - first.GetInt32() + 1));
                    Assert.Equal(digest.GetString(), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(excerpt))).ToLowerInvariant());
                }
                if (element.TryGetProperty("rawSource", out var raw) && element.TryGetProperty("source", out var source) &&
                    source.TryGetProperty("startLine", out var start) && source.TryGetProperty("endLine", out var end))
                    Assert.Equal(raw.GetString(), string.Join("\n", lines.Skip(start.GetInt32() - 1).Take(end.GetInt32() - start.GetInt32() + 1)));
                foreach (var property in element.EnumerateObject())
                    if (property.Name != "previousSource") CheckPins(property.Value);
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var child in element.EnumerateArray()) CheckPins(child);
        }
        foreach (var member in Map2322DataBundle.Expected2323Members.Where(x => x.File.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", member.File)));
            CheckPins(document.RootElement);
        }
        var branch = nav.RootElement.GetProperty("doubleWispExchange");
        Assert.Equal(1, branch.GetProperty("failureSelectionWisps").GetInt32());
        Assert.Contains("else\nset r5s=1", string.Join("\n", lines.Skip(branch.GetProperty("source").GetProperty("startLine").GetInt32() - 1)
            .Take(branch.GetProperty("source").GetProperty("endLine").GetInt32() - branch.GetProperty("source").GetProperty("startLine").GetInt32() + 1)));
    }

    public static IEnumerable<object[]> DatasetFiles() => Map2322DataBundle.Expected2323Members.Select(entry => new object[] { entry.File });

    [Theory]
    [MemberData(nameof(DatasetFiles))]
    public void Every2323BundleMemberTamperIsRejected(string member)
    {
        var directory = Path.Combine(Path.GetTempPath(), "map2323-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var name in Map2322DataBundle.Expected2323Members.Select(entry => entry.File).Append("map-source-manifest-2323.json"))
                File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", name), Path.Combine(directory, name));
            File.AppendAllText(Path.Combine(directory, member), "x", Encoding.UTF8);
            Assert.Throws<InvalidDataException>(() => Map2322DataBundle.LoadFromDirectory(directory, "2.323"));
            var manifest = Path.Combine(directory, "map-source-manifest-2323.json");
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"MapVersion\": \"2.323\"", "\"MapVersion\": \"2.322\"", StringComparison.Ordinal));
            Assert.Throws<InvalidDataException>(() => Map2322DataBundle.LoadFromDirectory(directory, "2.323"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void SelectionKeepsUnknownReadinessAndHistoricalDatasets()
    {
        var current = new DataCatalog(); current.Load(loadCarryPolicy: false, mapVersion: "2.323");
        Assert.Null(current.Bundle2322);
        Assert.NotNull(current.Bundle2323);
        Assert.Equal(current.Bundle2323!.Fingerprint, current.SelectedDatasetFingerprint);
        Assert.Equal("2.323", MainWindow.LoadApplicationStoryProfile(current).Source.MapVersion);
        Assert.Equal(15, MapNavigationCatalog.Options(current.Bundle2323).Count);
        Assert.Contains("2.323", MapNavigationCatalog.Options(current.Bundle2323)[0].Summary);
        Assert.Contains("KING", current.Unit("rawcode:2C0h").RecipeConditions!.UnresolvedSourceConditions);
        var plan = new NormalCraftPlanner(current).Build("rawcode:2C0h", new Dictionary<string, int>());
        Assert.Null(Assert.Single(plan.Steps).CombineKey);
        Assert.False(MapDatasetRuntimePolicy.AllowsReader("2.323", true, true));
        var match = new RuntimeMapIdentityResult(RuntimeMapIdentityState.Proven, RuntimeMapIdentityFailure.None,
            "", null, "", "", Map2323SourceContract.ArchiveLengthBytes,
            Map2323SourceContract.ArchiveSha256, 0, 0, 0, 0);
        Assert.True(MapDatasetRuntimePolicy.AllowsObservedArchive("2.323", match));
        Assert.False(MapDatasetRuntimePolicy.AllowsObservedArchive("2.323", match with { ActualArchiveSha256 = Map2322SourceContract.ArchiveSha256 }));
        Assert.False(MapDatasetRuntimePolicy.AllowsObservedArchive("2.322", match));
        Assert.False(MapDatasetRuntimePolicy.AllowsObservedArchive("2.323", null));
        var old = new DataCatalog(); old.Load(loadCarryPolicy: false, mapVersion: "2.322");
        Assert.Null(old.Bundle2323);
        Assert.Equal(Map2322SourceContract.ArchiveSha256, old.Bundle2322!.Source.Archive.Sha256);
        var now = new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
        var basic = DiagnosticBasicInventoryObservation.Create(current, Warcraft300Diagnostic.Version,
            Warcraft300Diagnostic.Hash, new string('A',64), 1,0,now.AddMilliseconds(-100),now,
            TimeSpan.FromMilliseconds(100), [], new string('B',64),new string('C',64));
        Assert.Equal(DiagnosticInventoryAvailability.Ready, basic.Availability);
        Assert.Equal(Map2323SourceContract.JassSha256,basic.MapSourceFingerprint);
        Assert.False(basic.GameplayReady);
        var full = DiagnosticInventoryObservation.Create(current, Warcraft300Diagnostic.Version,
            Warcraft300Diagnostic.Hash,new string('A',64),2,0,now.AddMilliseconds(-100),now,
            TimeSpan.FromMilliseconds(100),[],[]);
        Assert.Equal(DiagnosticInventoryAvailability.Ready,full.Availability);
        Assert.False(full.GameplayReady);
        Assert.True(DiagnosticInventoryConsumerPolicy.CanPresent(full,now,"2.323",current.SelectedDatasetFingerprint,new string('A',64),2,0));
        Assert.False(DiagnosticInventoryConsumerPolicy.CanPresent(full,now,"2.323",old.SelectedDatasetFingerprint,new string('A',64),2,0));
        Assert.False(DiagnosticInventoryConsumerPolicy.CanPresent(full,now.AddSeconds(3),"2.323",current.SelectedDatasetFingerprint,new string('A',64),2,0));
    }
}
