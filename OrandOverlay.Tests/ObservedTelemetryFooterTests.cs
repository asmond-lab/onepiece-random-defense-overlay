using Xunit;

namespace OrandOverlay.Tests;

public sealed class ObservedTelemetryFooterTests
{
    [Fact]
    public void RemoteBackpressureMustNotClaimLocalStorageIsFull()
    {
        var text = ObservedTelemetryFooter.Format(true, false);
        Assert.DoesNotContain("저장 공간이 부족", text, StringComparison.Ordinal);
        Assert.DoesNotContain("기록 저장 공간이 부족합니다", text, StringComparison.Ordinal);
    }
}
