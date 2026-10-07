using PlannerEvidenceCapture;
using Xunit;

namespace PlannerEvidenceCapture.Tests;

public sealed class CapturePixelContractTests
{
    [Fact]
    public void FlattenMakesPremultipliedPixelsOpaque()
    {
        var pixels = new byte[]
        {
            16, 24, 32, 128,
            80, 90, 100, 255
        };

        CapturePixelContract.FlattenOntoEvidenceBackground(
            pixels, width: 2, height: 1, stride: 8);

        Assert.Equal(new byte[]
        {
            22, 28, 35, 255,
            80, 90, 100, 255
        }, pixels);
    }

    [Fact]
    public void ValidateRejectsTransparentEvidence()
    {
        var pixels = CompleteFrame();
        pixels[3] = 254;

        Assert.Throws<InvalidDataException>(() =>
            CapturePixelContract.Validate(pixels, width: 8, height: 8, stride: 32));
    }

    [Fact]
    public void ValidateRejectsEmptyBackground()
    {
        var pixels = SolidFrame(8, 8, 13, 9, 7);

        Assert.Throws<InvalidDataException>(() =>
            CapturePixelContract.Validate(pixels, width: 8, height: 8, stride: 32));
    }

    [Fact]
    public void ValidateRejectsMissingLeftStart()
    {
        var pixels = CompleteFrame();
        PaintColumn(pixels, width: 8, x: 0, 13, 9, 7);

        Assert.Throws<InvalidDataException>(() =>
            CapturePixelContract.Validate(pixels, width: 8, height: 8, stride: 32));
    }

    [Fact]
    public void ValidateRejectsMissingBorder()
    {
        var pixels = CompleteFrame();
        PaintRow(pixels, width: 8, y: 0, 13, 9, 7);

        Assert.Throws<InvalidDataException>(() =>
            CapturePixelContract.Validate(pixels, width: 8, height: 8, stride: 32));
    }

    [Fact]
    public void ValidateRejectsEmptyImageTile()
    {
        var pixels = CompleteFrame();
        for (var y = 0; y < 4; y++)
            for (var x = 4; x < 8; x++)
                PaintPixel(pixels, width: 8, x, y, 13, 9, 7);

        var error = Assert.Throws<InvalidDataException>(() =>
            CapturePixelContract.Validate(pixels, width: 8, height: 8, stride: 32));

        Assert.Contains("tile", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateAcceptsOpaquePopulatedBorderedFrame()
    {
        var metrics = CapturePixelContract.Validate(
            CompleteFrame(), width: 8, height: 8, stride: 32);

        Assert.True(metrics.AlphaOpaque);
        Assert.Equal(8, metrics.PixelWidth);
        Assert.Equal(8, metrics.PixelHeight);
        Assert.True(metrics.BackgroundCoverage >= 0.5);
        Assert.True(metrics.MinimumTileCoverage >= 0.4);
        Assert.True(metrics.LeftStartCoverage >= 0.5);
        Assert.True(metrics.BorderCoverage >= 0.5);
    }

    private static byte[] CompleteFrame()
    {
        var pixels = SolidFrame(8, 8, 40, 44, 48);
        for (var y = 2; y < 6; y++)
            for (var x = 2; x < 6; x++)
                PaintPixel(pixels, width: 8, x, y, 90, 100, 110);
        return pixels;
    }

    private static byte[] SolidFrame(
        int width, int height, byte blue, byte green, byte red)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                PaintPixel(pixels, width, x, y, blue, green, red);
        return pixels;
    }

    private static void PaintColumn(
        byte[] pixels, int width, int x, byte blue, byte green, byte red)
    {
        for (var y = 0; y < pixels.Length / (width * 4); y++)
            PaintPixel(pixels, width, x, y, blue, green, red);
    }

    private static void PaintRow(
        byte[] pixels, int width, int y, byte blue, byte green, byte red)
    {
        for (var x = 0; x < width; x++)
            PaintPixel(pixels, width, x, y, blue, green, red);
    }

    private static void PaintPixel(
        byte[] pixels, int width, int x, int y,
        byte blue, byte green, byte red)
    {
        var offset = (y * width + x) * 4;
        pixels[offset] = blue;
        pixels[offset + 1] = green;
        pixels[offset + 2] = red;
        pixels[offset + 3] = 255;
    }
}
