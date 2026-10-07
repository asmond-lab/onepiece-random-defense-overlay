using Xunit;

namespace OrandOverlay.Tests;

public sealed class WarcraftCompatibilityInfoTests
{
    [Fact]
    public void UnsupportedBuildReportIncludesExactIdentityWithoutInventingSupport()
    {
        var hash = "BD2A0DC256289DE45287BB60F3725B88F1235216D5177984377FE1CA22840A12";
        var report = WarcraftCompatibilityInfo.Format(new RecognitionResult
        {
            State = RecognitionState.Unsupported,
            Diagnostics = new() { ProcessVersion = "3.0.0.24268", ExecutableSha256 = hash }
        });
        Assert.Contains("Unsupported", report);
        Assert.Contains("3.0.0.24268", report);
        Assert.Contains(hash, report);
        Assert.Contains("일치하는 검증 프로필 없음", report);
    }

    [Fact]
    public void ShareableReportDoesNotCopyPrivatePathsAddressesOrArbitraryDetails()
    {
        var report = WarcraftCompatibilityInfo.Format(new RecognitionResult
        {
            State = RecognitionState.UnverifiedProfile,
            Diagnostics = new()
            {
                ProcessVersion = "3.0.0.24268", ProfileId = "war3-2.0.4.23745", ProfileRevision = 5,
                ProfileSource = @"C:\Users\private-user\private-folder\memory-profiles.json",
                ResolvedListAddress = "0xABC123", Detail = "private-player-and-token",
                UnknownRawcodes = ["private-rawcode"]
            }
        });
        Assert.DoesNotContain("private", report);
        Assert.DoesNotContain("0xABC123", report);
        Assert.Contains("war3-2.0.4.23745", report);
    }

    [Fact]
    public void CacheWarningIsUsefulWithoutLeakingItsDetailedPath()
    {
        var report = WarcraftCompatibilityInfo.Format(new RecognitionResult
        {
            Diagnostics = new() { ProfileSource = "번들 (사용자 캐시 무시: C:\\private-user\\bad.json)" }
        });
        Assert.Contains("사용자 캐시 오류로 번들 프로필 사용", report);
        Assert.DoesNotContain("private-user", report);
    }

    [Fact]
    public void MalformedIdentityFieldsAreNotCopiedVerbatim()
    {
        var report = WarcraftCompatibilityInfo.Format(new RecognitionResult
        {
            Diagnostics = new() { ProcessVersion = "private\npath", ExecutableSha256 = "private-secret", ProfileId = "private/path" }
        });
        Assert.DoesNotContain("private", report);
    }

    [Theory]
    [InlineData("WarcraftMemoryDiagnostic300CurrentView", "", "")]
    [InlineData("WarcraftMemory", "3.0.0.24268", "")]
    [InlineData("Unknown", "", "BD2A0DC256289DE45287BB60F3725B88F1235216D5177984377FE1CA22840A12")]
    public void DiagnosticReadyCannotLookLikeProductionReady(string source, string version, string hash)
    {
        var diagnostics = new RecognitionDiagnostics { Source = source, ProcessVersion = version, ExecutableSha256 = hash };
        Assert.Equal(WarcraftCompatibilityClassification.DiagnosticOnly, WarcraftCompatibilityInfo.Classify(diagnostics));
        var report = WarcraftCompatibilityInfo.Format(new RecognitionResult { State = RecognitionState.Ready, Diagnostics = diagnostics });
        Assert.Contains("지원 분류: DiagnosticOnly", report);
        Assert.Contains("진단 인식 상태: Ready (운영 준비 상태 아님)", report);
        Assert.Contains("Warcraft 3.0 실전 인식 미지원", report);
        Assert.Contains("보조 리더도 미검증", report);
        Assert.DoesNotContain("ProductionReady", report);
    }

    [Fact]
    public void ArbitraryWellFormedIdentifiersAndFreeTextAreNotShareableEvidence()
    {
        var diagnostics = new RecognitionDiagnostics
        {
            Source = "private-player", ProfileId = "private-player", ProcessVersion = "2.0.4.23745",
            Detail = "DIAGNOSTIC-ONLY verificationReady private-player 0xABC123",
            ProfileSource = @"C:\Users\private-player\profiles.json"
        };
        Assert.Equal(WarcraftCompatibilityClassification.NotAssessed, WarcraftCompatibilityInfo.Classify(diagnostics));
        var report = WarcraftCompatibilityInfo.Format(new RecognitionResult { Status = "private-player", Diagnostics = diagnostics });
        Assert.DoesNotContain("private-player", report);
        Assert.DoesNotContain("verificationReady", report);
        Assert.DoesNotContain("0xABC123", report);
        Assert.Contains("일치하는 검증 프로필 없음", report);
    }

    [Fact]
    public void UnknownStateAndNullIdentityFailClosedWithoutCopyingDetails()
    {
        var report = WarcraftCompatibilityInfo.Format(new RecognitionResult
        {
            State = (RecognitionState)999,
            Diagnostics = new() { ProcessVersion = null!, ExecutableSha256 = null!, ProfileId = null!, ProfileSource = null! }
        });
        Assert.Contains("인식 상태: Unknown", report);
        Assert.Contains("확인되지 않음", report);
        Assert.Contains("지원 분류: NotAssessed", report);
    }
}
