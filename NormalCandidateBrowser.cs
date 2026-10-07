using System.Text.Json;

namespace OrandOverlay;

public enum NormalCandidateStage { Rare, Legend, Upper, Utility }
public sealed record NormalUtilityRole(string Category, string Value, string Condition);
public sealed record NormalUtilityUnit(string UnitId, string SourceName, string Direction, IReadOnlyList<NormalUtilityRole> Roles);
public sealed record NormalCandidate(UnitDefinition Unit, RecipeCompletionAllocation? Allocation, string Caveat, bool Owned)
{
    public double? Completion => Allocation?.Progress.CompletionRatio;
    public long? MissingLeafCount => Allocation is { } a ? a.Progress.RequiredLeafCount - a.Progress.OwnedLeafCount : null;
    public long? RecipeStepCount { get; init; }
    public int ObservedCount { get; init; }
    public bool StoryFast { get; init; }
    public string DamageType { get; init; } = "unknown";
    public IReadOnlyList<string> MovementRoles { get; init; } = [];
    public bool UsefulSupport { get; init; }
    public string RecommendationReason { get; init; } = "";
}
public sealed record NormalCandidateGroup(string Name, IReadOnlyList<NormalCandidate> Candidates);
public sealed record NormalCandidateSnapshot(NormalCandidateStage Stage, bool IsCurrent, string Direction,
    string Status, IReadOnlyList<NormalCandidateGroup> Groups);
public sealed record NormalMovementCoverage(string Role, IReadOnlyList<string> ObservedUnitIds)
{
    public bool Covered => ObservedUnitIds.Count > 0;
}

/// <summary>Session-only advice from observed cards. Does not authorize gameplay or persist goals.</summary>
public sealed partial class NormalCandidateBrowser
{
    private readonly Dictionary<string, UnitDefinition> _units;
    private readonly RecipeCompletionCalculator _calculator;
    private readonly Dictionary<string, NormalUtilityUnit> _roles;
    private readonly Dictionary<string, NormalGuideUnit> _guide;
    private readonly Dictionary<string, NormalCandidate> _candidateCache = new(StringComparer.OrdinalIgnoreCase);
    private long? _generation;
    private string? _referenceContext;
    private bool _presentationDirty = true;
    private IReadOnlyDictionary<string, int> _lastObserved = new Dictionary<string, int>();
    internal NormalCandidateSnapshot? LastKnownSnapshot { get; private set; }
    internal IReadOnlyDictionary<string, int> LastKnownInventory => _lastObserved;
    internal long BrowsingRevision { get; private set; }
    internal bool HasLastKnownInventory => LastKnownSnapshot is not null;
    public bool IsDiagnosticReference { get; private set; }
    public bool ReferenceGrowthAttributionAvailable { get; private set; }
    public int? ReferenceViewSlot { get; private set; }
    public bool IsPaused { get; private set; }
    public NormalCandidateStage Stage { get; private set; }
    public NormalCandidateStage ProgressStage { get; private set; }
    public long SessionRevision { get; private set; }
    public bool FollowingProgress { get; private set; } = true;
    public string? FirstUpperId { get; private set; }
    public IReadOnlyList<UnitDefinition> FirstUpperChoices { get; private set; } = [];
    public string FirstUpperDirection => FirstUpperId is { } id ? DamageTypeFor(id) : "unknown";
    public IReadOnlyList<NormalMovementCoverage> MovementCoverage { get; private set; } = [];
    public string? SelectedUnitId { get; private set; }
    public string UserDirection { get; private set; } = "unknown";
    public HashSet<string> CollapsedCategories { get; } = new(StringComparer.Ordinal);
    public bool HasRoleProfile => _roles.Count > 0;
    public IReadOnlyList<UnitDefinition> Units => _units.Values.ToList();
    public NormalCandidateSnapshot Snapshot { get; private set; } = new(NormalCandidateStage.Rare, false, "unknown", "현재 유닛을 확인하고 있어요", []);
    public event Action? PresentationChanged;
    public Func<bool>? ReferencePresentationIsValid { get; set; }

    public NormalCandidateBrowser(IEnumerable<UnitDefinition> units, IEnumerable<NormalUtilityUnit>? roles = null,
        IEnumerable<NormalGuideUnit>? guideProfile = null)
    {
        _units = units.GroupBy(u => u.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _calculator = new(ResolveMaterial);
        _roles = (roles ?? []).GroupBy(u => u.UnitId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var catalog in CatalogSupportRoles(_units.Values))
            if (!_roles.ContainsKey(catalog.UnitId)) _roles[catalog.UnitId] = catalog;
        _guide = (guideProfile ?? []).GroupBy(u => u.UnitId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var seraphim in SeraphimSupportOverrides())
        {
            _roles[seraphim.UnitId] = seraphim;
            if (_guide.TryGetValue(seraphim.UnitId, out var existing))
                _guide[seraphim.UnitId] = existing with
                {
                    DamageType = existing.DamageType is "physical" or "magical" or "both" ? existing.DamageType : seraphim.Direction,
                    MovementRoles = (existing.MovementRoles ?? []).Contains("공중이동") ? existing.MovementRoles : [.. existing.MovementRoles ?? [], "공중이동"]
                };
            else _guide[seraphim.UnitId] = new(seraphim.UnitId, false, seraphim.Direction, ["공중이동"]);
        }
    }
    public static NormalCandidateBrowser Create(DataCatalog catalog) => new(catalog.AllUnits,
        LoadProfile(Path.Combine(AppContext.BaseDirectory, "Data", "randypick-utility-48129.json")),
        NormalGuideProfile.LoadBundled(catalog.AllUnits.Select(u => u.Id)), catalog.MapVersion);
    public static IReadOnlyList<NormalUtilityUnit> LoadProfile(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("guideId").GetString() != "48129" ||
                root.GetProperty("sourceUrl").GetString() != "https://tmo.gg/g/ord/build-helper/48129") return [];
            return root.GetProperty("units").EnumerateArray().Select(u => new NormalUtilityUnit(
                u.GetProperty("unitId").GetString() ?? "", u.GetProperty("sourceName").GetString() ?? "",
                u.GetProperty("direction").GetString() ?? "unknown", u.GetProperty("roles").EnumerateArray().Select(v => new NormalUtilityRole(
                    v.GetProperty("category").GetString() ?? "", v.GetProperty("value").GetString() ?? "",
                    v.GetProperty("condition").GetString() ?? "")).ToArray())).Where(u => u.UnitId.Length > 0).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return []; }
    }
    public static string Tier(UnitDefinition unit) => TopGradePolicy.BaseTier(unit.Tier);
    public static bool IsUpper(UnitDefinition unit) => Tier(unit) is "초월" or "제한됨" or "제한" or "불멸" or "영원";
    private static bool IsLegend(UnitDefinition unit) => Tier(unit) is "전설" or "히든";
    internal static bool IsSeraphim(UnitDefinition unit) => Tier(unit) == "세라핌";
    internal static bool IsSupportGrade(UnitDefinition unit) => Tier(unit) is
        "특별함" or "희귀함" or "희귀" or "전설" or "히든" or "해적선" or "세라핌"
        or "초월" or "제한됨" or "제한" or "불멸" or "영원";
    private static IEnumerable<NormalUtilityUnit> CatalogSupportRoles(IEnumerable<UnitDefinition> units)
    {
        foreach (var unit in units)
        {
            if (!IsSupportGrade(unit)) continue;
            var abilities = unit.OfficialAbilities.Count > 0 ? unit.OfficialAbilities : [];
            var mapped = abilities.Select(ability => CatalogRole(ability)).OfType<NormalUtilityRole>().ToArray();
            if (mapped.Length == 0) continue;
            var names = mapped.Select(role => SupportCategory(role.Category)).ToHashSet(StringComparer.Ordinal);
            var magic = names.Overlaps(["마방깎", "마뎀증", "끝딜", "범위 끝딜", "폭뎀증"]);
            var physical = names.Overlaps(["방깎", "공증", "공속", "암브"]);
            yield return new(unit.Id, unit.Name, magic && physical ? "both" : magic ? "magical" : physical ? "physical" : "unknown", mapped);
        }
    }
    private static NormalUtilityRole? CatalogRole(UnitAbilityDisplay ability)
    {
        var category = ability.Name switch
        {
            "이동속도 감소" or "발동이동속도 감소" => "이감",
            "방어력 감소" => "방깎",
            "공격력 증가" => "공증",
            "공격속도 증가" => "공속",
            "아머브레이크" => "암브",
            "광폭화" or "광폭화 잡기" => "광폭화",
            "보스 잡기" => "보스 잡기",
            "마법 방어력 감소" => "마방깎",
            "마법 대미지 증가" => "마법 대미지 증가",
            "폭발형 대미지 증폭" => "폭뎀증",
            "마나 재생" or "체력 재생" or "스턴" or "끝딜" or "범위 끝딜" or "단일" => ability.Name,
            _ => null
        };
        return category is null ? null : new(category, ability.DisplayValue, "catalog");
    }
    private static IEnumerable<NormalUtilityUnit> SeraphimSupportOverrides() =>
    [
        new("rawcode:1A0h", "S-베어", "magical",
        [
            new("보스 잡기", "true", "베어 세라핌 광보잡"),
            new("광폭화", "true", "베어 세라핌 광보잡"),
            new("마법 대미지 증가", "4", "베어 세라핌 마뎀증"),
            new("마방깎", "1", "베어 세라핌 마방깎"),
            new("스턴", "2.5", "세라핌 단일스턴")
        ]),
        new("rawcode:3A0h", "S-호크", "physical",
        [
            new("방깎", "35", "호크 세라핌 깍"),
            new("보스 잡기", "true", "호크 세라핌 광보잡"),
            new("광폭화", "true", "호크 세라핌 광보잡"),
            new("아머브레이크", "2", "호크 세라핌 단일암브2"),
            new("스턴", "2.5", "세라핌 단일스턴")
        ]),
        new("rawcode:0A0h", "S-샤크", "physical",
        [
            new("방깎", "20", "샤크 세라핌 깍"),
            new("아머브레이크", "3", "샤크 세라핌 암브"),
            new("공증", "55", "샤크 세라핌 공증"),
            new("체력 재생", "1.75", "샤크 세라핌 체젠"),
            new("스턴", "2.5", "세라핌 단일스턴")
        ]),
        new("rawcode:Y90h", "S-스네이크", "magical",
        [
            new("폭뎀증", "3", "스네이크 세라핌 폭뎀증"),
            new("끝딜", "true", "스네이크 세라핌 끝딜"),
            new("범위 끝딜", "true", "스네이크 세라핌 범위전퍼"),
            new("스턴", "2.5", "세라핌 단일스턴")
        ])
    ];
    public UnitDefinition? SelectedUnit => SelectedUnitId is { } id ? _units.GetValueOrDefault(id) : null;
    public NormalCandidate? SelectedCandidate => SelectedUnit is { } unit ? Candidate(unit) : null;
    public IReadOnlyList<NormalUtilityRole> RolesFor(string id) => _roles.GetValueOrDefault(id)?.Roles ?? [];
    public bool DirectionUnverified(string id) => Stage == NormalCandidateStage.Utility && DamageTypeFor(id) == "unknown";
    public bool CanHighlight(NormalCandidate candidate) => !IsPaused && candidate.ObservedCount == 0 &&
        candidate.Allocation is not null &&
        (Stage != NormalCandidateStage.Utility || candidate.UsefulSupport);

    public void RevalidatePresentation()
    {
        if (IsDiagnosticReference && Snapshot.IsCurrent && ReferencePresentationIsValid?.Invoke() != true)
            InvalidateObservation(IsPaused);
    }
    public void UpdateReference(IDiagnosticInventoryReference observation, long generation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation is not (DiagnosticInventoryObservation or DiagnosticBasicInventoryObservation))
            throw new ArgumentException("Only sealed diagnostic reference contracts are supported.", nameof(observation));
        if (observation.Availability != DiagnosticInventoryAvailability.Ready || ReferencePresentationIsValid?.Invoke() == false)
        { InvalidateReference(); return; }
        var context = (observation.BindingContextId.Length > 0 ? observation.BindingContextId : observation.ContextId) +
            ":" + observation.ViewSlot;
        if (_referenceContext != context || (_generation.HasValue && _generation != generation)) Reset();
        _referenceContext = context;
        if (!IsDiagnosticReference) _presentationDirty = true;
        IsDiagnosticReference = true; ReferenceViewSlot = observation.ViewSlot;
        ReferenceGrowthAttributionAvailable = observation.GrowthAttributionAvailable;
        Update(observation.Counts, true, generation);
    }
    private static readonly IReadOnlyDictionary<string, int> EmptyInventory = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> CurrentInventory => Snapshot.IsCurrent ? _lastObserved : EmptyInventory;
    public void InvalidateReference(bool paused = false, bool resetContext = false)
    {
        if (resetContext) Reset();
        if (!IsDiagnosticReference) _presentationDirty = true;
        IsDiagnosticReference = true;
        InvalidateObservation(paused);
    }
    public void SetReferenceStage(NormalCandidateStage stage) { if (IsDiagnosticReference) SetStage(stage); }
    public void SetStage(NormalCandidateStage stage)
    {
        if (!Enum.IsDefined(stage)) return;
        RevalidatePresentation();
        if (!FollowingProgress && Stage == stage) return;
        FollowingProgress = false; Stage = stage; RefreshPresentation();
    }
    public void ResumeProgress()
    {
        RevalidatePresentation();
        if (FollowingProgress) return;
        FollowingProgress = true; Stage = ProgressStage; RefreshPresentation();
    }
    public void Select(string id)
    {
        RevalidatePresentation();
        if (!_units.ContainsKey(id) || SelectedUnitId == id) return;
        SelectedUnitId = id; PresentationChanged?.Invoke();
    }
    public void ClearSelection()
    {
        RevalidatePresentation();
        if (SelectedUnitId is null) return;
        SelectedUnitId = null; PresentationChanged?.Invoke();
    }
    public void Fold(string category, bool collapsed)
    {
        RevalidatePresentation();
        if (collapsed ? CollapsedCategories.Add(category) : CollapsedCategories.Remove(category)) PresentationChanged?.Invoke();
    }
    public void FoldAll(bool collapsed)
    {
        RevalidatePresentation();
        var changed = false;
        foreach (var group in Snapshot.Groups)
            changed |= collapsed ? CollapsedCategories.Add(group.Name) : CollapsedCategories.Remove(group.Name);
        if (changed) PresentationChanged?.Invoke();
    }
    public void SetDirection(string value)
    {
        RevalidatePresentation();
        var direction = value is "physical" or "magical" ? value : "unknown";
        if (UserDirection == direction) return;
        UserDirection = direction; RefreshPresentation();
    }
    private void RefreshPresentation()
    {
        _presentationDirty = true;
        Update(_lastObserved, Snapshot.IsCurrent, _generation ?? 0, IsPaused);
        PresentationChanged?.Invoke();
    }
    public void Reset()
    {
        SessionRevision++; LastKnownSnapshot = null; BrowsingRevision++;
        IsDiagnosticReference = false; ReferenceViewSlot = null; _referenceContext = null; ReferenceGrowthAttributionAvailable = false;
        Stage = ProgressStage = NormalCandidateStage.Rare; FollowingProgress = true;
        FirstUpperId = SelectedUnitId = null; FirstUpperChoices = []; _upperObserved = false;
        UserDirection = "unknown"; CollapsedCategories.Clear(); _generation = null;
        _lastObserved = new Dictionary<string, int>(); IsPaused = false; MovementCoverage = [];
        _candidateCache.Clear(); _presentationDirty = true;
        Snapshot = new(Stage, false, "unknown", "새 판이에요. 현재 유닛을 확인하고 있어요", []);
    }
    public void InvalidateObservation(bool paused = false) => Update(_lastObserved, false, _generation ?? 0, paused);
    public void Update(IReadOnlyDictionary<string, int> inventory, bool current, long generation, bool paused = false)
    {
        if (_generation.HasValue && _generation != generation) Reset();
        _generation = generation;
        current &= !paused;
        var observed = inventory.Where(p => p.Value > 0 && !RecipeWildcards.IsWildcard(p.Key) && _units.ContainsKey(p.Key))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        if (!_presentationDirty && Snapshot.IsCurrent == current && IsPaused == paused &&
            _lastObserved.Count == observed.Count && observed.All(p => _lastObserved.GetValueOrDefault(p.Key) == p.Value)) return;
        var sameHand = _lastObserved.Count == observed.Count && observed.All(p => _lastObserved.GetValueOrDefault(p.Key) == p.Value);
        if (_presentationDirty || !sameHand || LastKnownSnapshot is null) BrowsingRevision++;
        _lastObserved = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(observed); IsPaused = paused;
        if (current) ObserveProgress(observed);
        _candidateCache.Clear(); _presentationDirty = false;
        BuildSnapshot(current);
        if (current) LastKnownSnapshot = Snapshot;
    }
}
