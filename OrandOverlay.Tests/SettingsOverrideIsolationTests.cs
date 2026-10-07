using System.IO;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class SettingsOverrideIsolationTests
{
    [Fact]
    public void MainWindowNeverPersistsInjectedScenarioSettings()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MainWindow.xaml.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        foreach (var path in Directory.GetFiles(root!.FullName, "MainWindow*.cs"))
            foreach (var line in File.ReadLines(path).Where(line => line.Contains("SettingsStore.Save(_settings)")))
                Assert.Contains("_persistSettings", line);
    }
}
