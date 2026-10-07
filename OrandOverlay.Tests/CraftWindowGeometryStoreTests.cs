using Xunit;

namespace OrandOverlay.Tests;

public sealed class CraftWindowGeometryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "randypick-craft-geometry-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "geometry.json");

    [Fact]
    public void IsolatedRoundTripPreservesPositionAndDipSize()
    {
        var store = CraftWindowGeometryStore.Create(true, FilePath)!;
        Assert.Null(store.Load());
        Assert.False(Directory.Exists(_directory));
        var saved = new CraftWindowGeometry(-1800, 94, 520, 680) { DpiScale = 1.5 };
        Assert.True(store.Save(saved));
        Assert.Equal(saved, store.Load());
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void RuntimeEffectsOffDoesNotCreateAStoreOrTouchItsPath()
    {
        Assert.Null(CraftWindowGeometryStore.Create(false, FilePath));
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void ExecutionContextKeepsGeometryInsideItsOwnedRoot()
    {
        var production = OverlayExecutionContext.Production(_directory);
        Assert.Equal(Path.Combine(_directory, "craft-window-vertical-e-v1.json"), production.CraftWindowGeometryPath);
        Assert.Null(OverlayExecutionContext.Fixture(new AppSettings()).CraftWindowGeometryPath);
        Assert.False(Directory.Exists(_directory));
    }

    [Theory]
    [InlineData("broken json")]
    [InlineData("null")]
    [InlineData("{\"LeftPixels\":0,\"TopPixels\":0,\"WidthDip\":700,\"HeightDip\":320,\"SchemaVersion\":0}")]
    [InlineData("{\"LeftPixels\":0,\"TopPixels\":0,\"WidthDip\":700,\"HeightDip\":320,\"Layout\":\"horizontal\"}")]
    [InlineData("{\"LeftPixels\":0,\"TopPixels\":0,\"WidthDip\":-1,\"HeightDip\":440}")]
    [InlineData("{\"LeftPixels\":0,\"TopPixels\":0,\"WidthDip\":\"NaN\",\"HeightDip\":440}")]
    public void CorruptOrOtherLayoutDataFallsBackWithoutMutatingFile(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, json);
        Assert.Null(new CraftWindowGeometryStore(FilePath).Load());
        Assert.Equal(json, File.ReadAllText(FilePath));
    }

    [Theory]
    [InlineData(double.NaN, 0, 326, 440)]
    [InlineData(0, double.PositiveInfinity, 326, 440)]
    [InlineData(0, 0, double.NaN, 440)]
    [InlineData(0, 0, 326, -1)]
    public void InvalidNativeBoundsCannotOverwriteExistingGeometry(double x, double y, double width, double height)
    {
        var store = new CraftWindowGeometryStore(FilePath);
        var valid = new CraftWindowGeometry(-100, 50, 326, 440);
        Assert.True(store.Save(valid));
        Assert.False(store.Save(new(x, y, width, height)));
        Assert.Equal(valid, store.Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
