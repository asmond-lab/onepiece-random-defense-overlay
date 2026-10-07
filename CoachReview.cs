using System.Text.Json;

namespace OrandOverlay;

public sealed record CoachReviewEntry(long Sequence, int Round, string Kind, string Title,
    string Reason, string? AcquiredUnit);

public sealed record CoachReview(string Outcome, int LastRound, int CompletedStoryStage,
    IReadOnlyList<CoachReviewEntry> Entries, bool Incomplete)
{
    public static CoachReview Read(CoachJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        return Parse(journal.ReadbackLines());
    }

    public static CoachReview Read(string path) => Parse(File.ReadLines(path));

    public static CoachReview Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var entries = new List<CoachReviewEntry>();
        var outcome = "unknown";
        var round = 0;
        var story = 0;
        var incomplete = false;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var row = document.RootElement;
                if (row.GetProperty("SchemaVersion").GetInt32() != 1)
                {
                    incomplete = true;
                    continue;
                }
                round = row.GetProperty("Round").GetInt32();
                story = row.GetProperty("CompletedStoryStage").GetInt32();
                outcome = row.GetProperty("Outcome").GetString() ?? "unknown";
                var decision = row.GetProperty("Decision");
                var visible = !row.TryGetProperty("GuideVisible", out var guide) || guide.GetBoolean();
                var kind = ((CoachActionKind)decision.GetProperty("Kind").GetInt32()).ToString();
                var entry = new CoachReviewEntry(row.GetProperty("Sequence").GetInt64(), round,
                    visible ? kind : "ExpertObservation",
                    visible ? decision.GetProperty("Title").GetString() ?? "" : "고급 모드 진행",
                    visible ? decision.GetProperty("Reason").GetString() ?? "" : "이 구간은 패 변화를 기록했습니다. 초보자 안내는 표시하지 않았습니다.",
                    row.GetProperty("AcquiredRecommendedUnit").GetString());
                if (entries.LastOrDefault() is not { } previous || previous.Title != entry.Title ||
                    previous.Round != entry.Round || entry.AcquiredUnit is not null)
                    entries.Add(entry);
            }
            catch (JsonException) { incomplete = true; }
            catch (KeyNotFoundException) { incomplete = true; }
            catch (InvalidOperationException) { incomplete = true; }
        }
        return new CoachReview(outcome, round, story, entries, incomplete);
    }

    // Stored journal rows are evidence; clean only the on-screen projection of older rows.
    private static bool IsPlainCopy(string text) => !string.IsNullOrWhiteSpace(text) &&
        !text.Any(char.IsAsciiLetter) &&
        !new[] { "관측", "미검증", "현재뷰", "오프라인", "rawcode", "소스", "프로필", "포인터" }
            .Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string DisplayTitle(CoachReviewEntry entry) => IsPlainCopy(entry.Title)
        ? entry.Title : entry.Kind switch
        {
            "Craft" => "조합 안내", "Recognition" => "유닛 확인", "Finished" => "이번 판 종료",
            "ExpertObservation" => "이번 판 진행", _ => "추천 기록"
        };

    private static string DisplayReason(CoachReviewEntry entry) => IsPlainCopy(entry.Reason)
        ? entry.Reason : "당시 안내에 자세한 게임 정보가 포함되어 있어요. 현재 상태는 게임에서 다시 확인해 주세요.";

    public string Describe() =>
        $"이번 판: {(Outcome == "clear" ? "클리어 확인" : Outcome == "fail" ? "종료 확인" : "진행 중이거나 결과 미확인")}" +
        $" · 마지막 {LastRound}라 · 스토리 {CompletedStoryStage}단계\n" +
        "인식한 유닛과 추천을 남긴 기록이에요. 실제 조작 여부나 패배 원인은 알 수 없어요.\n\n" +
        (Entries.Count == 0 ? "표시할 기록이 없어요." : string.Join("\n\n", Entries.TakeLast(20).Select(entry =>
            $"{entry.Round}라 · {DisplayTitle(entry)}\n{DisplayReason(entry)}" +
            (entry.AcquiredUnit is null ? "" : "\n추천한 유닛의 보유 수가 늘어난 것을 확인했어요.")))) +
        (Incomplete ? "\n\n일부 기록을 읽지 못했어요. 저장이 끝난 뒤 다시 확인해 주세요." : "");
}
