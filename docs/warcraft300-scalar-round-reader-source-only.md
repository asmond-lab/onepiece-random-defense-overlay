# Warcraft 3.0 bounded scalar-round diagnostic producer (source only)

## Scope and status

Added only Warcraft300ScalarRoundReader.cs, its new xUnit test file, and this document.
No existing reader, model, profile, UI, project, data, tool, or diagnostics host file is edited.
No activation, game/process access, build, test execution, or live validation was performed.
This is NOT production approval, current-map identity proof, own-life evidence, or gameplay integration.
The default layout gate is false. No caller or discovery path is installed.
Existing Data/map-growth-globals-2320.json already declares pb and Eb as type 4; no new Data file is necessary.

## Exact inspected pins and source meaning

- Executable version: 3.0.0.24268.
- Executable SHA-256, as pinned by Warcraft300Diagnostic:
  BD2A0DC256289DE45287BB60F3725B88F1235216D5177984377FE1CA22840A12.
- Inspected JASS: docs/analysis-2320/modern-reader/members-ko-2.320/war3map.j.
- SHA-256 of its current bytes:
  6FDFC64BF8AD9463F5B5C8A351FFA7E1875D6CF9B51210129539375FA77A6C7C.
- Integer declarations: pb=0 at line 94, Eb=0 at line 99.
- Only set writes: line 79466 pb=Eb, then line 79467 Eb=Eb+1.
- yFT, lines 79461-79474, returns when Wb>0 and writes only when wb==ub.
- B8 is assigned yFT at 81380; SUy adds B8 at 56303 and registers the .5-second recurring timer at 56304.
- Timer titles consume pb at 40199, 55345, and 55350.
- 65 is the requested maximum authored diagnostic round, not an arithmetic clamp or a phase/end-state test. The generic writer itself does not clamp at 65.

The writes are sequential, not atomic. After a completed writer operation the relation is Eb=pb+1. Initial declarations are 0/0 and are rejected because they do not satisfy the completed-writer relation. The first completed operation is 0/1, which returns PreRoundUnknown, never positive-round evidence. Any other zero pair is rejected. Positive pb must be 1..65, with Eb=pb+1; therefore 65/66 is valid. Negative values, pb>65, Eb>66, or relation mismatch are rejected without clamping or retained payload. pb is not phase. No value here means victory, defeat, round end, active combat, or player life.

## Required parent adapter API

All added types are nested under Warcraft300ScalarRoundReader to avoid modifying shared Models.

    Observation Read(
        Func<ulong, int, byte[]> read,
        ResolvedScalarNodes nodes,
        string worldContextToken,
        Func<string> readWorldContextToken,
        Func<DateTimeOffset> utcNow,
        CancellationToken cancellationToken = default)

ResolvedScalarNodes supplies ModuleBase, Instance, OwnerAggregate, DataTable, exact owner header (24 bytes), exact table header (72 bytes), exactly two ScalarNode entries (pb and Eb), the context token, and MetadataObservedAt. Each ScalarNode includes address and a copied 32-byte structural snapshot from node+24 through node+55 (previous/next links, name pointer, runtime/declaration tags). There is no scan or cache in this function.

The parent must explicitly set CallerValidated300MetadataAndContext only after its complete validated current-instance traversal has established unique owner/table, well-formed complete list and tail, unique identifiers, complete expected declarations, integer type 4 for both tags, and unambiguous pb/Eb addresses. A list containing two names is not complete-discovery proof. Existing GrowthReader currently exposes only its QR result, so a future parent-owned adapter must retain/export these validated snapshots; do not introduce a second standalone scanner.

The parent supplies the exact three pins explicitly; defaults are empty. Source hash is a local-source contract, not proof that a live session loaded this map.

worldContextToken must be an opaque generation identity tied to the full validated session/module/current-view/world/UI/VM instance/script/manager/registry and owner/table context, not a reused world pointer or static session label. readWorldContextToken must freshly validate that context and return a changed token or throw on any mismatch. A constant-return callback, as used by synthetic tests, is NOT an acceptable live adapter. The caller must invalidate inputs on any generation change. The producer compares the supplied/resolved/proof tokens and samples the callback before and after its reads.

utcNow must be a trusted UTC clock. Metadata can be at most one second old, never future-dated. Completion must be within 100 ms of start and still within the metadata age limit. Stopwatch additionally measures elapsed real duration independently of the supplied clock. Rejected/cancelled/failure timestamps are last successfully sampled timestamps, not success/freshness evidence.

## Minimal native validation still required before gate can be set

The old RouteQuestMemory.Scalar implementation reads int32 at node+56. Its legacy decoder alone is NOT 3.0 layout proof. GrowthReader verifies 3.0 owner/table, node metadata offsets, and QR array payload, not the scalar integer representation.

Leave ExperimentalLayoutVerified=false until parent-owned, explicit read-only native validation under the exact executable and source pins:

1. Brackets a complete validated current 3.0 metadata context and reads the actual pinned pb/Eb nodes, names, both type-4 tags, and structural identities.
2. Independently establishes signed little-endian native integer payload width 4 at node+56, not a pointer, header field, array slot, or incidental stale value. Use multiple known immutable integer scalar sentinels with distinct nonzero source values, independently checked against declarations and all write sites in this pinned JASS; zero alone is insufficient. Do not call pb/Eb immutable sentinels, since they are mutable.
3. Records the sentinel names, source values/write audit, resolved typed nodes, native bytes, binary/source pins, full context generation, and bracketing checks in an external proof reference. This task does not fabricate sentinel identities or claim any such reads occurred.
4. Sets LayoutProof.ExperimentalLayoutVerified=true explicitly, EvidenceId to that reviewable proof reference, WorldContextToken to the validated generation, NativePayloadOffset=56, and IntegerWidth=4. Any absent/mismatched proof fails before the read delegate is called. The boolean is an assertion seam, not self-authenticating evidence or permission to activate anything.

The gate must never default true or be set solely because the pair happens to satisfy arithmetic. Revalidation/invalidation policy remains parent-owned. No proof infrastructure, data artifact, or live reader is created by this change.

## Read bracket and limits

Successful path, exactly 16 reads / 348 requested bytes:

1. Owner header (24), table header (72), pb metadata (32), pb name (3), Eb metadata (32), Eb name (3).
2. pb native int32, Eb native int32, Eb native int32 again, pb native int32 again (16 bytes total).
3. Repeat all six metadata/name/header checks (166 bytes per pass).
4. Recheck context, timestamps, cancellation, then pair equality and semantics.

Every address span is range-checked, every read must have exact length, and delegate buffers/snapshots are cloned. No payload is requested until explicit layout proof and typed structural checks pass. No retries, fallbacks, allocation-handle traversal, broad scans, unrelated globals reads, or persistent last-good pair. Names are exactly case-sensitive ASCII pb\0 and Eb\0. Both runtime and declaration tags must be 4.

Equality brackets detect observed tearing but are not atomicity or ABA proof. A valid pair is diagnostic source evidence, not guaranteed gameplay freshness. Read/context/clock delegates must themselves be bounded and honor the caller's cancellation arrangement: a synchronous delegate cannot be forcibly interrupted by this helper. Their external I/O is not included in the helper's 348-byte budget. The time cap rejects slow returns rather than promising preemptive timeouts.

Outcomes: SourceRoundObserved, PreRoundUnknown, Rejected, Cancelled, ReadFailure. Rejected/cancelled/failed observations publish neither pb nor Eb. Exceptions from reads/context/clock are caught; no error is thrown into gameplay by this producer. No gameplay consumer exists in this patch.

## Synthetic coverage and verification status

New tests cover positive bounds including 65/66; 0/0 rejected and 0/1 unknown; negative/overflow/out-of-range/mismatched pairs; missing layout proof; wrong pins/caller proof/offset/width; stale/future timestamps; cleared headers, changed links/names/types; wrong expected type; duplicate/missing names and duplicate addresses; changed pb or Eb; second-pass metadata changes; before/after context changes; cancellation before/during reads; short reads; delegate exceptions; timestamp regressions/duration overrun; and exact read budget.

Tests are SOURCE ONLY and were not built or run, per instruction. Native validation remains blocked on the parent collecting the external evidence above. Parent integration, project inclusion decisions, runtime wiring, and any later build are intentionally separate.
