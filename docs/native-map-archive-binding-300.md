# Native active-map path/archive binding (source-only, review pending)

## Scope and public meaning
New isolated capability only. No caller, UI, gate, profile, scoring, LiveRecognition, existing production file, or Data file is modified. No build/test execution or additional game access was performed for this change.

The real probe has UNKNOWN active-world-source semantics. A typed current-root path could still be retained lobby/previous-selection state. Static field-use evidence or a discriminating lifecycle experiment is required before asserting active-world ownership. Complete loaded-memory bytes are not required for this path capability.

NativeMapContext.ExperimentalActiveMapPathSemanticsVerified is optional and DEFAULT FALSE. Observe remains available for structural diagnostics. After a successful hash with the flag false, Bind returns StructuralPathArchiveMatch, with wording:

> structural native map-setup path resolves to pinned archive bytes; active-world source semantics unverified

Only an explicit independently verified semantics attestation allows NativeArchiveBindingState.NativeArchiveBound, with exact wording:

> active native map-setup path resolves to pinned archive bytes

This is NOT a loaded JASS buffer, loaded-memory byte, immutable local-player, unit life, gameplay, or general version-support claim. It does not reuse the legacy RuntimeMapIdentityState.Proven enum and is not wired into that provider.

## API
- Warcraft300MapPathReader.Observe(read, expectedContext, captureFreshValidatedContext, cancellationToken) returns NativeMapPathResult.
- NativeMapContext supplies independently validated process/start/version/EXE hash, module/root/world/UI/VM instance/script, nonempty epoch token, and explicit caller attestation.
- The capture closure MUST freshly validate current world/VM/process identity each invocation. It is a trusted caller boundary, not a mechanism for validating an arbitrary caller's assertion. A cached closure is unsupported. The reader additionally checks the exact native pointers/types and UI aliases itself.
- NativeMapPathObservation is immutable with no public constructor or public path setter. It carries its originating native refresh closure, timestamp, context and complete known-field stamp. Public archive binding has no UI-path/string or arbitrary source-pin overload.
- NativeMapArchiveBinding.Bind(observation, cancellationToken) uses Map2320SourceMetadata.LoadBundled().Archive as the sole source of expected length/hash. Internal clock and I/O/pin seams are for dedicated tests, not production input.

## Evidence and exact fields
Evidence: runtime-map-v2 probe of PID 50744 / start UTC 2026-09-14T05:21:07.0906646Z. Allowed cached EXE passed the exact Warcraft300Diagnostic version/hash pins. Those existing constants and DecodeRoot are reused, not duplicated.

Known path chain:
- decoded module RVA 0x2E9AD00 -> CGameWar3, VT RVA 0x26C8C70
- root+0x2638 -> CMapSetupWar3, VT RVA 0x26C8AE8
- setup+0x50 -> CStringRep, VT RVA 0x2282340
- rep+0x38 -> NUL-terminated UTF-8 text

Sampled value was C:/Users/123/Documents/Warcraft III/Maps/Download/ORDR_S2_2.320[R].w3x. This is evidence, not a hardcoded path or approval shortcut.

Context checks use CWorldFrameWar3 VT 0x2764A20 and world+0x40, CGameUI VT 0x275ED08 and matching globals 0x2F5EF00/0x2F85360; root instance+0x25D0 / script+0x25E0 / game state+0x2620 with VTs 0x27ECBC0 / 0x27ECC40 / 0x26CCE00. Instance's known first 48 bytes and script's first 16 bytes are bracketed field-wise; mutable refcount/header changes deliberately fail closed. There are no guessed full-object lengths, adjacent allocator-field reads or heap scans.

Three complete headers bracket two byte-identical path reads. Headers, root/VM/context epoch, path pointers, text and executable pins must agree. Maximum path is 2048 bytes including terminator; strict UTF-8; every delegate call stays within one 4096-byte page. Maximum reader requested bytes 16384, elapsed strictly less than 1000 ms, cancellation checks surround reads and context captures. The delegate itself must enforce readability/non-GUARD rules and have bounded execution; this pure reader does not attach to a process.

## Filesystem policy
Only local drive-absolute .w3x paths survive syntax validation. UNC, device, relative, ADS, invalid/reserved components, traversal, non-w3x and control characters fail. Actual I/O additionally requires a fixed Windows drive backed by a direct HarddiskVolume, rejecting network/remapped drives. Each ancestor is opened as an un-followed reparse point with read-attributes access and FileShare.Read only, rejected if reparse/non-directory, and held through the operation. The file is opened read-only, FileShare.Read only, OPEN_REPARSE_POINT; reparse/non-disk files are rejected before reading. Resolved handle path must match the native local path.

The same file handle supplies volume + 128-bit FileIdInfo, length, creation and last-write times and attributes before/after hashing, and after native revalidation. Exact expected length, bounded streaming SHA256, EOF and stable metadata/identity are required. File and ancestor locks remain held through the final native check. No duplicated game file handles, game file-pointer access, or process APIs exist in the binder.

LastWriteUtc must be <= process start. Otherwise result is explicitly UnsupportedArchiveModifiedAfterProcessStart, including maps downloaded/updated later. This deliberately sacrifices availability; timestamps are not a claim about loaded-memory content. Source metadata constants alone, matching filename, user choice, log selection or existence of one expected map cannot produce success.

The whole observation-to-final-success interval must be strictly <3 seconds, measured with the originating monotonic clock. Expired/backward clocks, cancellation, I/O/access errors, changes, unsupported filesystems and missing evidence return Unknown. Synchronous OS file calls and supplied delegates cannot be preempted by this API: time/cancellation are checked around every call, and late completion cannot return success. A hard wall-clock abort for a blocked kernel call requires a separately reviewed execution host; none is added here.

## Dedicated source tests (NOT RUN)
Warcraft300MapPathReaderTests covers exact chain, all eight type checks, EXE pins, trust/epoch, root/setup/rep/text/VM/context changes, strict UTF-8, page boundaries, truncation, terminator bounds, unsafe paths, local Documents/Unicode paths, cancellation and read timeout.
NativeMapArchiveBindingTests uses internal fake file I/O and tiny synthetic pins to cover success wording, length/hash/mtime/reparse, file identity and metadata tamper, native epoch/path changes after hashing, short files, expired observations, I/O/access failures, cancellation, delayed I/O rejection, and absence of public observation constructors. Production pins remain derived solely from verified Map2320SourceMetadata.

## Review gaps / no activation
- Build and tests intentionally not run. Source/API/interop compilation and offline tests require parent-approved follow-up.
- New Windows real temporary-file integration test SOURCE uses the exact OpenLocalArchive factory used by public Bind, tiny synthetic .w3x bytes and synthetic native/context pins with an actual monotonic clock. It covers both native slash forms, real 128-bit FileIdInfo, final-path checks, held ancestor rename rejection, file writer rejection, read-only access/flags/share constants and real-file hash mismatch. It has NOT RUN; kernel behavior remains unverified until the parent serial build/test. No game/custom map access is part of this test.
- No claim of atomicity against malicious kernel-level writers, filesystem metadata forgery or ABA changes between samples.
- No independent native loader/accessor disassembly or complete current-map JASS source proof was added. The bounded empirical map-setup path is the capability's provenance, not loaded-memory evidence.
- Production fresh-context integration and contract acceptance remain for independent review. Existing gates/profiles/Data remain unchanged.

## Review-condition regression tests
New equality tests reject exactly 3000 ms observation age and exactly 1000 ms reader elapsed. Default-false semantics tests retain structural observation and post-hash structural matching but prohibit active wording/state. Fixture contexts only set the flag true in clearly marked SYNTHETIC proof cases. No real-game semantics approval is supplied or inferred from the parent archive hash/mtime check.
