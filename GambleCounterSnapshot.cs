using System.Collections.Immutable;

namespace OrandOverlay;

public enum GambleCounterKind { Low, Middle, High, World, Absalom, LumberWisp }

/// <summary>
/// Pinned war3map.w3u ugol/ulum values, not a debit observation or net cost.
/// Lumber-wisp consumption has no gold/lumber purchase tariff here.
/// </summary>
public sealed record GambleSourceTariff(string UnitRawcode, int Gold, int Lumber)
{
    public string UnitObjectSha256 => "a9aa2cb9c08130c3bee970aecb05b62fdc3db867685f4997dd2533735d2ea278";
    public bool IsPaymentReceipt => false;
}

/// <summary>
/// Cumulative resolved gambles, not queued/canceled inputs. Success/failure describes
/// the map branch; compensation rewards do not turn a failed gamble into a success.
/// </summary>
public sealed record GambleCounterTotals(
    GambleCounterKind Kind, int Attempts, int Successes, int Failures,
    GambleSourceTariff? SourceTariff);

/// <summary>
/// One coherent local-owner observation. No event ordering, exact reward identity,
/// payment receipt, or inference about activity before the caller's consent boundary.
/// SourceCounters retain the original branch bins: ZE includes a ship, and nE also
/// includes fixed and random-exclusive rewards, so neither is a precise tier ledger.
/// The caller owns match generation and revision; never difference across a reset.
/// </summary>
public sealed record GambleCounterSnapshot(
    byte LocalSlot,
    string WarcraftVersion,
    string MapScriptSha256,
    ImmutableDictionary<string, int> SourceCounters,
    ImmutableArray<GambleCounterTotals> Counters);
