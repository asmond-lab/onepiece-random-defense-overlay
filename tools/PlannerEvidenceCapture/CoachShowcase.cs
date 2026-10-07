using System.Collections.Immutable;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using OrandOverlay;

namespace PlannerEvidenceCapture;

internal static partial class Program
{
    private static void CaptureCoachShowcase(string output)
    {
        CaptureCoachNavigationGuidance(output);
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var goal = catalog.Unit("rawcode:F40h");
        var frame = new CoachFrame
        {
            MatchGeneration = 1, Revision = 1, Round = 21, CompletedStoryStage = 9,
            IsCurrent = true, Difficulty = "악몽", GoalId = goal.Id,
            Inventory = ImmutableDictionary<string, int>.Empty.Add("rawcode:S20h", 1)
        };
        var decisions = new[]
        {
            new CoachDecision(CoachActionKind.Waiting, "start", "첫 패를 기다리고 있어요",
                "워크래프트에서 솔로 악몽을 시작하세요.", "패를 읽은 뒤 첫 행동부터 안내합니다.",
                "정상 패 인식 시 자동으로 시작합니다.", "첫 희귀함 확보"),
            new CoachDecision(CoachActionKind.Craft, "craft:rawcode:F40h", "스코퍼가반 불멸 조합",
                "샹크스 선택 → 채팅에 산먹깨비 입력", "대체 기물을 확보해 재료 소모 후에도 전력을 유지합니다.",
                "스코퍼가반 보유가 확인되면 다음 행동으로 넘어갑니다.", "55라 전 지원 수치 확인")
                { TargetUnitId = goal.Id, MaterialCompletion = 1 },
            new CoachDecision(CoachActionKind.Navigation, "navigation", "바운티헌터 항법 선택",
                "게임의 항법 창에서 바운티헌터 선택 → 선택 완료 확인",
                "추천 표시는 게임에서 선택됐다는 뜻이 아닙니다.", "게임에서 고른 후 확인 버튼을 누르세요.",
                "23라까지 항법 선택") { NavigationOptionId = "PathOfKings.BountyHunter", RequiresUserConfirmation = true },
            new CoachDecision(CoachActionKind.Recognition, "recognition", "인식을 복구하고 있어요",
                "판매·분해·조합을 잠시 멈추고 게임 화면을 유지하세요.",
                "이전 패의 재료로 새 조합을 권하지 않습니다. 확인되지 않은 자원과 전투 신호는 미확인으로 유지합니다.",
                "새 정상 패를 받으면 기존 목표와 진행을 유지하며 다시 안내합니다.", "새 정상 관측 확인")
                { UnknownSignals = "골드 · 목재 · 보스 체력 · 라인 수", Alternative = "게임 창과 맵 버전을 확인하세요." },
            new CoachDecision(CoachActionKind.Gather, "gather", "지원 유닛 재료를 모으세요",
                "필요 재료: 우솝 3개 · 루피 2개", "현재 목표 재료를 유지하며 지원을 먼저 보완합니다.",
                "필요 패가 들어오면 조합을 안내합니다.", "주력 조합 후에도 라인 제어 유지")
                { MaterialCompletion = 0.43, PreservedMaterials = "샹크스 1개 · 레이쥬 1개" },
            new CoachDecision(CoachActionKind.Story, "story", "스토리 13단계 진행",
                "스토리 담당 유닛 선택 → 현재 스토리 목표 공격", "35라 전 스토리 13단계 완료가 필요합니다.",
                "완료 단계 증가를 자동으로 확인합니다.", "35라까지 스토리 완료") { IsUrgent = true },
            new CoachDecision(CoachActionKind.Reward, "reward", "받은 위습을 한 번 사용하세요",
                "보상 위습 선택 → 소환 한 번 → 결과 확인", "새 패를 확인한 뒤 다음 조합을 계산합니다.",
                "위습 수량 변화와 새 유닛 확인", "희귀 보상 반영 후 목표 결정"),
            new CoachDecision(CoachActionKind.Economy, "economy", "목재 10개 확인 후 조합",
                "목재가 부족하면 저축하고, 충분하면 안내된 조합을 진행하세요.",
                "유닛 재료와 자원 확보는 다릅니다.", "조합 대상 보유 확인", "필요한 지원부터 완성")
                { MaterialCompletion = 1, UnknownSignals = "목재 · 골드" },
            new CoachDecision(CoachActionKind.Upgrade, "upgrade", "주력 강화 한 번 확인",
                "주력 선택 → 표시된 강화 비용 확인 → 강화 한 번", "현재 확인된 예산 범위 안에서 판단합니다.",
                "강화 단계 증가 확인", "다음 투자 전 패와 비용 재확인"),
            new CoachDecision(CoachActionKind.Maintain, "maintain", "라인 배치를 유지하세요",
                "주력과 스턴의 공격 범위 확인", "새 조합보다 현재 전력 유지가 우선입니다.",
                "새 패가 확인되면 갱신합니다.", "다음 보스 대응") { OperationGuide = "스턴 유닛: 라인 공격 유지\n보잡 유닛: 보스 출현 시 타깃 확인" },
            new CoachDecision(CoachActionKind.Finished, "finished", "이번 판 종료",
                "로컬 복기에서 마지막 패와 안내를 확인하세요.", "관측된 종료 결과입니다.",
                "패배 원인을 자동 단정하지 않습니다.", "복기 확인 후 다음 판 준비"),
            new CoachDecision(CoachActionKind.Item, "item", "그린블러드 사용",
                "보유 아이템 선택 → 확인된 전설에게 사용", "현재 보유 아이템과 대상에 근거합니다.",
                "아이템·유닛 변화 확인", "지원 수치 재확인"),
            new CoachDecision(CoachActionKind.Navigation, "navigation-confirm", "실제 항법을 확인하세요",
                "목록에서 게임에 표시된 항법 선택 → 확인", "강제 선택을 추측하지 않습니다.",
                "사용자 확인 후 재계산", "항법 제한에 맞춰 진행")
                { RequiresUserConfirmation = true, RequiresNavigationChoice = true },
            new CoachDecision(CoachActionKind.Waiting, "paused", "자동 안내 일시정지",
                "안내 재개를 누르면 현재 패로 다시 계산합니다.", "관측은 계속됩니다.",
                "재개 버튼으로 이어서 진행", "현재 목표 유지")
        };
        var count = 0;
        foreach (var scale in new[] { 1.0, 1.25, 1.5 })
        foreach (var decision in decisions)
        {
            var view = new BeginnerCoachView();
            var window = new Window
            {
                Width = 540 * scale, Height = 740 * scale, WindowStyle = WindowStyle.None,
                ShowInTaskbar = false, ShowActivated = false, Topmost = false,
                Background = OverlayTheme.RowBrush,
                Content = new Border { Padding = OverlayTheme.CoachPanelPadding, Child = view }
            };
            view.LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);
            view.Render(decision with { GoalLabel = goal.Name }, frame with
                { Paused = decision.Id == "paused", Round = decision.Id == "start" ? 0 : frame.Round },
                decision.TargetUnitId is null ? null : goal);
            try
            {
                window.Show();
                window.Left = SystemParameters.VirtualScreenLeft - window.Width - 20;
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                var controls = Descendants(view).OfType<FrameworkElement>().ToArray();
                if (controls.Count(element => AutomationProperties.GetAutomationId(element) == "coach-scroll") != 1)
                    throw new InvalidOperationException("Coach must have one scrolling body.");
                var action = controls.OfType<TextBlock>().Single(element =>
                    AutomationProperties.GetAutomationId(element) == "coach-action");
                if (action.Text != decision.Title || action.TextWrapping != TextWrapping.Wrap)
                    throw new InvalidOperationException("Coach action contract mismatch.");
                var confirm = controls.OfType<Button>().Single(element =>
                    AutomationProperties.GetAutomationId(element) == "coach-confirm");
                if (confirm.IsVisible != decision.RequiresUserConfirmation)
                    throw new InvalidOperationException("Confirmation visibility mismatch.");
                var scroll = controls.OfType<ScrollViewer>().Single(element =>
                    AutomationProperties.GetAutomationId(element) == "coach-scroll");
                foreach (var bottom in new[] { false, true })
                {
                    var details = controls.OfType<Expander>().Single();
                    details.IsExpanded = bottom;
                    if (bottom) scroll.ScrollToEnd(); else scroll.ScrollToHome();
                    window.UpdateLayout();
                    SaveCoachWindow(window, Path.Combine(output,
                        $"coach-{decision.Id.Replace(':', '_')}-{scale * 100:0}-{(bottom ? "bottom" : "top")}.png"));
                    count++;
                }
            }
            finally { window.Close(); }
        }
        Console.WriteLine($"COACH_SHOWCASE PASS states={decisions.Length} scales=3 captures={count}");
    }

    private static void SaveCoachWindow(Window window, string path)
    {
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight),
            96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output = Output.CreateNew(path);
        encoder.Save(output);
    }
}
