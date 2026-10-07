using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace OrandOverlay;

public sealed partial class NormalCandidateView
{
    private UIElement CraftStepCard(NormalCraftStep step, int number)
    {
        if (_craftDetached) return CompactCraftStep(step, number);
        var body = new StackPanel();
        var card = new Border { Child = body, Padding = new(_compactCraft ? CraftTinyGap : CraftSmallGap), CornerRadius = new(CraftRadius),
            BorderBrush = OverlayTheme.PlanLine,
            BorderThickness = new(0, 0, 0, 1), Margin = new(0, 0, 0, CraftGap) }.WithOverlayBackground(_craftDetached && number == 1 ? OverlayChrome.WellKey : OverlayChrome.CanvasKey);
        AutomationProperties.SetAutomationId(card, "normal-craft-step-" + step.UnitId);
        AutomationProperties.SetName(card, number + ". " + step.Name + " ×" + step.OutputCount + " · " + step.CombineCount + "회 조합");
        var top = new Grid(); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); top.ColumnDefinitions.Add(new());
        top.Children.Add(Text(number.ToString("00") + (number == 1 ? "  다음 순서" : "  조합 순서"), CraftMetaSize, _craftDetached && number == 1 ? Accent : OverlayTheme.PlanSecondary, true));
        var projected = step.Ingredients.Any(i => i.PriorStepCount > 0);
        var status = !step.IsMaterialReady ? "재료 부족" : projected ? "앞 단계 필요" : "재료 준비됨";
        var color = !step.IsMaterialReady ? OverlayTheme.PlanWarning : projected ? Accent : OverlayTheme.PlanSuccess;
        var state = FreshnessText(status, "현재 재료 미확인", CraftMetaSize, color, true, craft: true); state.HorizontalAlignment = HorizontalAlignment.Right;
        state.Margin = new(CraftGap, 2, 0, 2); Grid.SetColumn(state, 1); top.Children.Add(state); body.Children.Add(top);
        body.Children.Add(CraftActionRoute(step));
        Panel materials = _compactCraft ? new WrapPanel() : new StackPanel();
        materials.Margin = new(0, CraftTinyGap, 0, 0);
        foreach (var ingredient in step.Ingredients) materials.Children.Add(CraftIngredient(ingredient, step.UnitId));
        AutomationProperties.SetAutomationId(materials, "normal-craft-materials-" + step.UnitId);
        body.Children.Add(materials);
        if (!step.Conditions.IsSatisfied || step.Ingredients.Any(i => i.IsResource))
        {
            var condition = Text("추가 조건·자원 확인 필요", CraftMetaSize, OverlayTheme.PlanWarning);
            condition.ToolTip = CraftConditionGuidance(step);
            AutomationProperties.SetAutomationId(condition, "normal-craft-condition-" + step.UnitId); body.Children.Add(condition);
        }
        return card;
    }

    private UIElement CraftActionRoute(NormalCraftStep step)
    {
        var route = new Grid { Margin = new(0, CraftTinyGap, 0, CraftTinyGap) };
        route.ColumnDefinitions.Add(new()); route.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); route.ColumnDefinitions.Add(new());
        var hasChat = step.CombineCommands.Count > 0;
        var selection = hasChat ? CraftTextUnit("채팅 입력", "선택 조건 미확인") :
            CraftUnit(step.SelectionUnitId, CraftSelectionName(step), "선택");
        route.Children.Add(selection);
        var command = new StackPanel { Margin = new(CraftSmallGap, 0, CraftSmallGap, 0), VerticalAlignment = VerticalAlignment.Center };
        var key = Text(hasChat ? "채팅" : step.CombineKey ?? "확인", CraftKeySize, OverlayTheme.PlanText, true);
        key.FontFamily = new FontFamily("Consolas, Malgun Gothic"); key.TextAlignment = TextAlignment.Center; key.Margin = new(0);
        var keycap = new Border { Child = key, MinWidth = CraftKeyMinWidth, Padding = new(CraftSmallGap, 0, CraftSmallGap, 0),
            BorderBrush = OverlayTheme.PlanSecondary, BorderThickness = new(1, 1, 1, 3), CornerRadius = new(CraftTileRadius) }.WithOverlayBackground(OverlayChrome.RaisedKey);
        AutomationProperties.SetAutomationId(keycap, "normal-craft-key-" + step.UnitId);
        command.Children.Add(keycap);
        var times = Text(step.CombineCount + "회  →", CraftMetaSize, OverlayTheme.PlanSecondary, true); times.TextAlignment = TextAlignment.Center;
        command.Children.Add(times); Grid.SetColumn(command, 1); route.Children.Add(command);
        var output = CraftUnit(step.UnitId, step.Name, "결과 ×" + step.OutputCount);
        Grid.SetColumn(output, 2); route.Children.Add(output);
        var wrapper = new StackPanel(); wrapper.Children.Add(route);
        if (hasChat)
        {
            var chat = Text(step.CombineCommands[0], CraftBodySize, OverlayTheme.PlanText, true);
            chat.ToolTip = string.Join(" / ", step.CombineCommands);
            wrapper.Children.Add(new Border { Child = chat, Padding = new(CraftSmallGap), CornerRadius = new(CraftTileRadius) }.WithOverlayBackground(OverlayChrome.RaisedKey));
        }
        else if (step.CombineKey is null)
            wrapper.Children.Add(Text("조합 방법과 추가 조건은 게임에서 확인해 주세요", CraftMetaSize, OverlayTheme.PlanWarning));
        AutomationProperties.SetAutomationId(wrapper, "normal-craft-action-" + step.UnitId);
        AutomationProperties.SetName(wrapper, hasChat
            ? "채팅: " + step.CombineCommands[0] + " · " + step.CombineCount + "회 · " + CraftSelectionName(step)
            : "선택: " + CraftSelectionName(step) + " · " + (step.CombineKey is null ? "조합 방법은 게임에서 확인" : "키: " + step.CombineKey) + " · " + step.CombineCount + "회");
        return wrapper;
    }

    private UIElement CraftIngredient(NormalCraftIngredient ingredient, string stepId)
    {
        var status = ingredient.IsResource ? "게임에서 확인" :
            ingredient.MissingCount > 0 ? ingredient.MissingCount + " 부족" : ingredient.PriorStepCount > 0
                ? "앞 단계 " + ingredient.PriorStepCount : "보유 " + ingredient.OwnedCount;
        if (!ingredient.IsResource && ingredient.OwnedCount > 0 && (ingredient.PriorStepCount > 0 || ingredient.MissingCount > 0))
            status = "보유 " + ingredient.OwnedCount + " · " + status;
        if (ingredient.MissingCount > 0 && ingredient.PriorStepCount > 0)
            status += " · 앞 단계 " + ingredient.PriorStepCount;
        var color = ingredient.IsResource || ingredient.MissingCount > 0 ? OverlayTheme.PlanWarning :
            ingredient.PriorStepCount > 0 ? Accent : OverlayTheme.PlanSuccess;
        var name = CraftMaterialName(ingredient.UnitId, ingredient.Name);
        var tile = CraftMaterialTile(ingredient.UnitId, name, ingredient.RequiredCount, status, color, stepId);
        AutomationProperties.SetName(tile, name + " · 필요 " + ingredient.RequiredCount + " · 인식 " + ingredient.OwnedCount +
            " · 앞 단계 예상 " + ingredient.PriorStepCount + " · " + status);
        return tile;
    }
}
