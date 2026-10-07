# 2.320 diagnostic source-helper quality policy

## Scope

This corrects only diagnostic 3.0 / selected 2.320 mapping-quality accounting. The supplied verified source audit identifies exactly nine fixed startup services. No other unmapped rawcode is classified as a helper. No Data member, profile flag, package member, native reader, updater, UI, project, tool, or MemoryDiagnostics file changes.

The existing 0.6 minimum remains mandatory. A supplied threshold below 0.6 is rejected, not weakened. RecognitionState.Ready retains its existing diagnostic meaning. It is not gameplay-ready, local ownership, alive/dead, complete inventory, runtime-map identity, or exhaustive proof.

## Pins checked before acceptance

- Selected map must be exactly 2.320 for exclusions.
- Loaded Map2320DataBundle and Map2320GrowthSource objects are required. Their private constructors are accessible only through existing hash-checked loaders.
- Bundle fingerprint: 0638397cc88df886197f9fafbb6d8f292227a1863fa71cf23e4dd48fb1bc76cf
- Source archive SHA-256: 68445631fdaca12a343e9465e5bc5dc9b4c8c6cc927d701f52e2cb822821e15f
- Source JASS SHA-256 / GrowthSource JASS pin: 6fdfc64bf8ad9463f5b5c8a351ffa7e1875d6cf9b51210129539375fa77a6c7c; 3,014,709 bytes.
- Growth schema SHA-256: 08D09CA1386CFD57E5935BBFA9C2E595644B410AE61F7054089357FAA0904909
- Existing bundle still has seven closed dataset members. No eighth helper dataset or additional resource is introduced; no package-count change is requested.

These are offline source pins, not a hash of the running script. Future bundle/source revisions require policy review; the map label alone is insufficient.

## Exact allowlist provenance

Memory IDs are case-sensitive four-byte spellings consumed by RawcodeCodec. JASS IDs use reversed character order. No aliases, case folding, or arbitrary unmapped-ID rule is used.

| Memory ID | JASS ID | Verified name / controller role |
|---|---|---|
| 260h | h062 | 도박소1 |
| 4C0H | H0C4 | 기록지침, primary Tg |
| A70h | h07A | 미션 shop |
| A80h | h08A | 도움소 Ag |
| M50H | H05M | hidden 기록지침 yg |
| Q60h | h06Q | 강화소1 gg |
| R60h | h06R | 강화소2 |
| S60h | h06S | 연구소 |
| U50h | h05U | 연구소효과 SF |

The supplied verified source audit places creation of all nine fixed services at pinned JASS lines **56126-56141**. Its literal-use audit finds only startup uses plus two h08A filters. Its material/output-reference audit finds no references for these IDs in Data/tmo-unit-catalog, tmo-recipe-source, or map-recipes-2320. Prior object metadata evidence is available at docs/analysis-2320/object-diff/2.320.w3u.json; this implementation does not claim a new independent exhaustive re-audit of that file.

All nine IDs are checked through RawcodeUnitMap.IsRecognizedCard on every evaluation, including IDs absent from the sample. If any becomes a known or catalog-named card, SourceConflict=true, nothing is excluded, and acceptance fails closed. Future catalog additions are never silently dropped.

## Weighted accounting

Raw mapping and projected inventory are not modified. Only the quality denominator changes, using object multiplicities, not distinct-ID counts.

- ObservedObjects, MappedObjects, UnknownObjects, UnknownRawcodes remain raw values.
- ExcludedSourceHelperObjects: weighted exact pinned helper count.
- EligibleObjects = ObservedObjects - ExcludedSourceHelperObjects.
- EligibleMappedObjects = MappedObjects.
- EligibleUnknownObjects = UnknownObjects - ExcludedSourceHelperObjects.
- Detail explicitly reports raw mapped/observed, exclusions, eligible mapped/eligible, eligible unknown, source conflict, and minimum ratio.

The supplied example stays raw **7 mapped / 16 observed / 9 unknown**, and becomes **9 excluded / 7 eligible / 7 eligible mapped / 0 eligible unknown**. It passes only mapping quality at 7/7, not other gates. Seven cards plus nine helpers plus eight real unknowns remains 7/15 eligible and fails. Exactly 6/10 passes at 0.6; 5/10 fails. Helper-only and empty rosters never pass, even if RequireNonEmptyInventory is false.

Growth SPECIAL projection, counts, reservations, native reads, world stability, context/hash checks, 3-second observation checks, expectedTarget fences, and consumer eligibility remain unchanged. No extra growth resource is created.

## Alive/dead counterexamples remain required

The supplied pinned JASS evidence includes:

- **WPy lines 38008-38012**: life > .405 **AND !DEAD**. A helper role or present CUnit does not replace this predicate.
- Nonhero consumption at **21887-21904** and **52187-52199** uses **ShowUnit(false) + KillUnit**. This denominator policy does not solve hidden/killed-object or alive/dead validation.

These counterexamples remain blockers to claims of alive, local, exhaustive, or gameplay proof. State.Ready meaning and live-approval flags are unchanged.

## Verification status

Dedicated offline xUnit cases cover all nine source mappings and case variants, weighted copies, real unknowns, mixed inventory, raw versus eligible counts, helper-only and empty inventory, loaded/missing pins, non-2.320 selection including 2.314, every helper conflict including absent IDs, actual known-card conflict, unchanged mapping/growth entries, accounting mismatch, and the unchanged 0.6 rejection boundary.

No build, test execution, game/process access, network operation, or real sample probe was performed. Parent integration must build/run tests and read the real sample under existing gates. Source inspection is not a passing test-run claim.

## Changed files / API

1. Map2320DiagnosticHelperPolicy.cs: new internal policy, Evaluate, Quality.Accepts, immutable nine-entry Sources, compiled pins.
2. WarcraftMemoryRecognitionService.cs: only ReadDiagnosticInventoryCore quality math and returned diagnostics fields/detail.
3. Models.cs: only four new init-only diagnostic counters, read-only after initialization.
4. OrandOverlay.Tests/Map2320DiagnosticHelperPolicyTests.cs: dedicated offline tests.
5. docs/analysis-2320/diagnostic-helper-policy-provenance.md: this document.

Four new public diagnostic properties; no new public policy methods, no new resources.
