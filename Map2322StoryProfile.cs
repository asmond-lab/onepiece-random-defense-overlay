using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OrandOverlay;

public sealed record Map2322StageUnit(string Id, int Count);
public sealed record Map2322DirectPayoutOperation(string Kind, int SourceLine, string Statement, string Branch);
public sealed record Map2322StoryStage(int Ordinal, string ObjectiveRawcode, string CompletionFunction, int BaseGold,
    int BaseLumber, IReadOnlyList<Map2322StageUnit> BaseUnits, int BaseSourceLine, int SourceStartLine, int SourceEndLine, string RawSource,
    IReadOnlyList<Map2322DirectPayoutOperation> DirectPayoutOperations);

public sealed class Map2322StoryProfile
{
    public IReadOnlyList<Map2322StoryStage> Stages { get; }
    public int BaseClearBerry { get; }
    public int NightmareClearAdditionalBerry { get; }
    public int NightmareZeroUnitClearAdditionalBerry { get; }
    public int AllThreeMissionsClearAdditionalBerry { get; }
    public int PoneglyphSuccessImmediateBerry { get; }
    public int TreasureChanceNumerator { get; }
    public int TreasureChanceDenominator { get; }
    public int TreasureSuccessAdditionalBerry { get; }
    public bool AdditionalBerryRequiresUnobservedCondition { get; }

    internal Map2322StoryProfile(byte[] bytes, string mapVersion = "2.322")
    {
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        Map2322DataBundle.CheckIdentity(root, mapVersion);
        if (root.GetProperty("sourceSha256").GetString() != (mapVersion == Map2323SourceContract.MapVersion ? Map2323SourceContract.JassSha256 : Map2322SourceContract.JassSha256))
            throw new InvalidDataException("Story source mismatch.");
        Stages = root.GetProperty("stages").EnumerateArray().Select(x => new Map2322StoryStage(
            x.GetProperty("ordinal").GetInt32(), x.GetProperty("objectiveRawcode").GetString()!,
            x.GetProperty("completionFunction").GetString()!, x.GetProperty("baseGold").GetInt32(),
            x.GetProperty("baseLumber").GetInt32(),
            x.GetProperty("baseUnits").EnumerateArray().Select(u => new Map2322StageUnit(u.GetProperty("id").GetString()!, u.GetProperty("count").GetInt32())).ToArray(),
            x.GetProperty("baseSourceLine").GetInt32(), x.GetProperty("source").GetProperty("startLine").GetInt32(),
            x.GetProperty("source").GetProperty("endLine").GetInt32(), x.GetProperty("rawSource").GetString()!,
            x.GetProperty("directPayoutOperations").EnumerateArray().Select(operation => new Map2322DirectPayoutOperation(
                operation.GetProperty("kind").GetString()!, operation.GetProperty("sourceLine").GetInt32(),
                operation.GetProperty("statement").GetString()!, operation.GetProperty("branch").GetString()!)).ToArray())).ToArray();
        if (Stages.Count != 14 || Stages.Where((stage,index) => stage.Ordinal != index+1 || stage.BaseGold <= 0 ||
            stage.BaseLumber < 0 || stage.DirectPayoutOperations.Count == 0 ||
            stage.SourceStartLine <= 0 || stage.BaseSourceLine < stage.SourceStartLine || stage.BaseSourceLine > stage.SourceEndLine ||
            !stage.RawSource.StartsWith("function " + stage.CompletionFunction + " takes", StringComparison.Ordinal)).Any())
            throw new InvalidDataException("Incomplete 2.322 story stages.");
        var berry = root.GetProperty("berry");
        BaseClearBerry = berry.GetProperty("baseClear").GetInt32();
        NightmareClearAdditionalBerry = berry.GetProperty("nightmareClearAdditional").GetInt32();
        NightmareZeroUnitClearAdditionalBerry = berry.GetProperty("nightmareZeroUnitClearAdditional").GetInt32();
        AllThreeMissionsClearAdditionalBerry = berry.GetProperty("allThreeMissionsClearAdditional").GetInt32();
        PoneglyphSuccessImmediateBerry = berry.GetProperty("poneglyphSuccessImmediate").GetInt32();
        TreasureChanceNumerator = berry.GetProperty("treasureChanceNumerator").GetInt32();
        TreasureChanceDenominator = berry.GetProperty("treasureChanceDenominator").GetInt32();
        TreasureSuccessAdditionalBerry = berry.GetProperty("treasureSuccessAdditional").GetInt32();
        AdditionalBerryRequiresUnobservedCondition = mapVersion == Map2323SourceContract.MapVersion &&
            berry.GetProperty("poneglyphSuccessImmediateConditional").GetString() == "not Ss; condition is not an observed player state" &&
            berry.GetProperty("treasureAdditionalConditional").GetString() == "not Ss; condition is not an observed player state";
        if (mapVersion == Map2323SourceContract.MapVersion && !AdditionalBerryRequiresUnobservedCondition)
            throw new InvalidDataException("Missing 2.323 conditional Berry provenance.");
        if (BaseClearBerry != 1 || NightmareClearAdditionalBerry != 1 || NightmareZeroUnitClearAdditionalBerry != 1 ||
            AllThreeMissionsClearAdditionalBerry != 1 || PoneglyphSuccessImmediateBerry != 1 || TreasureChanceNumerator != 25 ||
            TreasureChanceDenominator != 1000 || TreasureSuccessAdditionalBerry != 1)
            throw new InvalidDataException("Unapproved Berry projection.");
    }

    /// <summary>Only direct active-player base grants; other branches remain unknown, not inherited from 2.320.</summary>
    public ImmutableArray<StoryStage> ProjectGuaranteedBaseStages() => Stages.Select(stage =>
    {
        JassSourcePin Pin(int line) => new(stage.CompletionFunction, stage.SourceStartLine, stage.SourceEndLine, line);
        var operations = stage.DirectPayoutOperations.Where(operation => operation.Branch == "BaseActivePlayer").ToArray();
        var rewards = ImmutableArray.CreateBuilder<StoryRewardComponent>();
        if (stage.BaseGold > 0)
            rewards.Add(new("Gold", "PLAYER_STATE_RESOURCE_GOLD", stage.BaseGold, 0, 1, 1, "ActivePlayer",
                [Pin(operations.First(operation => operation.Kind == "Resource").SourceLine)]));
        if (stage.BaseLumber > 0)
            rewards.Add(new("Lumber", "PLAYER_STATE_RESOURCE_LUMBER", stage.BaseLumber, 0, 1, 1, "ActivePlayer",
                [Pin(operations.First(operation => operation.Kind == "Resource" && operation.Statement.Contains("PLAYER_STATE_RESOURCE_LUMBER", StringComparison.Ordinal)).SourceLine)]));
        var unitOperations = operations.Where(operation => operation.Kind == "Unit").ToArray();
        if (unitOperations.Length != stage.BaseUnits.Count)
            throw new InvalidDataException("Incomplete 2.322 base unit provenance.");
        for (var i = 0; i < unitOperations.Length; i++)
            rewards.Add(new("Unit", stage.BaseUnits[i].Id, stage.BaseUnits[i].Count, 0, 1, 1, "ActivePlayer",
                [Pin(unitOperations[i].SourceLine)]));
        // All fourteen objective units are spawned for Player(5) in the pinned 2.322 JASS.
        // Only the named base rewards are projected; no MVP/contribution entitlement is inferred.
        var kinds = rewards.Where(reward => reward.Kind == "Unit").Select(reward => reward.Id switch
        {
            "e016" => "special", "e017" => "uncommon", "e018" => "common_selectable",
            "e019" => "rare", "e01A" => "transcendence", "e0IX" => "random", _ => null
        }).Where(kind => kind is not null).Cast<string>().Distinct(StringComparer.Ordinal).ToImmutableArray();
        return new StoryStage(stage.Ordinal, stage.ObjectiveRawcode, 5,
            stage.Ordinal == 9 ? "Marineford" : "stage" + stage.Ordinal,
            kinds, new RewardComponentGroups(rewards.ToImmutable(), [], [], []));
    }).ToImmutableArray();

    public int ClearBerry(bool nightmare, bool zeroUnitCount, bool allThreeMissionsComplete) => BaseClearBerry +
        (nightmare ? NightmareClearAdditionalBerry : 0) + (nightmare && zeroUnitCount ? NightmareZeroUnitClearAdditionalBerry : 0) +
        (allThreeMissionsComplete ? AllThreeMissionsClearAdditionalBerry : 0);
    public int? PoneglyphImmediateBerry(bool succeeded) => !succeeded ? 0 :
        AdditionalBerryRequiresUnobservedCondition ? null : PoneglyphSuccessImmediateBerry;
    public int? TreasureBerryForRoll(int roll) => roll is < 1 or > 1000 ? throw new ArgumentOutOfRangeException(nameof(roll)) :
        roll > TreasureChanceNumerator ? 0 : AdditionalBerryRequiresUnobservedCondition ? null : TreasureSuccessAdditionalBerry;
}
