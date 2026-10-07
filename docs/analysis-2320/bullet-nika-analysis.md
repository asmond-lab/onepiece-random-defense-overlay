# Actual 2.320 versus 2.314: Bullet and Nika

## Source boundary and identity

Read-only offline analysis. No game, process memory, input, profile activation, archive changes, production edits, or MemoryDiagnostics. JASS decoded with fatal UTF-8, not replacement decoding. SHA256 verified:
- NEW modern-reader/members-ko-2.320/war3map.j: 6FDFC64BF8AD9463F5B5C8A351FFA7E1875D6CF9B51210129539375FA77A6C7C (86,599 split lines).
- OLD indexed-members/2314/war3map.j: 0BCCC47907A9505F38EFAF6BBF20228A728EABDFAEC3209CCA7DF2269BFC2028 (88,593 split lines).
- Object evidence read from the other agent's object-diff/2.320.w3a.json and 2.320.w3u.json after those files appeared. I did not redo the binary parser or decode WTS lossily. Resolved string values below are from that parser; the five malformed WTS comments are not used as values.

**PROVED identity correction:** Bullet is native **h081 = $68303831**, app byte-reversed **180h**, not native h018. Parsed W3U h081 unam is 더글라스 불릿 - 불멸, with A0WU/A0WK/A0WL/A0WT/A0WR. Native h018 is 징베 - 특별. New recipe lookup explicitly associates h081 with IM12, 괴물의후계자, bullet im (34958-34966), and attacks register h081 at 84903. Literal h018 must not be used for Bullet migrations.

## 1. Bullet: what the formulas actually count

**PROVED:** The linked damage formulas read ability levels, not inventory slots, physical item count, or item charges, in BOTH maps. In 2.320 helper **NfT 24023-24029** reads GetUnitAbilityLevel(unit, ability), returns 3 if above 3, else the level. Thus L_A = min(level(A0WQ),3), L_S = min(level(A0WO),3), with no invented lower clamp. Old CLw 9193-9321, NkE 25756-25900 and jfG 62006-62172 read GetUnitAbilityLevel directly into the matching thread integer slots. No case-insensitive literal actualitemcount occurs in either supplied JASS, nor ActualItemCount in the inspected top-level app C# files.

An item-count-proportional description is therefore not a valid literal implementation model for these formulas. Upgrade item charges control tier transitions, which then change ability levels. Charges 15-29 do NOT produce continuously growing linked damage; they share level 2. Maxed item replacement does NOT make the multiplier 0.

### Exact old/new formulas (pre-damage-wrapper values)
H = target CURRENT life at the callback; A/S = the stored attack/speed ability level. New values are clamped at 3 as above. These are not claims about final HP loss after armor/resistance/global damage wrappers.

| Connected effect | OLD exact function / line / formula | NEW exact function / line / formula |
|---|---|---|
| Speed, base/unique AoE | Pmw 38693-38695, damage at 38694: 30000.+2500.*I2R(slot1) | RRT 30736-30738, 30737: 30000.+25000.*I2R(slot1) |
| Speed, legendary AoE | RSE 40965-40967, 40966: 50000.+4500.*I2R(slot1) | wob 77419-77421, 77420: 50000.+45000.*I2R(slot1) |
| Speed, unique/legendary extra single target | jfG 62006-62172: 10000.*I2R(slot1) | Mqy 24101 and 24105: 100000.*I2R(slot1) |
| Attack proc, ordinary-target filter | qPE 69666-69668, 69667: (250000.+H*.02)*I2R(slot0)*.01 | BTF 9456-9458, 9457: (250000.+H*.02)*(1.+I2R(slot0)*.1) |
| Attack proc, special-target filter | ZMw 50080-50082, 50081: 250000.*I2R(slot0)*.02+H*.01 | gGy 49542-49544, 49543: 250000.*(1.+I2R(slot0)*.2)+H*.01 |
| Strike initial hit | baw 51979-51981, 51980: 1500000.*(1.+I2R(slot1)*.05) | yOb 79677-79679, 79678: 1500000.*(1.+I2R(slot1)*.5) |
| Strike repeated hit | cRN 52608-52610, 52609: 100000.*(1.+I2R(slot2)*.01) | HLT 15660-15662, 15661: 100000.*(1.+I2R(slot2)*.1) |

Concrete tier-3 examples: speed legendary AoE 63,500 -> 185,000; speed extra target 30,000 -> 300,000; strike first hit 1,725,000 -> 3,750,000; repeated hit 103,000 -> 130,000 each, 12 repeats. Ordinary attack proc at level 2 changes from .02*(250000+.02H) to 1.2*(250000+.02H); at level 3 .03 to 1.3. This is not uniformly a 10x total-damage buff.

### Caller proof, not keyword proximity
- NEW 84903 npT(h081,mgb), 82027 mgb=function xiy. **xiy 78779-78795** executes sn on each attack; exactly mana==150 executes nn, plus Nn at A0WU>=2 and qn at >=3, then resets mana, otherwise increments mana by 1.
- sn/nn/Nn/qn/Kn action registration 84904-84918, code assignments 82028-82032 map to **Mqy/kOT/GyT/xUb/k8y**. OLD h081 registration 86927 -> BEG=function naw; **naw 65965-65981** has the corresponding Mg/Sg/Jg/Xg chain; 83953-83957 map jfG/NkE/CLw.
- **Mqy 24030-24122** watches EVENT_UNIT_DAMAGED, with **ogy 68701-68703** requiring damage source == stored attacker. Positive event damage is saved. At armor tier Bn==3 and captured damage >=600000, 24077-24078 applies captured-damage AoE plus .5 captured damage (chaos/universal); otherwise 24080 applies only captured-damage AoE. This is separate from item/skill multipliers.
- With B06P buff, Mqy 24083 stores clamped A0WO level, branches on fn tier; 24095-24105 select xC/OC and extra-target hits. Callback assignments 81178-81181 resolve xC=RRT, MC=cYb (Kpy 1), vC=YMy (Kpy 2), OC=wob. Otherwise 7% bloodlust trigger 24109-24111; independent 5% proc at 24113-24116 executes Kn.
- **k8y 53552-53628** stores clamped A0WQ level at 53570. Zn==2 applies QC; Zn==3 applies QC+tC and has 50% delayed repeat at 53597-53622. 81183/81185 resolve QC=BTF, tC=gGy. **kuT 54579-54590** excludes enemies with A902/A911/A904; **oFF 58192-58203** selects enemies with any of those abilities. Do not relabel these target classes solely from nearby text.
- **kOT 53803-53895** stores A0WQ into slot1 and A0WO into slot2 at 53818-53819. 53827 JC=yOb is initial radius-600 hit; 53875 HC=HLT is repeated radius-600 hit, counter <12 at 53877. Assignments 81186-81187 prove linkage. The new A0WU tooltip says attack-level for repeated hits, but JASS actually uses SPEED level for repeats. JASS also has the leading 1+ terms omitted from some tooltip prose.

## 2. Bullet upgrade charges and trait selection

**PROVED:** New attack upgrade Mhb 23791-23845, speed RLT 30531-30585, armor ICy 17277-17332. Charge thresholds remain >=15 to tier2 and >=30 to tier3, capped30; associated arrays NEW Zn attack, fn speed, Bn armor versus app's OLD Rg/Bg/ag. All three >=2 upgrade A0WU to2; all ==3 to3. At tier3, upgrade item replaced with I006/I007/I008 respectively. Craft **e6y 46663-46706** creates I091 slot0, I092 slot2, I093 slot4 and initializes Zn/fn/Bn=1.

Raw handler code first adds 1 charge, then adds random 1 (ptT(1,5)<=2) or 2, with messages reporting random amount. This apparent extra +1 must not be presented as an unconditional net +2/+3 gameplay result without accounting for item-use charge consumption; the app advises net 40% +1 / 60% +2. Source handler operation is proved, engine event net remains a boundary requiring item-use semantics, not guesswork. OLD AJE 5588-5642 follows the same pattern.

**PROVED new trait path:** A203 -> oIy via registration 84919 and assignment 82033 (see machine evidence). **oIy 58220-58268** requires h081, A203 present, no A207, and none of A0WQ/A0WO/A0WN >3. Requires 4 FOOD_USED trait points. Adds A207 selection book, hides A203, deducts4. A204/A205/A206 -> **Mob 23922-23973**, registered 84920-84922. Requires h081 and A207; chosen target is A0WQ/A0WO/A0WN respectively. **Only fob==3 passes**; then SetUnitAbilityLevel(qob,Bob,4) at23964 and removes selection book/choices. No further cost in selection handler. NfT makes linked damage remain based on level3, not4.

**PROVED data/text discrepancy, do not silently fix it:** Parsed NEW W3A A0WQ Cac1(level4)=0.5; A0WO Oae2(level4)=0.5; A0WN Had1(level4)=-50. Their level3 values are .4/.4/-40. The script selects level4. Tooltip says 48% / 48 and +20%, but actual selected object level is 50% / 50, a 25% increase over40. Objects contain stale level5/6 fields (.24/.48, -24/-48), yet alev=4 and the handler selects4 only. Treat tooltip claims and executable object values separately. Final in-game aura application is not tested.

## 3. Actual ITEM condition semantics (separate from Bullet scaling)

**PROVED:** Recipe ITEM is boolean player ownership of an ability-like token, not physical bag item count. NEW ITEM registration 85269-85278 -> Zdb=s2T / fdb=sBT (82194-82195). **s2T 72378-72391** computes missing=required, then subtracts exactly1 if **euy 72372-72377** returns saved boolean Zc[playerId,rawId]. Failure iff missing>0. Thus effective actualItemCount is **0 or1**, absent saved boolean=false. required1 succeeds exactly when true; required2 fails even when true; required0 would pass. Consumption **sBT 72489-72494 -> q8b 72474-72488 -> yby 72470-72473** hides the player's ability and saves false. OLD **lBN 63641-63654 -> wmw 21581-21586** uses the same boolean model in ZM.

NEW recipe validation chain: **EDT 7175-7197 -> oEb 7120-7141 -> v6y 7093-7119** loads per-condition type/id/required then evaluates condition registry; only after all validate does URb 7084-7092 -> IRT 7060-7083 consume. These tokens must not be confused with Bullet item charges or A0WQ/A0WO skill levels.

## 4. Nika eternal recipe: strengthened-input requirement

**PROVED craft gate change:** OLD **nrE 66259-66308** captures first owned H099/H0B2 hero stats/XP then tries ET02 or ET03. No trait gate there. NEW **MHy 22944-23071** still enumerates first H099/H0B2, then **22979 if sg[playerId]==0**, displays '특성강화가 필요합니다!' and RETURNS at22988, before EDT/consumption. Therefore sg!=0, not exactly sg==1, is the literal gate. After successful craft **sg=2 at23014**. This is a player flag, not a test on the exact ingredient handle's strengthened ability level. Avoid promising stronger per-unit validation than the source provides.

**Flag origin proved:** **WyT 38454-38479** requires4 trait points, sets sg=1, raises A04B to2, removes cast ability, deducts4. **tBT 73123-73137** requires4, sets sg=1, increments A0X4, hides A186, deducts4. Assignments q1=WyT 81682, mbb_1=tBT 81823; registrations A04E at84204 and A186 at84492. Those abilities occur on H099 and H0B2 respectively (W3U). NEW command registration 84697-84698 -> myb=MHy at81925.

**Recipe table change proved:** OLD ET02 35377-35415 required H099; ET03 35416-35454 required H0B2. NEW ET02 67403-67441 requires **H08T wildcard**, plus h03C, h02D, h00K, ITEM AI01 required1, WOOD5; output H0BK. No ET03 literal exists in NEW. **L4F 21509-21538**, on creating H099/H0B2, indexes either under H08T at21528-21529. W3U H08T is '루피 or 스네이크맨 - 초월'. New MHy calls only ET02. Thus do not mistake H08T for a real replacement hero required to sit on the board.

**Other Nika behavior, separately scoped:** MHy also handles hA/bg/Qy/hy/rF state at23016-23057. sg==2 is read elsewhere at76111 in a later event sequence. Initial bounded pass deferred this separate system. **Follow-through in section 6 now closes the source attribution** as Nika-triggered Five Elders suppression/removal, with exact runtime-effect limits. It is not labelled a secret mechanic.

## 5. Overlay impact assessment only

Read BulletGuidePolicy.cs, BulletGuideRuntimeReader.cs, BulletGuideUpgradePolicy.cs, Data/bullet-strategy-2314.json, and relevant map-recipe-overrides-2314.txt entries. No edits.

- Goal rawcode:180h remains the correct app identity. Do not migrate it to810h based on the h018 typo.
- Runtime reader explicitly targets 2.314 globals ag/Bg/Rg and requires version '2.0.4.23745' plus RouteQuestCatalog.MapScriptSha256. NEW uses Bn/fn/Zn and a new script hash. Preserve fail-closed behavior; do not activate old profile or merely bypass hash gate.
- BulletGuideRuntimeState currently maps armor tiers1/2/3 to5/20/40, slow20 for >=2, and exact charge tiers1/15/30. Base charge tier model still fits; trait choice does not change Bn/fn/Zn to4, but can make armor ability level4=-50. Tier-only UI would underreport actual selected armor aura by10. Needs separate trait selection observation when a future verified profile is built.
- Policy's upgrade priority armor then speed and round50/60 milestones are strategy, not mechanically proved optimal by these balance changes. Attack damage increases materially; recommend reassessment, not automatic priority inversion. Existing guide deliberately leaves attack spending user-managed. bullet-strategy-2314.json is explicitly not the source of opening groups per BulletGuidePolicy comment.
- Do not build damage estimates from ExactCounts as a continuous multiplier. Use verified ability level with clamp3 for linked skills, and keep selected trait aura separate.
- Current recipe override contains KB0H=2B0H:1,C30h:1,D20h:1,K00h:1,LUMBER:5,700I:1. It neither represents the new H099/H0B2 wildcard choice nor the sg prerequisite. Future 2.320 recipe readiness must require confirmed trait flag, preserve token semantics, and support both hero alternatives. Existing catalog item mapping700I versus JASS tokenAI01 needs explicit mapping validation, not assumed rawcode equivalence.
- Data migration alone cannot establish strengthened-input readiness or safe memory reads. Unknown signals must stay unknown. No production change is authorized by this report.

## Evidence file
bullet-nika-evidence.json stores exact old/new function blocks (with source line spans), relevant object records, and source hashes. It supports inspection without parsing obfuscated identifiers by proximity. Labels above distinguish script/object proof, untested runtime effects, and deliberately inconclusive out-of-scope mechanics.


## 6. Follow-through: Nika / Emet / Five Elders attribution CLOSED

This section supersedes section 4's earlier bounded inconclusive interaction note. **PROVED SOURCE CONNECTION in actual 2.320**, independently of patch-note text. Classify as **Nika-triggered Five Elders suppression/removal**. It is not necessary or appropriate to call it a 'hidden new feature'. Compared against the actual OLD matched Elder handler below, the Nika/Emet branches are added in these sources.

### Registered activation chain and exact conditions

1. **mEy 56063ff**, registered via WGb=mEy at82469 and TriggerAddCondition(V,Condition(WGb)) at86282, creates **bg = Player(6) e003** at56212. W3U e003 is a global line-mob movement dummy. It is the aura carrier, NOT Nika or the visible Elder.
2. ay==5 (신, see43595-43604) adds A0Z2 to bg; ay==6 (악몽,43630-43639) adds A0Z3. Each branch draws ptT(1,3), creates **OA under Player(7)** as o02E Warcury / o031 Saturn / o032 Nasjuro at56217-56223 or56240-56246, and gives it the waiting texttag QA. These raw unit identities are confirmed by parsed W3U names. They carry A13H,A0IQ,Avul. The selection is not determined by Nika.
3. mEy56302-56304 creates periodic .5-second trigger SUy -> B8=yFT (81380). **yFT79461-79474** returns while Wb>0; otherwise alternates round-start Yb (setting pb=Eb) and round-end Ub. **Ub -> PXb=wSy** is registered at86332, assignment82493.
4. **wSy77091-77162** returns unless **pb==50 AND Ng**. Ng is set true by difficulty setup for ay3..6 (43539,43565,43598,43633). It increments Wb, gives configured rewards, starts a70-second countdown via Q8=htT, then **TriggerExecute(rA) at77154**. htT51037-51039 decrements Wb at countdown completion. Thus the Elder sequence starts at the beginning of the round50 transition countdown, not only after the70 seconds finish.
5. **FiveElders__Init4110-4114** creates rA and adds ej; ej=function vYb at80653. Init executes at83722. This is the registered handler containing the sg check, not an unregistered animation fragment.

### Path A: Nika exists in the player flag before effect application

**vYb76072-76255**, initial state0: if tA is false and ay>=5, captures OA, destroys QA, schedules state1 after .03s. States1..9 fade the Elder, .1s each. At **state10,76108-76119**, loop over **player IDs0..3** sets PYb=true if **any sg[index]==2**. No ownership match to the host is required, no live-unit enumeration, hero health, distance, item count, or Nika ability is checked here. sg2 comes from **successful MHy craft23014**; sg1 from the strengthening handlers is insufficient.

- If any sg==2: schedule state11 after .1s, bypassing effect-application state13 entirely. hA remains0 in this path.
- If no sg==2: schedule state13 after .1s, apply selected Elder effects below.
- State11 (76121-76133) plays SE\Emet.mp3, creates visual effects at the captured Elder location, prints Emet's line, waits4s to state12.
- State12 (76134-76147) creates final effects, **Yhb(captured OA) at76145**. **Yhb7923-7926 = ShowUnit(false); KillUnit(unit)**. This kills/hides the captured visible Elder, not bg, Nika, all bosses, or all line enemies. Then prints '오로성 효과가 적용되지 않습니다' and ends via state14. There is no AoE damage call here.

The scripted delay from initial normal entry to Emet line is approximately1.03s (.03 + nine .1 + .1); kill follows4s later. These are scheduled intervals, not measured engine timing guarantees.

### Path B: Nika crafted after an Elder effect has already applied

**vYb state13** writes **hA=1/2/3** for Warcury/Saturn/Nasjuro respectively. Successful **MHy23014-23057** then:

| Active hA | Immediate arithmetic restore in MHy | Corresponding application in vYb |
|---|---|---|
|1 Warcury|rF +=10; every Qy[0..3] +=10; if ay==6 another +5 to each|76153-76157 subtract10, 76170-76174 another5 on ay6|
|2 Saturn|every hy[0..3] +=7, plus3 if ay==6|76186-76189 subtract7;76202-76205 another3 on ay6|
|3 Nasjuro|no rF/Qy/hy arithmetic, because this branch did not change those globals|76214-76236, aura and research effects only|

For **any hA!=0**, MHy23043-23056 removes A13D,A13E,A13F,A13G from **bg**, sets bg's A0Z2 (ay5) or A0Z3 (ay6) to1, sets **Player(6) R00S=0 and R01W=0**, sets **hA=0**, **tA=true**, and executes rA. All these reversals occur synchronously in the craft handler, BEFORE Emet's delayed animation.

vYb state0's **tA path76089-76095** clears tA, captures OA, sets OA=null, destroys/nulls QA, and schedules state11 after15s, bypassing the ay test, fade and sg scan. The same line/effects follow, then kill after another4s. This does not wait15 seconds to restore the globals/auras; it waits15 seconds only for the visible Emet sequence. Subsequent normal successful Nika crafts see hA0 and do not run this restore branch again unless some other caller has reapplied an Elder.

### Actual target/output state when NOT suppressed

Only three source writes make hA nonzero, all in vYb. Target scope of aura fields is relative to bg owner **Player(6)**, not the crafting player. Parsed W3A field evidence is saved in nika-interaction-evidence.json.

| Elder | ay5 effects set by script | ay6 effects set by script |
|---|---|---|
|Warcury o02E / hA1|rF,Qy all -10; bg A13D level1; A0Z2 level2; Player6 R00S5,R01W15|rF,Qy all -15; bg A13D level2; A0Z3 level2; R00S10,R01W25|
|Saturn o031 / hA2|hy all -7; bg A13G level1; A0Z2 level3; R00S5,R01W5|hy all -10; bg A13G level2; A0Z3 level3; R00S10,R01W10|
|Nasjuro o032 / hA3|bg A13E+A13F level1; A0Z2 level2; R00S15,R01W5|bg A13E+A13F level2; A0Z3 level2; R00S25,R01W10|

Executable object records, not just displayed claims:
- A13D Had1=10/15, radius99999, target mask air,invulnerable,self,ground,vulnerable,friend: friendly armor aura from Player6's carrier.
- A13G Cac1 approximately-.20/-.30, radius99999, target air,invulnerable,enemies,ground: attack reduction on Player6's enemies.
- A13E Oae2 approximately-.10/-.15, same enemy mask/radius: attack-speed reduction.
- A13F is AOae movement aura, friend/self mask, radius99999; level2 Oae1 approximately.15 is explicit. Level1 Oae1 is **not overridden in the parsed custom record**, so its numerical inherited base value is not independently proved from that record. The JASS announcement says10%; this report does not elevate that text to an inspected base-object value.
- A0Z2 Uau2 levels1/2/3 = **0 /250000 /475000**; A0Z3 = **0 /500000 /850000**. Both Uau1=.25 at all3 levels, radius999999, friend/self mask. Nika's reset to1 removes this carrier's extra regeneration but preserves the baseline .25 movement field. The new Saturn ay6 announcement's '30만' plus baseline50만 does not match explicit level3 **850000**; report object truth separately from prose.
- R00S/R01W source calls above and reset to0 are proved. Their upgrade-object definitions were not among the inspected W3A/W3U files. Exact maximum-life deltas and Warcraft's retroactive response to research-level changes are not independently proved here; table deliberately retains raw research IDs/levels rather than quoting announcements as measured HP changes.

### What rF / Qy / hy actually affect, and restoration boundary

- **ZyF11115-11120** adds A04Q if needed and sets its ability level to the passed integer. A04Q W3A Def5[level] ranges .75 at1 to1.15 at41 (approximately .74+.01*level), named magic resistance; these are level-driven native ability data, not bare HP values.
- **FHy13266-13357** spawns Player6 units. At13311 it applies Qy[player] through ZyF and at13312-13313 applies hy[player] as **A11S level**; normal spawns repeat at13340-13342. **MPT17165-17182** also assigns Qy and hy at17179-17180. **C4F13237-13265** assigns Qy but sets A11S=1 (13259-13260), so do not claim every enemy uses hy uniformly.
- rF is similarly copied into newly created bosses: **DAy10773ff** creates Player5 n00A, then SetUnitAbilityLevel(dAy,A04Q,rF) at10778. Several other boss creators use the same source global.
- A11S is a marker named 폭발형데미지 증폭. Damage handlers actually read target A11S level, e.g. 9243 and10236 multiply by **.79+.01*GetUnitAbilityLevel(target,A11S)**. Restoring hy controls those assigned levels where readers use it; it does not directly multiply every attack in the map.
- **PROVED LIMIT:** MHy's restore contains **no enumeration/rewrite of existing enemies' A04Q/A11S levels**. Changing rF/Qy/hy therefore does not itself rewrite already-copied per-unit levels. It restores the values subsequently read by spawns and other consumers. Removal/reset of carrier auras and research is separately explicit. Actual aura refresh timing and existing-unit health after research reset need engine behavior or a runtime test, neither performed. This is a narrow runtime-effect limit, NOT an unresolved source connection.

### Edge conditions worth preserving in overlay assumptions

- sg is a player progression flag, not an owned-Nika inventory check. The scan has no freshness/liveness test. A later use of one of the two strengthening handlers can write that player's sg back to1; the report makes no claim that simultaneous multiple eligible heroes are reachable.
- At normal entry, sg is sampled once at state10. The no-Nika branch schedules effect application .1s later. A craft in that scheduled interval could see hA0, set sg2 without undoing anything, then the already-selected state13 applies effects without rescanning sg. This is a **source-level ordering window**, not a reproduced gameplay exploit or guaranteed observed bug. No unconditional instant suppression promise is justified at that boundary.
- Normal lower difficulty ay<5 goes straight to state14; no Elder suppression animation solely for having sg2 there. The late tA branch precedes the ay check but is set by the hA!=0 craft path, whose inspected assignments originate in the higher-difficulty Elder path.

### Old comparison and conclusion

Matched OLD **vTE76946-77081**, assigned JMG=vTE at84101 and registered to ty at87222-87224, fades and goes directly to effect application state11 when AN>=5. It contains no sg-equivalent Nika check, no Emet branch, and no hA active-state marker. OLD Nika nrE66259-66308 also contains no Elder reversal. NEW directly links successful Nika craft to suppression via sg2, and reversal via hA/tA/rA. **Attribution is closed and source-proved for these supplied versions.** The remaining limitations are inherited object defaults, research-object semantics, and untested engine propagation, all explicitly separated above.

## 7. Independent seven-formula regression fixture

Created **bullet-formula-regression-2314-2320.json** outside the app. Contains35 cases (7 formulas x input levels0..4), exact expression strings, raw ability dimension (attack A0WQ or speed A0WO), source function spans/hashes, ideal-decimal expected values, JavaScript float64 values, and an explicitly labelled per-operation binary32 rounding model. Both numerical models match the ideal references within documented floating tolerance. New level4 equals new level3 for all7. No game was run.

All values below are **PRE-DAMAGE-WRAPPER expression values**, not observed damage/HP loss. H=1,000,000 CURRENT target life for the two H-dependent examples. Arrays are input levels[0,1,2,3,4]. Level0 and old level4 are counterfactual expression boundaries, not proof those branches activate at those levels. New uses min(level,3); spell branch/target filters still apply. No integer rounding belongs in the formula implementation.

| Formula | OLD ideal-decimal array | NEW ideal-decimal array |
|---|---|---|
|Speed base/unique AoE|[30000,32500,35000,37500,40000]|[30000,55000,80000,105000,105000]|
|Speed legendary AoE|[50000,54500,59000,63500,68000]|[50000,95000,140000,185000,185000]|
|Speed extra target|[0,10000,20000,30000,40000]|[0,100000,200000,300000,300000]|
|Attack ordinary H-dependent|[0,2700,5400,8100,10800]|[270000,297000,324000,351000,351000]|
|Attack special H-dependent|[10000,15000,20000,25000,30000]|[260000,310000,360000,410000,410000]|
|Strike initial|[1500000,1575000,1650000,1725000,1800000]|[1500000,2250000,3000000,3750000,3750000]|
|Strike repeat, EACH of12|[100000,101000,102000,103000,104000]|[100000,110000,120000,130000,130000]|

Floating caution: decimal1.1 is not exactly binary floating point; e.g. JavaScript yields110000.00000000001 for one repeat case. The fixture preserves this instead of silently truncating. Binary32 values model rounding each literal/operation, but no claim is made that this is a measured bit-exact Warcraft VM implementation. OeT6498-6502 draws a real multiplier and then calls UnitDamageTarget; fixture stops before it. In these seven callbacks the passed range is1..1, but native armor/resistance, buffs, attack/damage type, later events, target eligibility, and current-life changes remain outside the fixture.
