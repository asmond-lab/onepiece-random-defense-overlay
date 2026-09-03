using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PlannerEvidenceCapture;

internal readonly record struct CaptureSourceInput(string Path, byte[] Content);

internal static class CaptureSourceFingerprint
{
    internal static IReadOnlyList<string> CanonicalPaths { get; } =
    [
        "App.xaml",
        "DESIGN.md",
        "BulletOperatingBoard.cs",
        "Data/bullet-strategy-2314.json",
        "FirstRareTargetPolicy.cs",
        "MainWindow.xaml",
        "MainWindow.xaml.cs",
        "OverlayTheme.cs",
        "OverlayWindow.xaml",
        "OverlayWindow.xaml.cs",
        "RecommendationBoard.cs",
        "RecommendationPresentation.cs",
        "tools/PlannerEvidenceCapture/CapturePixelContract.cs",
        "tools/PlannerEvidenceCapture/Program.cs"
    ];

    internal static string FromCanonicalFiles() => Compute(CanonicalPaths.Select(path =>
        new CaptureSourceInput(path, File.ReadAllBytes(path))));

    internal static string Compute(IEnumerable<CaptureSourceInput> inputs)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var input in inputs.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            Append(hash, Encoding.UTF8.GetBytes(input.Path.Replace('\\', '/')));
            Append(hash, input.Content);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, byte[] value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }
}
