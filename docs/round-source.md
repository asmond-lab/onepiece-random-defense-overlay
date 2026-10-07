# Warcraft 3.0 current-round source: implementation blocked

Status: **BLOCKED, not activated, no production reader or API delivered.**

The requested strict reader cannot be implemented from the permitted evidence without inventing two required registry type IDs. No existing source, project, profile, UI, native reader, model, or tool was modified. The initial source-only pass performed no build, network, process, or game access. A subsequently authorized, bounded read-only attempt is documented below. MemoryDiagnostics was not read.

## Permitted evidence inspected

- docs/warcraft-3.0.0.24268-live-investigation.md, lines 98-106.
- WarcraftCurrentRoundReader.cs and Warcraft300HandleValidator.cs.
- Prior isolated round-probe source, findings and JSON under:
  C:\Users\123\.aside\u\0\sessions\2026-09-13_X2j2h7HlhcnGq0XN\tmp\live-3.0\round-probe
- In particular Program.cs, StaticAnalysis.cs, findings.md, dual-root-findings.md, handles.json, handles-final.json, rtti-measured.json, verified-final.json, dual-root-verified.json, after-progression.json, and object/title/UI JSON structure checks.

## Critical missing fact

The exact registry record type IDs at record +0x18 for **CTimerDialogWar3** and **CTimerWar3** are not present in the permitted source/findings/JSON.

Program.cs reads each registry record as 0x98 bytes, but its enumeration only emits serial (+0x24), state30 (+0x30), object (+0x90), RTTI, handle, and object backlink. Its Resolve function checks allocation marker, bounds, serial, object backlink and state30, but does not inspect or output record +0x18. Neither raw registry record byte arrays nor registry-record dumps are preserved in these JSON files. handles.json and handles-final.json dump the dialog OBJECT at 1606392041368, not its registry RECORD at 1610488838816. object-1610047173368.json dumps the timer OBJECT, not its registry RECORD at 1610488838472.

RTTI type names, vtable RVAs, and type-descriptor RVAs are not registry type IDs. Warcraft300HandleValidator.UnitTypeId (0x2B61676C) is explicitly a CUnit type ID and must not be reused. Its +0x83 state-bit check is likewise not independently evidenced for these timer classes by this probe.

The requested positive and negative synthetic implementation tests are also blocked: inventing fixture type IDs or omitting this required check would manufacture validation rather than test the requested ABI. No placeholder reader that silently returns unknown was added.

## Validated source facts available for later implementation

Pinned executable version: 3.0.0.24268.
Pinned original PE SHA-256: BD2A0DC256289DE45287BB60F3725B88F1235216D5177984377FE1CA22840A12.
These were verified by the prior probe, not newly checked against any live process. A later API must require independently validated caller image/context evidence and reject mismatches.

| Field | Recorded layout / requirement |
| --- | --- |
| Registry pointer | module +0x2F807F0 |
| Primary table / count | registry +0x18 / +0x30 |
| Alternate table / count | registry +0x50 / +0x68 |
| Handle index | low uint32, high bit selects alternate table; mask 0x7FFFFFFF |
| Handle serial | upper uint32 |
| Registry entry | 16-byte stride; uint32 marker 0xFFFFFFFE; record pointer +8 |
| Record validation | serial +0x24; qword state +0x30 must be zero; object pointer +0x90 |
| Object backlink | full 64-bit handle at object +0x18 |
| Dialog agent | CTimerDialogWar3 vtable RVA 0x2730B08 |
| Dialog UI | agent +0x58; CTimerDialog vtable RVA 0x2768190 |
| Timer | agent +0x60 is independent handle; resolved CTimerWar3 vtable RVA 0x26E04E0; same object as UI +0x2D8 |
| Title frame | UI +0x298; CTextFrame vtable RVA 0x228F5D0; frame +0x40 must equal UI |
| Current UI context | UI +0x40 must equal BOTH qwords at module +0x2F5EF00 and +0x2F85360; CGameUI vtable RVA 0x275ED08 |
| UI name | UI +0x280 points directly to null-terminated UTF-8 TimerDialog, observed with 32-byte bound |
| Title-frame name | frame +0x280 points directly to null-terminated UTF-8 TimerDialogTitle, 32-byte bound |
| Title aliases | frame +0x4C8 and +0x4D0 must equal the same direct UTF-8 buffer address |
| Title text | strict UTF-8, null terminator required within 96-byte read; never old +0x350 |

The prior discovery caps each table count at 262144. This is a defensive scan ceiling, not a native capacity claim. A future reader must impose its own explicit total bytes/calls/time/cancellation bounds and fail closed when discovery cannot complete, rather than return a partial unique result. Before/after comparisons must include registry/table identity and counts, all accepted allocation/serial/type/backlink evidence, timer identity, parent chain, dual globals, title aliases, and title text. Matching reads are not an atomic snapshot and cannot exclude an intervening ABA change. A synchronous read delegate must itself be bounded; checking elapsed time cannot interrupt a blocked delegate.

## Round semantics and limitations

- The existing ParseTitle accepts only three exact current/boss-round prefixes, a trailing |r, and an invariant unsigned decimal integer in [1,65]. Reuse its strict semantics; no injected relaxed parser.
- The recorded pre-round title \|cffFF0000 1라운드 시작까지\|r parses to null. It must not report round 1.
- after-progression.json records a consistent double observation at 2026-09-13T05:59:28.7712272Z with title \|cffFF0000현재 라운드\|r : 7\|r. The 7 is an observed historical value, not a constant or a present-game claim.
- Positive output requires a unique consistent native-owned current-round title after complete bounded discovery. Multiple matching sources, changing title/context, recycled handles, alias mismatch, unreadable memory or exhausted budget mean unknown.
- Unknown/absence never means game end. No LocalIdentity, Alive, full Coach approval, or GameMap identity may be inferred from this title source.

## Smallest unblock and intended integration boundary

An authorized evidence producer must supply independently verified numeric registry type IDs at record +0x18 for both classes, tied to this exact version/hash and validated allocation/serial/object/vtable evidence. Supply any additional class-specific allocation/state checks that are intended to be required, including whether the unit validator's +0x83 check applies. Do not obtain them by guessing old-version IDs.

Once that evidence exists, the planned (not implemented) API is a pure Read(Func<ulong,int,byte[]> read, typed validated current-view context, CancellationToken token), with module base/version/hash carried by required context. It returns a typed nullable diagnostic round observation containing value, provenance, context and timestamp, never a gameplay-approval object. The parent owns integration and activation. Required tests then cover positive/current round, pre-round, alias mismatch, duplicate, title mutation, recycled handle, wrong context, unreadable memory, bounds/budget and cancellation. No tests were built or run in this task.

## Authorized live follow-up: stopped on unreadable page

At 2026-09-14T06:05:38.4659745Z, an authorized minimal PowerShell Add-Type helper attempted discovery against PID 50744 after checking start UTC 2026-09-14T05:21:07.0906646Z. The exact allowed cached executable copy passed SHA-256 verification and offline RTTI checks for all five pinned classes. Version was caller-verified, not read from the installed executable. No installed executable was read through filesystem access.

The helper requested only process read/query rights (0x410), queried readability with VirtualQueryEx before every ReadProcessMemory request, and enforced a 2 MiB total requested-byte ceiling and 20-second elapsed guard. Discovery was based on the pinned dialog vtable, without assuming registry type IDs. It was designed to require complete discovery with a unique validated dialog, independently resolved timer, all serial/allocation/backlink/current-UI/alias relationships, and two equal full scans with four ownership observations.

Actual result: **blocked: NOT_ALREADY_READABLE**. A discovery address did not satisfy committed/already-readable/non-guard requirements. No ReadProcessMemory call was issued for that rejected span. Requested-byte accounting includes that attempted span: **420,412 bytes**, **1,261 read requests**, **37 ms** elapsed including preparation inside the helper. These are requested counts, not a claim that every span was read successfully. The helper stopped, closed its handle and did not retry, change protections or bypass the check. Complete candidate uniqueness and the two required type IDs were not established; no positive round observation was exported.

Private helper/evidence files:
- session tmp/round-live-pin/RoundLivePin.cs
- session tmp/round-live-pin/Run.ps1
- session tmp/round-live-pin/result.json

The result JSON contains no raw memory dump or pointers. No game input/functions, writes, injection, debug attachment, protection changes, network, MSBuild/dotnet build or activation occurred. Add-Type compiled only the authorized minimal diagnostic helper. Production reader and tests remain blocked; no type IDs were invented.
