using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace OrandOverlay;

internal static class AdaptivePlanningCoordinatorFingerprint
{
    public static string Create(AdaptivePlanningRefreshInput input)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        Write(writer, input.CanonicalInput.CanonicalBytes);
        Write(writer, input.BuildSnapshot);
        Write(writer, input.NavigationRequest);
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void Write(BinaryWriter writer, AdaptiveBuildSnapshot value)
    {
        writer.Write(value.MatchGeneration); writer.Write(value.Round);
        Write(writer, value.ActiveStoryStage); writer.Write(value.FirstRareObserved);
        Write(writer, value.SpecialUncommonWispCount); Write(writer, value.RareWispCount);
        writer.Write(value.LegendCandidates.Length);
        foreach (var candidate in value.LegendCandidates.OrderBy(item => item.UnitId, StringComparer.Ordinal))
        {
            Write(writer, candidate.UnitId); writer.Write(candidate.CardAllocationComplete);
            writer.Write((int)candidate.Resources); writer.Write(candidate.MissingLeaves);
            writer.Write(candidate.PreservedRouteCount);
        }
        Write(writer, value.NewlyObservedLegendIds);
        writer.Write(value.RouteCandidate is not null);
        if (value.RouteCandidate is { } route)
        {
            writer.Write((int)route.Lane); Write(writer, route.GoalUnitId);
            Write(writer, route.PackageId); writer.Write(route.ProgressBp);
            writer.Write(route.RobustAcrossFirstLegendAssignments);
        }
        writer.Write(value.NavigationCandidate is not null);
        if (value.NavigationCandidate is { } navigation)
        {
            Write(writer, navigation.OptionId); writer.Write(navigation.IsRobust);
        }
        writer.Write(value.ManualLatches.GoalOverride);
        writer.Write(value.ManualLatches.NavigationOverride);
        writer.Write(value.IsTransient); writer.Write(value.IsConfirmedReset);
    }

    private static void Write(BinaryWriter writer, NavigationIntervalScoringRequest value)
    {
        writer.Write(value.Round); writer.Write(value.BeforeBuildBp);
        writer.Write(value.BeforeCoreBp); writer.Write(value.BeforeCombatBp);
        writer.Write(value.RouteConfidenceBp); writer.Write(value.MechanicsConfidenceBp);
        writer.Write((int)value.InputState); writer.Write(value.ManualNavigationOverride);
        WriteNullable(writer, value.ManualOverlayOptionId);
        WriteNullable(writer, value.CurrentOverlayRecommendationId);
        WriteNullable(writer, value.LockedOverlayRecommendationId);
        Write(writer, value.ForcedRound24OptionId);
        writer.Write(value.MaxCoupledScenarios); writer.Write(value.MaxOutcomeStates);
        writer.Write(value.MaxRationalBits);
        var options = value.Options.IsDefault ? [] : value.Options;
        writer.Write(options.Length);
        foreach (var option in options.OrderBy(item => item.OrdinalId).ThenBy(item => item.OptionId,
                     StringComparer.Ordinal))
            Write(writer, option);
    }

    private static void Write(BinaryWriter writer, NavigationIntervalOptionInput value)
    {
        Write(writer, value.OptionId); writer.Write(value.OrdinalId);
        writer.Write(value.TopCompatible); writer.Write((int)value.Posture);
        Write(writer, value.ConfidenceFactorsBp.IsDefault ? [] : value.ConfidenceFactorsBp);
        Write(writer, value.MissingSignalIds.IsDefault ? [] : value.MissingSignalIds);
        Write(writer, value.SourceReasonIds.IsDefault ? [] : value.SourceReasonIds);
        var scenarios = value.CoupledScenarios.IsDefault ? [] : value.CoupledScenarios;
        writer.Write(scenarios.Length);
        foreach (var scenario in scenarios.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            Write(writer, scenario.Id); writer.Write(scenario.Outcomes.Length);
            foreach (var outcome in scenario.Outcomes)
            {
                Write(writer, outcome.Probability.Numerator.ToString());
                Write(writer, outcome.Probability.Denominator.ToString());
                writer.Write(outcome.AfterBuildBp); writer.Write(outcome.AfterCoreBp);
                writer.Write(outcome.AfterCombatBp);
            }
        }
    }

    private static void Write(BinaryWriter writer, ImmutableArray<byte> values)
    {
        writer.Write(values.Length); writer.Write(values.AsSpan());
    }

    private static void Write(BinaryWriter writer, ImmutableArray<int> values)
    {
        writer.Write(values.Length); foreach (var value in values) writer.Write(value);
    }

    private static void Write(BinaryWriter writer, ImmutableArray<string> values)
    {
        writer.Write(values.Length);
        foreach (var value in values.Order(StringComparer.Ordinal)) Write(writer, value);
    }

    private static void Write(BinaryWriter writer, int? value)
    {
        writer.Write(value.HasValue); if (value.HasValue) writer.Write(value.Value);
    }

    private static void WriteNullable(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null); if (value is not null) Write(writer, value);
    }

    private static void Write(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length); writer.Write(bytes);
    }
}
