# Standalone scalar evidence adapter: bounded tool only

## Checkpoint

R6 SOURCE-SCOPE AUDIT SOURCE COMPLETE, PENDING PARENT SERIALIZED GATES THEN ONE METADATA-AUDIT CAPTURE. No build, test, live probe, target execution, or publish was performed for this patch. The earlier 117/3098 results DO NOT cover this R2 cleanup. This checkpoint is not a current test-gate claim.

## Entry and scope

Use --probe-scalars-only with --authorized-exe-copy and the existing explicit --pid, --started-at, --samples (1..3), --output arguments. Standalone mode rejects --capture-ui, --probe-scalars, missing copy paths, and a copy flag in legacy mode. Legacy modes and their Ready fence remain separate. No recognition service, profile, environment flag, UI host, fake RecognitionResult, coaching, or normal Round field is created by standalone mode.

The copy must be a caller-supplied local regular file, exactly 52,885,712 bytes, SHA-256 BD2A0DC256289DE45287BB60F3725B88F1235216D5177984377FE1CA22840A12, pinned version 3.0.0.24268. Installed image path is obtained only as OS module metadata. The adapter never reads installed-file bytes, attributes, or FileVersionInfo. UNC/device/ADS/short-name paths, reparse components, hard links, and the installed path are rejected; the opened copy's final path is checked before reading it. No loader copy is required.

## Binding and lifetime

BoundReadSession.Acquire orders: exact unique PID/start/module metadata guard; copy validation/read; repeated guard immediately before OpenProcess(0x410); bind. Binding or cancellation failure disposes the lease; Program owns the successful lease in using. The entire standalone invocation has a 45-second cancellation budget. Every requested native span is completely covered by existing strict readable-region queries before RPM, and false/partial RPM fails closed. No protection changes or writes. PBI PID/native-x64 and PEB +0x10 main-image fields are bracketed. No process parameters, command line, environment, or user saved-record payload is read.

Binding compares ALL SizeOfHeaders bytes after the previously observed PE32+ ImageBase normalization, plus the entire raw .rsrc section against the authorized copy. No arbitrary header-byte exclusions. This is copy-hash plus bounded runtime header/resource equivalence, NOT full live-image/code equivalence. Unexamined runtime code changes remain unknown; no executable-memory bypass is attempted. The authorized helper sources were inspected but never run or shell-invoked; only main-image/read-query/PEB methods were adapted.

## Discovery and shared probe

StandaloneScalarRunner creates a fresh Warcraft300GrowthReader per sample and calls its unchanged delegate-based Read with Map2320GrowthSource.LoadBundled().Globals. This preserves the actual normalized manifest hash, all 3936 source declarations, complete strict region enumeration, unique owner aggregate, 8-GiB/32-second full discovery bounds, metadata/QR validation and rechecks. No first-match or cache bypass. Growth's full-list safety ceiling remains 20,000; the existing scalar probe additionally requires exactly the 3936 pinned globals and rejects unknown identifiers.

NativeScalarProbe retains its old Ready-fenced overload. A second overload requires a completed standalone Growth observation, its matching private cache/context, and the bound lease. Both reuse one probe body. Reflection reads the standalone reader's completed cache only, never sets it. Cache schema changes block. The qg DWORD hypothesis follows the validated VM manager table entry to pinned CTimerDialogWar3, native allocation marker/serial/backreference, owned UI/title/timer and title aliases. Registry record +0x18 IDs are discovered and exported, not guessed or borrowed from CUnit.

Before/after world/root/VM, complete globals, scalar headers/payloads, native allocation/UI/title and main-image binding comparisons remain mandatory. The extra phase has 2-MiB/5-second read limits including its bound-lease identity reads, in addition to the core's local read accounting. No title or scalar success can enable ExperimentalLayoutVerified.

## Freshness and outputs

ScanStartedAt and the monotonic timer start BEFORE full world/global discovery and never reset after completion. Age >=3 seconds is expired, including equality. The extra five-second cap never extends this freshness budget. Expired completed evidence is retained only under HistoricalOnly. Fresh matched hypotheses still report UnknownLayout with DiagnosticCurrentValue=null. All capability/proof flags remain false. Evidence is redacted: diagnostic raw scalar values, statuses, discovered type IDs and RVAs only, not native addresses or arbitrary title strings.

Outputs use the existing new private session-tmp directory rules and CreateNew files, only after target binding. Separate sample-N-standalone-scalars.json files do not modify or retimestamp any main result. Standalone complete.json uses ReportsWritten and BlockedReports. Exit 0 means the requested diagnostic reports were written, NOT successful validation or usable current gameplay data. Legacy-mode counters remain unchanged.

## Pending test command (parent executes, not executed here)

    dotnet test .\OrandOverlay.Tests\OrandOverlay.Tests.csproj --filter FullyQualifiedName~StandaloneScalar --no-restore

The test project adds a single reference to the existing tool project; a tool-local InternalsVisibleTo exposes test seams. No core assembly change. Tests exercise identity-before-I/O, repeat guards, failed binding/cancellation closure, copy path rejection, compared-byte mismatch, partial/unreadable reads, cancelled reads, propagation of incomplete/ambiguous discovery failures, exact freshness boundary, historical-only evidence and always-false approvals. Full native integration, PE-layout agreement against this live process and prior source-assertion regressions still need parent review/build/testing. No new live command is executed by this patch.


## R2 cleanup: single closing bracket and truthful failure propagation

The former third full-resource pass inherently exceeded the unchanged 2-MiB cap: 3 * 857,600 = 2,572,800 bytes before any other reads. The parent-reviewed resource size is 857,600 bytes. R2 performs exactly TWO full bindings inside the original shared probe budget: core preamble, then one closing callback after all journal and cache rechecks. The callback checks the original world locator, performs full binding.Revalidate, checks cancellation, and checks the ORIGINAL lease deadline. The runner has no duplicate native-read tail. Discovery-side binding checks before BeginProbeBudget remain discovery checks; none of the extra-phase closing checks are moved outside, exempted, or reset.

Two resource passes consume 1,715,200 bytes, leaving 381,952 bytes for headers, PEB fields, payload/replay and world reads. If the actual total exceeds 2,097,152 bytes it still blocks. Deadline equality at 5 seconds also blocks. BoundReadSession checks after each returned read, after final binding metadata, and the core checks the original lease clock again immediately before returning its completed result. Synchronous native calls cannot be forcibly interrupted, but a late return cannot become a successful outcome.

SharedBudget.RequestedReadBytes/ReadCalls includes binding and world RPM requests, not just core payload/replay reads. The separate PayloadRequestedReadBytes/PayloadReadCalls counters describe the core-local R path. These are requested-memory-read accounting, not VQ structure/PBI syscall buffer accounting. Failure snapshots remain readable after expiry and retain partial-read request charges.

NativeScalarProbe.Result has an explicit Outcome: IndependentAgreement, LimitedPreRound, BlockedComparison, or FailedRead. FailedRead always becomes outer Status=Blocked with FailureStage/Failure, even when also expired; HistoricalOnly remains true for expired evidence. Completed BlockedComparison is distinct and reports ProbeReadCompleted=true without any approval. Missing outcomes also block. Inner timestamps are ProbeStartedAt, ProbeCompletedAt and ProbeElapsedMilliseconds only. Original ScanStartedAt/monotonic ScanAgeMilliseconds alone govern the strict three-second expiry; a newer probe clock cannot renew it.

Functional native-free R2 tests exercise actual shared closing/budget helpers, journal/cache ordering, two-resource-plus-known-payload success, rejection of a third resource or a genuinely excessive two-pass total, world/binding/final-deadline order, exact five-second rejection after the last metadata callback, cancellation, partial-read propagation into outer Blocked, comparison-vs-read-failure distinction, shared-vs-payload accounting, and source expiry at exactly three seconds despite a fresh probe timestamp. Existing adapter tests were migrated to typed outcomes without weakening their assertions. No test project reference changes were needed for R2.

R2 cleanup remains limited to the OLD RandyPickLiveValidation tool and its tests/README. No new WarcraftProbe files, core/App/Data/proof flags, filesystem access policy, native targets, build, test, or publish were touched/executed.


## R3: aggregate-only discovery diagnosis

Parent-reported baseline: R2 compiled and 47 focused tests passed. The first authorized-copy-bound standalone run stopped after 32.0597 seconds at complete-growth-discovery with DiscoveryComplete=false and InvalidDataException, expired source timing, and no scalar evidence. The standalone read wrapper performs per-read VQ coverage checks, unlike the production direct ReadAvailable RPM path; the shared Growth reader still uses its existing SIMD IndexOf and 64-KiB/seven-byte overlap. These observations do NOT establish that VQ overhead caused the time-cap failure. R3 adds measurement only, not optimization, extra retries, limit extensions or profiling.

The instrumentation collection is created/reset exactly once immediately before the call to GrowthReader.Read, and frozen in a finally when that call completes or throws. It covers that call's context/metadata work, complete discovery and rechecks. It excludes earlier copy/root preparation and all later scalar-probe work. DiscoveryElapsedMilliseconds is this narrower interval; original ScanStartedAt/ScanAgeMilliseconds remain the independent authoritative three-second freshness clock. Snapshot/failure formatting performs no target I/O and never resets a read/probe budget. Snapshots remain available after timeout, partial reads, cancellation or expiry.

DiscoveryMetrics fields are fixed-size, bounded numeric aggregates only:

- ReadCalls/RequestedBytes: positive wrapper requests after existing budget checks; a later rejected address or unreadable span may never reach RPM. ReturnedBytes records obtained RPM bytes even when the read ultimately fails, clamped to the requested count.
- LargeSizeClassAtLeast4096 and SmallSizeClassBelow4096: request count and requested/returned bytes. These are SIZE CLASSES ONLY, not scan-vs-metadata classifications.
- AddressWalkVq and ReadValidationVq: separate call count, total/max milliseconds, failure count, and last numeric OS error. QueryWalk is used only by strict streaming region enumeration; QueryRead is used only by exact-read coverage validation. No region arrays are retained.
- RpmCalls/RpmRequestedBytes/RpmReturnedBytes, total/max RPM milliseconds, failed and partial call counts, last numeric OS error. A false RPM can return partial bytes: both failure and partial counts may increase. A successful but short read is still partial and rejected. Stale OS error values are not attributed to successful calls.
- YieldedRegions/YieldedRegionBytes: regions actually yielded by the strict enumerator, not bytes necessarily scanned before a later failure.
- DiscoveryElapsedMilliseconds, NativeCallMilliseconds (VQ + RPM), and nonnegative ElapsedMinusNativeCallsMilliseconds. Timings use Stopwatch.GetTimestamp and floating-point Stopwatch.Frequency conversion. The difference includes managed matching/copy/allocation, checks, instrumentation, scheduling and other unaccounted work; it is NOT measured CPU time or instruction profiling. Call timers measure the P/Invoke boundary including minimal marshaling/error-capture overhead, not kernel-only execution.

Counters saturate rather than overflow; timing fields are finite, nonnegative and bounded. No address, raw buffer, target string/name, per-call record, exception message or other opaque value is stored in the metrics. Existing read/query rights, range/protection checks, streaming discovery, raw RPM false/partial rejection, post-read deadline checks, 8-GiB/32-second Growth limits, 2-MiB/5-second extra-probe limits and three-second scan-start freshness are unchanged. No reset or exemption was added to a budget.

FailureCode is a serialized allowlisted enum alongside the unchanged original Failure exception-type field and false proof flags. Exact InvalidDataException messages map only as follows:

- Incomplete discovery: time cap. -> DiscoveryTimeCap
- Incomplete discovery: byte cap. -> DiscoveryByteCap
- Incomplete discovery: region exceeds remaining byte cap. -> DiscoveryByteCap
- Diagnostic metadata budget exceeded. -> MetadataByteCap

Cancellation maps by exception type to Cancelled; other failures map to ProtocolRejected. No substring matching and no raw exception-message export occur, including address-bearing short-read messages.

StandaloneScalarInstrumentationTests functionally cover partial request accounting, the 4096 size boundary, distinct VQ categories and duration/max/error aggregates, fractional tick conversion, frozen snapshots without target/clock access, rejected addresses without address retention, once-only finally capture on failure without budget reset, exact failure-code allowlisting, numeric-only export, and counter saturation. Existing 47 tests were not edited. Run the existing README filter FullyQualifiedName~StandaloneScalar to include R3 with the prior suites. None of these new tests, any build, or another live attempt was executed by this patch. Parent owns serialized validation and any separately authorized ONE bounded live attempt.


## R4: bounded discovery block sizing, not caching

Parent-reported R3 gates: 63 focused tests passed. The single R3 live attempt reached DiscoveryTimeCap at 32.016 seconds. Reported aggregates: 34,374 requests / 2,096,618,326 requested bytes; read-validation VQ 30,598.9556 ms, address-walk VQ about 42.55 ms, RPM about 1,170.7383 ms, unaccounted about 204 ms. A separate worker reported a one-name 3.472-GB full scan in 1.477 seconds. These measurements support read-validation query overhead as the dominant measured cost of this failed attempt. R4 changes only bulk request granularity; an actual improvement and sub-three-second freshness remain unverified until parent gates and ONE bounded authorized run.

Warcraft300GrowthReader.Read now has the trailing optional parameter:

    CancellationToken token = default,
    int discoveryBlockBytes = DefaultDiscoveryBlockBytes

Allowed sizes are EXACTLY 65,536 bytes (the unchanged default) or 4,194,304 bytes. Other values fail before any target read or region enumeration. The setting is used only by the discovery bulk loop's Math.Min(blockBytes, regionEnd - address). Existing metadata reads, names, structural/type validations, allocation checks, expected source declarations, and before/after stamps are unchanged. The standalone runner explicitly passes its DiscoveryBlockBytes constant, equal to the permitted 4-MiB option. Existing callers omitting the new argument retain their old 64-KiB behavior.

No caching, first-match return, working-set query, region-array materialization, protection/readability bypass, or extra retry was introduced. Every read still uses the same guarded delegate. Each bulk request stays inside its enumerated region and is at most the selected size; the seven-byte overlap remains and its repeated bytes still count toward the unchanged 8-GiB discovery ceiling. Complete region enumeration and owner uniqueness are required every read, including cached successful identity and zero-QR cases. The 32-second discovery cap, 8-MiB metadata cap, strict partial-read/cancellation failure, full pinned globals validation, and 2-MiB/5-second later scalar-probe budget remain unchanged.

Original ScanStartedAt still precedes full discovery. An outcome at or after three seconds is historical only even if the new block size completes discovery or a later one-millisecond scalar probe agrees. No proof/capability flag is enabled and no current-value/normal-round claim is introduced.

Focused functional tests reuse the existing Growth Memory fixture, with only a bounded 4-MiB test block rather than multi-GB fixtures. They cover an aligned owner QWORD split across the block boundary of an unaligned region (seven-byte overlap required), unchanged omitted-size default read pattern, per-region/max-request bounds, identical metadata request sequence, type mismatch rejection, a second owner in the final region after cached success (also zero QR), overlap-charged total-budget rejection before a huge next region is read, partial bulk reads, cancellation before/after reads, and invalid sizes rejected before all I/O. Standalone tests retain strict original-clock three-second expiry and false proof flags with the explicit four-MiB setting. Existing tests were retained.

Pending parent command, not executed by this source patch:

    dotnet test .\OrandOverlay.Tests\OrandOverlay.Tests.csproj --filter "FullyQualifiedName~Warcraft300GrowthReaderTests|FullyQualifiedName~StandaloneScalar" --no-restore

No game access, build, tests, publish, new WarcraftProbe edits, App/UI/flag changes, or optimization beyond the validated bulk-size option was performed here. R3 results are historical and do not count as R4 gates.


## Parent validation, R4 (2026-09-15 KST)

- R2 serialized Release compile and focused tests: 47 passed, zero failures/skips. R3: 63 passed. R4 Growth+Standalone suite: 150 passed, zero failures/skips. These are focused gates, not a fresh full-app release gate.
- Exact authorized-copy-bound R3 live observation confirmed DiscoveryTimeCap. Discovery took 32,016.3164 ms; read-validation VQ consumed 30,598.9556 ms, RPM 1,170.7383 ms. No failed/partial native reads.
- R4 full unique discovery completed in 2,513.8126 ms. Original-start total age at failure was 2,614.6423 ms, within the unchanged three-second budget. Full discovery requested/returned 3,473,572,610 bytes including metadata; no cached/first-match discovery, skipped read validation or raised total byte/time limits.
- The following scalar stage FAILED at complete-source-globals with Unknown/duplicate global. Source Growth validates all expected declarations but can contain additional typed identifiers; the scalar adapter intentionally still requires the stricter exact source scope. The observation does not yet establish which identifier or runtime-scope assumption caused this failure. Do NOT remove unknown/duplicate rejection or raise the source-count ceiling merely to make this sample pass. Need an independently grounded scope/layout correction and corresponding tests.
- No pb/Eb/qg scalar evidence, current round, local/alive proof, gameplay readiness or coaching approval was obtained. Status=Blocked and all proof flags remain false. Report completion is not validation success.
- Evidence: session tmp/probe-guided-integration-v1/scalar-gates/focused-r4.trx and scalar-live-r4/sample-1-standalone-scalars.json. WarcraftProbe 0.1.0 packages and public RandyPick 1.0.1-test.1 release were not changed or republished.


## First-mismatch diagnosis after R4: rejection policy unchanged

Parent-reported R4 gates: 150 passed. The one live attempt completed Growth discovery in 2513.8126 ms and the original outer scan in 2614.6423 ms, within the existing freshness window. Scalar then failed at complete-source-globals after 34 core read calls / 362 requested bytes with the combined Unknown/duplicate global reason. That reason alone does not establish which branch fired or identify the name. This patch diagnoses the FIRST failure only; it does not skip extras, expand the expected set, raise the count, retry discovery, or read any scalar payload after a mismatch.

NativeScalarProbe.Result gains an immutable optional Mismatch member, default null, preserving previous constructor call sites. UnknownSourceIdentifier and DuplicateSourceIdentifier are distinct controlled failure reasons. The descriptor contains only kind, NormalizedNameSha256, IdentifierLength, RuntimeTypeTag, DeclaredTypeTag, EarlierExpectedMatches, and BracketVerified plus fixed verification-scope text. IdentifierLength is decoded UTF-16 length (equal to byte/character length for valid ASCII JASS identifiers). The hash is SHA-256 over NFC-normalized UTF-8, emitted as uppercase hexadecimal. There is NO trimming/case-folding, and normalization does not affect the ordinal source lookup. The pure IdentifierHashMatches helper compares a caller-supplied reference name with a captured hash; it does not discover names, access the target, or print the reference. No optional name classification/whitelist was added.

A descriptor starts with BracketVerified=false. Before marking a COPY verified, the existing journal replays only the already-read prefix, including the suspect node's 32-byte metadata, name bytes and terminator; the cache/context checks and existing closing world/full-binding callback then run under the original 2-MiB/5-second budgets and cancellation/deadline checks. This is the existing second binding pass, not a new third pass. The final return also rechecks the original deadline. If any step fails, the initial descriptor remains unverified in the failed Result. A completed bracket still returns FailedRead / outer Blocked with no scalar Evidence and all proof/capability flags false. BracketVerified only describes that bounded observation, not source acceptance, full-list completion, scalar layout proof, or current freshness. The outer original three-second clock remains authoritative, including historical-only expiry.

The mismatch branch returns immediately after that bounded prefix replay/closing attempt. It does not walk the remaining list, restart the full globals pipeline, reach pb/Eb/qg node payload reads, or perform a second mismatch-capture attempt. No raw runtime identifier, raw node value/payload, native address or arbitrary suspect string is exported. Replay failures retain the existing controlled/redacted failure reason and closing stage; the descriptor Kind still identifies the original mismatch category.

### Inspected source comparison: structural offsets agree, validation policies differ

| Field | GrowthReader | ScalarProbe | Agreement |
| --- | --- | --- | --- |
| Structural buffer | full node read of 64 bytes, recheck slice [24,56) | read 32 bytes at node+24 | Same structural span |
| Previous link | full-node offset 24 | metadata offset 0 | node+24 |
| Next link | full-node offset 32 | metadata offset 8 | node+32 |
| Name pointer | full-node offset 40 | metadata offset 16 | node+40 |
| Runtime type | full-node offset 48 | metadata offset 24 | node+48 |
| Declared type | full-node offset 52 | metadata offset 28 | node+52 |

Growth ReadName reads page-bounded chunks up to 32 bytes, requires a terminator within 256 bytes, rejects non-ASCII bytes and validates identifier grammar. Its globals walk uses ordinal uniqueness, checks runtime/declared tags are <=13, checks source types when a name belongs to the expected set, and finally requires every expected name. Its list safety ceiling is 20,000. Scalar Text reads bytes individually, requires a terminator within the same 256-byte bound, and uses strict UTF-8 decoding; it does not itself apply Growth's ASCII/identifier grammar check. On stable valid ASCII identifiers these decoders produce the same text. Scalar's walk still demands exactly the 3936 expected names and rejects any extra/duplicate. These are evidenced policy/grammar differences, not an observed offset error. No runtime culprit is inferred before the hashed mismatch capture.

New StandaloneScalarMismatchTests cover known SHA-256 output, NFC normalization, case/whitespace sensitivity, pure hash matching, separate classification, exact metadata offsets excluding payload, privacy-safe descriptor fields, immutable unverified-to-verified copies, failure at every replay/context/closing/deadline stage, original-budget accounting, original three-second expiry, false proofs, and default-null Result constructor compatibility. No existing test was weakened. Parent command (not executed here):

    dotnet test .\OrandOverlay.Tests\OrandOverlay.Tests.csproj --filter "FullyQualifiedName~StandaloneScalar" --no-restore

This is source-only diagnosis in NativeScalarProbe.cs, its focused tests and this README. No game read, build, publish, deployment, new WarcraftProbe work, loader analysis, count relaxation or additional allowance was performed. Parent owns focused gates and ONE captured-hash attempt before deciding any follow-up.


## R6: complete source-scope metadata audit, no scalar acceptance change

Parent-reported R5 gates: 163 focused Growth/Standalone tests passed. The single live run completed in 2133.9448 ms and produced a bracket-verified unknown-identifier fingerprint (length 5, runtime/declaration tags 8/8, zero earlier expected-map matches). Parent matched that fingerprint only against approved executable static ASCII identifiers. This is metadata/source-scope evidence, not a scalar value, boolean-value interpretation, layout proof, or a trusted complete public prelude catalog. No new catalog is inferred or fetched here.

Growth.Read gains the trailing optional sourceScopeObserver callback, default null. It runs only after EVERY existing owner/list/name/header/type/context/QR/allocation recheck has passed and cancellation has been checked. If no observer is supplied, no audit is built and old calls retain their behavior. Audit generation uses only the existing local Node.Name and previously verified Node.Inputs fields, with no additional native/query/file I/O. Native reads end before audit construction. The callback must itself be metadata-only; the standalone callback only assigns a pending immutable snapshot. Exceptions retain the reader's existing catch/reset behavior.

Warcraft300SourceScopeAudit copies both entries and the collection into a read-only list, bounds the list to 20,000, verifies numeric metadata ranges and count consistency, and exports:

- NormalizedNameSha256: NFC UTF-8 SHA-256, uppercase hex, identical normalization to the earlier mismatch helper.
- IdentifierLength, RuntimeTypeTag, DeclaredTypeTag.
- IsExpectedMapDeclaration: ordinal membership in the already-validated source declaration dictionary.
- TotalCount, ExpectedSourceDeclarationCount, MatchedExpectedMapDeclarationCount, OtherIdentifierCount.

ALL names, including expected-map names, remain hash-only. No raw unknown identifier, payload, native address, node header or extra proof flag is exported. Extra entries preserve the existing Growth validation rules (tags in 0..13; expected-name tags checked against source; unknown tags need not equal one another). No meaning or layout is inferred from these records.

Standalone receives the observer's pending snapshot, but only attaches SourceScopeAudit after the SAME Growth.Read call returns successfully. Its consumer requires exactly 3936 expected declarations and matched map flags, with TotalCount in 3936..20000. Report publication also checks these counts. A failed Growth call always exports SourceScopeAudit=null, even if a pending callback value existed. The snapshot may remain attached when the later strict scalar probe blocks; it describes the successful earlier Growth metadata observation only. The scalar's exact count, unknown-name rejection, and all existing gates are unchanged.

The audit carries fixed FreshnessAuthority/Scope text pointing to the ORIGINAL outer ScanStartedAt/ScanAgeMilliseconds. Hashing/export CPU time does not reset any clock. At or after three seconds, existing HistoricalOnly/expiry behavior still applies, and no audit grants current-map, script identity, semantic, scalar-layout, current-value, coaching or gameplay approval. No native read budgets were enlarged or reset.

Focused tests cover identical native read sequences with/without the observer, callback publication only after successful guards, no observer on source-type/name/QR/final-context/ambiguous-owner/cancellation failure, immutable cloned arrays/entries, count/type/hash/list bounds, consumer enforcement of all 3936 map declarations while retaining extra fingerprints, no audit on failed Growth, unchanged expired/blocked scalar reports and false flags, matching NFC hashes and no raw-name fields. Earlier tests remain in place.

Pending parent gates (not executed by this patch):

    dotnet test .\OrandOverlay.Tests\OrandOverlay.Tests.csproj --filter "FullyQualifiedName~Warcraft300GrowthReaderTests|FullyQualifiedName~StandaloneScalar" --no-restore

Source-only R6 touches GrowthReader, the old standalone runner, focused tests and this README. No build, live capture, artifacts, new WarcraftProbe, publication, deployment, public-prelude lookup or scalar-value/layout claim was performed. Parent owns ONE later audit capture and any comparison restricted to a trusted static public declaration catalog.
