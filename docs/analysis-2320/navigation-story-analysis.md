# 2.314 -> 2.320 navigation / gambling / story / missions

Offline source comparison only. No production files, game, memory, MemoryDiagnostics or TMO changed. Object parsing deliberately left to the object-analysis agent. Source line numbers are 1-based, including blank lines. Evidence JSON contains verbatim function bodies, exact ranges and event registrations.

Sources: old indexed-members/2314/war3map.j (3069240 bytes, 88593 split lines), new modern-reader/members-ko-2.320/war3map.j (3014709 bytes, 86599 split lines). New source path is the strict-UTF8 verified extraction supplied by the parent. Existing migration notes still contain historical extraction blockers; those blockers do not describe this readable source. This report does not itself approve operational 2.320 support.

## 1. ContinuousBetting is replaced, not renamed

Old Ciw 10022-10076: XP[player]==3 increments cP at 10032-10034, then exclusive modulo ladder 12 > 6 > 4 > 2 (10035-10048): h060 ship; e0IX random wisp +1 lumber; e018 selectable wisp; e0IX random wisp. Rewards precede the attempt outcome.

New Ejy 12494-12539: Og[player]==3 adds exactly **1 charge** to rg[player] at 12508-12510, before failure roll. Thus every middle-gamble attempt, including failure, earns one 콤푸. No retroactive middle-attempt payout here. Same outcome thresholds remain: roll 1..100 <=30 fails; on success roll 1..1000 <=45 gives h060, otherwise gq random-middle pool (12512,12524-12530). Costs to initiate gambling live in objects and were not independently parsed here.

Selection PnN 11063-11122 -> Fxy 26777-26850. New exchange branch 26818-26846 sets Og=3, disables h06D, creates shop **h0BU**, creates **I009** charge item if null, sets charges 0 and player user-data, and gives item to shop. A0MA is disabled ordinarily, enabled if **Xy** otherworld-mode flag (26828,26837-26840). Existing rare rerolls are enabled only if ng>0; unlike Casino/RiskHedge this branch does not add a reroll. Do not convert the old category tooltip into a universal +1 grant.

### Exact exchange menu runtime

All handlers check sufficient item charges, stop and return if insufficient, then debit before grant. Registration 85613-85619 uses xpb event index 13 with the rawcodes below; callback bindings 82262-82268.

|Ability|New function/range|Cost|Executed reward|
|---|---|---:|---|
|A0CI|FWT 13725-13753|1|ptT(1,5): 4..5 = 5000 gold; 2..3 = e0IX x1; 1 = e018 x1|
|A0EZ|SrT 32161-32179|3|ptT(1,2) lumber, DMb|
|A0DB|mKy 56437-56455|3|ptT(1,2) e018 selectable wisps, gpy|
|A0IY|MKT 23098-23119|5|h060 x1|
|A0JR|OLT 26111-26128|3|dly(player,1,0) normal excavation stack; disables A0JR afterward|
|A0BV|eMy 46853-46870|10|dly(player,1,1) unique excavation stack; disables A0BV afterward|
|A0MA|hsy 51005-51036|20|xx[ptT(0,13)] x1 with A0YZ, plus h06G x1|

uTT 10413-10417 creates e0IX; gpy 11194-11198 creates e018; DMb 12138-12140 directly adds lumber. dly 11130-11193 caps each excavation stack at 10 and can return without a grant. The exchange debits first and still disables the button / displays success after this call, so **a full-stack purchase can consume points without increasing stacks**. No refund is present in the inspected handlers. Disable-after-use is verified, lifetime one-use semantics are not fully proved because re-enable paths were not exhaustively traced.

Random bucket shares above are nominal (2/5,2/5,1/5; 1/2 each), not a proof of perfect RNG uniformity. New ptT 6993-7001 is a custom stateful LCG with floating conversion, not GetRandomInt directly; old rcw 4779-4783 calls Fdw() then float-to-integer conversion. Fdw internals were not traced, so this is not evidence that the underlying distribution changed. Do not silently inherit exact rational-uniform assumptions across versions. Full pool identities and object mana/cooldown costs remain delegated/unknown.

## 2. Otherworld Casino / RiskHedge and charges

Old vAG 76528-76559: base 22 at 76532; RiskHedge 22+ww at 76539; on failure h06G x1, risk accumulator +11 at 76547; success resets ww=0 at 76553.

New IhT 17789-17831: **base 15** (17798); **15+iF** for Og==2 (17804-17806); roll > threshold fails (17807). Every attempt NF-- and GF++ (17802-17803). Failure risk accumulator **+15** at 17818; success resets 0 at 17823 and creates xx[0..13] with A0YZ. No explicit threshold clamp, so base+six failures=105 is certain next success under intended 1..100 range. Casino failure branch: if Og==1 AND ptT(1,2)==1, create ix[0..41] rare with A0YE (17808-17811), **instead of** h06G fallback; otherwise h06G at 17813. This is a conditional nominal 50% consolation, not a separate world-gamble success.

Fxy Casino Xy-only selection at 26795-26798 recalculates remaining charges **NF=2*NF+GF**, preserving the consumed-attempt count as compensation, then AddUnitToStock H0AW with max 16. This is not merely NF*=2. Old PnN had no corresponding mode branch.

New AuT 17386-17392 increments requested charge guT by 1 whenever Og==1, then NF+=guT, stock max16. All eight inspected call sites pass 1: 17516,21612,23477,33104,41569,50925,52693,58462. Thus future qualifying grants are 2 for Casino and 1 otherwise. Old direct stock additions used max8 (examples vNN 12951-12974, old 66983-66984 and 67238-67239).

### Creep-stage charging is a mission handler, not generic kill loot

Old SPE 41706-41721 on n00G mission registered at 66451: reads dead unit A90A level-1 as player index, sets Su completed, **no world charge**. New TZy 33098-33112 registered n00G at 9619: same attributed-player mechanism, guard RK already-completed, then **AuT(player,1)** at 33104. This is stage-three creep mission credit, once per player, not arbitrary killing-unit ownership. Stage-three normal loot s_y 72853-72859 separately gives killing player's 5000 gold +1 lumber +1 excavation. Old stage-three loot LAG 22683-22689 already did that. Old stage-two kvE 63523-63548 spawns n00G and transfers A90A owner marker. Old definition-of-justice-door completion has charge at 67238-67239; exact new removal from that door handler was not fully paired, so do not claim all charge sources migrated exclusively to creep.

RiskHedge normal high-gamble failure remains **two total h06G tokens plus 1 lumber**: ykb 79869-79943, base at 79888, additional at 79892-79893; old IoN 20093-20180. Rare reroll Vvy 37312-37358 sets cost 0 for RiskHedge, otherwise 2 lumber; still consumes ng allowance. Otherworld reroll JbF 19330-19368 skips 1-token cost if Og==2, rejects same xx rawcode, replaces unit. Exact old otherworld reroll counterpart not traced here.

## 3. Path of Kings / Leo / MartialLaw

Old kRN 11226-11295: all options add A0II to old helper; MartialLaw at 11241 doubles BE. New m7y 26995-27076 retains choose-time top-unit restrictions (Martial >=1 rejected; other nodes >=2 rejected), sets vg, then **creates h0C8 at 27066** for every Path option. MartialLaw sets this unit's **A0II level2** at 27068. No BE-like clear-reward multiplication in new selection. UI 39682 identifies summoned legend Leo; 39591 states strengthened damage/slow/stun, but object-backed slow percentage not independently verified here.

Old boss +2 lumber Path bonuses are removed in paired handlers: LRE 23056-23088 -> OdT 26415-26438 (o01X); ScE 42049-42081 -> ROT 30629-30652 (o01Y); pyN 69217-69249 -> Xhy 39923-39946 (o01Z). New handlers retain base gold/lumber but contain no vg Path bonus block. Therefore remove CategoryBasics Path boss_lumber:2_each and PathBosses extra rewards from a 2.320 model.

New Leo attack link: h0C8 registered npT(...,gu) 4293; gu=uRy 80697; uRy 74603-74610 executes tc, tc action Au at 4295; Au=J9y 80698. J9y 18579-18651 branches A0II level1: radius550, damage300000 (18602-18605); level>=2 and userData<3: increment userData, radius650, damage500000 (18606-18610); fourth upgraded cycle: reset userData0, radius650, damage1200000, stage4 (18611-18615). Stage4 calls dreadlordinferno via suT dummy h04V/A0Q6 (18572-18578,18645). Final damage at 18648 uses ATTACK_TYPE_CHAOS/DAMAGE_TYPE_UNIVERSAL. Stun duration, aura **10%** and upgraded slow require object-agent confirmation, not proved solely by this handler. Important inconsistency to reconcile: new utility metadata still maps A0II/B035 to **7.0000** at 5943-5947; do not treat that as the new effective aura value or silently sum it with Leo. Parent should compare object A0II levels and this stale-looking metadata.

Top-unit crafting restriction old Elw 13612-13625 -> new l0b 54591-54604, branch54595-54601 (full function in JSON). Preserve max0 Martial, max1 other Path constraints; do not remove with old berry effect.

## 4. DoubleBenefit retrospective payout

Old paE 11296-11354 sets option without retro payout; old h9E 57573-57587 awards ongoing point-value>100 craft base1 and extra1 for DoubleBenefit. New Njy 26917-26965 calls kqT at26923; auto/default CFy 21203-21218 calls same at21210. kqT **21196-21202** sums **dg+mg+Vg+Dg+eg+Gg+Xg+ag+Pg**, awards that many e0IX once at selection when sum>0. This is **one extra per counted prior craft**, not two and not a replay of the category basic effect.

Counters traced in jfT 53003-53061: A07O hero -> dg, A905 -> mg, A906 -> Vg, A907 -> Dg, A909 -> eg, A910 -> Gg, A912 -> Xg, A07N -> ag; otherwise rawcode membership fx[0..4] -> Pg. These are cumulative counters, not enumeration of surviving army. Full event dispatch / counter decrement audit not completed. New continuing craft XSy 39736-39750 still rejects Mg<=0 or Mg==2, rejects point value<=100, gives base e0IX and extra1 if Mg==1. Keep future 2 total but add distinct retrospective counter-based grant model; do not conflate the two predicates.

## 5. Story thresholds and first three

Old fixed threshold directly shown aFw 6492-6523 (6505 >=25), reward y0E 6524-6534 gated SE and Qu. First stage old AeG 6535ff; second Ucw 44238-44258 with dlE reward44213-44223; third FIw16198-16218 with a_w reward16187-16197. Full exact ranges in JSON.

New first-three unconditional extra e0IX: n000 fqT40713-40795 calls tkT40702-40712 at40783; n002 Dub46349-46419 calls x1y46338-46348 at46407; n003 iJy23614-23685 calls E7T23603-23613 at23673. Helpers loop players0..3 with **Zy[player] only**, no contribution gate, add1 e0IX. This is additional to unchanged displayed baseline resources; MVP remains extra e0IX + respective gold500/1500/2500 (40784-40785,46408-46409,23674-23675). Do not convert all reward types to unconditional MVP.

For later stories, example n005 **ZAy18253-18325** counts players satisfying Zy AND slot PLAYING AND controller USER (18278), sets **2 players->30, 3->25, else->20** (18283-18289), computes damage/max-life*100 and sets Df>=cf (18294-18295), then d4F18191-18201 gives e0IX if Zy AND Df. The else covers 1/0 as well as4; represent executable condition rather than only tooltip 4-player wording. Threshold recalculated at completion, so departed players can affect it. Damage denominator is objective max life, not total player damage.

All new completion handlers captured in evidence JSON: n000 fqT; n002 Dub; n003 iJy; n004 bOT76462; n005 ZAy18317; n006 pmT79438; n007 NTF47899; n008 Dey41670; n009 YTF37126; n00A kOb58477; n00B ulb52709; n00C N3T21628; n00D ZbT23493; n001 NNb17532. Threshold patterns are present in these later completions. Complete regeneration of all base/MVP/side-effect rewards was not attempted; do not mark every story field reverified from this focused analysis.

## 6. Missions and quest reroll

New sync i_F **51610-51650**, QUST_INDX binding86270, rejects if **pb>=10 OR player<0 OR player>=4 OR index<0 OR index>=3 OR iA[player] OR qA[player*3+index] OR NA[slot]==0** (51622). Thus round<10, one reroll total per player, only valid nonempty uncompleted quest. Replaces quest using ex pool, returns old quest to pool, sets iA=true at51636, calls VpT to install, hides all three reroll buttons. This is quest reroll, not navigation rare reroll. Old equivalent not found in scoped search; do not fabricate old handler or a previous limit.

New mystery craft quest Q017: EqT36394-36400 definition; agy42822-42838 actual handler requires active VK AND KCy(Q017,player,eK slot), then crafted FirstOfGroup(dy) A909 ability>0. Marks qA complete, grants **5 lumber** via DMb at42836. Pool inclusion ex[Gx]=Q017 at43416 and trigger creation85738-85740. Pool builder b7b 43214-43757 gates inclusion with Gy[2] at43415-43418; semantic identity of Gy[2] not traced, so otherworld-only eligibility remains unknown.

Demolition old thE74720-74753 rejects **OG>=10** (round<=9 accepted), new Cly10418-10457 rejects **pb>=9** (round<=8 accepted). Both pending/completed guard; new JK=false once completed; new active players receive1 e0IX at10433-10440. Event registration new n002->e7 at12223, e7=Cly81316; old n002->yv39855, yv=thE83245. Therefore exact target **Alabasta n002**, deadline last valid round8, before round9 begins. No need to infer this only from UI12220.

## 7. Pinned app fields requiring migration, no edits made

- Data/navigation-mechanics-2314.json Source identity/ranges/functions/branches and all bindings cannot be relabelled 2.320. New names/lines reordered widely. RNG helper structure changed; underlying old Fdw was not traced, so an actual distribution change is not established.
- ContinuousBetting object, Options[Gambler.ContinuousBetting], Transitions[continuous-exclusive-milestones], Formulas[continuous-formula], milestone fixtures and source ranges: replace with exchange resource balance + selectable cost/reward actions, not string rename.
- Transitions[risk-rising-hazard].Integers.worldInitialPercent **22->15**, worldFailureStepPercent **11->15**; Options[Gambler.RiskHedge].Effects world_pity_step:11->15. Preserve ordinary failure token/lumber semantics separately from world failure.
- Casino: add world selection NF=2NF+GF, future charge bonus and failure-conditional rare consolation, distinguish normal high gamble. Source/max stock8->16 relevant where stored. Exact normalized pool replacement deferred.
- CategoryBasics[PathOfKings].Effects line_movement_reduction:7/100 and boss_lumber:2_each invalid as new fixed category effects. PathBosses[].LumberReward extra2 removed. Add h0C8 summon with object-verified aura, not duplicated fixed slow. Options[PathOfKings.MartialLaw].Effects clear_berry_multiplier:2 and Formulas[martial-formula].clearBerryMultiplier remove; add A0II level2 strengthened effect, preserve zero-top restriction.
- Formulas[allied-double-formula]: keep future minimumUnitPointValue101 / totalRandomWisps2 but add retrospective sum-counter grant1 each for explicit+forced selection; update fixtures.
- Data/story-progression-2314.json Stages[1..3].RewardComponents.ContributionAtLeast25Percent move extra e0IX into active-player unconditional reward group. Later stages must replace misleading ContributionAtLeast25Percent key/Condition ActivePlayerAndDamagePercentAtLeast25 with player-count-dependent rule. Re-anchor Sources for all changed fields; preserve unreviewed rewards as 2.314 evidence, not newly verified.
- Quest schema / source files containing old fixed deadline9 must become deadline8 (pre-round9). Add Q017 mission and quest reroll round<10, one-per-player, uncompleted slot. Precise owning production class paths not searched in this subtask; parent to locate rather than assume.

## Explicit unknowns / caveats

Object aura10% / upgraded slow / stun duration; object exchange mana/cooldown/other costs; full xx/ix/gq pool identities; exact RNG distribution; all exchange ability re-enable paths; complete old/new charge-source exclusivity; old otherworld reroll counterpart; Q017 mode pool gate; retrospective counter event coverage/decrements; full story side-effect matrix. These are not marked verified by patchnote or tooltip alone. No production-support approval or exhaustive analysis claim.


## Follow-up: Q017 mode gate and exchange object costs

This section supersedes the earlier Q017 mode-identity unknown and cooldown unknown. It uses the existing parsed object outputs, not a duplicate parser. Leo reconciliation remains delegated to the object agent.

### Q017 is gated by otherworld-mode selection: VERIFIED

- **Gtb 15226-15265** is the mode-selection handler. It rejects non-authorized/locked selections at15227 and difficulties ay<4 at15233-15235. For the cx selection unit, it toggles **Gy[2]**: false at15249; the enabling branch announces **이세계모드**, describes adding otherworld units / activating otherworld gambling at15252, then sets **Gy[2]=true at15253**.
- Difficulty selection **DwT 11769-12078** clears selected modes Gy[1..3] at12022-12031 and announces cancellation at12033.
- Quest pool builder **b7b 43214-43757** adds **Q017 only inside if Gy[2] at43415-43418**, after ordinary Q016. This is a selected-mode gate, not a test of owning a mystery unit.
- In the same start/init function, ay<4 clears mode flags at43667-43673. **if Gy[2]** then announces otherworld mode at43680, sets sy[2] to 이세계, gives players0..3 **R00P research1** at43682-43685, and sets actual runtime **Xy=true at43686** before TriggerEvaluate(D). Thus Gy[2] is the selection flag and Xy the runtime activation flag for the same mode, supported by executed side effects, not just variable-name inference.
- Initial three quest assignments B8b are invoked for players0..3 at43696-43701, after this activation branch. Ordinary mode-selection flow prevents ay<4 selecting Gy[2]; the pool insertion precedes the defensive reset in source order, which should not be concealed if modeling anomalous externally-mutated state.

The earlier Q017 unknown is closed: label its normal pool eligibility **OtherworldModeSelected**, source Gy[2]. Actual completion conditions remain agy42822-42838 as previously traced.

### Exchange ability object data

Source **object-diff/2.320.w3a.json**, full EOF validated, 699383 bytes. All seven custom abilities inherit **ANcl (Channel)**, have alev=1, and explicitly set level1 **acdn=0.20000000298023224**, i.e. stored float32 approximately **0.2 seconds**. All have Ncl1=0, Ncl3=1, Ncl4=0, Ncl5=0, and range aran=700. These are exact stored fields; zero channel follow-through is not a declaration that all inherited timing/cost fields are zero.

Offsets below are **zero-based byte offsets in war3map.w3a**, not JSON line numbers; object ends are exclusive. acdn field record offset and value offset are both supplied.

|Ability|Object bytes [start,end)|acdn record / value byte|Confirmed handler debit (콤푸)|amcs mana override|
|---|---|---|---:|---|
|A0CI|684972..685582|685111 / 685127|1|absent, inherited ANcl|
|A0DB|685582..686154|685690 / 685706|3|absent, inherited ANcl|
|A0EZ|686154..686759|686296 / 686312|3|absent, inherited ANcl|
|A0IY|686759..687331|686861 / 686877|5|absent, inherited ANcl|
|A0JR|687331..687958|687467 / 687483|3|absent, inherited ANcl|
|A0BV|687958..688586|688070 / 688086|10|absent, inherited ANcl|
|A0MA|688586..689190|688702 / 688718|20|absent, inherited ANcl|

**Mana limitation:** no amcs record exists for any of these seven abilities, and no original/custom ANcl record exists in this map modification file. Therefore **explicit map mana override = absent**, but **effective inherited mana = unresolved from these artifacts**. Do not emit ManaCost:0 as a verified numeric value. The smallest remaining source for an exact inherited value is the matching base-game ANcl AbilityData.slk/default ability record; this was not supplied or parsed here. New JASS search found no direct BlzSetUnitAbilityManaCost / BlzSetUnitAbilityCooldown / ABILITY_ILF_MANA_COST / ABILITY_RLF_COOLDOWN overrides. This bounded negative check is not a blanket audit of every generic numeric field setter.

**Other automatic resource costs:** these are castable ANcl abilities, not sold-unit/train-item menu entries. Their inspected object records do not add an explicit gold/lumber/food/item-consuming cost beyond the handlers' 콤푸 debit; the handlers do not subtract player gold/lumber/mana for casting. Shop **h0BU**, base hrif, war3map.w3u object bytes859096..860100, uabi record859116 explicitly contains **AInv,Avul,A0CI,A0EZ,A0DB,A0IY,A0JR,A0BV,A0MA**, grounding all seven to the shop. Its ugol=0 (859530), ulum=0 (859575), ufoo=0 (859514) are the **shop unit's object production costs**, not additional redemption fees; selection already creates the shop directly in Fxy26827. No shop mana override is present, so this does not resolve inherited ANcl mana either. Safe conclusion is no explicit additional cost found, not proof that all engine defaults are zero.

### Two object tooltip prices conflict with executed JASS

- **A0JR normal excavation:** aub1 TRIGSTR_10964, field byte687879/value687895, says **2 콤푸**. **OLT26115 requires3 and26122 subtracts3**. Use **3** as actual cost. Tooltip also says once only; inspected handler disables the ability after debit, but a complete re-enable/lifetime trace remains unperformed.
- **A0BV unique excavation:** aub1 TRIGSTR_10965 (resolved text stored in JSON evidence), field byte688483/value688499, says **6 콤푸**. **eMy46857 requires10 and46864 subtracts10**. Use **10** as actual cost. Same lifetime restriction caveat.
- Other five displayed prices match their inspected handlers:1/3/3/5/20. Approx0.2-second cooldown is separate from the script's post-redemption disable and does not establish per-game one-use.

### Bounded pool rawcodes

Definitions only, no probability proof and no exhaustive dynamic pool mutation audit. JSON followup.poolDefinitions has each index, exact JASS line, rawcode and gq weight.

- **xx[0..13]**, lines25826-25839, all fourteen sampled by otherworld success and A0MA exchange: h06U, h06V, h070, h06T, h09L, h06Z, h073, h09K, h06Y, h071, h072, h06W, h065, h06X.
- **ix[0..41]**, lines25635-25676, forty-two eligible Casino-failure / rare-reroll entries: h09X, h01Q, h025, h01L, h022, h021, h020, h01Z, h01Y, h01X, h01W, h01V, h01U, h01T, h02M, h01S, h01R, h01M, h01N, h01O, h05L, h01P, h02I, h029, h028, h027, h026, h02A, h024, h02C, h02D, h02G, h02F, h02E, h04H, h023, h02B, h02H, h02J, h02K, h02L, h05K.
- **Important boundary:** ix actually also defines index42=**h05X** at25677, but IhT17809 and Vvy37344 sample0..41. Do not include h05X in these 42-entry outcome pools merely because it shares the array.
- **gq**, created80360;32 UnitPoolAddUnitType entries85537-85568, each explicit weight1: h01K, h00Q, h00B, h00P, h00S, h00R, h00W, h00T, h00U, h017, h016, h015, h014, h013, h01F, h012, h011, h00Z, h00Y, h00X, h018, h019, h01C, h00V, h01A, h01B, h01D, h01E, h01H, h01G, h01J, h01I.

### Follow-up impact / remaining boundary

Add verified approx0.2-second cooldown to all exchange actions; use explicit-null/inherited state for mana until base ANcl is sourced; retain script-authoritative3/10 excavation costs with tooltip conflict provenance. Q017 mode gate is now verified, and requested rawcode definitions are supplied. No new per-game one-use claim. Effective inherited mana / engine default cost data remains **[blocked: missing base ANcl default record]**, rather than falsely claiming the object-cost gap completely closed.
