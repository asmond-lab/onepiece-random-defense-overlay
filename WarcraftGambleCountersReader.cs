using System.Collections.Immutable;
using System.ComponentModel;

namespace OrandOverlay;

/// <summary>
/// Read-only, stateless reader: no discovery cache, last-good fallback, process opening,
/// persistence or upload. The caller supplies a verified local slot and current map-table
/// anchor only after consent, and drops the result at its match/revision boundary.
/// Null means unbound/incoherent, never zero. Cancellation is propagated.
/// </summary>
internal sealed class WarcraftGambleCountersReader
{
    // Pinned JASS integer arrays (declarations 192-207), native type 9 as for OE.
    // SON:41637, Ciw:10022, IoN:20093, vAG:76528, ZRw:50147, LbN:23500.
    // VQG:45674-45679 independently displays these cumulative counters.
    private static readonly (string Name, int Type)[] Bindings =
    [
        ("IE", 9), ("LE", 9), ("ZE", 9), ("VE", 9),
        ("eE", 9), ("DE", 9), ("kE", 9), ("OE", 9), ("QE", 9), ("nE", 9),
        ("YE", 9), ("iE", 9), ("AE", 9), ("vE", 9), ("Gw", 9), ("Nw", 9)
    ];

    internal GambleCounterSnapshot? Read(Func<ulong, int, byte[]> read, string version,
        string mapHash, byte? localSlot, ulong? anchor, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (version != "2.0.4.23745" || mapHash != RouteQuestCatalog.MapScriptSha256 ||
            localSlot is null or > 3 || anchor is null ||
            !ReadOnlyProcessMemory.IsPlausibleUserAddress(anchor.Value)) return null;
        try
        {
            // Reuses named discovery, node/header/data checks and two complete read traces.
            // These counters exist independently of any active/completed Q006 assignment.
            var values = StableMapGlobals.Read(read, anchor.Value, Bindings, localSlot.Value, token);
            token.ThrowIfCancellationRequested();
            if (values.Values.Any(value => value < 0) ||
                (long)values["LE"] + values["ZE"] + values["VE"] != values["IE"] ||
                values["DE"] > values["eE"] ||
                (long)values["OE"] + values["QE"] + values["nE"] != values["kE"] ||
                values["iE"] > values["YE"] || values["vE"] > values["AE"] ||
                values["Nw"] > values["Gw"]) return null;

            return new(localSlot.Value, version, mapHash,
                values.ToImmutableDictionary(StringComparer.Ordinal),
                [
                    new(GambleCounterKind.Low, values["IE"], values["IE"] - values["LE"], values["LE"],
                        new("h06B", 200, 1)),
                    new(GambleCounterKind.Middle, values["eE"], values["DE"], values["eE"] - values["DE"],
                        new("h06C", 1000, 2)),
                    new(GambleCounterKind.High, values["kE"], values["kE"] - values["OE"], values["OE"],
                        new("h06D", 2000, 4)),
                    new(GambleCounterKind.World, values["YE"], values["iE"], values["YE"] - values["iE"],
                        new("H0AW", 3500, 5)),
                    new(GambleCounterKind.Absalom, values["AE"], values["vE"], values["AE"] - values["vE"],
                        new("h069", 500, 1)),
                    new(GambleCounterKind.LumberWisp, values["Gw"], values["Nw"], values["Gw"] - values["Nw"], null)
                ]);
        }
        catch (Exception exception) when (exception is InvalidDataException or Win32Exception or OverflowException)
        {
            // An unreadable or moving native table is explicitly unavailable to consumers.
            return null;
        }
    }
}
