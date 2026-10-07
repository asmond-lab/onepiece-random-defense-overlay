using System.IO;

namespace PlannerEvidenceCapture;

internal readonly record struct CapturePixelMetrics(
    int PixelWidth,
    int PixelHeight,
    bool AlphaOpaque,
    double BackgroundCoverage,
    double MinimumTileCoverage,
    double LeftStartCoverage,
    double BorderCoverage);

internal static class CapturePixelContract
{
    internal const byte BackgroundBlue = 13;
    internal const byte BackgroundGreen = 9;
    internal const byte BackgroundRed = 7;

    internal static void FlattenOntoEvidenceBackground(
        Span<byte> pixels, int width, int height, int stride)
    {
        ValidateBuffer(pixels.Length, width, height, stride);
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                var offset = row + x * 4;
                var alpha = pixels[offset + 3];
                pixels[offset] = Composite(
                    pixels[offset], BackgroundBlue, alpha);
                pixels[offset + 1] = Composite(
                    pixels[offset + 1], BackgroundGreen, alpha);
                pixels[offset + 2] = Composite(
                    pixels[offset + 2], BackgroundRed, alpha);
                pixels[offset + 3] = byte.MaxValue;
            }
        }
    }

    internal static CapturePixelMetrics Validate(
        ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        ValidateBuffer(pixels.Length, width, height, stride);
        var populated = 0;
        var populatedByTile = new int[16];
        var pixelsByTile = new int[16];
        var leftStarted = 0;
        var topBorder = 0;
        var bottomBorder = 0;
        var leftBorder = 0;
        var rightBorder = 0;
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                var offset = row + x * 4;
                if (pixels[offset + 3] != byte.MaxValue)
                    throw new InvalidDataException(
                        "Capture alpha must be fully opaque.");
                var isPopulated = IsPopulated(pixels, offset);
                if (isPopulated)
                {
                    populated++;
                    populatedByTile[TileIndex(x, y, width, height)]++;
                }
                pixelsByTile[TileIndex(x, y, width, height)]++;
                if (x == 0 && isPopulated)
                    leftStarted++;
                if (y == 0 && isPopulated)
                    topBorder++;
                if (y == height - 1 && isPopulated)
                    bottomBorder++;
                if (x == 0 && isPopulated)
                    leftBorder++;
                if (x == width - 1 && isPopulated)
                    rightBorder++;
            }
        }

        var backgroundCoverage = (double)populated / (width * height);
        if (backgroundCoverage < 0.5)
            throw new InvalidDataException(
                "Capture background is blank or incompletely composited.");
        var minimumTileCoverage = populatedByTile
            .Zip(pixelsByTile, (count, total) => (double)count / total)
            .Min();
        if (minimumTileCoverage < 0.4)
            throw new InvalidDataException(
                "Capture tile is blank or incompletely composited.");
        var leftStartCoverage = (double)leftStarted / height;
        if (leftStartCoverage < 0.5)
            throw new InvalidDataException(
                "Capture left start is clipped or missing.");
        var borderCoverage = new[]
        {
            (double)topBorder / width,
            (double)bottomBorder / width,
            (double)leftBorder / height,
            (double)rightBorder / height
        }.Min();
        if (borderCoverage < 0.5)
            throw new InvalidDataException(
                "Capture border is missing or incompletely composited.");
        return new CapturePixelMetrics(
            width, height, true, backgroundCoverage, minimumTileCoverage,
            leftStartCoverage, borderCoverage);
    }

    private static byte Composite(byte source, byte background, byte alpha)
    {
        var value = source + background * (byte.MaxValue - alpha) /
            byte.MaxValue;
        return (byte)Math.Min(byte.MaxValue, value);
    }

    private static bool IsPopulated(ReadOnlySpan<byte> pixels, int offset) =>
        Math.Abs(pixels[offset] - BackgroundBlue) > 3 ||
        Math.Abs(pixels[offset + 1] - BackgroundGreen) > 3 ||
        Math.Abs(pixels[offset + 2] - BackgroundRed) > 3;

    private static int TileIndex(
        int x, int y, int width, int height) =>
        Math.Min(3, x * 4 / width) + Math.Min(3, y * 4 / height) * 4;

    private static void ValidateBuffer(
        int length, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0 || stride < checked(width * 4) ||
            length < checked(stride * height))
            throw new ArgumentException("Invalid BGRA32 capture buffer.");
    }
}
