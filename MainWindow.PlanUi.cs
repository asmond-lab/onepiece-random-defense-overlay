using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Threading;

namespace OrandOverlay;

public partial class MainWindow
{
    internal int NativeBorderApplyResult { get; private set; }
    private void MainShell_OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeAuxiliaryNavigation();
        OverlayTheme.AttachRoundClip(MainShell, OverlayTheme.ChromeRadius);
        var border = MainPlanTheme.NativeBorderColor;
        var result = DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 34, ref border, sizeof(int));
        NativeBorderApplyResult = result;
        if (result != 0) System.Diagnostics.Trace.TraceWarning("Neutral DWM border unavailable: {0}", result);
    }

    private AuxiliaryWindow? _auxiliaryWindow;
    private Control? _auxiliaryReturnFocus;
    private bool _auxiliaryNavigationReady;
    private bool _profileControlsAttached;

    private void InitializeAuxiliaryNavigation()
    {
        if (_auxiliaryNavigationReady) return;
        _auxiliaryNavigationReady = true;
        CoachRendered += (_, frame) => UpdateAuxiliaryContext(frame);
        UpdateAuxiliaryContext(_lastCoachFrame);
    }

    private void UpdateAuxiliaryContext(CoachFrame? frame)
    {
        if (!_profileControlsAttached)
        {
            var index = 1;
            foreach (var name in new[] { "GuideChoice", "QueenCondition" })
            {
                var control = (FrameworkElement)MainCoachView.FindName(name);
                ((Panel)control.Parent).Children.Remove(control);
                control.HorizontalAlignment = HorizontalAlignment.Stretch;
                ProfilePane.Children.Insert(index++, control);
            }
            _profileControlsAttached = true;
        }
        if (_profileControlsAttached)
        {
            var guideChoice = (ComboBox)MainCoachView.FindName("GuideChoice");
            if (guideChoice.SelectedItem is null)
                guideChoice.SelectedItem = GuideCatalog.Find(_settings.GuideNumber);
            ((FrameworkElement)MainCoachView.FindName("GuideChoice")).Visibility =
                CurrentPlayMode == PlayMode.Guide ? Visibility.Visible : Visibility.Collapsed;
        }
        if (UsesMap2320)
        {
            RenderDiagnosticInventoryList();
            return;
        }
        var stopped = AutoScanCheck.IsChecked != true;
        InventoryOriginText.Text = stopped ? "자동 확인이 꺼져 있어요 · 마지막으로 확인한 유닛 · 여기서는 변경할 수 없어요"
            : frame?.IsCurrent == true ? "게임에서 인식한 유닛 · 여기서는 변경할 수 없어요"
            : _automatic.Count > 0 ? "마지막으로 확인한 유닛 · 현재 상태는 게임에서 다시 확인해 주세요"
            : "유닛 확인 중 · 여기서는 변경할 수 없어요";
        AutomationProperties.SetItemStatus(InventoryOriginText, stopped ? "자동 확인이 꺼져 있어요" : frame?.IsCurrent == true ? "게임에서 읽은 유닛 · 실제 보유 여부는 확인해 주세요" : "현재 유닛 상태를 다시 확인하고 있어요");
    }

    private void ShowAuxiliary(AuxiliaryPane pane, Control navigation)
    {
        InitializeAuxiliaryNavigation();
        UpdateAuxiliaryContext(_lastCoachFrame);
        _auxiliaryReturnFocus = navigation;
        if (_auxiliaryWindow is { } existing && existing.Pane == pane)
        {
            existing.Close();
            return;
        }
        if (_auxiliaryWindow is null)
        {
            var window = _auxiliaryWindow = new AuxiliaryWindow(this);
            window.Closed += (_, _) =>
            {
                RestoreAuxiliaryContent(window);
                _auxiliaryWindow = null;
                var focus = _auxiliaryReturnFocus;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (!IsVisible) return;
                    Activate(); focus?.Focus();
                }));
            };
        }
        else RestoreAuxiliaryContent(_auxiliaryWindow);
        FrameworkElement content = pane switch
        {
            AuxiliaryPane.Inventory => InventoryPane,
            AuxiliaryPane.Profile => ProfilePane,
            _ => SettingsPane
        };
        AuxiliaryParking.Children.Remove(content);
        _auxiliaryWindow.SetPane(pane, content);
        if (!_auxiliaryWindow.IsVisible) _auxiliaryWindow.Show();
        else _auxiliaryWindow.Activate();
        _auxiliaryWindow.PlaceBesideOwner();
    }

    private void RestoreAuxiliaryContent(AuxiliaryWindow window)
    {
        if (window.DetachPane() is { } content) AuxiliaryParking.Children.Add(content);
    }

    private void PlanNavigation_OnClick(object sender, RoutedEventArgs e)
    {
        _auxiliaryReturnFocus = PlanNavigationButton;
        _auxiliaryWindow?.Close();
        ExpertSettings.IsExpanded = false;
        UpdateLayout();
        MainCoachView.ShowPlan();
    }

    private void InventoryNavigation_OnClick(object sender, RoutedEventArgs e) =>
        ShowAuxiliary(AuxiliaryPane.Inventory, InventoryNavigationButton);
    private void ReviewNavigation_OnClick(object sender, RoutedEventArgs e) => OpenCoachReview();
    private void SettingsNavigation_OnClick(object sender, RoutedEventArgs e) =>
        ShowAuxiliary(AuxiliaryPane.Settings, SettingsNavigationButton);
    private void ProfileNavigation_OnClick(object sender, RoutedEventArgs e) =>
        ShowAuxiliary(AuxiliaryPane.Profile, ProfileNavigationButton);

}

// Product layout tokens. Keep plan/action hierarchy without fixed mockup whitespace.
internal static class MainPlanTheme
{
    public static Brush Chrome { get; } = RandyPickTheme.Canvas;
    public static Brush Gold { get; } = RandyPickTheme.Accent;
    public static Brush Panel { get; } = RandyPickTheme.Surface;
    public static Brush Selected { get; } = RandyPickTheme.Selection;
    public static Brush Missing { get; } = RandyPickTheme.Raised;
    public static Brush MissingLine => OverlayTheme.SelectionBorderBrush;
    public static Brush Goal => OverlayTheme.PlanSurface;
    public static Brush GoalLine => OverlayTheme.OutlineBrush;
    public static Brush Command { get; } = RandyPickTheme.Raised;
    public static Brush SuccessPill { get; } = RandyPickTheme.Raised;
    public static Brush MissingPill { get; } = RandyPickTheme.Raised;
    public static Brush PathLine { get; } = RandyPickTheme.Border;
    public static int NativeBorderColor => RandyPickTheme.ToColorRef(RandyPickTheme.Border);
    public const double SplitWidth = 680;
    public const double ComponentIconSize = 36;
    public static Thickness CompactInset { get; } = new(12);
    public const double HeaderSize = 20;
    public const double MetricSize = 20;
    public const double HeadingSize = 16;
    public const double BodySize = 14;
    public const double MetaSize = 12;
    public const double CaptionSize = 11;
    public const double LineHeightFactor = 1.45;
    public const double RecipeSize = 13;
    public const double PathIconSize = 30;
    public const double GoalIconSize = 40;
    public const double ProgressHeight = 5;
    public const double ChromeHeight = 34;
    public const double FooterHeight = 32;
    public static Brush KeySurface { get; } = RandyPickTheme.Text;
    public static Brush KeyText { get; } = RandyPickTheme.Canvas;
    public static Thickness KeyInset { get; } = new(10, 3, 10, 3);
    public const double DefaultWidth = 1280;
    public const double DefaultHeight = 800;
    public const double DrawerWidth = 360;
    public static Thickness PanelHeaderGap { get; } = new(0, 0, 0, 12);
    public static Thickness SmallSectionGap { get; } = new(0, 0, 0, 16);
    public static Thickness MilestoneInset { get; } = new(0, 0, 0, 16);
    public static Thickness NumberUnitGap { get; } = new(8, 0, 0, 0);
    public static Thickness GoalInset { get; } = new(16, 4, 16, 4);
    public static Thickness BadgeInset { get; } = new(10, 5, 10, 5);
    public static CornerRadius BadgeRadius { get; } = new(20);
    public static CornerRadius PillRadius { get; } = new(6);
    public static CornerRadius KeyRadius { get; } = new(4);
    public static CornerRadius ShellRadius { get; } = new(16);
    public static Thickness ActionCountGap { get; } = new(0, 8, 0, 8);
    public static Thickness JournalRowInset { get; } = new(0, 6, 0, 6);
    public static Thickness RecipeSpacing { get; } = new(0, 8, 0, 8);
    public static Thickness CommandInset { get; } = new(12);
    public static Thickness NavSectionInset { get; } = new(12, 12, 12, 20);
    public static Thickness NavManagementInset { get; } = new(12, 24, 12, 20);
    public static Thickness RailNoteGap { get; } = new(0, 4, 0, 8);
    public const double ActionWidth = 300;
    public const double RailWidth = 160;
    public static GridLength RailColumn { get; } = new(RailWidth);
    public static Thickness WorkspaceInset { get; } = new(16);
    public static Thickness PanelInset { get; } = new(12);
    public static Thickness RailInset { get; } = new(12, 16, 12, 16);
    public static Thickness ChromeInset { get; } = new(16, 8, 16, 8);
    public static Thickness NavInset { get; } = new(12, 10, 12, 10);
    public static Thickness SectionGap { get; } = new(0, 0, 0, 12);
    public static Thickness PanelGap { get; } = new(0, 0, 16, 0);
    public static Thickness CardGap { get; } = new(0, 0, 0, 8);
    public static Thickness MetricGap { get; } = new(0, 0, 8, 0);
    public static Thickness PathInset { get; } = new(12, 0, 0, 0);
    public static Thickness PathMargin { get; } = new(20, 0, 0, 12);
    public static Thickness PathRowInset { get; } = new(0, 6, 0, 6);
    public static Thickness PillInset { get; } = new(8, 4, 8, 4);
    public static Thickness BottomLine { get; } = new(0, 0, 0, 1);
    public static Thickness TopLine { get; } = new(0, 1, 0, 0);
    public static Thickness RightLine { get; } = new(0, 0, 1, 0);
    public static Thickness LeftLine { get; } = new(1, 0, 0, 0);
    public static CornerRadius CardRadius { get; } = new(10);
    public static CornerRadius ButtonRadius { get; } = new(8);
}
