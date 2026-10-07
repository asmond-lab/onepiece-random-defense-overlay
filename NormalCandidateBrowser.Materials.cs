namespace OrandOverlay;

public sealed partial class NormalCandidateBrowser
{
    private static readonly HashSet<string> Resources = new(["GOLD", "LUMBER", "POINT", "RANDOM"], StringComparer.OrdinalIgnoreCase);
    private string? ResourceKey(string id)
    {
        var bare = id.StartsWith("rawcode:", StringComparison.OrdinalIgnoreCase) ? id[8..]
            : id.StartsWith("resource:", StringComparison.OrdinalIgnoreCase) ? id[9..] : id;
        if (Resources.Contains(bare)) return bare.ToUpperInvariant();
        if (_units.TryGetValue(id, out var known) && known.Tier == "자원")
            return known.Rawcodes.FirstOrDefault(Resources.Contains)?.ToUpperInvariant();
        return null;
    }
    private UnitDefinition ResolveMaterial(string id) => ResourceKey(id) is { } resource
        ? new UnitDefinition { Id = id, Name = resource, Tier = "자원", Rawcodes = [resource] }
        : _units.GetValueOrDefault(id) ?? new UnitDefinition { Id = id, Name = "미등록 재료" };
    // Pinned 2.320 UNIT inputs; X50h's UU01 lists only UPUN metadata, with no consumed materials.
    private static bool IsSourceMaterialLeaf(UnitDefinition unit) => unit.Tier == "기타" && unit.Rawcodes.Any(code =>
        (code is "S40h" or "H00h" or "060h" or "Y50h" or "X50h") && unit.Id == "rawcode:" + code);
    private bool ValidGraph(string id, HashSet<string> visiting, HashSet<string> done, out bool choiceRequired)
    {
        choiceRequired = false;
        if (ResourceKey(id) is not null) return true;
        if (RecipeWildcards.IsWildcard(id)) { choiceRequired = true; return false; }
        if (!_units.TryGetValue(id, out var unit)) return false;
        if (unit.Recipe.Count == 0 && !IsSourceMaterialLeaf(unit) &&
            Tier(unit) is not ("흔함" or "일반" or "특수" or "특수함" or "아이템" or "랜덤유닛")) return false;
        if (done.Contains(id)) return true;
        if (!visiting.Add(id)) return false;
        foreach (var material in unit.Recipe)
        {
            if (material.Value <= 0 || !ValidGraph(material.Key, visiting, done, out choiceRequired)) return false;
        }
        visiting.Remove(id); done.Add(id); return true;
    }
}
