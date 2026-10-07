using System.Net;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RuntimeIsolationTests
{
    [Fact]
    public async Task InertTelemetryNeverTouchesQueueOrTransportEvenAfterEnable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "orand-isolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sentinel = Path.Combine(directory, "pending.v2.json");
            byte[] before = [1, 2, 3, 4];
            File.WriteAllBytes(sentinel, before);
            using var handler = new RecordingTransport();
            using var http = new HttpClient(handler);
            // Compatibility fallback reproduces the old bug ONLY in this disposable directory.
            var constructor = typeof(TelemetryUploader).GetConstructors().Single();
            object?[] arguments = constructor.GetParameters().Length == 5
                ? ["https://example.invalid", directory, http, false, false]
                : ["https://example.invalid", directory, http, false];
            var uploader = (TelemetryUploader)constructor.Invoke(arguments);
            Assert.True(File.Exists(sentinel), "Non-runtime construction deleted the queue sentinel");
            uploader.SetEnabled(true);
            uploader.Enqueue(MatchTelemetryRecorder.Build("0.6.64", "2.314", "악몽",
                DamageLane.Physical, "불멸[물리]", RecommendationSurface.TopAndNavigation,
                RecommendationUrgency.BossSurvival, 11, 2, new RecognitionTelemetryCounts(18, 2, 1, 0), "clear"));
            await uploader.FlushPendingAsync();
            uploader.TrimQueue();
            uploader.SetEnabled(false, deletePending: true);
            uploader.DeletePending();
            Assert.False(uploader.Enabled);
            Assert.Equal(0, uploader.PendingCount);
            Assert.Equal(before, File.ReadAllBytes(sentinel));
            Assert.Single(Directory.GetFiles(directory));
            Assert.Equal(0, handler.Calls);
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class RecordingTransport : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
