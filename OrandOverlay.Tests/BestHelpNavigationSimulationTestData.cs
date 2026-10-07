using System.Collections.Immutable;

namespace OrandOverlay.Tests;

internal static class BestHelpNavigationSimulationTestData
{
    internal static NavigationMechanicsProfile Profile() =>
        NavigationMechanicsProfileLoader.LoadFromDirectory(
            Path.Combine(ProjectDirectory(), "Data"));

    internal static ImmutableArray<BestHelpSpellScenario> SpellScenarios(
        int busterTargets = 1,
        long poisonLevel2Damage = 0) =>
        [
            new() { SpellId = "A0BZ", TargetCount = busterTargets },
            new()
            {
                SpellId = "A0BY", TargetCount = 1,
                Level1DamagePerTarget = 0, Level2DamagePerTarget = 0
            },
            new()
            {
                SpellId = "A07T", TargetCount = 1,
                Level1DamagePerTarget = 0, Level2DamagePerTarget = 0,
                Level1ControlTargetSeconds = 2,
                Level2ControlTargetSeconds = 3
            },
            new() { SpellId = "A07W", TargetCount = 0 },
            new()
            {
                SpellId = "A07X", TargetCount = 1,
                Level1DamagePerTarget = 0,
                Level2DamagePerTarget = poisonLevel2Damage
            }
        ];

    internal static string ProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "OrandOverlay.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate the OrandOverlay project directory.");
    }
}
