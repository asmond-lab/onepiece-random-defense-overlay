using System.Reflection;
using PlannerEvidenceCapture;
using Xunit;

namespace PlannerEvidenceCapture.Tests;

public sealed class CaptureOutputScopeTests : IDisposable
{
    private readonly string temp = Path.Combine(Path.GetTempPath(), "orand-output-tests-" + Guid.NewGuid().ToString("N"));
    public CaptureOutputScopeTests() => Directory.CreateDirectory(temp);
    public void Dispose() => Directory.Delete(temp, true);

    [Fact(Skip = "Host lacks CreateSymbolicLink privilege; no elevation or policy change. Reparse mask is covered with fake attributes.")]
    public void RejectsSymbolicLinkParentWithoutWritingToTarget()
    {
        var target = Directory.CreateDirectory(Path.Combine(temp, "target")).FullName;
        var link = Path.Combine(temp, "link");
        Directory.CreateSymbolicLink(link, target);
        try
        {
            Assert.Throws<IOException>(() => CaptureOutputScope.Create(link, Path.Combine(link, "run")));
            Assert.Empty(Directory.GetFileSystemEntries(target));
        }
        finally { Directory.Delete(link); }
    }

    [Theory]
    [InlineData(FileAttributes.Directory | FileAttributes.ReparsePoint)]
    [InlineData(FileAttributes.ReparsePoint)]
    [InlineData(FileAttributes.Normal)]
    public void RejectsAllReparseTagsAndNonDirectoriesWithoutPathProbe(FileAttributes attributes)
    {
        var method = typeof(CaptureOutputScope).GetMethod("RequirePlainDirectory", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.IsType<IOException>(Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [attributes])).InnerException);
    }

    [Fact]
    public void RunDirectoryCannotBeRenamedWhileScopeIsAlive()
    {
        var output = Path.Combine(temp, "run");
        using (var scope = CaptureOutputScope.Create(temp, output))
            Assert.Throws<IOException>(() => Directory.Move(output, Path.Combine(temp, "replacement")));
        Directory.Move(output, Path.Combine(temp, "replacement"));
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("C:relative")]
    [InlineData("\\\\fake-server\\share\\out")]
    public void RejectsNonLocalAbsolutePathsBeforeIo(string output)
    {
        Assert.Throws<ArgumentException>(() => CaptureOutputScope.Create(temp, output));
    }

    [Fact]
    public void RejectsDotSegmentsInsteadOfNormalizingThemIntoApprovedRoot()
    {
        var output = Path.Combine(temp, "child", "..", "run");
        Assert.Throws<ArgumentException>(() => CaptureOutputScope.Create(temp, output));
        Assert.False(Directory.Exists(Path.Combine(temp, "run")));
    }

    [Fact]
    public void ApprovedParentMustAlreadyExist()
    {
        var missing = Path.Combine(temp, "missing");
        Assert.Throws<DirectoryNotFoundException>(() => CaptureOutputScope.Create(missing, Path.Combine(missing, "run")));
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void JunctionAncestorAndInjectedJunctionSinkAreRejected()
    {
        var target = Directory.CreateDirectory(Path.Combine(temp, "target")).FullName;
        var link = Path.Combine(temp, "junction");
        TempJunction.Create(temp, link, target);
        try
        {
            Assert.Throws<IOException>(() => CaptureOutputScope.Create(link, Path.Combine(link, "run")));
            Assert.Empty(Directory.GetFileSystemEntries(target));
        }
        finally { Directory.Delete(link); }
        using var scope = CaptureOutputScope.Create(temp, Path.Combine(temp, "run"));
        link = Path.Combine(scope.Root, "injected-junction");
        TempJunction.Create(temp, link, target);
        try
        {
            Assert.Throws<IOException>(() => scope.CreateNew(Path.Combine(link, "evidence.txt")));
            Assert.Empty(Directory.GetFileSystemEntries(target));
        }
        finally { Directory.Delete(link); }
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("NUL.txt")]
    [InlineData("COM1.json")]
    [InlineData("LPT1.png")]
    public void ReservedDeviceNamesAreRejectedBeforeFileOpen(string name)
    {
        using var scope = CaptureOutputScope.Create(temp, Path.Combine(temp, "run"));
        Assert.Throws<ArgumentException>(() => scope.CreateNew(Path.Combine(scope.Root, name)));
    }

    [Fact]
    public void NestedOutputsAreOwnedAndPinnedAndScopeDisposalClosesWriters()
    {
        var root = Path.Combine(temp, "run");
        using var scope = CaptureOutputScope.Create(temp, root);
        var stream = scope.CreateNew(Path.Combine(root, "nested", "image.png"));
        stream.WriteByte(7);
        Assert.Throws<IOException>(() => Directory.Move(Path.Combine(root, "nested"), Path.Combine(root, "other")));
        scope.Dispose();
        Assert.Throws<ObjectDisposedException>(() => stream.WriteByte(8));
        Assert.Throws<ObjectDisposedException>(() => scope.CreateNew(Path.Combine(root, "late.txt")));
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(Path.Combine(root, "nested", "image.png")));
    }

    [Fact]
    public void InjectedExistingChildDirectoryIsNotAdopted()
    {
        using var scope = CaptureOutputScope.Create(temp, Path.Combine(temp, "run"));
        var injected = Directory.CreateDirectory(Path.Combine(scope.Root, "injected")).FullName;
        Assert.Throws<IOException>(() => scope.CreateNew(Path.Combine(injected, "evidence.txt")));
        Assert.Empty(Directory.GetFileSystemEntries(injected));
    }

    [Fact]
    public void OutputWritesAreCreateNewAndContained()
    {
        using var scope = CaptureOutputScope.Create(temp, Path.Combine(temp, "run"));
        var create = typeof(CaptureOutputScope).GetMethod("CreateNew");
        Assert.NotNull(create);
        var path = Path.Combine(scope.Root, "evidence.json");
        using (var stream = (Stream)create.Invoke(scope, [path])!) stream.WriteByte(42);
        Assert.IsType<IOException>(Assert.Throws<TargetInvocationException>(() => create.Invoke(scope, [path])).InnerException);
        Assert.Equal(new byte[] { 42 }, File.ReadAllBytes(path));
        var outside = Path.Combine(temp, "sentinel.json");
        File.WriteAllText(outside, "sentinel");
        Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(() => create.Invoke(scope, [outside])).InnerException);
        Assert.Equal("sentinel", File.ReadAllText(outside));
    }

    [Fact]
    public void NewOwnedRunRejectsSiblingPrefixAndExistingOutput()
    {
        var type = typeof(CapturePixelContract).Assembly.GetType("PlannerEvidenceCapture.CaptureOutputScope");
        Assert.NotNull(type);
        var create = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
        var output = Path.Combine(temp, "run");
        using (var scope = (IDisposable)create.Invoke(null, [temp, output])!)
            Assert.True(Directory.Exists(output));
        Assert.IsType<IOException>(Assert.Throws<TargetInvocationException>(() => create.Invoke(null, [temp, output])).InnerException);
        var sibling = temp + "-sibling";
        Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(() => create.Invoke(null, [temp, sibling])).InnerException);
        Assert.False(Directory.Exists(sibling));
    }
}
