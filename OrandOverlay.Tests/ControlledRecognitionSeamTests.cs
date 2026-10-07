using System.Reflection;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ControlledRecognitionSeamTests
{
    [Fact]
    public void NonRuntimeRecognitionHasExplicitControlledResultEntryPoint()
    {
        var seam = typeof(MainWindow).GetMethod("ScanControlledAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(seam is not null,
            "Non-runtime ScanAsync returns before fixture recognition: an explicit controlled-result pipeline entry point is missing.");
        Assert.Equal(typeof(Task), seam!.ReturnType);
        Assert.Equal(typeof(RecognitionResult), Assert.Single(seam.GetParameters()).ParameterType);
    }
}
