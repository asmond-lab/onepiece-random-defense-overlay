namespace OrandOverlay;

internal enum WarcraftCompatibilityClassification { NotAssessed, DiagnosticOnly }

/// <summary>Minimal shareable evidence. Never copy free-form diagnostic details or identity data.</summary>
internal static class WarcraftCompatibilityInfo
{
    // Exact source token emitted by the isolated reader, not a substring of Detail/Status.
    private const string DiagnosticSource = "WarcraftMemoryDiagnostic300CurrentView";
    private const string Unsupported300Hash = "BD2A0DC256289DE45287BB60F3725B88F1235216D5177984377FE1CA22840A12";

    internal static WarcraftCompatibilityClassification Classify(RecognitionDiagnostics diagnostics) =>
        diagnostics.Source == DiagnosticSource ||
        (Version.TryParse(diagnostics.ProcessVersion, out var version) && version.Major == 3) ||
        string.Equals(diagnostics.ExecutableSha256, Unsupported300Hash, StringComparison.OrdinalIgnoreCase)
            ? WarcraftCompatibilityClassification.DiagnosticOnly
            : WarcraftCompatibilityClassification.NotAssessed;

    public static string Format(RecognitionResult result)
    {
        var diagnostics = result.Diagnostics;
        var classification = Classify(diagnostics);
        var version = Version.TryParse(diagnostics.ProcessVersion, out var parsed) ? parsed.ToString() : "확인되지 않음";
        var hash = diagnostics.ExecutableSha256;
        if (string.IsNullOrEmpty(hash) || hash.Length != 64 || !hash.All(char.IsAsciiHexDigit)) hash = "확인되지 않음";
        // A syntactically valid arbitrary token can still be a player name. Only known IDs are shareable.
        var profile = diagnostics.ProfileId == "war3-2.0.4.23745" ? diagnostics.ProfileId : "일치하는 검증 프로필 없음";
        var state = Enum.IsDefined(result.State) ? result.State.ToString() : "Unknown";
        var lines = new List<string>
        {
            $"랜디픽 빌드: {UpdateService.CurrentBuildVersion}",
            $"지원 분류: {classification}",
            classification == WarcraftCompatibilityClassification.DiagnosticOnly
                ? $"진단 인식 상태: {state} (운영 준비 상태 아님)" : $"인식 상태: {state}",
            $"Warcraft 파일 버전: {version}",
            $"Warcraft EXE SHA-256: {hash}",
            $"인식 프로필: {profile}",
            $"프로필 리비전: {diagnostics.ProfileRevision?.ToString() ?? "없음"}"
        };
        if (classification == WarcraftCompatibilityClassification.DiagnosticOnly)
        {
            lines.Add("Warcraft 3.0 실전 인식 미지원; 격리 검증의 Ready는 운영 지원 승인이 아닙니다.");
            lines.Add("CURRENT-VIEW 관측은 검증된 로컬 플레이어 identity가 아닙니다. 보조 리더도 미검증입니다.");
        }
        if (diagnostics.ProfileSource?.Contains("사용자 캐시 무시:", StringComparison.Ordinal) == true)
            lines.Add("프로필 경고: 사용자 캐시 오류로 번들 프로필 사용");
        lines.Add("이 요약만으로 운영 준비 또는 native 읽기 승인을 부여하지 않습니다.");
        lines.Add("읽기 전용 인식; 미검증 빌드의 기존 주소 강제 적용 없음");
        return string.Join(Environment.NewLine, lines);
    }
}
