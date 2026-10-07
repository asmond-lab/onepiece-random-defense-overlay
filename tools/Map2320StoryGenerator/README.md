# Bounded 2.320 story profile

Offline-only implementation. No MemoryDiagnostics, game, live-memory access, legacy profile approval, MainWindow, Models, or readers are modified.

## Public API
- Map2320StoryProfile.LoadBundled(): Data/story-progression-2320.json under AppContext.BaseDirectory.
- LoadFromDirectory(directory), Load(byte[]): exact byte hash, duplicate/unknown/missing fields, closed source members/functions/objects, semantic checks.
- Stages: immutable Map2320StoryStage array. EveryPlayerBase, ContributionQualified, Mvp, HiddenOrSideEffect use Map2320StoryReward.
- stage.Qualify(active, damage, objectiveMaxLife, completionPlayers): tri-state Qualified/NotQualified/Unknown. Pass exactly four completion-time slots. Missing/nonfinite/invalid denominator inputs produce Unknown. First three need only known active status; no MVP conversion.
- ProjectGuaranteedBaseStages(): immutable legacy DTO shape for StoryRewardSequencePlanner input only. Never invokes or approves MapStoryProfileLoader or any memory reader. Only known, constant, probability-one active-player Gold/Lumber and supported wisp units survive. MVP, contribution, hidden effects and non-wisp h05Y are excluded. Known=false rewards are excluded.

## Final data identity
SHA256 8715432b734c97c11c735d00337a3c8676f4b2e7060cb5de0870a2e025809b46
14 ordered stages, 158 components across all four groups.
JASS SHA256 6fdfc64bf8ad9463f5b5c8a351ffa7e1875d6cf9b51210129539375fa77a6c7c (3014709 bytes).

## Derivation, not old-pin copying
Run node tools/Map2320StoryGenerator/generate.mjs from repository root. It reads only the modern 2.320 extracted JASS/W3U/WTS and parsed 2.320 object JSON. Legacy 2314 structure was consulted for shape, never as payout or pin authority. The navigation/story analysis supplied handler entry points, which were independently inspected along with all payout helpers.

Source.Lines are verbatim complete functions. Every source pin binds function, start/end, exact evidence line and text. Compiler pins close the full function body set and object identity evidence independently of the document hash. Stage evidence includes Ii[0..13] chronology 85353-85366, owner-5 CreateUnit, death registration, callback binding, and wrapper-to-completion call. All data is source-version-specific; no unchanged claim is inherited.

Completion functions in order: fqT, Dub, iJy, bOT, ZAy, pmT, NTF, Dey, kOb, NNb, N3T, ZbT, ulb, YTF.
Base gold in order: 180,800,1000,2000,3000,4000,6000,8000,9000,10000,12500,10000,5000,5000.
Base lumber: 0,0,0,1,1,3,4,4,5,4,4,3,3,0.
First three extra e0IX: tkT40708, x1y46344, E7T23609, each active Zy only. Their separate MVP gold is 500/1500/2500 plus e0IX.
Later qualification: active Zy recipient; humans counted as Zy && PLAYING && USER at completion, exactly2=>30%, exactly3=>25%, otherwise20%. Damage/objective max life, not sum of player damage. Equal-damage MVP uses <= in ascending active-player loop, so last equal contributor wins.
Factories: uTT10413-10417, gpy11194-11198, Eiy12141-12145, KAT7591-7601 (active guard, count loop, CreateUnit7598), bnT14709-14711.

Hidden effects include technology unlocks, FOOD_USED increment, clear-score formula and ky update, MVP QF/VT/DT updates, board/timer lifecycle, wrapper completion dispatch, XP300 per enumerated hero (yoy79976/QBT27907), permanent A13A (37120 -> MVy14362-14365), Moria displayed activation notice23499 (not an invented mission state assignment), and ay>=5 Egghead120-second timer52731/52760.
AuT17386-17392 grants H0AW charge: caller1, Casino Og==1 adds another1, NF accumulation, stock cap16. It is not a fixed stock maximum increment.
AI03/AI00/AI02 item-system rewards are nominal5/100 roll buckets, missing-item AND matching pool-entry dependent. GnT16462-16499 can return -1 without reward. Success removes pool entry via tZb, jwb enables the ability and saves ownership, i0y adds A0O0/A0KM/A0NZ respectively. Exact ptT distribution uniformity is NOT proved; 1/20 records nominal accepted buckets only. These conditional grants are excluded from guaranteed projection.
e01A (letter A after digit1) is object-offset769524-769793, name 초월위습. e0IA (capital I) is mechanical dummy offset456861-457363, bijuuexplosion.mdl. Case-sensitive identities are never normalized together.

Lifecycle and CompletionDispatch components intentionally carry exact source bodies rather than inferring downstream gameplay guarantees from every global TriggerEvaluate subscriber. They are side-effect audit entries, never guaranteed rewards. No reward amount is fabricated or marked unknown merely because a generic event dispatcher has other subscribers. No static reward source gap remained in the bounded 14-completion payout paths.

## Verification
Standalone compiler + executable verification PASSED using the new profile, existing DTO definitions, and no production app startup. Checks: all14 objectives/groups, projection, first3/MVP separation, five population thresholds, unknown qualification, duplicate/unknown JSON and amount/dummy mutations. Artifacts isolated under .story2320-artifacts; requested version properties were 0.6.70.
OrandOverlay.Tests/Map2320StoryProfileTests.cs adds xUnit coverage for the same plus every reward group mutation, missing fields, source-pin mutation, semantic base checks, and unknown projection.
Full dotnet test invocation was blocked by unrelated existing RecipeCompletionCalculator.cs(204,69) CS1503 IDictionary<string,long> to IReadOnlyDictionary<string,long>. The new implementation itself compiled in both the attempted full build (after avoiding an existing Map2320Reward name collision) and standalone verification. The xUnit suite must be rerun by parent after that parallel-file build blocker is fixed; do not report it as passing.

Do not blindly update compiler approval hashes when source changes. generate.mjs is reproducible data production; finalize.mjs is a one-time development helper for the explicit COMPILED_SOURCE_PINS marker. Any future source change requires re-review and explicit new pin finalization.
