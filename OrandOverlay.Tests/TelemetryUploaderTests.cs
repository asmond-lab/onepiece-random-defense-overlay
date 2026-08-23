using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class TelemetryUploaderTests
{
    [Fact]
    public void Enqueue_PersistsRecordBeforeReturning()
    {
        var queueDirectory = Path.Combine(
            Path.GetTempPath(), $"orand-telemetry-{Guid.NewGuid():N}");
        try
        {
            var uploader = new TelemetryUploader(
                "http://127.0.0.1:9/v1/records", queueDirectory);
            var record = new TelemetryRecord
            {
                RecordId = Guid.NewGuid().ToString(),
                AnonId = Guid.NewGuid().ToString(),
                AppVersion = "0.6.24",
                MapVersion = "2.314",
                Outcome = "fail",
                OutcomeSource = "unitWipe"
            };

            uploader.Enqueue(record);

            Assert.Equal(1, uploader.PendingCount);
        }
        finally
        {
            if (Directory.Exists(queueDirectory))
                Directory.Delete(queueDirectory, recursive: true);
        }
    }
}
