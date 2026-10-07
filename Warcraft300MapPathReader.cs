using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace OrandOverlay;

/// <summary>Caller-attested epoch, not independently discovered local-player/gameplay identity.
/// ExperimentalActiveMapPathSemanticsVerified requires independent field-use/lifecycle proof; default false.
/// Capture must freshly validate the current world/VM epoch on EVERY invocation; never return a cached stamp.</summary>
public sealed record NativeMapContext(int ProcessId, DateTimeOffset ProcessStartedAt, string Version,
    string ExecutableSha256, ulong ModuleBase, ulong Root, ulong World, ulong Ui,
    ulong Instance, ulong Script, Guid Epoch, bool CallerValidated,
    bool ExperimentalActiveMapPathSemanticsVerified = false);

public sealed record NativeMapPathResult(NativeMapPathObservation? Observation, string Failure, long BytesRead);

/// <summary>No public constructor: filesystem binding cannot start from an arbitrary UI path.</summary>
public sealed class NativeMapPathObservation
{
    public string Path { get; }
    public NativeMapContext Context { get; }
    public string Provenance => "CGameWar3+0x2638 -> CMapSetupWar3+0x50 -> CStringRep+0x38 -> UTF-8 path";
    internal long Created { get; }
    internal Func<long> Clock { get; }
    internal Func<CancellationToken, NativeMapPathResult> Refresh { get; }
    internal string Stamp { get; }
    internal NativeMapPathObservation(string path, NativeMapContext context, long created,
        Func<long> clock, Func<CancellationToken, NativeMapPathResult> refresh, string stamp)
    { Path = path; Context = context; Created = created; Clock = clock; Refresh = refresh; Stamp = stamp; }
    internal void CheckFresh(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var age = Clock() - Created;
        if (age < 0 || age >= 3000) throw new InvalidDataException("ObservationExpired");
    }
    internal void Revalidate(CancellationToken token)
    {
        CheckFresh(token);
        var fresh = Refresh(token);
        CheckFresh(token);
        if (fresh.Observation is not { } other || other.Path != Path || other.Context != Context || other.Stamp != Stamp)
            throw new InvalidDataException("NativeContextOrPathChanged: " + fresh.Failure);
    }
}

/// <summary>Pure read-delegate capability. No process attachment or filesystem access.
/// Delegate must only read committed readable non-guard pages and must itself be bounded/cancellable.</summary>
public static class Warcraft300MapPathReader
{
    public const int MaximumPathBytes = 2048, MaximumReadBytes = 16384, MaximumMilliseconds = 1000;
    public static NativeMapPathResult Observe(Func<ulong, int, byte[]> read, NativeMapContext expected,
        Func<NativeMapContext> captureFreshValidatedContext, CancellationToken token = default) =>
        ObserveCore(read, expected, captureFreshValidatedContext, () => Environment.TickCount64, token);

    internal static NativeMapPathResult ObserveCore(Func<ulong, int, byte[]> read, NativeMapContext expected,
        Func<NativeMapContext> capture, Func<long> clock, CancellationToken token)
    {
        long used = 0, started = clock();
        try
        {
            void Check()
            {
                token.ThrowIfCancellationRequested();
                long elapsed = clock() - started;
                if (elapsed < 0 || elapsed >= MaximumMilliseconds) throw new InvalidDataException("ReadTimeBudget");
                if (used > MaximumReadBytes) throw new InvalidDataException("ReadByteBudget");
            }
            void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool yes, string why) { if (!yes) throw new InvalidDataException(why); }
            ulong Add(ulong a, ulong n)
            { Require(a >= 65536 && a <= 0x7FFFFFFFFFFFUL - n, "AddressRange"); return a + n; }
            byte[] Read(ulong a, int n)
            {
                Check(); Add(a, checked((ulong)n)); used = checked(used + n); Check();
                var output = new byte[n];
                for (int done = 0; done < n;)
                {
                    Check(); var address = Add(a, (ulong)done);
                    int count = Math.Min(n - done, (int)(4096UL - (address & 4095)));
                    var part = read(address, count); Check();
                    Require(part is not null && part.Length == count, "ShortRead");
                    Buffer.BlockCopy(part!, 0, output, done, count); done += count;
                }
                return output;
            }
            ulong Q(ulong a) => BitConverter.ToUInt64(Read(a, 8), 0);
            void Typed(ulong a, ulong rva) => Require(Q(a) == Add(expected.ModuleBase, rva), "VtableMismatch");
            Require(expected is not null && read is not null && capture is not null, "MissingContext");
            Require(expected!.CallerValidated && expected.Epoch != Guid.Empty && expected.ProcessId > 0 &&
                expected.ProcessStartedAt != default, "UntrustedContext");
            Require(expected.Version == Warcraft300Diagnostic.Version && string.Equals(expected.ExecutableSha256,
                Warcraft300Diagnostic.Hash, StringComparison.OrdinalIgnoreCase), "ExecutablePinMismatch");
            ulong[] Header()
            {
                Check(); Require(capture() == expected, "ContextChanged"); Check();
                ulong b = expected.ModuleBase, encoded = Q(Add(b, 0x2E9AD00));
                ulong root = Warcraft300Diagnostic.DecodeRoot(encoded);
                Require(root == expected.Root, "RootChanged"); Typed(root, 0x26C8C70);
                Typed(expected.World, 0x2764A20); Typed(expected.Ui, 0x275ED08);
                Require(Q(Add(expected.World, 0x40)) == expected.Ui && Q(Add(b, 0x2F5EF00)) == expected.Ui &&
                    Q(Add(b, 0x2F85360)) == expected.Ui, "WorldUiMismatch");
                ulong instance = Q(Add(root, 0x25D0)), script = Q(Add(root, 0x25E0));
                Require(instance == expected.Instance && script == expected.Script, "VmEpochChanged");
                Typed(instance, 0x27ECBC0); Typed(script, 0x27ECC40);
                ulong state = Q(Add(root, 0x2620)); Typed(state, 0x26CCE00);
                ulong setup = Q(Add(root, 0x2638)); Typed(setup, 0x26C8AE8);
                ulong rep = Q(Add(setup, 0x50)); Typed(rep, 0x2282340);
                ulong text = Q(Add(rep, 0x38)); Add(text, 1);
                // Only known fields, not guessed object sizes or adjacent allocator contents.
                return new[] { encoded, root, instance, script, state, setup, rep, text,
                    Q(Add(instance, 8)), Q(Add(instance, 16)), Q(Add(instance, 24)),
                    Q(Add(instance, 32)), Q(Add(instance, 40)), Q(Add(script, 8)) };
            }
            byte[] Text(ulong a)
            {
                var bytes = new List<byte>(MaximumPathBytes);
                for (int i = 0; i < MaximumPathBytes;)
                {
                    int n = Math.Min(32, Math.Min(MaximumPathBytes - i, (int)(4096UL - ((a + (ulong)i) & 4095))));
                    var block = Read(Add(a, (ulong)i), n); i += n;
                    foreach (byte value in block) { if (value == 0) return bytes.ToArray(); bytes.Add(value); }
                }
                throw new InvalidDataException("UnterminatedPath");
            }
            var h1 = Header(); var t1 = Text(h1[7]); var h2 = Header(); var t2 = Text(h2[7]); var h3 = Header();
            Require(h1.SequenceEqual(h2) && h1.SequenceEqual(h3) && t1.SequenceEqual(t2), "PathOrHeaderChanged");
            string path = new UTF8Encoding(false, true).GetString(t1);
            Require(IsSafeLocalMapPath(path), "UnsafeMapPath"); Check();
            var stamp = string.Join(":", h1.Select(v => v.ToString("X")));
            var observation = new NativeMapPathObservation(path, expected, started, clock,
                ct => ObserveCore(read, expected, capture, clock, ct), stamp);
            return new(observation, "", used);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
        { return new(null, ex is OperationCanceledException ? "Cancelled" : ex.Message, used); }
    }

    internal static bool IsSafeLocalMapPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length < 8 || !char.IsAsciiLetter(path[0]) || path[1] != ':' ||
            (path[2] != '/' && path[2] != (char)92)) return false;
        if (!path.EndsWith(".w3x", StringComparison.OrdinalIgnoreCase)) return false;
        for (int i = 2; i < path.Length; i++)
            if (path[i] < 32 || path[i] == 127 || path[i] == (char)34 || ":*?<>|".Contains(path[i])) return false;
        var parts = path.Substring(3).Replace('/', (char)92).Split((char)92);
        foreach (var part in parts)
        {
            if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(' ') || part.EndsWith('.')) return false;
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
                (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]))) return false;
        }
        return true;
    }
}
