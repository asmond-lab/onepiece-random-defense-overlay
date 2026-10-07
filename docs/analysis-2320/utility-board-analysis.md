# NEW 2.320 utility-board stat aggregation and local-view provenance

## Scope and result

Read-only static analysis of modern-reader/members-ko-2.320/war3map.j (86,599 split lines, 1-based references below). No source execution, app edits, MemoryDiagnostics, game/memory/input interaction, or browser activity. Object evidence reused from the other agent's object-diff/2.320.w3u.json, .w3a.json and .wts.json, not reparsed. Both object parsers report EOF validated. WTS content IDs have no invalid UTF-8 entries; comments 253,254,294,387,388 are invalid, so global WTS UTF-8 must not be claimed.

**An authoritative dataset for this map's utility-board algorithm is feasible and produced: 221 explicit registry rows, 199 distinct abilities, 156 static unit-to-ability joins, plus conditional formulas and direct mutation evidence. It is not an authoritative complete combat-stat dataset, and cannot be reduced safely to unconditional per-unit sums.** 44 registry rows have no direct uabi/uhab unit join. Dynamic grants and spellbook membership explain some, not automatically all. All 199 registered ability objects were found.

Files:
- utility-board-formulas.json: all IDs, numeric constants, category, exact ability level, family, JASS lines, object field byte offsets, static unit joins, 70 direct ability mutation references, exclusions, story timers and limitations.
- utility-board-evidence.txt: line-numbered registry, aggregation, UI/sync/render functions and all functions containing the 70 indexed mutations.

## UI action and per-player binding

- r7T (71533-71579) creates setting button index 0 at ARb parameters (80,60,95,48), texture Interface\SettingButtonOff.tga. It creates board toggle index 4 (71568-71572), initially OFF, and manual utility refresh index 5 (71573-71577). These map UI constructions corroborate the gear/settings entry path; JASS coordinates alone are not a visual proof of the patch-note's 11-o'clock description.
- ARb stores button index in yb and binds iu (71528-71531). iu=function lay at 80712. lay (55122-55137) obtains BlzGetTriggerFrame, looks up the index, then calls FaT.
- FaT (55104-55121): index 0 toggles settings panel Wc for GetTriggerPlayer, with local display gates. Other indices are sent only under GetLocalPlayer()==GetTriggerPlayer(), using BlzSendSyncData("CMBT_INDX",I2S(index)).
- TRT (4143-4153) registers synchronized events for Player(0)..Player(3), false server flag. Registration at 85341 uses Idb; assignment 82220 is Idb=function HQb.
- HQb (15764-16001): eQb=S2I(BlzGetTriggerSyncData()); GQb=GetPlayerId(GetTriggerPlayer()) (15765-15766). Index 4 calls m0y(GQb,Uy[GQb]); index 5 refreshes **the triggering player's owned units**. No selected-player/view index is substituted.
- m0y (15720-15742) changes Uy[p], then hides with j0y or initializes with jib. Uy starts false (28832). jib creates jc[p] if needed and calls PlayerSetLeaderboard(Player(p),jc[p]) (15705-15718). phb independently makes the same binding (9355-9361).
- Refresh uses per-player timer li[p]. A remaining duration >0 rejects refresh; accepted refresh starts 5 seconds (15798-15806,15993). pc[p] becomes true (15982); toggling the board does not itself recalculate totals.

## Exact eligibility, deduplication and formulas

UtilityObjectData__Init (5053-6463), invoked via ExecuteFunc at 83720, declares the table. MT starts 0 at 131. Registry arrays are vT=ability, OT=category, rT=exact level, QT=family, hT=value.

Refresh scratch tables HT and JT are flushed for categories 1..4, and presence flags tT cleared (15810-15822). GroupEnumUnitsOfPlayer(...Player(p),null) is at 15823. A unit qualifies only when:

1. GetWidgetLife(unit)>0.405 (15828).
2. unit==SF[p] OR GetUnitTypeId(unit)==h08L (15831), OR
3. IsUnitType(unit,UNIT_TYPE_GIANT), except sx[1..9] and nx[0..12] (15836-15886).

SF[p] is a per-player unit handle, initialized as h05U at 56126, not a view or player handle. The explicit h08L exception is the curse doll, object offset 139393; explicit abilities A0PM (-7 armor, family B05B) and A0PO (7% aura slow, family B05C). Its object lacks explicit utyp, so the exception matters without guessing inherited classification.

Excluded types initialized 25580-25601: sx = h003,h002,h001,h007,h004,h008,h005,h009,h006; nx = h00M,h00A,h00O,h00C,h00D,h00I,h00N,h00E,h00G,h00J,h00K,h00L,h00F. SF/h08L exception is checked first.

**Two-stage selection, not an ordinary highest-value aura rule:**

- A row is present iff some eligible unit has GetUnitAbilityLevel(unit,ability)==registered level (15901). It is an exact equality, not >=.
- HT[category,ability] retains the **lowest registered level observed** (15903-15904). Later only rows at that minimum survive (15920). Duplicates of one ability at one level do not multiply it. A lower-level copy can suppress the higher-level registered row.
- JT[category,family] retains one surviving row: the first row initially, replaced only if abs(new value)<abs(old value) (15921-15953). Equal magnitudes keep the earlier registry row. This selects the **smallest absolute value**, including when that is the weaker reduction. Do not silently repair it to maximum; whether intended or a bug is unknown.
- Category boundaries are part of both keys. The same family in different categories does not collide.
- Only chosen rows contribute (15962-15971):
  - category 1: oT[p] = sum(-hT), 79 rows, armor category.
  - category 2: kT[p] = sum(hT), 101 rows, aura slow percentage points.
  - category 3: LT[p] = sum(hT), 37 rows, proc slow percentage points.
  - category 4: jT[p] = sum(-hT), 4 rows, proc armor.

No per-unit-count multiplier, slow multiplicative formula, cap, proc uptime/probability, active buff, range/target check, attack event, or actual debuff state occurs in this refresh calculation. All registered procs with qualifying abilities are included as potential simultaneously. The four category sums are independent. In particular the displayed armor-label '합계' does NOT add category 4 to category 1.

Examples, conditional on both abilities qualifying:
- category 1 / B02M: A15G=-35 (5054-5060), A0GJ=-25 (5404-5410), chooses -25 and contributes 25, not 35 or 60.
- category 1 / B00A: A112=-35 (5110-5116), A0X8=-20 (5145-5151), chooses -20.
- category 2 / B004: A10D=35 (5649-5654), A08H=5 (6099-6104), A03S=15 (6183-6188), chooses 5 if all qualify.
- A173 level 1=45, level 2=60, both family A173 (5613-5624). If both levels exist on eligible units, the lower registered level is retained, yielding 45 for this family.
- Category 4 is exactly A0GG=-15 (6435-6441), A132=-30 / B00C (6442-6448), A074=-20 (6449-6455), A0H8=-30 (6456-6462). This does not justify summing all four without an eligible owned roster.

## Traits, modes, transforms and incomplete coverage

HQb does not read a standalone trait flag or transform identity list. These matter via current ownership, life, classification, unit handle identity and exact ability presence/level. Static rawcode/count does not supply them.

- B5T (9044-9061) requires at least 4 FOOD_USED trait points, increments A173 and A174 and spends 4 points. The board's A173 levels are 45/60 aura slow; this trait can change the qualifying row, subject to duplicate-level suppression.
- Black Maria AZT (7799-7821) cycles A09R -> A0T7 -> A0T6 -> A09R. A0T7 is registered as category 2, level 1, B065, 40 (5727-5732); its removal changes eligibility after a manual refresh. A single fixed Black Maria number cannot express all modes.
- ICy (17277-17332), item progression, upgrades A0WN to level 2 at charges>=15 when Bn[p]==1 and to level 3 at >=30 when Bn[p]==2; adds A0WV at the first upgrade. The board registers **A0WN only at level 1, -5** (5152-5158). Thus its contribution is omitted at runtime levels 2/3, rather than upgraded automatically. Object A0WN Had1 values are -5/-20/-40 at levels 1/2/3 (w3a field offsets 216648/216672/216696). Those object numbers must not be substituted into the board registry. Spellbook A0WR contains A0WN (spb1 offset 213045); h081 Douglas Bullet has A0WR. Whether and how a runtime spellbook exposes that ability is an engine/runtime condition, not a direct uabi proof. A0WV is a separate registered 20 aura-slow row (5703-5708).
- G1y (26851-26916) maximum-output navigation raises A07X to 2 (26866); reverse option removes it (26895). Registry A07X level 1=-20 and 2=-25 (5537-5550). Eligibility of Ag[p] is still required; this is not an unconditional navigation bonus.
- i0y (16392-16440): input IDs AI03/AI04/AI14 grant A0O0/A0O1/A0O7 to SF[p]. Their board rows are 6 armor (B04R,5292-5298), 7 aura slow (B04O,5823-5828), 3 armor (B04S,5285-5291). These are dynamic grants to a specially eligible unit, not normal inventory unit counts.
- 70 direct rawcode-bearing UnitAddAbility/UnitRemoveAbility/SetUnitAbilityLevel/IncUnitAbilityLevel-family references are indexed with containing functions and exact lines. This lexical index is not an exhaustive symbolic execution or all variable-based mutations. Unit replacement/morph engine behavior, learned hero abilities, inherited object fields, conditional spellbook exposure and hidden state remain unsupported for unconditional per-unit totals.

## Rendering, manual stats versus live story

Tny (9336-9347) saves uc[p] as the first stat-section item index, adds a manual-refresh heading or prompt based on pc[p], then exactly four rows:

1. 방어력 감소 합계 : oT[p]
2. 발동형 방어력 감소 : jT[p]
3. 오라형 이동속도 감소 : kT[p]%
4. 발동형 이동속도 감소 : LT[p]%

Each uses R2SW(value,1,1), yellow |cffffcc00 markup and |r reset. LeaderboardAddItem numeric value is always 0; rendered numbers are strings, not the leaderboard numeric score field. LeaderboardSetStyle(...true,true,false,false) appears in phb/jib. On refresh, rows from uc[p] onward are removed and Tny appends fresh stat rows; story rows above are preserved (15983-15991).

h_F (9362-9367) binds/clears a player's board, labels it '유틸보드 - '+story name and inserts story heading. RHb (9348-9354) appends cached stats and locally displays it according to Uy[p]. Fourteen story render functions have repeating .25-second timers (all listed in JSON). For example BNT (9373-9420) is E7 (81351), started by Lkb damage handling at 22361. It renders every active player's NB[i]/sB*100 to each enabled viewer p, with two decimal places R2SW(...,1,2). Threshold KB is 30 for two active humans, 25 for three, otherwise 20 (9381-9396). This example is not asserted as the formula/threshold for every story. Other renderers and their exact functions are preserved. Crucially story timers call h_F/RHb, not the HQb aggregation, so live story updates can coexist with stale manually refreshed stat totals.

## Local identity versus view provenance

Proved by this JASS: synchronized player identity for calculation is GetTriggerPlayer from the sync event; enumeration is Player(p)'s units; board object jc[p], toggle Uy[p], refresh timer li[p], totals and headings are indexed by that p; PlayerSetLeaderboard binds that player. GetLocalPlayer()==Player(p) gates LeaderboardDisplay and messages at 9351,9369,15716,15722,15732,15803,15994. FaT also gates outgoing sync on equality with GetTriggerPlayer. Story board viewer p is separate from each contribution row's player i.

Not proved: GetLocalPlayer's native implementation, immutable local identity, native memory location, relation to an observer/replay-selected view, safety of any external view pointer, or whether client engine state can change local identity. There is no view-selection variable consulted in this board path; that establishes source-level non-use, not an engine-level invariant. No immutable-native-local-identity claim should be derived from these calls.

## Existing app impact comparison only

Read HandStatsProfile.cs and InventoryStatsCalculator.cs, no edits. HandStatsProfile is explicitly a TMO helper 48784 transcription, with schema/source validation and limited approved armor non-stacking groups, not map ability registry provenance. InventoryStatsCalculator.Calculate takes inventory counts (line 64 onward), applies source/fallback values per count, and Amount (208-217) uses value*count unless an approved group exists, then the **maximum**. Board semantics differ materially: per-ability existence, minimum registered level, minimum absolute family magnitude, per-category keys and runtime alive/classification/ability state.

InventoryStatSummary.TotalSlow is Slow+TriggeredSlow (29); TotalArmorReduction combines armor, proc armor and observed stacking (31). Map board prints its separate category arrays, and its first 'armor total' is only oT. Existing profile fields for stun, buffs, mana, damage and single/stacking armor exceed this board's four-category scope. Do not replace the whole profile with this registry or claim display-equivalence by copying numeric unit values. A separate versioned board-calculation dataset/state contract would be needed, with explicit source and unknown-state handling.

## Confidence and next-use boundary

High confidence: constants, exact-level match, owner/life/type gates, duplicate/family comparisons, signs, cached display formatting, UI/sync binding and local display gates are directly present. The JSON is suitable for an auditable offline board-model implementation supplied with an explicit qualifying ability-state snapshot. It is not a measurement of a live player's board or combat output. No live numeric totals were computed, no native behavior was assumed, no unresolved case was coerced to a speculative sum.
