using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OrandOverlay;

public sealed record Map2322BoxOutcome(string Kind, int RollCount, int Amount);
public sealed record Map2322NavigationOption(string Id, string Name, string Category, IReadOnlyList<int> RegistrationLines,
    string HandlerName, string HandlerSource);
public sealed record Map2322ExchangeRegistration(string Ability, int? Cost, int RegistrationLine, string FunctionName, string RawSource);

/// <summary>Bounded exchange branches, not a claim about uninitialized JASS local behavior in game.</summary>
public sealed class Map2322NavigationProfile
{
    public int DoubleWispCost { get; }
    public int DoubleWispSuccessNumerator { get; }
    public int DoubleWispRollDenominator { get; }
    public int DoubleWispSuccessGrant { get; }
    public int? DoubleWispFailureGrant { get; }
    public string DoubleWispFailureStatus { get; }
    public IReadOnlyList<Map2322NavigationOption> Options { get; }
    public IReadOnlyList<Map2322ExchangeRegistration> Exchanges { get; }
    public int BoxCost { get; }
    public int BoxRollDenominator { get; }
    public IReadOnlyList<Map2322BoxOutcome> BoxOutcomes { get; }

    internal Map2322NavigationProfile(byte[] bytes, string mapVersion = "2.322")
    {
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        Map2322DataBundle.CheckIdentity(root, mapVersion);
        if (root.GetProperty("sourceSha256").GetString() != (mapVersion == Map2323SourceContract.MapVersion ? Map2323SourceContract.JassSha256 : Map2322SourceContract.JassSha256))
            throw new InvalidDataException("Navigation source mismatch.");
        Options = root.GetProperty("options").EnumerateArray().Select(x => new Map2322NavigationOption(
            x.GetProperty("id").GetString()!, x.GetProperty("name").GetString()!, x.GetProperty("category").GetString()!,
            x.GetProperty("registrationLines").EnumerateArray().Select(v => v.GetInt32()).ToArray(),
            x.GetProperty("handler").GetProperty("name").GetString()!, x.GetProperty("handler").GetProperty("rawSource").GetString()!)).ToArray();
        Exchanges = root.GetProperty("exchanges").EnumerateArray().Select(x => new Map2322ExchangeRegistration(
            x.GetProperty("ability").GetString()!, x.GetProperty("cost").ValueKind == JsonValueKind.Null ? null : x.GetProperty("cost").GetInt32(),
            x.GetProperty("registrationLine").GetInt32(), x.GetProperty("name").GetString()!, x.GetProperty("rawSource").GetString()!)).ToArray();
        if (Options.Count != 15 || Options.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != 15 ||
            Options.Any(x => x.RegistrationLines.Count == 0 || !x.HandlerSource.Contains(x.Name, StringComparison.Ordinal) ||
                !x.HandlerSource.StartsWith("function " + x.HandlerName + " takes", StringComparison.Ordinal)) || Exchanges.Count != 7 ||
            Exchanges.Select(x => x.Ability).Distinct(StringComparer.Ordinal).Count() != 7 ||
            Exchanges.Any(x => !x.RawSource.StartsWith("function " + x.FunctionName + " takes", StringComparison.Ordinal)))
            throw new InvalidDataException("Incomplete 2.322 navigation registrations.");
        var doubleWisp = root.GetProperty("doubleWispExchange");
        DoubleWispCost = doubleWisp.GetProperty("cost").GetInt32();
        DoubleWispSuccessNumerator = doubleWisp.GetProperty("successNumerator").GetInt32();
        DoubleWispRollDenominator = doubleWisp.GetProperty("rollDenominator").GetInt32();
        DoubleWispSuccessGrant = doubleWisp.GetProperty("successSelectionWisps").GetInt32();
        var failure = doubleWisp.GetProperty("failureSelectionWisps");
        DoubleWispFailureGrant = failure.ValueKind == JsonValueKind.Null ? null : failure.GetInt32();
        DoubleWispFailureStatus = doubleWisp.GetProperty("failureStatus").GetString()!;
        var box = root.GetProperty("randomBox");
        BoxCost = box.GetProperty("cost").GetInt32();
        BoxRollDenominator = box.GetProperty("rollDenominator").GetInt32();
        BoxOutcomes = [new("Gold", box.GetProperty("goldRolls").GetInt32(), box.GetProperty("goldAmount").GetInt32()),
            new("RandomWisp", box.GetProperty("randomWispRolls").GetInt32(), 1),
            new("SelectionWisp", box.GetProperty("selectionWispRolls").GetInt32(), 1)];
        if (DoubleWispCost != 3 || DoubleWispSuccessNumerator != 35 || DoubleWispRollDenominator != 100 || DoubleWispSuccessGrant != 2 ||
            (mapVersion == Map2323SourceContract.MapVersion
                ? DoubleWispFailureGrant != 1 || DoubleWispFailureStatus != "ExplicitSingleWispElse"
                : DoubleWispFailureGrant is not null || DoubleWispFailureStatus != "UninitializedJassLocal") || BoxCost != 1 ||
            BoxRollDenominator != 10 || BoxOutcomes.Sum(x => x.RollCount) != BoxRollDenominator ||
            BoxOutcomes[0] != new Map2322BoxOutcome("Gold", 5, 5000) || BoxOutcomes[1].RollCount != 4 || BoxOutcomes[2].RollCount != 1)
            throw new InvalidDataException("Unapproved exchange projection.");
    }

    public int? SelectionWispsForDoubleWispRoll(int roll) => roll is < 1 or > 100
        ? throw new ArgumentOutOfRangeException(nameof(roll)) : roll <= DoubleWispSuccessNumerator ? DoubleWispSuccessGrant : DoubleWispFailureGrant;
    public Map2322BoxOutcome BoxForRoll(int roll)
    {
        if (roll < 1 || roll > BoxRollDenominator) throw new ArgumentOutOfRangeException(nameof(roll));
        return roll == 1 ? BoxOutcomes[2] : roll <= 5 ? BoxOutcomes[1] : BoxOutcomes[0];
    }
}
