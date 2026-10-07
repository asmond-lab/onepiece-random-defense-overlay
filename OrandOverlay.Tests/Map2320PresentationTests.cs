using System.Xml.Linq;
using Xunit;

namespace OrandOverlay.Tests;

// Source/BAML checks only. Never construct MainWindow, load user settings or start a live reader.
public sealed class Map2320PresentationTests
{
    private static string Source(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MainWindow.xaml"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, name));
    }

    [Fact]
    public void CatalogTreats321AsCompatibleModernOrdrDataset()
    {
        var catalog = new DataCatalog();
        catalog.Load(mapVersion: "2.321");
        Assert.Equal("2.321", catalog.MapVersion);
        Assert.NotNull(catalog.OfflineBundle);
        Assert.True(Map2320DataBundle.IsCompatible("2.321"));
        Assert.Equal(Map2320DataBundle.Version, catalog.OfflineBundle!.Source.MapVersion);
        Assert.Equal(Map2320DataBundle.Version, catalog.OfflineBundle.Recipes.Document.MapVersion);
        Assert.Null(catalog.Bundle2322);
        Assert.True(MapDatasetRuntimePolicy.AllowsReferenceObservation("2.321",
            DiagnosticInventoryObservation.PinnedDatasetFingerprint,
            new MemoryProfile { Layout = Warcraft300Diagnostic.LayoutName, FileVersion = Warcraft300Diagnostic.Version, Sha256 = Warcraft300Diagnostic.Hash },
            Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash));
        Assert.Contains(catalog.MapVersion, MainWindow.VersionLabel(catalog, ""));
    }
    [Fact]
    public void VersionSupportIsVisibleWrappingAndCannotOverlapWorkspace()
    {
        XNamespace w = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var doc = XDocument.Parse(Source("MainWindow.xaml"));
        var text = Assert.Single(doc.Descendants(w + "TextBlock").Where(e => (string?)e.Attribute(x + "Name") == "DataVersionText"));
        Assert.Equal("Visible", (string?)text.Attribute("Visibility"));
        Assert.Equal("Wrap", (string?)text.Attribute("TextWrapping"));
        Assert.Equal("2", (string?)text.Attribute("Grid.Row"));
        Assert.Null(text.Attribute("Text")); // Parent owns accurate startup/refresh version text.
        var rows = text.Parent!.Element(w + "Grid.RowDefinitions")!.Elements().ToArray();
        Assert.Equal(3, rows.Length);
        Assert.All(rows, r => Assert.Equal("Auto", (string?)r.Attribute("Height")));
        var button = Assert.Single(doc.Descendants(w + "Button").Where(e => (string?)e.Attribute("Content") == "2.320 변경점"));
        Assert.Equal("Map2320Patch_OnClick", (string?)button.Attribute("Click"));
    }

    [Fact]
    public void DialogContainsSourceConflictsAndDoesNotOfferRuntimeActions()
    {
        XNamespace w = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var doc = XDocument.Parse(Source("Map2320PatchWindow.xaml"));
        var text = string.Join("\n", doc.Descendants(w + "TextBlock").Select(e => (string?)e.Attribute("Text")));
        foreach (var expected in new[] { "50%", "48%", "10%", "20%", "15초", "2.314", "2.320" })
            Assert.Contains(expected, text);
        var evidence = Assert.Single(doc.Descendants(w + "Expander"));
        Assert.Equal("False", (string?)evidence.Attribute("IsExpanded"));
        Assert.NotNull(evidence.Attribute("Header"));
        Assert.True(evidence.Descendants(w + "TextBlock").Count() >= 6);
        Assert.Empty(doc.Descendants(w + "TextBox"));
        Assert.Empty(doc.Descendants(w + "CheckBox"));
        var close = Assert.Single(doc.Descendants(w + "Button"));
        Assert.Equal("True", (string?)close.Attribute("IsCancel"));
        Assert.Equal("닫기", (string?)close.Attribute("Content"));
        Assert.Single(doc.Descendants(w + "ScrollViewer"));
        var handler = Source("MainWindow.Map2320.cs") + Source("Map2320PatchWindow.xaml.cs");
        Assert.Contains("Owner = this", handler);
        Assert.Contains("ShowDialog()", handler);
        foreach (var forbidden in new[] { "Process.Start", "ReadOnlyProcessMemory", "RecognizeAsync", "SettingsService", "DataCatalog.Load", "RefreshProfiles", "_execution" })
            Assert.DoesNotContain(forbidden, handler);
    }

    [Fact]
    public void CompiledDialogResourceExistsWithoutLaunchingApp()
    {
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("OrandOverlay.g.resources");
        Assert.NotNull(stream);
        using var reader = new System.Resources.ResourceReader(stream!);
        Assert.Contains(reader.Cast<System.Collections.DictionaryEntry>(), entry => (string)entry.Key == "map2320patchwindow.baml");
    }
}
