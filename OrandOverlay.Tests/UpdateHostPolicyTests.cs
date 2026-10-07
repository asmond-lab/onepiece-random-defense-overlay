using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class UpdateHostPolicyTests
{
    [Theory]
    [InlineData(@"C:\app\RandyPick.exe")]
    [InlineData(@"C:\app\RandyPick (2).exe")]
    [InlineData(@"C:\app\OrandOverlay.exe")]
    [InlineData(@"C:\app\My renamed overlay.EXE")]
    public void ActualApplicationEntryAssemblyMayReplaceExistingStandaloneHost(string path)
    {
        var application = typeof(UpdateService).Assembly;
        Assert.True(UpdateHostPolicy.CanSelfInstall(application, application, path, true, false));
        // No Assembly.Location assumption: extracted bundles and ordinary loaded test assemblies
        // can both have a location without affecting this pure identity decision.
    }

    [Theory]
    [InlineData(@"C:\tools\testhost.exe")]
    [InlineData(@"C:\tools\pwsh.exe")]
    [InlineData(@"C:\tools\dotnet.exe")]
    [InlineData(@"C:\tools\RandyPick.exe")]
    public void LoadingUpdaterAssemblyDoesNotAuthorizeReplacingAnotherEntryAssembly(string path)
    {
        Assert.False(UpdateHostPolicy.CanSelfInstall(typeof(UpdateHostPolicyTests).Assembly,
            typeof(UpdateService).Assembly, path, true, false));
        Assert.False(UpdateHostPolicy.CanSelfInstall(null, typeof(UpdateService).Assembly, path, true, false));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" ")]
    [InlineData("RandyPick.exe")]
    [InlineData(@"C:\app\RandyPick.dll")]
    [InlineData(@"C:\app\")]
    public void MissingRelativeOrNonExecutablePathsAreRejected(string? path)
    {
        var application = typeof(UpdateService).Assembly;
        Assert.False(UpdateHostPolicy.CanSelfInstall(application, application, path, true, false));
    }

    [Fact]
    public void ExistingProcessFileAndNoAdjacentApplicationDllAreBothRequired()
    {
        var application = typeof(UpdateService).Assembly;
        Assert.False(UpdateHostPolicy.CanSelfInstall(application, application, @"C:\app\RandyPick.exe", false, false));
        Assert.False(UpdateHostPolicy.CanSelfInstall(application, application, @"C:\app\RandyPick.exe", true, true));
        Assert.False(UpdateHostPolicy.CanSelfInstall(application, application, @"C:\app\" + '\0' + "RandyPick.exe", true, false));
    }

    [Fact]
    public void RealUnitTestHostNeverReceivesSelfInstallCapability()
    {
        Assert.False(UpdateService.CanSelfInstall);
        Assert.False(UpdateService.CanAutomaticallyInstall);
    }
}
