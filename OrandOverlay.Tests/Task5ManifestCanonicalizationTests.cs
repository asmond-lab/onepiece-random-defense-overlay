using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Task5ManifestCanonicalizationTests
{
    private static readonly string[] MemberPaths =
    [
        "Data/map-source-metadata-2314.json",
        "Data/story-progression-2314.json",
        "Data/navigation-mechanics-2314.json",
        "Data/map-recipe-overrides-2314.txt",
        "Data/map-combine-commands-2314.txt"
    ];

    [Fact]
    public void ClosedManifestAcceptsCrLfCheckoutForEveryTextMember()
    {
        using var copy = ManifestCopy.Create();
        copy.RewriteMembersWithCrLf();

        var manifest = MapSourceManifestVerifier.VerifyDirectory(copy.Root);

        Assert.Equal(MemberPaths.Length, manifest.Members.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ClosedManifestRejectsNonLineEndingByteMutation(int memberIndex)
    {
        using var copy = ManifestCopy.Create();
        copy.MutateMemberByte(memberIndex);

        Assert.Throws<InvalidDataException>(() =>
            MapSourceManifestVerifier.VerifyDirectory(copy.Root));
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../"));

    private sealed class ManifestCopy : IDisposable
    {
        public string Root { get; }

        private ManifestCopy(string root) => Root = root;

        public static ManifestCopy Create()
        {
            var root = Path.Combine(Path.GetTempPath(),
                $"orand-task5-canonical-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(root, "Data"));
            var repository = RepositoryRoot();
            foreach (var memberPath in MemberPaths)
            {
                var relative = memberPath.Replace('/', Path.DirectorySeparatorChar);
                File.Copy(Path.Combine(repository, relative), Path.Combine(root, relative));
            }
            File.Copy(Path.Combine(repository, "Data", MapSourceManifestVerifier.FileName),
                Path.Combine(root, "Data", MapSourceManifestVerifier.FileName));
            return new ManifestCopy(root);
        }

        public void RewriteMembersWithCrLf()
        {
            foreach (var memberPath in MemberPaths)
            {
                var path = Path.Combine(Root,
                    memberPath.Replace('/', Path.DirectorySeparatorChar));
                var canonical = File.ReadAllText(path)
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n');
                File.WriteAllText(path,
                    canonical.Replace("\n", "\r\n", StringComparison.Ordinal));
            }
        }

        public void MutateMemberByte(int memberIndex)
        {
            var path = Path.Combine(Root,
                MemberPaths[memberIndex].Replace('/', Path.DirectorySeparatorChar));
            var bytes = File.ReadAllBytes(path);
            var index = Array.FindIndex(bytes,
                value => value is not (byte)'\r' and not (byte)'\n');
            bytes[index] ^= 1;
            File.WriteAllBytes(path, bytes);
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
