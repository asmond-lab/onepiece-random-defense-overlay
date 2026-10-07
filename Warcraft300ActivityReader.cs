using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;

namespace OrandOverlay;

internal sealed record Warcraft300ActivityInputs(
    Warcraft300RoundInputs Context, IReadOnlyList<Warcraft300SnapshotNode> Nodes);

internal sealed record Warcraft300ActivityObservation(
    ImmutableDictionary<string, int>? Values, string Status, int ReadCalls, int ReadBytes, double DurationMs)
{
    internal ImmutableArray<string> UnavailableNames { get; init; } = [];
}

internal static class Warcraft300ActivityReader
{
    internal static Warcraft300ActivityObservation Read(Func<ulong, int, byte[]> read,
        Warcraft300GrowthObservation growth,
        Func<Func<ulong, int, byte[]>, Warcraft300ObservedRoundReader.Context> currentContext,
        CancellationToken token = default, Func<long>? timestamp = null)
    {
        token.ThrowIfCancellationRequested();
        timestamp ??= Stopwatch.GetTimestamp;
        var started = timestamp();
        var calls = 0;
        var bytes = 0;
        var input = growth.ActivityInputs;
        double Elapsed() => Stopwatch.GetElapsedTime(started, timestamp()).TotalMilliseconds;
        var journal = new List<(ulong Address, byte[] Bytes)>();
        var recording = true;
        string? currentVariable = null;
        try
        {
            void Need(bool condition, string reason)
            {
                if (!condition) throw new InvalidDataException(reason);
            }
            void Check()
            {
                token.ThrowIfCancellationRequested();
                var now = timestamp();
                Need(input is not null && now >= input.Context.StartedTimestamp &&
                    Stopwatch.GetElapsedTime(input.Context.StartedTimestamp, now) < TimeSpan.FromSeconds(3),
                    "activity-input-expired");
                Need(now >= started && Stopwatch.GetElapsedTime(started, now).TotalMilliseconds < 100,
                    "activity-time-budget");
            }
            byte[] R(ulong address, int size)
            {
                Check();
                Need(size > 0 && address >= 0x10000 && address <= 0x7FFFFFFFFFFF &&
                    (ulong)(size - 1) <= 0x7FFFFFFFFFFF - address, "activity-address-bounds");
                Need(calls < 1024 && size <= 32768 - bytes, "activity-read-budget");
                calls++; bytes += size;
                var result = read(address, size);
                Check();
                Need(result.Length == size, "activity-short-read");
                var copy = result.ToArray();
                if (recording) journal.Add((address, copy));
                return copy;
            }
            ulong Add(ulong address, ulong offset) => checked(address + offset);
            ulong Q(ulong address) => BitConverter.ToUInt64(R(address, 8));
            uint D(ulong address) => BitConverter.ToUInt32(R(address, 4));

            Check();
            var data = input!;
            var context = data.Context;
            Need(context.MapVersion is "2.321" or "2.322" or "2.323" && data.Nodes.Count is > 0 and <= 64 &&
                data.Nodes.Select(node => node.Name).Distinct(StringComparer.Ordinal).Count() == data.Nodes.Count,
                "activity-source-selection");
            Need(growth.SessionKey == context.Session && growth.CurrentView == context.View &&
                growth.World == context.World && growth.Instance == context.Instance &&
                growth.Script == context.Script && growth.MapManager == context.Manager &&
                growth.NativeRegistry == context.Registry && growth.OwnerAggregate == context.Owner &&
                growth.DataTable == context.Table && context.View.Slot <= 3, "activity-context-binding");
            var expected = new Warcraft300ObservedRoundReader.Context(
                context.Session, context.Module, context.View, context.World);
            Need(currentContext(R) == expected, "activity-current-context");
            Need(Q(Add(context.Module, 0x2F5EF00)) == context.Ui &&
                Q(Add(context.Module, 0x2F85360)) == context.Ui &&
                Q(context.Ui) == Add(context.Module, 0x275ED08) &&
                Q(context.World) == Add(context.Module, 0x2764A20) &&
                Q(Add(context.World, 0x40)) == context.Ui, "activity-world-identity");
            Need(Q(Add(context.View.Root, 0x25D0)) == context.Instance &&
                Q(Add(context.View.Root, 0x25E0)) == context.Script &&
                Q(Add(context.View.Root, 0x2620)) == context.Manager &&
                Q(Add(context.Module, 0x2F807F0)) == context.Registry, "activity-vm-identity");
            context.RecheckHeaders(R);
            var values = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
            var unavailable = ImmutableArray.CreateBuilder<string>();
            foreach (var node in data.Nodes)
            {
                currentVariable = node.Name;
                Need(node.RuntimeTag == 9 && node.DeclaredTag == 9, "activity-integer-array-type");
                node.Recheck(R);
                var array = Q(Add(node.Address, 56));
                if (array == 0)
                {
                    unavailable.Add(node.Name);
                    continue;
                }
                Need((array & 7) == 0 && Q(array) == Add(context.Module, 0x2809EF8), "activity-array-vtable");
                var count = D(Add(array, 8));
                var pointer = Q(Add(array, 16));
                var capacity = D(Add(array, 24));
                Need(count <= capacity && capacity <= 8192 &&
                    (pointer & 3) == 0, "activity-array-bounds");
                if (count <= context.View.Slot)
                {
                    unavailable.Add(node.Name);
                    continue;
                }
                var value = BitConverter.ToInt32(R(Add(pointer, 4UL * context.View.Slot), 4));
                Need(value >= 0, "activity-negative-counter");
                values.Add(node.Name, value);
            }
            currentVariable = null;
            recording = false;
            foreach (var item in journal)
                Need(item.Bytes.AsSpan().SequenceEqual(R(item.Address, item.Bytes.Length)), "activity-snapshot-changed");
            Need(currentContext(R) == expected, "activity-current-context");
            Check();
            return new(values.Count == 0 ? null : values.ToImmutable(),
                unavailable.Count == 0 ? "ready" : "partial", calls, bytes, Elapsed())
            { UnavailableNames = unavailable.ToImmutable() };
        }
        catch (Exception error) when (error is InvalidDataException or Win32Exception or OverflowException)
        {
            return new(null, currentVariable is null ? error.Message : error.Message + ":" + currentVariable,
                calls, bytes, Elapsed());
        }
    }
}
