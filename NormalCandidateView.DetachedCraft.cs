using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace OrandOverlay;

public sealed partial class NormalCandidateView
{
    private bool _craftDetached;
    private bool _renderCraft = true;
    private Button? _openCraftWindow;
    public event Action? CraftWindowRequested;
    internal FrameworkElement CraftWorkspace => _detailWell;
    internal bool HasCraftSelection => _model?.SelectedUnit is not null;

    internal FrameworkElement DetachCraftWorkspace(bool renderCraft)
    {
        _craftDetached = true; _renderCraft = renderCraft;
        ((Grid)Content).Children.Remove(_detailWell);
        _detailWell.Margin = new(0);
        _craftHeader.Margin = new Thickness(0);
        if (renderCraft) _detailWell.MaxWidth = double.PositiveInfinity;
        UpdateResponsiveLayout();
        return _detailWell;
    }

    private void AddCraftWindowButton(Panel tools)
    {
        if (!_craftDetached) return;
        _openCraftWindow ??= Action("조합창 열기", () => CraftWindowRequested?.Invoke());
        AutomationProperties.SetAutomationId(_openCraftWindow, "normal-open-craft-window");
        if (_openCraftWindow.Parent is Panel previous) previous.Children.Remove(_openCraftWindow);
        _openCraftWindow.IsEnabled = HasCraftSelection;
        _openCraftWindow.ToolTip = HasCraftSelection ? "선택한 유닛의 조합 흐름" : "먼저 유닛을 선택해 주세요";
        tools.Children.Add(_openCraftWindow);
    }
}
