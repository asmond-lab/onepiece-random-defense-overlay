namespace OrandOverlay;

public sealed partial class NormalCandidateBrowser
{
    private bool _upperObserved;
    private string _movementMapVersion = "2.320";
    private bool IsBrowsableUpper(UnitDefinition unit) => IsUpper(unit) ||
        (_movementMapVersion is "2.322" or "2.323") &&
        unit.Id == "rawcode:2C0h" && Tier(unit) == "신비" && unit.Rawcodes.Contains("2C0h");

    // Runtime integration supplies the selected map explicitly; historical callers retain their guide.
    public NormalCandidateBrowser(IEnumerable<UnitDefinition> units, IEnumerable<NormalUtilityUnit>? roles,
        IEnumerable<NormalGuideUnit>? guideProfile, string mapVersion) : this(units, roles, guideProfile)
    {
        _movementMapVersion = mapVersion;
        if (mapVersion is "2.322" or "2.323")
        {
            if (_guide.TryGetValue("rawcode:M70h", out var kaido))
            {
                _guide[kaido.UnitId] = kaido with
                {
                    MovementRoles = kaido.MovementRoles.Where(role => role != "공중이동").ToArray()
                };
                if (_units.ContainsKey("rawcode:DA0h"))
                    _guide["rawcode:DA0h"] = kaido with { UnitId = "rawcode:DA0h" };
            }
            if (_guide.TryGetValue("rawcode:WB0h", out var hybrid))
                _guide[hybrid.UnitId] = hybrid with
                {
                    MovementRoles = hybrid.MovementRoles.Where(role => role != "공중이동").ToArray()
                };
        }
    }

    private bool HasMovement(string id, string role) =>
        _guide.GetValueOrDefault(id)?.MovementRoles.Contains(role) == true &&
        (role != "공중이동" || Map2322KaidoAirRole.CountsAsAir(_movementMapVersion, _units[id], true));

    private IReadOnlyList<string> MovementFor(string id) =>
        (_guide.GetValueOrDefault(id)?.MovementRoles ?? [])
            .Where(role => HasMovement(id, role)).ToArray();
    private static readonly string[] MovementNames = ["공중이동", "지형무시이동", "순간이동"];
    private static string[] SupportNames(string direction) => direction == "physical"
        ? ["방깎", "공증", "공속", "스턴", "이감", "암브", "보스 잡기", "광폭화 잡기", "마젠", "체젠"]
        : direction == "magical" ? ["끝딜", "범위 끝딜", "단일", "폭뎀증", "마방깎", "마뎀증", "마젠", "스턴", "이감", "암브", "보스 잡기", "광폭화 잡기", "체젠"] : [];
    private static string SupportCategory(string category) => category switch
    {
        "마나 재생" => "마젠",
        "체력 재생" => "체젠",
        "아머브레이크" => "암브",
        "광폭화" => "광폭화 잡기",
        "마법 대미지 증가" => "마뎀증",
        _ => category
    };
    private static string DamageLaneName(string direction) => direction == "physical" ? "물딜" : "마딜";
    private static string NestedUtilityName(string direction, string util) => DamageLaneName(direction) + "+" + util;
    private bool MatchesDamageLane(string id, string lane)
    {
        var type = DamageTypeFor(id);
        return type == lane || type == "both" || type == "unknown" && EffectiveDirection == lane;
    }
    private bool CarriesUtility(UnitDefinition unit) => RolesFor(unit.Id).Count > 0 ||
        MovementFor(unit.Id).Count > 0;
    private bool IsUtilityCandidate(UnitDefinition unit) =>
        IsSupportGrade(unit) && (CarriesUtility(unit) || IsRecommendedPartner(unit));
    private IEnumerable<(string Name, IEnumerable<UnitDefinition> Units)> NestedUtilityGroups(
        IReadOnlyList<UnitDefinition> pool, IReadOnlyList<string> lanes) =>
        lanes.SelectMany(lane => SupportNames(lane).Select(name => (NestedUtilityName(lane, name),
                pool.Where(unit => MatchesDamageLane(unit.Id, lane) &&
                    RolesFor(unit.Id).Any(role => SupportCategory(role.Category) == name)))))
            .Where(category => category.Item2.Any());
    private string EffectiveDirection => UserDirection != "unknown" ? UserDirection
        : FirstUpperDirection is "physical" or "magical" ? FirstUpperDirection : "unknown";
    private string DamageTypeFor(string id)
    {
        if (_guide.TryGetValue(id, out var guide))
            return guide.DamageType is "physical" or "magical" or "both" ? guide.DamageType : "unknown";
        var fromUtility = _roles.GetValueOrDefault(id)?.Direction;
        return fromUtility is "physical" or "magical" or "both" ? fromUtility : "unknown";
    }
    private bool Compatible(string id, string direction) => direction is "physical" or "magical" &&
        (DamageTypeFor(id) == direction || DamageTypeFor(id) == "both");
    private void ObserveProgress(IReadOnlyDictionary<string, int> observed)
    {
        var present = observed.Keys.Select(id => _units[id]).ToArray();
        var uppers = present.Where(IsBrowsableUpper).OrderBy(u => u.Id, StringComparer.Ordinal).ToArray();
        var next = uppers.Length > 0 ? NormalCandidateStage.Utility : present.Any(IsLegend) ? NormalCandidateStage.Upper
            : present.Any(u => Tier(u) is "희귀함" or "희귀") ? NormalCandidateStage.Legend : NormalCandidateStage.Rare;
        if (!_upperObserved && uppers.Length > 0)
        {
            _upperObserved = true;
            if (uppers.Length == 1) FirstUpperId = uppers[0].Id;
            else FirstUpperChoices = Array.AsReadOnly(uppers);
        }
        ProgressStage = (NormalCandidateStage)Math.Max((int)ProgressStage, (int)next);
        if (FollowingProgress) Stage = ProgressStage;
    }
    public bool SelectFirstUpper(string id)
    {
        RevalidatePresentation();
        if (FirstUpperId is not null) return FirstUpperId.Equals(id, StringComparison.OrdinalIgnoreCase);
        var choice = FirstUpperChoices.FirstOrDefault(u => u.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (choice is null) return false;
        FirstUpperId = choice.Id; RefreshPresentation(); return true;
    }
    private void BuildSnapshot(bool current)
    {
        var direction = EffectiveDirection;
        if (!current && LastKnownSnapshot is { } previous && previous.Stage == Stage && previous.Direction == direction)
        {
            Snapshot = previous with { IsCurrent = false, Status = Status(false, direction),
                Groups = previous.Groups.Select(group => new NormalCandidateGroup(group.Name, group.Candidates.Select(candidate =>
                    candidate with { Caveat = candidate.Caveat.Length == 0 ? "이전에 인식한 정보예요. 지금 유닛은 다시 확인 중이에요." : candidate.Caveat }).ToArray())).ToArray() };
            return;
        }
        MovementCoverage = MovementNames.Select(name => new NormalMovementCoverage(name, current
            ? _lastObserved.Keys.Where(id => HasMovement(id, name))
                .Order(StringComparer.Ordinal).ToArray() : [])).ToArray();
        // Candidate creation reads the new fenced state, including cards opened outside the visible stage.
        Snapshot = new(Stage, current, direction, Status(current, direction), []);
        var pool = _units.Values.Where(u => Stage switch
        {
            NormalCandidateStage.Rare => Tier(u) is "희귀함" or "희귀",
            NormalCandidateStage.Legend => IsLegend(u) || IsBrowsableUpper(u),
            NormalCandidateStage.Upper => IsBrowsableUpper(u),
            _ => IsUtilityCandidate(u)
        }).Where(u => _lastObserved.GetValueOrDefault(u.Id) == 0 &&
            (Stage is not (NormalCandidateStage.Utility or NormalCandidateStage.Legend) ||
             !NormalSpecialObtainPolicy.HideCandidate(u, _lastObserved))).ToArray();
        IEnumerable<(string Name, IEnumerable<UnitDefinition> Units)> categories;
        if (Stage == NormalCandidateStage.Utility)
        {
            var lanes = direction == "magical" ? new[] { "magical" } : direction == "physical" ? new[] { "physical" } : new[] { "physical", "magical" };
            categories = NestedUtilityGroups(pool, lanes)
                .Concat(MovementNames.Select(name => (name, pool.Where(u =>
                    HasMovement(u.Id, name)))).Where(category => category.Item2.Any()));
            var partners = pool.Where(u => IsRecommendedPartner(u) && (!current || _lastObserved.GetValueOrDefault(u.Id) == 0)).ToArray();
            if (partners.Length > 0) categories = new[] { ("주력 궁합", partners.AsEnumerable()) }.Concat(categories);
        }
        else if (Stage == NormalCandidateStage.Legend)
        {
            var legendPool = pool.Where(IsLegend).ToArray();
            categories =
            [
                ("스토리", legendPool.Where(unit => _guide.GetValueOrDefault(unit.Id)?.StoryFast == true)),
                ("공중이동", legendPool.Where(unit =>
                    HasMovement(unit.Id, "공중이동"))),
                ("가까운 조합", pool)
            ];
        }
        else if (Stage == NormalCandidateStage.Upper) categories = [("상위", pool)];
        else categories = pool.GroupBy(u => Tier(u) == "제한됨" ? "제한" : Tier(u))
            .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => (g.Key, g.AsEnumerable()));
        var groups = categories.Select(category => new NormalCandidateGroup(category.Name,
            OrderCandidates(category.Units.Select(Candidate)).ToArray())).ToArray();
        Snapshot = Snapshot with { Groups = groups };
    }
    public static IOrderedEnumerable<NormalCandidate> OrderCandidates(IEnumerable<NormalCandidate> candidates) => candidates
        .OrderBy(c => c.MissingLeafCount ?? long.MaxValue).ThenByDescending(c => c.UsefulSupport)
        .ThenBy(c => c.RecipeStepCount ?? long.MaxValue)
        .ThenByDescending(c => c.Completion ?? -1).ThenBy(c => c.Unit.Id, StringComparer.Ordinal);
    private string Status(bool current, string direction)
    {
        if (IsPaused) return "추천 일시정지 · 재개를 누르면 현재 패를 다시 확인합니다";
        if (!current) return HasLastKnownInventory ? "이전에 인식한 유닛 정보예요. 지금 유닛은 다시 확인 중이에요." : "아직 유닛을 확인하지 못했어요. 확인되면 재료를 비교해요.";
        var note = IsDiagnosticReference ? "인식한 패 기준 참고 안내 · " : "지금 인식한 유닛 · ";
        if (Stage == NormalCandidateStage.Utility && FirstUpperId is null && FirstUpperChoices.Count > 1)
            return note + "상위 유닛이 여러 개예요. 먼저 만든 유닛을 골라 주세요" + KaidoConditionalNote();
        if (Stage == NormalCandidateStage.Utility && direction == "unknown")
            return note + "주력의 물리·마법 유형을 알 수 없어요. 이동 능력을 먼저 확인해 주세요" + KaidoConditionalNote();
        if (Stage == NormalCandidateStage.Utility && !HasRoleProfile)
            return note + "지원 능력 정보를 확인할 수 없어 이동 능력만 안내해요" + KaidoConditionalNote();
        return note + (FollowingProgress ? "" : "선택한 단계 · ") + "부족한 재료가 적은 순서예요. 실제 조합 가능 여부는 게임에서 확인해 주세요" + KaidoConditionalNote();
    }

    private string KaidoConditionalNote() =>
        (_movementMapVersion is "2.322" or "2.323") &&
        _lastObserved.Keys.Any(id => Map2322KaidoAirRole.Decide(_movementMapVersion, id, _units[id].Rawcodes) == Map2322KaidoMovement.Conditional)
            ? " · " + Map2322KaidoAirRole.ConditionalNote : "";
}
