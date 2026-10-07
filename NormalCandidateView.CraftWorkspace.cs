using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace OrandOverlay;

public sealed partial class NormalCandidateView
{
    private readonly StackPanel _craftHeader = new();
    private readonly StackPanel _craftResources = new();
    private readonly StackPanel _missingHost = new();
    private readonly Grid _craftPanes = new();
    private readonly ScrollViewer _missingScroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    private readonly Border _missingPane = new() { BorderBrush = OverlayTheme.PlanLine };
    private bool _hasMissingMaterials;
    private const double CraftSplitWidth = 420, CraftRailWidth = 140, CraftPanelMaxWidth = 480, CraftPanelPadding = 8;

    private UIElement CreateCraftWorkspace()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new());
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(_craftHeader);
        _craftPanes.ColumnDefinitions.Add(new());
        _craftPanes.ColumnDefinitions.Add(new());
        _craftPanes.RowDefinitions.Add(new());
        _craftPanes.RowDefinitions.Add(new() { Height = new GridLength(0) });
        AutomationProperties.SetAutomationId(_craftPanes, "normal-craft-panes");
        AutomationProperties.SetAutomationId(_missingScroll, "normal-missing-scroll");
        AutomationProperties.SetName(_missingScroll, "모을 재료 목록");
        _missingScroll.Content = _missingHost;
        _missingPane.Child = _missingScroll;
        _craftPanes.Children.Add(_missingPane);
        _craftPanes.Children.Add(_detailScroll);
        Grid.SetRow(_craftPanes, 1); root.Children.Add(_craftPanes);
        Grid.SetRow(_craftResources, 2); root.Children.Add(_craftResources);
        _craftPanes.SizeChanged += (_, _) => UpdateCraftPaneLayout();
        return root;
    }

    private void UpdateCraftPaneLayout()
    {
        if (_craftDetached)
        {
            _missingPane.Visibility = _hasMissingMaterials ? Visibility.Visible : Visibility.Collapsed;
            _craftPanes.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            _craftPanes.ColumnDefinitions[1].Width = new GridLength(0);
            _craftPanes.RowDefinitions[0].Height = _hasMissingMaterials ? GridLength.Auto : new GridLength(0);
            // Auto rows measure children with infinite height: constrain the viewer itself so it owns a real viewport.
            var missingHeight = Math.Min(190, Math.Max(48, _craftPanes.ActualHeight * 0.36));
            _craftPanes.RowDefinitions[0].MaxHeight = missingHeight;
            _missingPane.MaxHeight = missingHeight;
            _missingScroll.MaxHeight = Math.Max(0, missingHeight - CraftSmallGap - 1);
            _craftPanes.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(_missingPane, 0); Grid.SetColumnSpan(_missingPane, 1);
            Grid.SetColumn(_detailScroll, 0); Grid.SetColumnSpan(_detailScroll, 1);
            Grid.SetRow(_detailScroll, 1);
            _missingPane.BorderThickness = new(0, 0, 0, 1);
            _missingPane.Padding = new(0, 0, 0, CraftSmallGap);
            _detailScroll.Margin = _hasMissingMaterials ? new(0, CraftSmallGap, 0, 0) : new(0);
            return;
        }
        var split = _hasMissingMaterials && _craftPanes.ActualWidth >= CraftSplitWidth;
        var stacked = _hasMissingMaterials && !split;
        _missingPane.Visibility = _hasMissingMaterials ? Visibility.Visible : Visibility.Collapsed;
        _craftPanes.ColumnDefinitions[0].Width = split
            ? new GridLength(CraftRailWidth)
            : new GridLength(0);
        _craftPanes.RowDefinitions[0].MaxHeight = double.PositiveInfinity;
        _missingPane.MaxHeight = double.PositiveInfinity;
        _missingScroll.MaxHeight = double.PositiveInfinity;
        _craftPanes.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        _craftPanes.RowDefinitions[0].Height = new GridLength(stacked ? 0.35 : 1, GridUnitType.Star);
        _craftPanes.RowDefinitions[1].Height = stacked ? new GridLength(0.65, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(_missingPane, 0); Grid.SetColumnSpan(_missingPane, stacked ? 2 : 1);
        Grid.SetColumn(_detailScroll, stacked ? 0 : 1); Grid.SetColumnSpan(_detailScroll, stacked ? 2 : 1);
        Grid.SetRow(_detailScroll, stacked ? 1 : 0);
        _missingPane.BorderThickness = split ? new(0, 0, 1, 0) : new(0, 0, 0, 1);
        _missingPane.Padding = split ? new(0, 0, CraftSmallGap, 0) : new(0, 0, 0, CraftSmallGap);
        _detailScroll.Margin = split ? new(CraftGap, 0, 0, 0) : stacked ? new(0, CraftSmallGap, 0, 0) : new(0);
    }
}
