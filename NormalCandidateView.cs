using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Data;

namespace OrandOverlay;

/// <summary>Two independent view instances share selection and fold state, never a gameplay goal.</summary>
public sealed partial class NormalCandidateView : UserControl
{
    public bool IsMainWorkspace { get; set; }
    private readonly StackPanel _header = new();
    private readonly WrapPanel _categoryChips = new();
    private readonly ComboBox _category = new() { Width = 130, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _bannerTitle = Text("현재 유닛을 확인하고 있어요", 12, Secondary, true);
    private readonly TextBlock _bannerCaption = Text("", 10, Secondary);
    private readonly Border _banner = new() { CornerRadius = new(9), BorderThickness = new(1), Padding = new(12, 7, 12, 7) };
    private Border _detailWell = null!;
    private string? _selectedCategory;
    private NormalCandidateStage? _presentedStage;
    private long _presentedSession = -1;
    private bool _hasCategoryChoice;
    private string? _selectionCategory;
    private string? _selectionCategoryUnit;
    private readonly TextBlock _overlayTitle = Text("조합 후보 살펴보기", 14, White, true);
    private readonly Border _upperSummary = new Border { BorderBrush = Line, BorderThickness = new(1), CornerRadius = new(8), Padding = new(9,6,9,6), Margin = new(0,6,0,7) };
    private sealed class FoldPresentation
    {
        public long SessionRevision = -1;
        public NormalCandidateStage? Stage;
        public bool HasUserChoice;
        public string? SelectedCategory;
        public string? SelectedUnitId;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NormalCandidateBrowser, FoldPresentation> FoldChoices = new();
    private readonly TextBlock _status = Text("현재 유닛을 확인하고 있어요", 12, Secondary);
    private readonly StackPanel _groups = new();
    private readonly StackPanel _detail = new();
    private readonly ScrollViewer _candidateScroll;
    private readonly ScrollViewer _detailScroll;
    private readonly RowDefinition _selectedDetailRow = new() { Height = new GridLength(150) };
    private readonly ComboBox _referenceStage = new() { Width = 110, Margin = new(4, 0, 0, 0) };
    private readonly ComboBox _direction = new() { Width = 148, Margin = new(8, 0, 0, 0) };
    private NormalCandidateBrowser? _model;
    private readonly Button _resume;
    public event Action? ResumeRequested;
    public bool CanResume => _model?.IsPaused == true;
    public void RequestResume() { if (CanResume) ResumeRequested?.Invoke(); }
    private bool _painting;
    private readonly Dictionary<string, int> _visibleCounts = new(StringComparer.Ordinal);
    private static readonly Brush Line = RandyPickTheme.Border,
        Mint = RandyPickTheme.Success, Gold = RandyPickTheme.Accent, Secondary = RandyPickTheme.Muted, White = RandyPickTheme.Text, Accent = RandyPickTheme.Accent;
    public NormalCandidateView()
    {
        this.WithOverlayBackground(OverlayChrome.CanvasKey);
        _upperSummary.WithOverlayBackground(OverlayChrome.RaisedKey);
        AutomationProperties.SetAutomationId(_category, "normal-category-picker");
        AutomationProperties.SetName(_category, "후보 종류 선택");
        AutomationProperties.SetAutomationId(_direction, "normal-direction-picker");
        AutomationProperties.SetName(_direction, "물리·마법 유형 선택");
        var root = new Grid();
        root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new() { Width = new GridLength(0) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(_selectedDetailRow);
        var header = _header; Grid.SetColumnSpan(header, 2); header.Margin = new(0, 0, 0, 4);
        header.Children.Add(Text("조합 후보", 20, White, true));
        header.Children.Add(_status);
        var actions = new WrapPanel { Margin = new(0, 10, 0, 0) };
        var collapse = Action("전체 접기", () => FoldAllPresentation(true));
        var expand = Action("전체 펼치기", () => FoldAllPresentation(false));
        actions.Children.Add(collapse); actions.Children.Add(expand);
        _resume = Action("추천 재개", RequestResume); _resume.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(_resume, "normal-resume");
        actions.Children.Add(_resume);
        _direction.ItemsSource = new[] { "자동 방향", "물리 탐색", "마법 탐색" }; _direction.SelectedIndex = 0;
        _direction.SelectionChanged += (_, _) => { if (!_painting) _model?.SetDirection(_direction.SelectedIndex == 1 ? "physical" : _direction.SelectedIndex == 2 ? "magical" : "unknown"); };
        _referenceStage.ItemsSource = new[] { "희귀 탐색", "전설·히든", "상위 탐색", "지원 탐색" };
        _referenceStage.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(_referenceStage, "normal-stage-picker");
        AutomationProperties.SetName(_referenceStage, "살펴볼 조합 단계 선택");
        _referenceStage.SelectionChanged += (_, _) => { if (!_painting && _referenceStage.SelectedIndex >= 0) _model?.SetReferenceStage((NormalCandidateStage)_referenceStage.SelectedIndex); };
        actions.Children.Add(_referenceStage);
        actions.Children.Add(_direction); header.Children.Add(actions); root.Children.Add(header);
        var bannerWords = new StackPanel(); bannerWords.Children.Add(_bannerTitle); bannerWords.Children.Add(_bannerCaption); _banner.Child = bannerWords;
        _category.SelectionChanged += (_, _) =>
        {
            if (!_painting && _category.SelectedItem is string displayName)
            {
                _hasCategoryChoice = true;
                var name = CanonicalCategoryName(displayName);
                _selectedCategory = name == "전체 카테고리" ? null : name;
                Render();
            }
        };
        _candidateScroll = new ScrollViewer { Content = _groups, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetAutomationId(_candidateScroll, "normal-candidate-scroll");
        _candidateScroll.SizeChanged += (_, _) => UpdateCandidateColumns();
        Grid.SetRow(_candidateScroll, 1); root.Children.Add(_candidateScroll);
        var detailScroll = _detailScroll = new ScrollViewer { Content = _detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetAutomationId(detailScroll, "normal-craft-scroll");
        var well = _detailWell = new Border { Child = CreateCraftWorkspace(), BorderBrush = OverlayTheme.PlanLine, BorderThickness = new(1), CornerRadius = new(10), Padding = new(CraftPanelPadding), MaxWidth = CraftPanelMaxWidth, Margin = new(0, CraftGap, 0, 0), Background = OverlayTheme.PlanCanvas };
        AutomationProperties.SetAutomationId(well, "normal-selected-detail");
        Grid.SetRow(well, 2); root.Children.Add(well); Content = root;
        SizeChanged += (_, _) =>
        {
            UpdateResponsiveLayout();
            UpdateCandidateColumns();
        };
    }
    private int Columns => !IsMainWorkspace && _candidateScroll.ActualWidth >= 430 ? 5 : _candidateScroll.ActualWidth >= 760 ? 5 : _candidateScroll.ActualWidth >= 570 ? 4 : _candidateScroll.ActualWidth >= 390 ? 3 : 2;
    private void UpdateCandidateColumns()
    {
        foreach (var grid in _groups.Children.Cast<UIElement>().Select(x => x is Border b ? b.Child : x).OfType<Expander>()
            .Select(x => x.Content).OfType<StackPanel>().SelectMany(panel => panel.Children.OfType<UniformGrid>())) grid.Columns = Columns;
    }
    public void SetModel(NormalCandidateBrowser model) { if (!ReferenceEquals(_model, model)) { _browsingRenderKey = null; _craftRenderKey = null; } _model = model; Render(); }
    public void Render()
    {
        if (_model is null) return;
        _model.RevalidatePresentation();
        if (_browsingRenderKey == BrowsingRenderKey()) { RefreshFreshnessLabels(); RenderDetail(); return; }
        _freshnessLabels.Clear();
        _painting = true;
        var foldChoice = FoldChoices.GetValue(_model, _ => new FoldPresentation());
        if (foldChoice.SessionRevision != _model.SessionRevision)
        {
            foldChoice.SessionRevision = _model.SessionRevision;
            foldChoice.Stage = null;
            foldChoice.HasUserChoice = false; foldChoice.SelectedCategory = null; foldChoice.SelectedUnitId = null;
        }
        var newSession = _presentedSession != _model.SessionRevision;
        if (newSession)
        {
            _presentedSession = _model.SessionRevision;
            _selectedCategory = null; _hasCategoryChoice = false; _presentedStage = null;
            _visibleCounts.Clear(); _movementExpanded = false; _sourceDetailsExpanded = false;
            _candidateScroll.ScrollToHome(); _detailScroll.ScrollToHome(); _missingScroll.ScrollToHome();
        }
        _referenceStage.Visibility = Visibility.Visible;
        _referenceStage.SelectedIndex = (int)_model.Stage;
        var offset = newSession ? 0 : _candidateScroll.VerticalOffset;
        var snapshot = BrowsingSnapshot;
        var stageChanged = _presentedStage != snapshot.Stage;
        var enteringLegend = foldChoice.Stage is { } sharedStage && sharedStage != NormalCandidateStage.Legend &&
            snapshot.Stage == NormalCandidateStage.Legend;
        if (enteringLegend)
        {
            foldChoice.HasUserChoice = false; foldChoice.SelectedCategory = null; foldChoice.SelectedUnitId = null;
            _model.CollapsedCategories.Clear();
        }
        foldChoice.Stage = snapshot.Stage;
        if (stageChanged && snapshot.Stage == NormalCandidateStage.Legend)
        {
            _selectedCategory = null;
            _hasCategoryChoice = false;
        }
        _status.Text = CandidateStatusCopy(snapshot.Status);
        if (IsMainWorkspace)
        {
            if (!_header.Children.Contains(_banner))
            {
                _header.Children.Clear(); _header.Children.Add(_banner);
                var controls = new Grid { Margin = new(0,8,0,0) };
                controls.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); controls.ColumnDefinitions.Add(new()); controls.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                _categoryChips.VerticalAlignment = VerticalAlignment.Center; controls.Children.Add(_categoryChips);
                var tools = new WrapPanel { Margin = new(8,0,8,0), VerticalAlignment = VerticalAlignment.Center };
                tools.Children.Add(Action("전체 접기", () => FoldAllPresentation(true))); tools.Children.Add(Action("전체 펼치기", () => FoldAllPresentation(false)));
                ((Panel)_resume.Parent).Children.Remove(_resume); tools.Children.Add(_resume);
                ((Panel)_referenceStage.Parent).Children.Remove(_referenceStage); tools.Children.Add(_referenceStage);
                ((Panel)_direction.Parent).Children.Remove(_direction); tools.Children.Add(_direction);
                AddCraftWindowButton(tools); Grid.SetColumn(tools, 1); controls.Children.Add(tools); Grid.SetColumn(_category, 2); controls.Children.Add(_category); _header.Children.Add(controls);
            }
            if (_selectedCategory is not null && !snapshot.Groups.Any(g => g.Name == _selectedCategory)) { _selectedCategory = null; _hasCategoryChoice = false; }
            // Pick a starting category once per stage, never chase changing rankings or replace an explicit choice.
            if (stageChanged && !_hasCategoryChoice)
                _selectedCategory = SuggestedCategory() ?? snapshot.Groups.FirstOrDefault()?.Name;
            _category.ItemsSource = new[] { "전체 카테고리" }.Concat(snapshot.Groups.Select(g => CategoryDisplayName(g.Name))).ToArray();
            _category.SelectedItem = CategoryDisplayName(_selectedCategory ?? "전체 카테고리");
            var closestScope = snapshot.Stage == NormalCandidateStage.Legend ? "가까운 조합에는 전설 이상 유닛도 함께 표시돼요." : "";
            _category.Width = snapshot.Stage == NormalCandidateStage.Legend ? 190 : 130;
            _category.ToolTip = closestScope.Length == 0 ? null : closestScope;
            AutomationProperties.SetHelpText(_category, closestScope);
            _categoryChips.Visibility = snapshot.Stage == NormalCandidateStage.Utility ? Visibility.Visible : Visibility.Collapsed;
            _categoryChips.Children.Clear();
            foreach (var categoryName in new[] { "전체 카테고리" }.Concat(snapshot.Groups.Select(g => g.Name)))
            {
                var name = categoryName;
                var chip = Action(CategoryDisplayName(name), () => { _hasCategoryChoice = true; _selectedCategory = name == "전체 카테고리" ? null : name; Render(); });
                var active = name == (_selectedCategory ?? "전체 카테고리");
                var closestCategory = name == SuggestedCategory();
                chip.WithOverlayBackground(active ? OverlayChrome.SelectionKey : closestCategory ? OverlayChrome.RaisedKey : OverlayChrome.WellKey); chip.Foreground = closestCategory ? Mint : active ? Accent : Secondary;
                chip.BorderBrush = active ? RandyPickTheme.SelectionBorder : closestCategory ? RandyPickTheme.StrongBorder : Line;
                if (closestCategory) chip.ToolTip = "인식한 재료로 비교한 가까운 후보예요. 필요한 능력이 충분한지는 게임에서 확인해 주세요."; chip.BorderThickness = new(1); chip.FontSize = 10; chip.Padding = new(8, 4, 8, 4);
                AutomationProperties.SetAutomationId(chip, "normal-category-" + name); _categoryChips.Children.Add(chip);
            }
            var recommendation = snapshot.Groups.Where(g => _selectedCategory is null || g.Name == _selectedCategory)
                .SelectMany(g => g.Candidates).Where(CanHighlightForBrowsing).OrderBy(c => c.MissingLeafCount).ThenBy(c => c.RecipeStepCount).FirstOrDefault();
            DisplayedRecommendationId = recommendation?.Unit.Id;
            _banner.WithOverlayBackground(recommendation is null ? OverlayChrome.WellKey : OverlayChrome.RaisedKey);
            _banner.BorderBrush = recommendation is null ? RandyPickTheme.Border : RandyPickTheme.StrongBorder;
            _bannerTitle.Foreground = recommendation is null ? RandyPickTheme.Secondary : RandyPickTheme.Text;
            _bannerTitle.Text = StageTitle(_model.ProgressStage) + (_model.FollowingProgress ? "" : " · 선택한 단계 살펴보는 중");
            var banner = recommendation is null ? CandidateStatusCopy(_model.Snapshot.Status) : $"가까운 후보 · {recommendation.Unit.Name}";
            _freshnessLabels.Add(fresh => _bannerCaption.Text = fresh || !HasLastPlan ? banner : "이전에 가까웠던 후보 · " + (recommendation?.Unit.Name ?? "조합 후보") + " · 지금 유닛은 다시 확인 중");
            _bannerCaption.Foreground = _bannerTitle.Foreground;
            _detailWell.BorderBrush = OverlayTheme.PlanLine;
        }
        if (!IsMainWorkspace)
        {
            this.WithOverlayBackground(OverlayChrome.CanvasKey);
            _detailWell.WithOverlayBackground(OverlayChrome.CanvasKey); _detailWell.BorderBrush = OverlayTheme.PlanLine; _detailWell.Padding = new(CraftPanelPadding);
            if (!_header.Children.Contains(_upperSummary))
            {
                _header.Children.Clear(); _header.Children.Add(_overlayTitle); _header.Children.Add(_upperSummary);
                var tools = new WrapPanel();
                foreach (var pair in new[] { ("모두 접기", true), ("모두 펼치기", false) })
                { var fold = pair.Item2; var button = Action(pair.Item1, () => FoldAllPresentation(fold)); button.FontSize = 9; button.Padding = new(6,3,6,3); tools.Children.Add(button); }
                ((Panel)_resume.Parent).Children.Remove(_resume); _resume.FontSize = 9; _resume.Padding = new(6,3,6,3); tools.Children.Add(_resume);
                ((Panel)_referenceStage.Parent).Children.Remove(_referenceStage); tools.Children.Add(_referenceStage);
                ((Panel)_direction.Parent).Children.Remove(_direction); _direction.Width = 110; _direction.FontSize = 10; tools.Children.Add(_direction);
                AddCraftWindowButton(tools); _header.Children.Add(tools); _header.Children.Add(_status);
                _status.FontSize = 9; _status.TextWrapping = TextWrapping.NoWrap; _status.TextTrimming = TextTrimming.CharacterEllipsis;
            }
            _status.ToolTip = CandidateStatusCopy(snapshot.Status);
            _overlayTitle.Text = StageTitle(_model.ProgressStage);
            _upperSummary.Visibility = Visibility.Collapsed;
        }
        _presentedStage = snapshot.Stage;
        _resume.Visibility = CanResume ? Visibility.Visible : Visibility.Collapsed;
        _direction.Visibility = snapshot.Stage == NormalCandidateStage.Utility ? Visibility.Visible : Visibility.Collapsed;
        _direction.SelectedIndex = _model.UserDirection == "physical" ? 1 : _model.UserDirection == "magical" ? 2 : 0;
        RenderProgressControls();
        if (_openCraftWindow is not null) _openCraftWindow.IsEnabled = HasCraftSelection;
        _groups.Children.Clear();
        RenderMovementCoverage();
        foreach (var group in snapshot.Groups.Where(g => !IsMainWorkspace || _selectedCategory is null || g.Name == _selectedCategory))
        {
            var grid = new UniformGrid { Columns = Columns, Margin = IsMainWorkspace ? new(0, 0, 0, 2) : new(0, 3, 0, 3) };
            var highlighted = group.Candidates.FirstOrDefault(CanHighlightForBrowsing);
            var visibleCandidates = group.Candidates.Take(_visibleCounts.GetValueOrDefault(group.Name, IsMainWorkspace ? 10 : 5)).ToArray();
            foreach (var candidate in visibleCandidates)
            {
                var chosen = candidate.Unit.Id == _model.SelectedUnitId;
                var nearby = candidate == highlighted && CanHighlightForBrowsing(candidate);
                var unclassified = _model.DirectionUnverified(candidate.Unit.Id);
                var contents = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
                var icon = UnitImageFactory.Create(candidate.Unit.Image, candidate.Unit.Name, IsMainWorkspace ? 36 : 28, candidate.Unit.Id);
                icon.HorizontalAlignment = HorizontalAlignment.Center; contents.Children.Add(icon);
                var nameText = Text(candidate.Unit.Name, 12, White, true);
                nameText.TextWrapping = TextWrapping.NoWrap; nameText.TextTrimming = TextTrimming.CharacterEllipsis;
                nameText.ToolTip = candidate.Unit.Name;
                nameText.Margin = new(0, 3, 0, 0);
                nameText.TextAlignment = TextAlignment.Center; contents.Children.Add(nameText);
                var tier = Text(NormalCandidateBrowser.Tier(candidate.Unit), 9, Secondary); tier.Margin = new(0); tier.TextAlignment = TextAlignment.Center; contents.Children.Add(tier);
                var percent = FreshnessText(_model.IsDiagnosticReference ? (candidate.Completion is { } referenceRate ? $"재료 {referenceRate:P0}" : snapshot.IsCurrent ? "재료 확인 중" : "다시 확인 중") : candidate.Completion is { } rate ? (IsMainWorkspace ? $"{rate:P0}" : $"{rate:P0} 확보") : IsMainWorkspace ? "?" : "확보율 확인 불가", candidate.Completion is { } lastRate ? $"이전 {lastRate:P0}" : "지금 재료 확인 중", IsMainWorkspace ? 14 : 13, IsMainWorkspace ? Accent : chosen ? Gold : nearby ? Mint : Secondary, true); percent.Margin = new(0,2,0,0); percent.TextAlignment = TextAlignment.Center; contents.Children.Add(percent);
                AutomationProperties.SetHelpText(percent, _model.IsDiagnosticReference ? "인식한 유닛으로 계산한 재료 비율이에요. 실제 조합 가능 여부는 게임에서 확인해 주세요." : candidate.Completion is null ? "재료 확보율을 확인할 수 없어요." : "재료 확보율만 보여 줘요. 실제 조합 가능 여부는 게임에서 확인해 주세요.");
                var badge = chosen ? "선택됨" : nearby ? "가까움" : candidate.Owned || candidate.ObservedCount > 0 ? "인식됨" : "";
                var badgeText = FreshnessText(badge, chosen ? "선택됨" : nearby ? "이전에 가까웠음" : "", 9, chosen ? (IsMainWorkspace ? Accent : Gold) : nearby ? Mint : Secondary); badgeText.TextAlignment = TextAlignment.Right;
                badgeText.TextWrapping = TextWrapping.NoWrap; badgeText.TextTrimming = TextTrimming.CharacterEllipsis;
                badgeText.ToolTip = unclassified ? "물리·마법 유형을 알 수 없어요. 게임에서 확인해 주세요." : badge;
                if (IsMainWorkspace)
                {
                    contents.Children.Remove(icon);
                    var imageRow = new Grid(); imageRow.Children.Add(icon);
                    badgeText.HorizontalAlignment = HorizontalAlignment.Right; badgeText.VerticalAlignment = VerticalAlignment.Top;
                    badgeText.Margin = new(0); imageRow.Children.Add(badgeText); contents.Children.Insert(0, imageRow);
                }
                else contents.Children.Add(badgeText);
                AddCandidateGuide(contents, candidate);
                var button = new Button { Content = contents,
                    Foreground = White, BorderBrush = chosen ? RandyPickTheme.SelectionBorder : nearby ? RandyPickTheme.StrongBorder : Line, BorderThickness = new(1),
                    Margin = new(3), Padding = new(6), MinHeight = IsMainWorkspace ? 110 : 96, ToolTip = CandidateCaveatCopy(candidate.Caveat) + (unclassified ? " · 물리·마법 유형을 알 수 없어요" : ""),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch }.WithOverlayBackground(chosen ? OverlayChrome.SelectionKey : nearby ? OverlayChrome.RaisedKey : OverlayChrome.WellKey);
                if (!IsMainWorkspace)
                {
                    button.Content = OverlayCandidateCard(candidate, chosen, nearby, Array.IndexOf(visibleCandidates, candidate) + 1, unclassified);
                    button.WithOverlayBackground(chosen ? OverlayChrome.SelectionKey : nearby ? OverlayChrome.RaisedKey : OverlayChrome.WellKey);
                    button.Padding = new(4); button.Margin = new(2); button.MinHeight = 80;
                }
                AutomationProperties.SetAutomationId(button, "normal-candidate-" + candidate.Unit.Id);
                AutomationProperties.SetName(button, candidate.Unit.Name + " · " + (chosen ? "선택됨 · " : "") + CandidateCaveatCopy(candidate.Caveat));
                AutomationProperties.SetHelpText(button, CandidateCaveatCopy(candidate.Caveat) + (unclassified ? " 물리·마법 유형은 게임에서 확인해 주세요." : ""));
                button.Click += (_, _) =>
                {
                    var already = _model.SelectedUnitId == candidate.Unit.Id;
                    _selectionCategory = already ? null : group.Name;
                    _selectionCategoryUnit = already ? null : candidate.Unit.Id;
                    var selection = FoldChoices.GetValue(_model, _ => new FoldPresentation());
                    selection.SelectedCategory = already ? selection.SelectedCategory : group.Name;
                    selection.SelectedUnitId = already ? null : candidate.Unit.Id;
                    if (already) _model.ClearSelection(); else _model.Select(candidate.Unit.Id);
                    CraftWindowRequested?.Invoke();
                }; grid.Children.Add(button);
            }
            var groupContent = new StackPanel(); groupContent.Children.Add(grid);
            if (group.Candidates.Count > visibleCandidates.Length) groupContent.Children.Add(Action("후보 더 보기", () => { _visibleCounts[group.Name] = visibleCandidates.Length + (IsMainWorkspace ? 10 : 5); Render(); }));
            var heading = Text($"{CategoryDisplayName(group.Name)}  {group.Candidates.Count} 후보" + (highlighted is not null ? " · 재료가 가까운 후보 있음" : "") + (group.Candidates.Count > visibleCandidates.Length ? $" · {visibleCandidates.Length}개 표시" : ""), 11, Secondary, false);
            var expander = new Expander { Header = new CandidateHeader(heading.Text, heading), HeaderTemplate = CandidateHeaderTemplate, Content = groupContent, Foreground = White, IsExpanded = IsCategoryExpanded(group.Name), Margin = new(0, 3, 0, 6) };
            expander.Expanded += (_, _) => { if (!_painting) FoldPresentationChoice(group.Name, false); };
            expander.Collapsed += (_, _) => { if (!_painting) FoldPresentationChoice(group.Name, true); };
            if (visibleCandidates.Length == 0) expander.Content = Text("이 종류에는 표시할 후보가 없어요.", 12, Secondary);
            if (IsMainWorkspace) _groups.Children.Add(expander);
            else
            {
                var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                row.Children.Add(Text(CategoryDisplayName(group.Name) + "  " + group.Candidates.Count, 11, White, true));
                var nearest = Text(_model.IsDiagnosticReference ? "재료 비교" : highlighted is null ? "재료 확인 중" : highlighted.Unit.Name + " " + $"{highlighted.Completion:P0}", 9, highlighted is null ? Secondary : Mint);
                Grid.SetColumn(nearest, 1); row.Children.Add(nearest);
                expander.Header = new CandidateHeader(CategoryDisplayName(group.Name) + " · 후보 " + group.Candidates.Count + "개 · " + nearest.Text, row);
                expander.Margin = new(0);
                AutomationProperties.SetAutomationId(expander, "overlay-category-" + group.Name);
                _groups.Children.Add(new Border { Child = expander, BorderBrush = Line, BorderThickness = new(1), CornerRadius = new(7), Padding = new(6,2,6,2), Margin = new(0,0,0,5) }.WithOverlayBackground(expander.IsExpanded && highlighted is not null ? OverlayChrome.RaisedKey : OverlayChrome.WellKey));
            }
        }
        if (snapshot.Groups.Count == 0) _groups.Children.Add(Text(snapshot.Stage == NormalCandidateStage.Utility ? "지원 능력이나 물리·마법 유형을 확인하면 후보를 볼 수 있어요." : "지금 표시할 조합 후보가 없어요.", 13, Secondary));
        RenderDetail();
        UpdateResponsiveLayout();
        _painting = false;
        _browsingRenderKey = BrowsingRenderKey();
        RefreshFreshnessLabels();
        _candidateScroll.ScrollToVerticalOffset(offset);
    }
    private bool _compactCraft;
    private void UpdateResponsiveLayout()
    {
        var compact = _craftDetached ? _renderCraft : IsMainWorkspace && ActualHeight > 0 && ActualHeight < 380;
        var split = !_craftDetached && IsMainWorkspace && ActualWidth >= 640 && _model?.SelectedUnit is not null;
        var root = (Grid)Content;
        root.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        root.ColumnDefinitions[1].Width = new GridLength(split ? Math.Min(CraftPanelMaxWidth + CraftGap, ActualWidth * 0.6) : 0);
        if (!_craftDetached)
        {
            Grid.SetRow(_detailWell, split ? 1 : 2); Grid.SetColumn(_detailWell, split ? 1 : 0);
            Grid.SetColumnSpan(_detailWell, split ? 1 : 2);
        }
        var detailHeight = _craftDetached || split ? 0 : _model?.SelectedUnit is null ? 46 : IsMainWorkspace
            ? compact ? 96 : ActualHeight < 560 ? 150 : 195
            : ActualWidth < CraftSplitWidth + 30 ? Math.Clamp(ActualHeight * 0.7, 300, 420)
            : Math.Clamp(ActualHeight * 0.45, 220, 290);
        if (_selectedDetailRow.Height.Value != detailHeight) _selectedDetailRow.Height = new GridLength(detailHeight);
        if (_compactCraft != compact)
        {
            _compactCraft = compact;
            RenderDetail();
        }
        if (IsMainWorkspace)
        {
            _detailWell.Padding = new(CraftPanelPadding); _detailWell.Margin = _craftDetached ? new(0) : split ? new(CraftGap,0,0,0) : new(0,CraftGap,0,0);
            _bannerCaption.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            _banner.ToolTip = _bannerCaption.Text;
            _categoryChips.Visibility = !compact && _model?.Snapshot.Stage == NormalCandidateStage.Utility ? Visibility.Visible : Visibility.Collapsed;
            _categoryChips.MaxWidth = Math.Max(150, ActualWidth - 490);
            foreach (var summary in _detail.Children.OfType<TextBlock>().Where(text => AutomationProperties.GetAutomationId(text) == "normal-detail-source-summary"))
                summary.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }
    }
    private UIElement OverlayCandidateCard(NormalCandidate candidate, bool chosen, bool nearby, int rank, bool unclassified)
    {
        var body = new StackPanel();
        var top = new Grid();
        top.Children.Add(Text(rank.ToString(), 8, Secondary));
        var badge = FreshnessText(chosen ? "선택됨" : nearby ? "가까움" : candidate.ObservedCount > 0 || candidate.Owned ? "인식됨" : unclassified ? "유형 미확인" : "", chosen ? "선택됨" : nearby ? "이전에 가까웠음" : "", 8, chosen ? Gold : nearby ? Mint : Secondary);
        badge.HorizontalAlignment = HorizontalAlignment.Right; top.Children.Add(badge); body.Children.Add(top);
        var icon = UnitImageFactory.Create(candidate.Unit.Image, candidate.Unit.Name, 25, candidate.Unit.Id); icon.HorizontalAlignment = HorizontalAlignment.Center; body.Children.Add(icon);
        var name = Text(candidate.Unit.Name, 10, White, true); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; name.TextAlignment = TextAlignment.Center; name.ToolTip = candidate.Unit.Name; body.Children.Add(name);
        var bottom = new TextBlock { TextAlignment = TextAlignment.Center, FontSize = 9, Margin = new(0,2,0,1) };
        bottom.Inlines.Add(new System.Windows.Documents.Run(NormalCandidateBrowser.Tier(candidate.Unit) + " ") { Foreground = Secondary });
        var ratioRun = new System.Windows.Documents.Run { Foreground = chosen ? Gold : nearby ? Mint : Accent, FontWeight = FontWeights.Bold };
        _freshnessLabels.Add(fresh => ratioRun.Text = candidate.Completion is { } ratio ? (fresh ? "" : "이전 ") + $"{ratio:P0}" : "미확인");
        bottom.Inlines.Add(ratioRun);
        body.Children.Add(bottom); AddCandidateGuide(body, candidate, compact: true);
        return body;
    }
    private string? SuggestedCategory()
    {
        var groups = _model?.Snapshot.Groups;
        if (groups is null) return null;
        if (_model!.Stage == NormalCandidateStage.Legend && groups.Any(group => group.Name == "스토리")) return "스토리";
        var partners = groups.FirstOrDefault(g => g.Name == "주력 궁합" && g.Candidates.Any(c => _model.CanHighlight(c)));
        return partners?.Name ?? groups.Select(g => new { g.Name, Candidate = g.Candidates.FirstOrDefault(c => _model!.CanHighlight(c)) })
            .Where(g => g.Candidate is not null).OrderBy(g => g.Candidate!.MissingLeafCount)
            .ThenBy(g => g.Candidate!.RecipeStepCount).FirstOrDefault()?.Name;
    }
    private bool IsCategoryExpanded(string category)
    {
        if (_model is null) return false;
        if (_model.Stage != NormalCandidateStage.Legend && _model.IsDiagnosticReference)
            return !_model.CollapsedCategories.Contains(category);
        if (IsMainWorkspace || FoldChoices.GetValue(_model, _ => new FoldPresentation()).HasUserChoice || _model.CollapsedCategories.Count > 0)
            return !_model.CollapsedCategories.Contains(category);
        return category == SuggestedCategory();
    }
    private void FoldPresentationChoice(string category, bool collapsed)
    {
        if (_model is null) return;
        var state = FoldChoices.GetValue(_model, _ => new FoldPresentation());
        if (!IsMainWorkspace && !state.HasUserChoice && _model.CollapsedCategories.Count == 0)
            foreach (var group in _model.Snapshot.Groups.Where(g => g.Name != SuggestedCategory())) _model.CollapsedCategories.Add(group.Name);
        state.HasUserChoice = true; _model.Fold(category, collapsed);
    }
    private void FoldAllPresentation(bool collapsed)
    {
        if (_model is null) return;
        FoldChoices.GetValue(_model, _ => new FoldPresentation()).HasUserChoice = true;
        _model.FoldAll(collapsed);
        Render();
    }
    internal static string CandidateCaveatCopy(string caveat) => caveat switch
    {
        "현재 패 미확인" => "지금 유닛을 확인할 수 없어 재료를 비교하지 못해요.",
        "조합 재료 근거 미확인" => "조합 재료를 확인할 수 없어 확보율을 계산하지 못해요.",
        "선택형 재료가 포함되어 재료 비율을 계산하지 못해요." => "선택해야 하는 재료가 있어 확보율을 계산할 수 없어요. 게임에서 확인해 주세요.",
        "재료 확보율 · 조합 조건 별도 확인" => "재료 확보율만 보여 줘요. 다른 조합 조건은 게임에서 확인해 주세요.",
        "인식한 유닛 기준 재료 비율 · 조합 가능 여부는 게임에서 확인해 주세요." => "인식한 유닛으로 계산한 재료 비율이에요. 실제 조합 가능 여부는 게임에서 확인해 주세요.",
        "카드 재료 계산 대상 없음" => "이 유닛은 재료 확보율을 계산할 수 없어요.",
        "재료 계산 근거 미확인" => "재료를 계산하지 못했어요. 게임에서 확인해 주세요.",
        _ => caveat
    };
    private static string CandidateStatusCopy(string status) => status
        .Replace("추천 일시정지 · 재개를 누르면 현재 패를 다시 확인합니다", "추천을 멈췄어요. 재개하면 현재 유닛을 다시 확인해요.", StringComparison.Ordinal)
        .Replace("인식한 패 기준 참고 안내", "인식한 유닛을 기준으로 참고해 주세요", StringComparison.Ordinal)
        .Replace(Map2322KaidoAirRole.ConditionalNote, "카이도는 용 형태일 때만 공중 이동을 확인했어요. 현재 형태는 게임에서 확인해 주세요.", StringComparison.Ordinal);
    private static string CategoryDisplayName(string name) => name == "가까운 조합" ? "가까운 조합 · 전설 이상도 포함" : name.Replace("물딜+", "물리 · ", StringComparison.Ordinal).Replace("마딜+", "마법 · ", StringComparison.Ordinal);
    private string CanonicalCategoryName(string displayName) => displayName.StartsWith("가까운 조합", StringComparison.Ordinal) ? "가까운 조합" : _model?.Snapshot.Groups.FirstOrDefault(group => CategoryDisplayName(group.Name) == displayName)?.Name ?? displayName;
    private static string RoleDisplayValue(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => "수치 미표기", "true" => "역할 있음", "ture" => "역할 여부 확인 필요", "0" => "0", _ => value!
    };
    // The shared Expander template uses Header as the toggle's accessible Name.
    // A raw visual header would otherwise be announced as its WPF class name.
    private sealed record CandidateHeader(string Name, UIElement Content)
    {
        public override string ToString() => Name;
    }
    private readonly DataTemplate CandidateHeaderTemplate = CreateCandidateHeaderTemplate();
    private static DataTemplate CreateCandidateHeaderTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(CandidateHeader.Content)));
        return new DataTemplate { VisualTree = presenter };
    }
    private static Button Action(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new(9, 5, 9, 5), Margin = new(0, 0, 5, 0), Foreground = Secondary, BorderBrush = Line, BorderThickness = new(1) }.WithOverlayBackground(OverlayChrome.WellKey);
        button.Click += (_, _) => action(); return button;
    }
    private static TextBlock Text(string text, double size, Brush color, bool bold = false) => new() { Text = text, FontSize = size, Foreground = color, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new(0, 2, 0, 2) };
    private static Brush Brush(string value) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value)!; brush.Freeze(); return brush; }
}
