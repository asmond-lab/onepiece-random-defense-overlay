using System.IO.Compression;
using System.Text;
using WarcraftProbe;
using Xunit;

public sealed class CommandAndFilesTests
{
    [Fact] public void RepeatedUnknownAndMissingOptionsFail()
    {
        Assert.Throws<ArgumentException>(() => Program.Options(["capture","--copy","a","--copy","b"], ["--copy"], []));
        Assert.Throws<ArgumentException>(() => Program.Options(["capture","--bad","a"], ["--copy"], []));
        Assert.Throws<ArgumentException>(() => Program.Options(["capture","--copy"], ["--copy"], []));
    }
    [Fact] public void FlagsDoNotConsumeTheFollowingKey()
    {
        var r = Program.Options(["capture","--metadata-only","--copy","a"], ["--copy","--metadata-only"], ["--metadata-only"]);
        Assert.Equal("true",r["--metadata-only"]); Assert.Equal("a",r["--copy"]);
    }
    [Theory]
    [InlineData("")]
    [InlineData("line\nbreak")]
    [InlineData("line\rbreak")]
    public void InvalidLabelIsRejected(string s) => Assert.Throws<ArgumentException>(() => Program.Label(s));
    [Fact] public void LabelHasBoundedLength() { Assert.Throws<ArgumentException>(() => Program.Label(new string('x',81))); Assert.Equal("조합 전",Program.Label("조합 전")); }
    [Theory]
    [InlineData(@"relative.json")]
    [InlineData(@"\\server\share\x.json")]
    [InlineData(@"\\?\C:\x.json")]
    [InlineData(@"C:\x.json:stream")]
    [InlineData(@"C:\x.\sample")]
    [InlineData(@"C:\CON.txt")]
    [InlineData(@"C:\COM1\x.json")]
    [InlineData(@"C:\MemoryDiagnostics\x.json")]
    public void ForbiddenPathsFailLexicallyBeforeIo(string p) => Assert.Throws<ArgumentException>(() => ProbeFiles.LocalPath(p));
    [Fact] public void AbsoluteLocalSpacePathIsAllowed() => Assert.Equal(@"C:\Users\sample\Warcraft III.exe", ProbeFiles.LocalPath(@"C:\Users\sample\Warcraft III.exe"));
    private static string Root()
    {
        var root = Path.Combine(Environment.GetEnvironmentVariable("WARCRAFT_PROBE_TEST_ROOT") ?? AppContext.BaseDirectory, "probe-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); return root;
    }
    [Fact] public void OutputArchiveContainsOnlyWhitelistedReportFiles()
    {
        var root = Root(); var target = Path.Combine(root,"capture");
        try
        {
            ProbeFiles.SaveNew(target,"snapshot.json",Encoding.UTF8.GetBytes("{}"),Encoding.UTF8.GetBytes("# test"));
            Assert.Equal(new[]{"report.md","report.zip","snapshot.json"},Directory.GetFiles(target).Select(Path.GetFileName).Order().ToArray());
            using var zip=ZipFile.OpenRead(Path.Combine(target,"report.zip"));
            Assert.Equal(new[]{"report.md","snapshot.json"},zip.Entries.Select(e=>e.FullName).Order().ToArray());
        }
        finally { Directory.Delete(root,true); }
    }
    [Fact] public void ExistingOutputIsPreserved()
    {
        var root=Root();var target=Path.Combine(root,"capture");Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"keep.txt"),"untouched");
        try { Assert.Throws<IOException>(()=>ProbeFiles.SaveNew(target,"snapshot.json",[1],[2]));Assert.Equal("untouched",File.ReadAllText(Path.Combine(target,"keep.txt"))); }
        finally { Directory.Delete(root,true); }
    }
    [Fact] public void InputSizeLimitAndExactBytesAreEnforced()
    {
        var root=Root();var file=Path.Combine(root,"input.json");File.WriteAllBytes(file,[1,2,3,4]);
        try { Assert.Throws<InvalidDataException>(()=>ProbeFiles.ReadLocalFile(file,3));Assert.Equal(new byte[]{1,2,3,4},ProbeFiles.ReadLocalFile(file,4)); }
        finally { Directory.Delete(root,true); }
    }
    [Fact] public void WrongEpochCannotReachModuleMetadata()
    {
        int touched=0; var time=DateTimeOffset.UtcNow;
        Assert.Throws<InvalidDataException>(()=>WindowsCollector.AfterEpoch(5,time,5,time.AddTicks(1),()=>++touched));
        Assert.Equal(0,touched);
    }
    [Fact] public void MatchingEpochAllowsExactlyOneMetadataRead()
    {
        int touched=0; var time=DateTimeOffset.UtcNow;
        Assert.Equal(1,WindowsCollector.AfterEpoch(5,time,5,time,()=>++touched)); Assert.Equal(1,touched);
    }
    [Fact] public void NativeCopyPathExclusionIsLexical() => Assert.Throws<InvalidDataException>(() => WindowsCollector.ValidateCopyPath(@"C:\MemoryDiagnostics\copy.exe", @"C:\installed\Warcraft III.exe"));
    [Fact] public void OutputNameCannotEscapeWhitelist() => Assert.Throws<ArgumentException>(()=>ProbeFiles.SaveNew(@"C:\test", "../other.bin", [1], [2]));
}
