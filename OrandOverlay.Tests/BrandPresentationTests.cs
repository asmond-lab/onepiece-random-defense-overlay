using System.Xml.Linq;
using Xunit;

namespace OrandOverlay.Tests;

public class BrandPresentationTests
{
    private static string Source(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MainWindow.xaml"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, name));
    }

    [Fact]
    public void CompiledBamlAndCoachIlContainBrandWithoutLaunchingWindows()
    {
        var assembly = typeof(OrandOverlay.MainWindow).Assembly;
        using var stream = assembly.GetManifestResourceStream("OrandOverlay.g.resources");
        Assert.NotNull(stream);
        using var resources = new System.Resources.ResourceReader(stream!);
        var baml = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry entry in resources)
        {
            if (entry.Value is not Stream value) continue;
            using var bytes = new MemoryStream();
            value.CopyTo(bytes);
            baml[(string)entry.Key] = System.Text.Encoding.UTF8.GetString(bytes.ToArray());
        }
        Assert.Contains(nameof(RandyPickBrand), baml["mainwindow.baml"]);
        Assert.Contains(nameof(RandyPickBrand.ProductLabel), baml["mainwindow.baml"]);
        Assert.Contains(nameof(RandyPickBrand.BetaLabel), baml["mainwindow.baml"]);
        Assert.Contains(nameof(RandyPickBrand.BetaLabel), baml["overlaywindow.baml"]);
        Assert.Contains(nameof(RandyPickBrand.ProductLabel), baml["statsoverlaywindow.baml"]);
        Assert.True(baml.ContainsKey("assets/randypick-logo-64.png"), "Confirmed logo must be compiled into WPF resources");
        var overlayTitle = (string?)XDocument.Parse(Source("OverlayWindow.xaml")).Root!.Attribute("Title");
        Assert.NotNull(overlayTitle);
        Assert.StartsWith("랜디픽", overlayTitle);
        Assert.Contains(overlayTitle, baml["overlaywindow.baml"]);
        var method = typeof(OrandOverlay.OverlayWindow).GetMethod("RenderCoach")!;
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        var strings = new List<string>();
        for (var i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] != 0x72) continue;
            try { strings.Add(method.Module.ResolveString(BitConverter.ToInt32(il, i + 1))); }
            catch (ArgumentException) { }
        }
        Assert.Contains("랜디픽", strings);
        Assert.DoesNotContain("원랜디 코치", strings);
    }

    [Fact]
    public void MainAndCoachUseConfirmedBrandWithoutGrowingOverlay()
    {
        var main = XDocument.Parse(Source("MainWindow.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        Assert.Equal("{x:Static local:RandyPickBrand.ProductLabel}", (string?)main.Root!.Attribute("Title"));
        Assert.Contains(main.Descendants(wpf + "TextBlock"), x => (string?)x.Attribute("Text") == "랜디픽");
        Assert.Contains(main.Descendants(wpf + "TextBlock"), x => (string?)x.Attribute("Text") == "{x:Static local:RandyPickBrand.BetaLabel}");
        Assert.Contains(main.Descendants(wpf + "Image"), x => (string?)x.Attribute("Source") == "Assets/randypick-logo-64.png");
        var overlay = XDocument.Parse(Source("OverlayWindow.xaml"));
        Assert.StartsWith("랜디픽", (string?)overlay.Root!.Attribute("Title"));
        Assert.Equal("540", (string?)overlay.Root.Attribute("Width"));
        Assert.Equal("740", (string?)overlay.Root.Attribute("Height"));
        Assert.Contains(overlay.Descendants(wpf + "TextBlock"), x => (string?)x.Attribute("Text") == "{x:Static local:RandyPickBrand.BetaLabel}");
        var stats = XDocument.Parse(Source("StatsOverlayWindow.xaml"));
        Assert.Contains(stats.Descendants(wpf + "TextBlock"), x => (string?)x.Attribute("Text") == "{x:Static local:RandyPickBrand.ProductLabel}");
        Assert.Contains("GoalText.Text = \"랜디픽\";", Source("OverlayWindow.Coach.cs"));
        Assert.DoesNotContain("원랜디 코치", Source("MainWindow.xaml") + Source("OverlayWindow.Coach.cs"));
        Assert.Contains("<AssemblyName>OrandOverlay</AssemblyName>", Source("OrandOverlay.csproj"));
        Assert.Contains("<ApplicationIcon>Assets\\app.ico</ApplicationIcon>", Source("OrandOverlay.csproj"));
    }

    [Fact]
    public void BrandLabelsMatchTheActualCompiledVersionCore()
    {
        var version = typeof(RandyPickBrand).Assembly.GetName().Version!;
        var expectedCore = $"{version.Major}.{version.Minor}.{version.Build}";
        Assert.Equal(version, UpdateService.CurrentVersion);
        Assert.Equal($"BETA {expectedCore}", RandyPickBrand.BetaLabel);
        Assert.Equal($"랜디픽 BETA {expectedCore}", RandyPickBrand.ProductLabel);
        Assert.StartsWith(expectedCore + "-test.", UpdateService.CurrentBuildVersion);
        Assert.DoesNotContain("-test.", RandyPickBrand.BetaLabel);
    }

    [Fact]
    public void PublishedBrandKeepsInternalAssemblyAndSingleFileScope()
    {
        var project = XDocument.Parse(Source("OrandOverlay.csproj"));
        Assert.Equal("OrandOverlay", project.Descendants("AssemblyName").Single().Value);
        Assert.Equal("OrandOverlay", project.Descendants("RootNamespace").Single().Value);
        Assert.Equal("1.0.21", project.Descendants("Version").Single().Value);
        Assert.Equal("1.0.21-test.1", UpdateService.CurrentBuildVersion);
        Assert.True(UpdateChannelVersion.TryParse(UpdateService.CurrentBuildVersion, out var candidate));
        Assert.True(UpdateChannelVersion.TryParse("1.0.14-test.1", out var published));
        Assert.True(UpdateChannelVersion.TryParse("1.0.22-test.1", out var next));
        Assert.True(candidate.IsUpgradeFrom(published));
        Assert.True(next.IsUpgradeFrom(candidate));
        var target = project.Descendants("Target").Single(x => (string?)x.Attribute("Name") == "PublishRandyPickExecutable");
        Assert.Equal("Publish", (string?)target.Attribute("AfterTargets"));
        Assert.Contains("'$(RuntimeIdentifier)' == 'win-x64'", (string?)target.Attribute("Condition"));
        Assert.Contains("'$(PublishSingleFile)' == 'true'", (string?)target.Attribute("Condition"));
        Assert.EndsWith("RandyPick.exe", target.Descendants("_RandyPickPublishedExe").Single().Value);
        Assert.Equal("false", (string?)target.Element("Copy")!.Attribute("SkipUnchangedFiles"));
        Assert.Equal("Exists('$(_RandyPickPublishedExe)')", (string?)target.Element("Delete")!.Attribute("Condition"));
        var operations = target.Elements().Select(x => x.Name.LocalName).ToArray();
        Assert.Equal(new[] { "PropertyGroup", "Error", "Copy", "Error", "Delete" }, operations);
        Assert.Equal("!Exists('$(_RandyPickPublishedExe)')", (string?)target.Elements("Error").Last().Attribute("Condition"));
    }

    [Theory]
    [InlineData("1.0.3-test.1", "BETA 1.0.3")]
    [InlineData("1.0.2-test.14.recognition.3", "BETA 1.0.2")]
    [InlineData("1.0.4", "BETA 1.0.4")]
    [InlineData("not a version", "새 버전")]
    [InlineData(null, "새 버전")]
    public void UpdateNoticesUseNumericLabelsWithoutEchoingInternalIdentity(string? identity, string expected)
    {
        Assert.Equal(expected, RandyPickBrand.UpdateLabel(identity));
    }

}
