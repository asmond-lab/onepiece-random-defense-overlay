using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Text;

namespace OrandOverlay;

/// <summary>
/// 지금 할 일 / 조합 흐름 / 초상화 보드 3구역.
/// </summary>
internal static class RecommendationBoard
{
    /// <summary>창 폭은 이 칸 3개가 한 화면에 들어오는 크기로 맞춘다.</summary>
    public const int FlowVisibleSteps = 3;
    public const double FlowNodeWidth = 140;
    public static void Fill(
        Panel nowPanel,
        Panel flowPanel,
        Panel boardPanel,
        IReadOnlyList<Recommendation> recs,
        IReadOnlyList<AutoCombineStep> plan,
        string? selectedId,
        Action<string> onSelect,
        string? banner = null,
        IReadOnlyList<Recommendation>? selectedChildren = null,
        string? clusterHeadId = null,
        PlannerEvidenceView? plannerEvidence = null,
        BulletOperatingBoard? bulletOperatingBoard = null,
        bool showRouteRootAsCurrentCraft = false)
    {
        nowPanel.Children.Clear();
        flowPanel.Children.Clear();
        boardPanel.Children.Clear();

        if (bulletOperatingBoard is not null)
            boardPanel.Children.Add(BulletOperatingBoardBlock(bulletOperatingBoard));
        else if (plannerEvidence is not null)
            boardPanel.Children.Add(PlannerEvidenceBlock(plannerEvidence));

        if (recs.Count == 0)
        {
            if (plannerEvidence is
                {
                    State: PlannerEvidenceState.SequenceStoryReward or
                    PlannerEvidenceState.SequenceRareReward or
                    PlannerEvidenceState.SequenceTopNavigation
                })
            {
                var nextAction = plannerEvidence[PlannerEvidenceFieldKind.Action].DisplayValue;
                if (string.IsNullOrWhiteSpace(nextAction))
                    nextAction = banner ?? "현재 스토리 행동을 이어가세요.";
                nowPanel.Children.Add(PendingRecommendationCard(nextAction));
                return;
            }

            var progressMessage = !string.IsNullOrWhiteSpace(banner)
                ? banner
                : plannerEvidence is null
                    ? null
                    : plannerEvidence[PlannerEvidenceFieldKind.Action].DisplayValue;
            var storyProgress = !string.IsNullOrWhiteSpace(progressMessage) &&
                                (banner is not null || plannerEvidence?.State is
                                    PlannerEvidenceState.SequenceStoryReward or
                                    PlannerEvidenceState.SequenceFirstLegend or
                                    PlannerEvidenceState.SequenceRareReward or
                                    PlannerEvidenceState.SequenceTopNavigation);
            var emptyState = new TextBlock
            {
                Text = storyProgress ? "현재 단계 진행 중" : "패 인식 대기 중",
                Foreground = OverlayTheme.MutedBrush,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 10, 0, 10)
            };
            AutomationProperties.SetAutomationId(
                emptyState, storyProgress
                    ? "story-progress-state"
                    : "recognition-waiting-state");
            nowPanel.Children.Add(emptyState);
            nowPanel.Children.Add(new TextBlock
            {
                Text = storyProgress
                    ? progressMessage!
                    : "게임이 잡히면 지금 할 일과 후보 보드가 여기에 뜹니다.",
                Foreground = OverlayTheme.MutedBrush,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 0, 0, 4)
            });
            return;
        }

        var hideIngredientCards = plannerEvidence?.State is
            PlannerEvidenceState.SequenceStoryReward or
            PlannerEvidenceState.SequenceFirstLegend or
            PlannerEvidenceState.SequenceRareReward or
            PlannerEvidenceState.SequenceTopNavigation;
        var children = hideIngredientCards ? [] : selectedChildren ?? [];
        var selected = BoardSelection.Resolve(recs, children, selectedId) ?? recs[0];
        var viewingChild = BoardSelection.Contains(children, selected.Route.Id);
        var clusterHead = BoardSelection.Find(recs,
                             BoardSelection.ClusterHeadId(recs, children, selected.Route.Id, clusterHeadId))
                         ?? recs[0];
        var nowPlan = viewingChild ? Array.Empty<AutoCombineStep>() : plan;

        if (!string.IsNullOrWhiteSpace(banner))
            nowPanel.Children.Add(new TextBlock
            {
                Text = banner,
                Foreground = OverlayTheme.WarnBrush,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });

        nowPanel.Children.Add(NowBlock(selected, nowPlan, showRouteRootAsCurrentCraft));
        flowPanel.Children.Add(FlowBlock(selected));
        var missingLeaves = RecommendationPresentation.BoardMissingLeaves(
            selected.RecipeProgress, viewingChild);
        if (missingLeaves.Count > 0)
        {
            if (viewingChild)
                boardPanel.Children.Add(new TextBlock
                {
                    Text = "부족한 흔함",
                    Foreground = OverlayTheme.MutedBrush,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            var missing = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var leaf in missingLeaves.Take(12))
                missing.Children.Add(OverlayTheme.MissingChip(
                    UnitImageFactory.Create(leaf.Image, leaf.Name, 24, leaf.UnitId),
                    RecommendationPresentation.CraftUnitName(leaf.Name, leaf.Tier),
                    leaf.MissingCount));
            boardPanel.Children.Add(missing);
        }
        var tiles = new WrapPanel();
        var childIds = children.Select(item => item.Route.GoalUnitId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var rec in recs)
        {
            var isHead = BoardSelection.Matches(rec, clusterHead.Route.Id);
            if (!isHead && childIds.Contains(rec.Route.GoalUnitId)) continue;
            tiles.Children.Add(isHead
                ? RenderCluster(new BoardCluster(rec, children), selected.Route.Id, onSelect)
                : BoardTile(rec, BoardSelection.Matches(rec, selected.Route.Id), onSelect));
        }
        boardPanel.Children.Add(tiles);
    }

    private static FrameworkElement PendingRecommendationCard(string nextAction)
    {
        var stack = new StackPanel();
        var header = new Grid { Margin = OverlayTheme.PlannerHeaderMargin };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "추천은 계속됩니다",
            Foreground = OverlayTheme.WhiteBrush,
            FontSize = OverlayTheme.PlannerTitleTypeSize,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap
        });
        var status = new TextBlock
        {
            Text = "상위 추천 준비 중",
            Foreground = OverlayTheme.GoldBrush,
            FontSize = OverlayTheme.PlannerStateTypeSize,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(status, "pending-recommendation-status");
        AutomationProperties.SetName(status, "상태");
        AutomationProperties.SetItemStatus(status, "상위 추천 준비 중");
        Grid.SetColumn(status, 1);
        header.Children.Add(status);
        stack.Children.Add(header);
        stack.Children.Add(PendingRecommendationRow(
            "pending-recommendation-action", "다음 행동", nextAction,
            OverlayTheme.GoldBrush));
        stack.Children.Add(PendingRecommendationRow(
            "pending-recommendation-reason", "이유",
            "희귀 보상 결과가 상위 경로를 바꿀 수 있어 결과를 먼저 반영합니다."));
        stack.Children.Add(PendingRecommendationRow(
            "pending-recommendation-resume", "자동 재개",
            "희귀위습 결과가 반영되면 상위·항법 추천이 자동으로 다시 표시됩니다."));

        AutomationProperties.SetAutomationId(stack, "pending-recommendation-card");
        AutomationProperties.SetName(stack, "추천은 계속됩니다");
        AutomationProperties.SetItemStatus(stack, "상위 추천 준비 중");
        return stack;
    }

    private static FrameworkElement PendingRecommendationRow(
        string automationId, string label, string value, Brush? valueBrush = null)
    {
        var row = new Grid { Margin = OverlayTheme.PlannerRowMargin };
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(OverlayTheme.PlannerLabelColumnWidth)
        });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = OverlayTheme.MutedBrush,
            FontSize = OverlayTheme.PlannerLabelTypeSize,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.NoWrap
        });
        var text = new TextBlock
        {
            Text = KeepKoreanWordsTogether(value),
            Foreground = valueBrush ?? OverlayTheme.WhiteBrush,
            FontSize = OverlayTheme.PlannerValueTypeSize,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        AutomationProperties.SetAutomationId(row, automationId);
        AutomationProperties.SetName(row, label);
        AutomationProperties.SetItemStatus(row, value);
        return row;
    }

    internal static FrameworkElement PlannerEvidenceBlock(PlannerEvidenceView evidence)
    {
        var stack = new StackPanel();
        var title = new Grid { Margin = OverlayTheme.PlannerHeaderMargin };
        title.ColumnDefinitions.Add(new ColumnDefinition());
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.Children.Add(new TextBlock
        {
            Text = "적응형 판단 근거",
            Foreground = OverlayTheme.WhiteBrush,
            FontSize = OverlayTheme.PlannerTitleTypeSize,
            FontWeight = FontWeights.Bold
        });
        var state = new TextBlock
        {
            Text = evidence[PlannerEvidenceFieldKind.Phase].DisplayValue,
            Foreground = OverlayTheme.GoldBrush,
            FontFamily = new FontFamily("Consolas, Malgun Gothic"),
            FontSize = OverlayTheme.PlannerStateTypeSize,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(state, 1);
        title.Children.Add(state);
        stack.Children.Add(title);

        foreach (var field in evidence.Fields.Where(item =>
                     item.Kind != PlannerEvidenceFieldKind.Phase))
            stack.Children.Add(PlannerEvidenceRow(field));

        return new Border
        {
            Background = OverlayTheme.RowAltBrush,
            BorderBrush = OverlayTheme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(OverlayTheme.TileRadius),
            Padding = OverlayTheme.PlannerBlockPadding,
            Margin = OverlayTheme.PlannerBlockMargin,
            Child = stack
        };
    }

    internal static FrameworkElement BulletOperatingBoardBlock(BulletOperatingBoard board)
    {
        var stack = new StackPanel();
        var header = new Grid { Margin = OverlayTheme.PlannerHeaderMargin };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock
        {
            Text = "Bullet",
            Foreground = OverlayTheme.WhiteBrush,
            FontSize = OverlayTheme.PlannerTitleTypeSize,
            FontWeight = FontWeights.Bold
        };
        AutomationProperties.SetAutomationId(title, "bullet-board-title");
        AutomationProperties.SetName(title, "Bullet");
        AutomationProperties.SetItemStatus(title, "Bullet");
        header.Children.Add(title);
        var phase = new TextBlock
        {
            Text = board.Phase,
            Foreground = OverlayTheme.GoldBrush,
            FontFamily = new FontFamily("Consolas, Malgun Gothic"),
            FontSize = OverlayTheme.PlannerStateTypeSize,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(phase, "bullet-board-phase");
        AutomationProperties.SetName(phase, "운영 단계");
        AutomationProperties.SetItemStatus(phase, board.Phase);
        Grid.SetColumn(phase, 1);
        header.Children.Add(phase);
        stack.Children.Add(header);
        stack.Children.Add(BulletRow("bullet-board-round", "현재 라운드",
            board.Round.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        stack.Children.Add(BulletRow("bullet-board-confidence", "추천 신뢰도",
            board.Confidence));
        stack.Children.Add(BulletRow("bullet-board-action", "즉시 할 일", board.Action,
            OverlayTheme.GoldBrush));
        stack.Children.Add(BulletRow("bullet-board-objective", "목표", board.Objective));
        for (var index = 0; index < board.Routes.Length; index++)
            stack.Children.Add(BulletRow($"bullet-board-route-{index + 1}",
                $"실행 경로 {index + 1}", board.Routes[index]));
        stack.Children.Add(BulletRow("bullet-board-focus", "능력/장비", board.Focus));
        stack.Children.Add(BulletRow("bullet-board-gate", "다음 관문", board.Gate));
        if (board.CheckNeeded is { Length: > 0 } checkNeeded)
            stack.Children.Add(BulletRow("bullet-board-check-needed", "확인 필요", checkNeeded,
                OverlayTheme.WarnBrush));
        if (board.Recovery is { Length: > 0 } recovery)
            stack.Children.Add(BulletRow("bullet-board-recovery", "회복 행동", recovery,
                OverlayTheme.WarnBrush));
        foreach (var field in board.Fields)
            stack.Children.Add(BulletRow(field.AutomationId, field.Label, field.DisplayValue,
                field.IsWarning ? OverlayTheme.WarnBrush : OverlayTheme.WhiteBrush));

        var root = new Border
        {
            Background = OverlayTheme.RowAltBrush,
            BorderBrush = OverlayTheme.HairlineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(OverlayTheme.TileRadius),
            Padding = OverlayTheme.PlannerBlockPadding,
            Margin = OverlayTheme.PlannerBlockMargin,
            Child = stack
        };
        AutomationProperties.SetAutomationId(root, "bullet-operating-board");
        AutomationProperties.SetName(root, "Bullet 운영 보드");
        AutomationProperties.SetItemStatus(root,
            $"라운드 {board.Round}, {board.Phase}, {board.Confidence}");
        return root;
    }

    private static FrameworkElement BulletRow(string automationId, string label, string value,
        Brush? valueBrush = null)
    {
        var row = new Grid { Margin = OverlayTheme.PlannerRowMargin };
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(OverlayTheme.PlannerLabelColumnWidth)
        });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = OverlayTheme.MutedBrush,
            FontSize = OverlayTheme.PlannerLabelTypeSize,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.NoWrap
        });
        var displayedValue = KeepKoreanWordsTogether(value);
        var text = new TextBlock
        {
            Text = displayedValue,
            Foreground = valueBrush ?? OverlayTheme.WhiteBrush,
            FontSize = OverlayTheme.PlannerValueTypeSize,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        AutomationProperties.SetAutomationId(row, automationId);
        AutomationProperties.SetName(row, label);
        AutomationProperties.SetItemStatus(row, value);
        return row;
    }

    private static FrameworkElement PlannerEvidenceRow(PlannerEvidenceField field)
    {
        var row = new Grid { Margin = OverlayTheme.PlannerRowMargin };
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(OverlayTheme.PlannerLabelColumnWidth)
        });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock
        {
            Text = field.Label,
            Foreground = OverlayTheme.MutedBrush,
            FontSize = OverlayTheme.PlannerLabelTypeSize,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        var value = new TextBlock
        {
            Text = KeepKoreanWordsTogether(field.DisplayValue),
            Foreground = field.IsWarning
                ? OverlayTheme.WarnBrush
                : field.Kind == PlannerEvidenceFieldKind.Sequence
                    ? OverlayTheme.GoldBrush
                    : OverlayTheme.WhiteBrush,
            FontSize = OverlayTheme.PlannerValueTypeSize,
            TextWrapping = TextWrapping.WrapWithOverflow
        };
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        AutomationProperties.SetAutomationId(row, field.AutomationId);
        AutomationProperties.SetName(row, field.AccessibilityName);
        AutomationProperties.SetItemStatus(row, field.AccessibilityValue);
        return row;
    }

    internal static string KeepKoreanWordsTogether(string value)
    {
        var semanticValue = value.Replace(
            "회복 가능성을 다시 계산합니다.",
            "회복\u00A0가능성을\u00A0다시\u00A0계산합니다.",
            StringComparison.Ordinal);
        var result = new StringBuilder(semanticValue.Length);
        for (var index = 0; index < semanticValue.Length; index++)
        {
            if (index > 0 && IsHangulSyllable(semanticValue[index - 1]) &&
                IsHangulSyllable(semanticValue[index]))
                result.Append('\u2060');
            result.Append(semanticValue[index]);
        }
        return result.ToString();
    }

    private static bool IsHangulSyllable(char value) => value is >= '\uAC00' and <= '\uD7A3';

    public readonly record struct BoardCluster(Recommendation Head, IReadOnlyList<Recommendation> Children);

    public static IReadOnlyList<BoardCluster> Clusters(IReadOnlyList<Recommendation> recs)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clusters = new List<BoardCluster>();
        foreach (var rec in recs)
        {
            if (rec.ClusterParentUnitId is { Length: > 0 }) continue;
            if (!used.Add(rec.Route.GoalUnitId)) continue;
            var children = recs
                .Where(child => child.ClusterParentUnitId is { } parent &&
                                parent.Equals(rec.Route.GoalUnitId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var child in children)
                used.Add(child.Route.GoalUnitId);
            clusters.Add(new BoardCluster(rec, children));
        }
        foreach (var rec in recs)
        {
            if (!used.Add(rec.Route.GoalUnitId)) continue;
            clusters.Add(new BoardCluster(rec, []));
        }
        return clusters;
    }

    private static UIElement NowBlock(Recommendation selected, IReadOnlyList<AutoCombineStep> plan,
        bool showRouteRootAsCurrentCraft)
    {
        var unit = selected.CompositionUnits[0];
        var currentStep = plan.Count > 0 && !showRouteRootAsCurrentCraft
            ? selected.RemainingCraftSteps.FirstOrDefault(step =>
                step.UnitId.Equals(plan[0].TargetUnitId, StringComparison.OrdinalIgnoreCase))
            : null;
        var currentCraftName = plan.Count > 0 && !showRouteRootAsCurrentCraft
            ? plan[0].TargetName
            : RecommendationPresentation.CraftUnitName(unit);
        var currentCraftProgress = currentStep is null || showRouteRootAsCurrentCraft
            ? RecommendationPresentation.CompletionPercent(selected.RecipeProgress)
            : $"{Math.Round(currentStep.CompletionRatio * 100,
                MidpointRounding.AwayFromZero):0}%";
        var icon = UnitImageFactory.Create(currentStep?.Image ?? unit.Image,
            currentStep?.Name ?? unit.Name, 48, currentStep?.UnitId ?? unit.UnitId);
        icon.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(icon, "current-craft-icon");
        AutomationProperties.SetName(icon, currentCraftName);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = showRouteRootAsCurrentCraft
                ? "첫 희귀함 목표"
                : plan.Count > 0 ? "지금 조합" : "다음 행동",
            Foreground = OverlayTheme.GoldBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        });
        var craftTitle = new TextBlock
        {
            Text = currentCraftName,
            Foreground = OverlayTheme.WhiteBrush,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 42
        };
        AutomationProperties.SetAutomationId(craftTitle, "current-craft-title");
        AutomationProperties.SetName(craftTitle, "현재 추천 이름");
        AutomationProperties.SetItemStatus(craftTitle, currentCraftName);
        text.Children.Add(craftTitle);
        var hostLine = RecommendationPresentation.GreenBloodHostLine(selected, compact: false);
        if (hostLine is not null)
            text.Children.Add(new TextBlock
            {
                Text = hostLine,
                Foreground = OverlayTheme.GoldBrush,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0)
            });

        if (plan.Count > 0)
        {
            var step = plan[0];
            text.Children.Add(new TextBlock
            {
                Text = $"{step.TriggerName} 선택 → {step.TargetName}" +
                       (plan.Count > 1 ? $"  외 {plan.Count - 1}건" : ""),
                Foreground = OverlayTheme.MutedBrush,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 4)
            });
            var commands = step.Commands.Count > 0
                ? step.Commands
                : step.Key is { Length: > 0 } key ? (IReadOnlyList<string>)[key] : [];
            if (commands.Count > 0) text.Children.Add(OverlayTheme.Keycaps(commands));
        }
        else
        {
            text.Children.Add(new TextBlock
            {
                Text = selected.NextAction,
                Foreground = OverlayTheme.MutedBrush,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 4)
            });
            if (selected.CombineCommands.Count > 0)
                text.Children.Add(OverlayTheme.Keycaps(selected.CombineCommands));
        }

        if (selected.Warnings.Count > 0)
            text.Children.Add(new TextBlock
            {
                Text = selected.Warnings[0],
                Foreground = OverlayTheme.WarnBrush,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });

        var meta = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 0, 0, 0),
            MinWidth = 72,
            MaxWidth = 132
        };
        var progress = new TextBlock
        {
            Text = currentCraftProgress,
            Foreground = OverlayTheme.GoldBrush,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Right
        };
        AutomationProperties.SetAutomationId(progress, "current-craft-progress");
        AutomationProperties.SetName(progress, "현재 추천 진행률");
        AutomationProperties.SetItemStatus(progress, currentCraftProgress);
        meta.Children.Add(progress);
        foreach (var line in currentStep is null
                     ? RecommendationPresentation.NowAbilityLines(unit)
                     : Array.Empty<string>())
            meta.Children.Add(new TextBlock
            {
                Text = line,
                Foreground = OverlayTheme.WhiteBrush,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Right,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        icon.Margin = new Thickness(0, 0, 12, 0);
        row.Children.Add(icon);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        Grid.SetColumn(meta, 2);
        row.Children.Add(meta);
        AutomationProperties.SetAutomationId(row, "current-craft");
        AutomationProperties.SetName(row, "현재 추천");
        AutomationProperties.SetItemStatus(row,
            $"{currentCraftName} · 진행률 {currentCraftProgress}");
        return row;
    }

    private static UIElement FlowBlock(Recommendation selected)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = selected.ProgressionGoalName is { Length: > 0 } target
                ? $"{selected.CompositionUnits[0].Name}부터 {target}까지 조합 흐름"
                : "조합 흐름",
            Foreground = OverlayTheme.MutedBrush,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var steps = selected.RemainingCraftSteps;
        if (steps.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "바로 조합 가능",
                Foreground = OverlayTheme.OkBrush,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            });
        }
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var currentId = steps.FirstOrDefault(step => step.MissingCount > 0)?.UnitId
                            ?? steps[0].UnitId;
            for (var i = 0; i < steps.Count; i++)
            {
                if (i > 0) row.Children.Add(Arrow());
                row.Children.Add(FlowNode(steps[i],
                    steps[i].UnitId.Equals(currentId, StringComparison.OrdinalIgnoreCase)));
            }
            stack.Children.Add(row);
        }

        return stack;
    }

    private static UIElement FlowNode(RecipeCraftStep step, bool current)
    {
        var keys = RecommendationPresentation.CraftActionKeys(step);
        var selectName = RecommendationPresentation.CraftSelectUnitName(step);
        var missingNames = RecommendationPresentation.CraftMissingIngredientNames(step);
        var companions = RecommendationPresentation.CraftCompanionNames(step);
        var select = step.Ingredients.OrderBy(item => item.SelectionOrder).FirstOrDefault();
        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Width = FlowNodeWidth };
        var icon = UnitImageFactory.Create(step.Image, step.Name, 40, step.UnitId);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        body.Children.Add(icon);
        body.Children.Add(new TextBlock
        {
            Text = RecommendationPresentation.SafeText(step.Name).Trim(),
            Foreground = OverlayTheme.WhiteBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 30,
            Margin = new Thickness(0, 3, 0, 0)
        });
        body.Children.Add(new TextBlock
        {
            Text = $"{RecommendationPresentation.FlowRemainingCount(step)}개 남음",
            Foreground = current ? OverlayTheme.GoldBrush : OverlayTheme.MutedBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center
        });
        if (selectName is { Length: > 0 } && select is not null)
        {
            var pick = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0)
            };
            var pickIcon = UnitImageFactory.Create("", select.Name, 18, select.UnitId);
            pickIcon.VerticalAlignment = VerticalAlignment.Center;
            pickIcon.Margin = new Thickness(0, 0, 4, 0);
            pick.Children.Add(pickIcon);
            pick.Children.Add(new TextBlock
            {
                Text = selectName,
                Foreground = current ? OverlayTheme.GoldBrush : OverlayTheme.WhiteBrush,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 88
            });
            pick.Children.Add(new TextBlock
            {
                Text = " 선택",
                Foreground = OverlayTheme.MutedBrush,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            });
            body.Children.Add(pick);
        }
        else if (missingNames is { Length: > 0 })
        {
            body.Children.Add(new TextBlock
            {
                Text = "먼저 " + missingNames,
                Foreground = OverlayTheme.WarnBrush,
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0)
            });
        }
        else if (companions is { Length: > 0 })
        {
            body.Children.Add(new TextBlock
            {
                Text = "함께 " + companions,
                Foreground = OverlayTheme.MutedBrush,
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 28,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }
        if (keys.Count > 0)
        {
            var chips = OverlayTheme.Keycaps(keys.Take(1).ToList());
            chips.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            chips.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 0));
            body.Children.Add(chips);
        }
        return new Border
        {
            Background = current ? OverlayTheme.FeaturedBrush : OverlayTheme.RowBrush,
            BorderBrush = current ? OverlayTheme.GoldBrush : OverlayTheme.HairlineBrush,
            BorderThickness = new Thickness(current ? 2 : 1),
            CornerRadius = new CornerRadius(OverlayTheme.TileRadius),
            Padding = new Thickness(5, 6, 5, 6),
            ToolTip = RecommendationPresentation.CraftIngredientLine(step),
            Child = body
        };
    }

    private static UIElement Arrow()
    {
        return new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L7,5 0,10"),
            Stroke = OverlayTheme.GoldBrush,
            StrokeThickness = 2,
            Margin = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static UIElement RenderCluster(BoardCluster cluster, string selectedId, Action<string> onSelect)
    {
        if (cluster.Children.Count == 0)
            return BoardTile(cluster.Head, cluster.Head.Route.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase),
                onSelect);

        var selectedInCluster = cluster.Head.Route.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase) ||
                                cluster.Children.Any(child =>
                                    child.Route.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(BoardPortrait(cluster.Head, 48, 11,
            cluster.Head.Route.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase), onSelect));
        row.Children.Add(new Border
        {
            Width = 1,
            Height = 34,
            Background = OverlayTheme.GoldBrush,
            Opacity = 0.5,
            Margin = new Thickness(6, 0, 6, 10),
            VerticalAlignment = VerticalAlignment.Center
        });
        foreach (var child in cluster.Children)
            row.Children.Add(BoardPortrait(child, 32, 10,
                child.Route.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase), onSelect));
        return new Border
        {
            Background = selectedInCluster ? OverlayTheme.FeaturedBrush : OverlayTheme.RowBrush,
            BorderBrush = selectedInCluster ? OverlayTheme.GoldBrush : OverlayTheme.HairlineBrush,
            BorderThickness = new Thickness(selectedInCluster ? 1.5 : 1),
            CornerRadius = new CornerRadius(OverlayTheme.WellRadius),
            Padding = new Thickness(8, 8, 8, 6),
            Margin = new Thickness(0, 0, 10, 10),
            Child = row
        };
    }

    private static UIElement BoardPortrait(Recommendation item, double size, double percentSize,
        bool selected, Action<string> onSelect)
    {
        var unit = item.CompositionUnits[0];
        var icon = UnitImageFactory.Create(unit.Image, unit.Name, size, unit.UnitId);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var body = new StackPanel { Width = size + 8 };
        var ring = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            BorderBrush = selected ? OverlayTheme.GoldBrush : OverlayTheme.HairlineBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(OverlayTheme.ImageRadius),
            Child = icon
        };
        body.Children.Add(ring);
        body.Children.Add(new TextBlock
        {
            Text = RecommendationPresentation.CompletionPercent(item.RecipeProgress),
            Foreground = OverlayTheme.GoldBrush,
            FontSize = percentSize,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0)
        });
        AddGreenBloodCaption(body, item, 9, size + 8);
        var hit = new Border
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = body,
            Margin = new Thickness(0, 0, 4, 0)
        };
        hit.ToolTip = BoardTooltip(item);
        AutomationProperties.SetName(hit, BoardTooltip(item).Replace('\n', ' '));
        hit.MouseEnter += (_, _) => ring.BorderBrush = OverlayTheme.GoldBrush;
        hit.MouseLeave += (_, _) =>
            ring.BorderBrush = selected ? OverlayTheme.GoldBrush : OverlayTheme.HairlineBrush;
        hit.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            onSelect(item.Route.Id);
        };
        return hit;
    }

    private static UIElement BoardTile(Recommendation item, bool selected, Action<string> onSelect)
    {
        var unit = item.CompositionUnits[0];
        var body = new StackPanel { Width = 64 };
        var icon = UnitImageFactory.Create(unit.Image, unit.Name, 52, unit.UnitId);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        body.Children.Add(icon);
        body.Children.Add(new TextBlock
        {
            Text = RecommendationPresentation.CompletionPercent(item.RecipeProgress),
            Foreground = OverlayTheme.GoldBrush,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0)
        });
        AddGreenBloodCaption(body, item, 9, 64);
        var tile = new Border
        {
            Background = selected ? OverlayTheme.FeaturedBrush : OverlayTheme.RowBrush,
            BorderBrush = selected ? OverlayTheme.GoldBrush : OverlayTheme.HairlineBrush,
            BorderThickness = new Thickness(selected ? 2 : 1),
            CornerRadius = new CornerRadius(OverlayTheme.TileRadius),
            Padding = new Thickness(6, 6, 6, 6),
            Margin = new Thickness(0, 0, 10, 10),
            Cursor = Cursors.Hand,
            Child = body
        };
        tile.ToolTip = BoardTooltip(item);
        AutomationProperties.SetName(tile, BoardTooltip(item).Replace('\n', ' '));
        tile.MouseEnter += (_, _) =>
        {
            if (!selected) tile.BorderBrush = OverlayTheme.GoldBrush;
            tile.Background = OverlayTheme.FeaturedBrush;
        };
        tile.MouseLeave += (_, _) =>
        {
            tile.BorderBrush = selected ? OverlayTheme.GoldBrush : OverlayTheme.HairlineBrush;
            tile.Background = selected ? OverlayTheme.FeaturedBrush : OverlayTheme.RowBrush;
        };
        tile.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            onSelect(item.Route.Id);
        };
        return tile;
    }

    private static void AddGreenBloodCaption(Panel body, Recommendation item, double fontSize, double maxWidth)
    {
        var line = RecommendationPresentation.GreenBloodHostLine(item, compact: true);
        if (line is null) return;
        body.Children.Add(new TextBlock
        {
            Text = line,
            Foreground = OverlayTheme.GoldBrush,
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = maxWidth,
            MaxHeight = fontSize * 2.6,
            Margin = new Thickness(0, 1, 0, 0)
        });
    }

    private static string BoardTooltip(Recommendation item)
    {
        var name = RecommendationPresentation.CraftUnitName(item.CompositionUnits[0]);
        var host = RecommendationPresentation.GreenBloodHostLine(item, compact: false);
        return host is null ? name : name + "\n" + host;
    }
}
