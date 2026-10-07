namespace OrandOverlay;

/// <summary>Session-only recommendation filters from the current observed hand. Not a drop table.</summary>
internal static class NormalSpecialObtainPolicy
{
    internal const string RayleighRare = "rawcode:X50h";
    internal const string RayleighLegend = "rawcode:A30h";
    internal const string RayleighImmortal = "rawcode:940h";
    internal const string TranscendenceKuma = "rawcode:S40h";
    internal const string AncientShip = "rawcode:Y50h";
    internal const string TranscendenceWisp = "e01A";
    internal const string RareWisp = "e019";

    internal static bool IsRayleigh(string id) =>
        Same(id, RayleighRare) || Same(id, RayleighLegend) || Same(id, RayleighImmortal);

    internal static bool HasRayleighObtainMeans(IReadOnlyDictionary<string, int> observed) =>
        Count(observed, TranscendenceWisp) > 0 || Count(observed, AncientShip) > 0 || Count(observed, RareWisp) > 0;

    internal static bool HideRayleigh(IReadOnlyDictionary<string, int> observed) =>
        Count(observed, RayleighRare) <= 0 && !HasRayleighObtainMeans(observed);

    internal static bool HideTranscendence(IReadOnlyDictionary<string, int> observed) =>
        Count(observed, TranscendenceWisp) <= 0 && Count(observed, TranscendenceKuma) <= 0;

    internal static bool HideCandidate(UnitDefinition unit, IReadOnlyDictionary<string, int> observed)
    {
        if (IsRayleigh(unit.Id) && HideRayleigh(observed)) return true;
        return NormalCandidateBrowser.Tier(unit) == "초월" && HideTranscendence(observed);
    }

    private static bool Same(string left, string right) =>
        string.Equals(Canonical(left), Canonical(right), StringComparison.OrdinalIgnoreCase);

    private static string Canonical(string id) =>
        id.StartsWith("rawcode:", StringComparison.OrdinalIgnoreCase) ? id : "rawcode:" + id;

    private static int Count(IReadOnlyDictionary<string, int> observed, string id)
    {
        var total = observed.GetValueOrDefault(id);
        if (id.StartsWith("rawcode:", StringComparison.OrdinalIgnoreCase))
            total += observed.GetValueOrDefault(id[8..]);
        else
            total += observed.GetValueOrDefault("rawcode:" + id);
        return total;
    }
}
