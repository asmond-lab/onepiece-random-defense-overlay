using System.Globalization;
using System.IO;
using System.Text;

namespace WarcraftProbe;

/// <summary>Pure Markdown rendering. All supplied strings are rendered as inert text.</summary>
public static class ReportWriter
{
    private const int PrintedRvas = 16;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static string Value(object? value) => value is null ? "Unknown" : Convert.ToString(value, Invariant) ?? "Unknown";

    public static string SnapshotMarkdown(Snapshot snapshot)
    {
        SnapshotDiff.Validate(snapshot);
        var output = new StringBuilder();
        var image = snapshot.Image;
        output.AppendLine("# WarcraftProbe 스냅샷").AppendLine().AppendLine("## 요약").AppendLine();
        Bullet(output, "샘플 ID", snapshot.CaptureId);
        Bullet(output, "라벨", snapshot.Label);
        Bullet(output, "모드", snapshot.Mode);
        Bullet(output, "캡처 시작 / 종료", snapshot.StartedAt.ToString("O", Invariant) + " / " + snapshot.FinishedAt.ToString("O", Invariant));
        Bullet(output, "복사본", image.FileName);
        Bullet(output, "파일 버전", image.FileVersion);
        Bullet(output, "Copy SHA-256", image.Sha256);
        Bullet(output, "파일 / 이미지 크기", Value(image.FileSize) + " / " + Value(image.ImageSize));
        Bullet(output, "진입점 RVA", Hex(image.EntryPointRva));
        Bullet(output, "정적 앵커", image.Anchors.Length + " records, " + image.Anchors.Sum(a => a.Rvas.Length) + " RVA occurrences (라이브 함수 수 아님)");
        if (snapshot.Live is null)
        {
            Bullet(output, "PID / 프로세스 시작", "Unknown (NoLive)");
            Bullet(output, "바인딩 / 메모리 구간 / 코드 검사 / 구조", "Unknown (NoLive, 0이 아님)");
        }
        else
        {
            var live = snapshot.Live;
            Bullet(output, "PID", live.ProcessId);
            Bullet(output, "프로세스 시작", live.ProcessStartedAt.ToString("O", Invariant));
            Bullet(output, "Live 상태", live.Status);
            Bullet(output, "바인딩 범위", live.BindingScope);
            Bullet(output, "헤더 / 리소스 일치", Value(live.HeaderBytesEqual) + " / " + Value(live.ResourceBytesEqual));
            Bullet(output, "런타임 전체 이미지 해시 검증", false);
            Bullet(output, "메모리 구간", live.Regions.Length == 0 ? "Unknown (구간 자료 없음)" : "Total=" + live.Regions.Length + ", Readable=" + live.Regions.Count(SnapshotDiff.IsReadable) + ", NoAccess=" + live.Regions.Count(SnapshotDiff.IsNoAccess));
            Bullet(output, "읽기 요청 / 쿼리 수", live.RequestedBytes + " bytes / " + live.QueryCalls);
            Bullet(output, "구조 상태", live.Structures.Status);
            Bullet(output, "구조 프로필", live.Structures.ProfileId);
            Bullet(output, "구조 제한 사유", live.Structures.Reason);
            if (live.Structures.Status == "Observed")
            {
                Bullet(output, "관측 벡터 전체 행", live.Structures.ObservedObjectCount);
                Bullet(output, "CURRENTVIEW 할당 행 / 다른 뷰 행", Value(live.Structures.CurrentViewObjectCount) + " / " + Value(live.Structures.OtherViewObjectCount));
                Bullet(output, "현재 뷰 슬롯", live.Structures.CurrentViewSlot);
                output.AppendLine().AppendLine("### CURRENTVIEW rawcode 관측").AppendLine();
                TableHeader(output, "rawcode", "할당 행 수");
                foreach (var raw in live.Structures.Rawcodes.OrderBy(x => x.Rawcode, StringComparer.Ordinal))
                    Row(output, raw.Rawcode, Value(raw.Count));
                if (live.Structures.Rawcodes.Length == 0) output.AppendLine().AppendLine("관측된 rawcode 행 없음. 로컬·생존·전체 게임플레이 목록이 비었다는 뜻은 아닙니다.");
            }
            else Bullet(output, "구조 개수", "Unknown (미관측/미지원/만료를 0으로 표시하지 않음)");
            output.AppendLine().AppendLine("### 코드 검사: Readability / Hash").AppendLine();
            if (live.CodeChecks.Length == 0) output.AppendLine("Unknown: 코드 검사 자료 없음.");
            else
            {
                TableHeader(output, "이름", "RVA", "크기", "상태", "부분 SHA-256");
                foreach (var check in live.CodeChecks.OrderBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.Rva))
                    Row(output, check.Name, Hex(check.Rva), Value(check.Size), check.Status, Value(check.Sha256));
            }
            output.AppendLine().AppendLine("### 모듈 구간 분포").AppendLine();
            if (live.Regions.Length == 0) output.AppendLine("Unknown: 구간 자료 없음.");
            else
            {
                TableHeader(output, "보호 / 상태 / 유형 / 섹션", "구간 수", "바이트");
                foreach (var entry in SnapshotDiff.RegionDistribution(snapshot))
                    Row(output, entry.Key, Value(entry.Value.Count), Value(entry.Value.Bytes));
            }
        }
        output.AppendLine().AppendLine("## 정적 섹션").AppendLine();
        TableHeader(output, "이름", "RVA", "가상 / 원시 크기", "플래그", "복사본 SHA-256");
        foreach (var section in image.Sections)
            Row(output, section.Name, Hex(section.Rva), section.VirtualSize + " / " + section.RawSize, Hex(section.Characteristics), section.Sha256);
        output.AppendLine().AppendLine("## 정적 앵커").AppendLine();
        TableHeader(output, "KIND", "NAME", "상태", "RVA (일부 표시 가능)");
        foreach (var anchor in image.Anchors.OrderBy(a => a.Kind, StringComparer.Ordinal).ThenBy(a => a.Name, StringComparer.Ordinal))
            Row(output, anchor.Kind, anchor.Name, anchor.Status, RvaPreview(SnapshotDiff.Rvas(anchor.Rvas)));
        output.AppendLine().AppendLine("## 제한 및 수동 검토").AppendLine();
        output.AppendLine("- 로컬 소유: Unknown. 생존: Unknown. 전체 게임플레이 목록: Unknown. 지금 제작 가능: Unknown.");
        output.AppendLine("- CURRENTVIEW는 현재 뷰의 할당 행입니다. 로컬 플레이어·생존 객체·완전한 게임플레이 목록을 뜻하지 않습니다.");
        output.AppendLine("- NativeNameString 및 NodeNames는 문자열 데이터입니다. 이름이 발견되어도 함수·vtable 또는 라이브 실행 코드의 증거가 아닙니다. 필드 의미를 추론하지 않습니다.");
        output.AppendLine("- 복사본 SHA와 제한 바인딩만 보고합니다. RuntimeWholeImageHashVerified=false, AtomicSnapshot=false, GameplayReady=false, AutomaticProfileApproval=false.");
        output.AppendLine("- 보호 플래그 기반 Readable은 실제 바이트 읽기 성공과 다릅니다. NoAccess는 PAGE_NOACCESS 구간 수이며 미커밋·가드 페이지 전체를 뜻하지 않습니다.");
        output.AppendLine("- 섹션 분포는 RVA 교차 구간 수/교차 바이트입니다. 여러 섹션에 걸친 구간은 중복 집계될 수 있으며 각 분포의 합을 더하지 마세요.");
        output.AppendLine("- 출력 RVA는 정렬된 앞 " + PrintedRvas + "개까지입니다. 전체 배열은 원본 스냅샷/JSON에 보존됩니다.");
        foreach (var duplicate in image.Sections.GroupBy(s => s.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
            Bullet(output, "중복 섹션 이름: 대응 불명", duplicate.Key);
        foreach (var duplicate in image.Anchors.GroupBy(a => (a.Kind, a.Name)).Where(g => g.Count() > 1))
            Bullet(output, "중복 KIND+NAME: 대응 불명", duplicate.Key.Kind + ": " + duplicate.Key.Name);
        output.AppendLine("- 새 빌드/UnsupportedBuild는 구조 Unknown입니다. 변경된 섹션·RVA·코드 검사 및 프로필은 수동 리더 검토가 필요합니다. 자동 승인이나 남은 시간 추정은 하지 않습니다.");
        return output.ToString();
    }

    public static string ComparisonMarkdown(Comparison comparison)
    {
        if (comparison is null || comparison.SchemaVersion != 1 || comparison.Differences is null || comparison.Limitations is null)
            throw new InvalidDataException("Invalid comparison");
        if (comparison.Differences.Any(d => d is null) || comparison.Limitations.Any(l => l is null))
            throw new InvalidDataException("Null comparison entry");
        var output = new StringBuilder();
        output.AppendLine("# WarcraftProbe 비교").AppendLine().AppendLine("## 요약").AppendLine();
        Bullet(output, "이전 / 이후 샘플", Value(comparison.BeforeCaptureId) + " / " + Value(comparison.AfterCaptureId));
        Bullet(output, "이전 / 이후 버전", Value(comparison.BeforeVersion) + " / " + Value(comparison.AfterVersion));
        Bullet(output, "SameImage (정확한 복사본 SHA)", comparison.SameImage);
        Bullet(output, "SameProcessEpoch (PID + 시작 시각)", comparison.SameProcessEpoch);
        Bullet(output, "변경 항목", comparison.Differences.Length);
        Bullet(output, "자동 프로필 승인", false);
        output.AppendLine().AppendLine("## 변경 표").AppendLine();
        if (comparison.Differences.Length == 0) output.AppendLine("관측 가능한 비교 필드의 변경 없음. 미관측 항목의 동일성이나 게임플레이 준비 상태를 보증하지 않습니다.");
        else
        {
            TableHeader(output, "영역", "이름", "이전", "이후", "해석 / 수동 검토");
            foreach (var diff in comparison.Differences)
                Row(output, diff.Area, diff.Name,
                    diff.Area == "Anchor RVA" ? RvaPreview(diff.Before) : diff.Before,
                    diff.Area == "Anchor RVA" ? RvaPreview(diff.After) : diff.After, diff.Meaning);
        }
        output.AppendLine().AppendLine("## 차단 사유 및 제한").AppendLine();
        foreach (var limitation in comparison.Limitations) output.Append("- ").AppendLine(Escape(limitation));
        output.AppendLine("- 로컬 소유·생존·전체 게임플레이 목록·지금 제작 가능: 항상 Unknown. CURRENTVIEW 할당 행을 로컬/생존 목록으로 해석하지 마세요.");
        output.AppendLine("- NativeNameString/NodeNames는 문자열이지 함수 코드 증거가 아닙니다. 바뀐 RVA·섹션·코드 검사·구조 상태를 수동 리더가 검토해야 합니다.");
        output.AppendLine("- 긴 앵커 RVA 목록은 앞 " + PrintedRvas + "개와 전체 수만 표시합니다. 전체 비교 값은 Comparison JSON에 보존됩니다. 자동 승인 및 완료 시간 추정 없음.");
        return output.ToString();
    }

    private static string Hex(uint value) => "0x" + value.ToString("X8", Invariant);
    private static string RvaPreview(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "없음 (총 0 RVA)";
        var items = value.Split(", ", StringSplitOptions.None);
        if (items.Length <= PrintedRvas) return value + " (총 " + items.Length + " RVA)";
        return string.Join(", ", items.Take(PrintedRvas)) + " … (앞 " + PrintedRvas + "개 / 총 " + items.Length + " RVA; 전체는 JSON)";
    }
    private static void Bullet(StringBuilder output, string label, object? value) => output.Append("- ").Append(Escape(label)).Append(": ").AppendLine(Escape(Value(value)));
    private static void TableHeader(StringBuilder output, params string[] labels)
    {
        Row(output, labels);
        output.Append('|');
        foreach (var unused in labels) output.Append(" --- |");
        output.AppendLine();
    }
    private static void Row(StringBuilder output, params string?[] cells)
    {
        output.Append('|');
        foreach (var cell in cells) output.Append(' ').Append(Escape(cell ?? "Unknown")).Append(" |");
        output.AppendLine();
    }
    internal static string Escape(string text)
    {
        var output = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsControl(c) || char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                output.Append(' ');
                continue;
            }
            if (c == '<') { output.Append("&lt;"); continue; }
            if (c == '>') { output.Append("&gt;"); continue; }
            if (c == '&') { output.Append("&amp;"); continue; }
            // Escape every ASCII punctuation mark: tables, HTML, links, entities and fences stay inert.
            if (c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '\u0060' or >= '{' and <= '~') output.Append('\\');
            output.Append(c);
        }
        return output.ToString();
    }
}
