# Gorosei / Saturn regeneration: 2.314 versus 2.320

## Scope and verdict

Static parsing only. No game/process access, memory reads, input, MemoryDiagnostics, production edits, or live-HP extrapolation. The existing application/profile was not used as evidence: the extracted JASS and fully parsed object tables are sufficient and authoritative for this bounded comparison. All line numbers are 1-based in the files below.

- New JASS: `modern-reader/members-ko-2.320/war3map.j`.
- Old JASS: `indexed-members/2314/war3map.j`.
- Objects: `object-diff/{2.314,2.320}.{w3u,w3a}.json` (actual directory name is `object-diff`, not `parsedobject-diff`).

**Confirmed:** the God common/Saturn mix-up is fixed in the aura object coefficients, without swapping JASS branch selectors. **Confirmed:** the new Saturn increment is 225,000 in God and 350,000 in Nightmare. **Important qualification:** 2.314 God actual common coefficient was 200,000, leaving an actual Saturn marginal coefficient of 250,000, although its announcement described common 250,000 and Saturn 200,000. Thus the patchnote 200,000 → 225,000 compares the intended/described Saturn contribution, not the old actual marginal coefficient.

## Actual object coefficient table

`Uau2` is the life-regeneration field on an ability derived from `AUau` (Unholy Aura). These are authored aura coefficients, interpreted as flat life regeneration per second by that native ability, not sampled HP outcomes. Level 3 replaces level 2 on the same ability: **do not add level 2 again**.

| Difficulty | Ability | Selection | 2.314 Uau2 | 2.320 Uau2 | Meaning |
|---|---|---|---:|---:|---|
| God (`AN/ay == 5`) | `A0Z2` | Level 1 | 0 | 0 | Initial, no activated Gorosei regen |
| God | `A0Z2` | Level 2 | 200,000 | 250,000 | Common effect, Warcury/Nasjuro |
| God | `A0Z2` | Level 3 | 450,000 | 475,000 | Combined common + Saturn |
| God | derived L3 minus L2 | marginal Saturn | 250,000 | 225,000 | Old actual differs from old announcement |
| Nightmare (`AN/ay == 6`) | `A0Z3` | Level 1 | 0 | 0 | Initial, no activated Gorosei regen |
| Nightmare | `A0Z3` | Level 2 | 500,000 | 500,000 | Common effect |
| Nightmare | `A0Z3` | Level 3 | 800,000 | 850,000 | Combined common + Saturn |
| Nightmare | derived L3 minus L2 | marginal Saturn | 300,000 | 350,000 | Matches patchnote increase |

This reconciles both God changes: the new common value is 250,000 and 475,000 − 250,000 = 225,000. Old L3 stayed consistent with the old described total (250,000 + 200,000 = 450,000), but old L2 was 200,000, proving the common-versus-Saturn allocation error. The observed God total change is only +25,000 in the authored L3 coefficient, while the common-only level changes +50,000. These arithmetic comparisons are not live HP predictions.

## Handler, target matching, period and scaling

1. **Carrier is not Saturn.** New JASS L56212 and old L66736 create `e003` (`$65303033`) for `Player(6)` at (-1700,1600); its name is `라인몹이속더미`. New `bg` is old `lw`. God adds `A0Z2` (`$41305a32`); Nightmare adds `A0Z3` (`$41305a33`).
2. **Saturn identity and branch.** `o031` (`$6f303331`) resolves to 제이가르시아 새턴, while `o02E` is 워큐리 and `o032` is 나스쥬로. Their `uabi` is `A13H,A0IQ,Avul` and `uhpr` is zero in both versions. They select the effect; this is not an edit to Saturn's own `uhpr`. The separate `A13G` (`$41313347`, `[오로성]새턴`, base `ACac`) is an attack-reduction aura: `Cac1` is approximately -0.2/-0.3. It is not the regeneration coefficient.
3. **Application handler.** Old `vTE` (L76946–77080 region) enters for `AN>=5`, fades at one-shot .03/.1 delays, then applies effects in stage 11. New `vYb` (L76072–76255) handles `tA` first (15-second route), otherwise `ay>=5`, fades, checks `sg[0..3]==2` for the Emet suppression route, and applies the effects only at stage 13. Warcury/Nasjuro choose level 2; Saturn chooses level 3. God/Nightmare are distinguished by equality to 5/6 and confirmed by the adjacent Korean difficulty announcements.
4. **No custom heal tick in this path.** These handlers call `UnitAddAbility` / `SetUnitAbilityLevel`, not a repeated life addition. The .03/.1/4/15 values here are one-shot state-machine delays: new `TYT` L6528–6535 and old `P1P` L4830–4837 use `TimerStart(...,false,...)`. They are not regeneration periods. `ndF` advances the stage by one. Therefore do not divide or multiply Uau2 by .03/.1, and do not infer a 10 Hz or 1 Hz scripted healing loop.
5. **Scaling once.** `Uau1=0.25` is the separate movement-speed field and must not scale `Uau2`. No max-life, hero-stat, difficulty multiplier, or frame multiplier is applied to these regeneration values by the inspected selector handler. The target mask is `air,invulnerable,self,ground,vulnerable,friend`, aura area 999999, and buff `B02F`. This is a native aura on Player(6)'s dummy; do not replace that mask with an invented global script enumeration or assume every unit's final effective healing.
6. **Native-engine boundary.** The supplied custom records do not override `Uau3` and contain no explicit `adur`, `ahdu`, or `acdn` fields. Base-game AUau metadata and engine tick cadence were not extracted in this audit. Exact internal aura refresh/healing interval is therefore **not established by these files**; the per-second interpretation is the native regeneration field semantics, consistent with the supplied patchnotes, not a proven custom timer. No stack interactions, caps, healing reductions, or live-HP outcome are claimed.
7. **Reset in 2.320.** L23042–23056 removes special Gorosei attack/stat auras, resets God/Nightmare regeneration ability to level 1 (zero Uau2), resets associated research, sets `hA=0`, `tA=true`, and reexecutes the handler. Thus the coefficient is conditional on the currently applied effect, not an unconditional difficulty-only bonus.

## Text discrepancy

Old God announcement L77018 says Saturn 20만 and L77019 common 25만, but old A0Z2 L2 is 200,000. New God announcement L76192 correctly says 22만 5000 and L76193 common 25만. **New Nightmare announcement L76200 still says 30만**, unchanged from old L77026, despite A0Z3 L3 changing to 850,000 and common L2 remaining 500,000. Runtime object coefficients, not that stale announcement, support 350,000.

## Exact JASS evidence

### 2.320 initialization

```jass
56211: set kx=CreateUnit(Player(5),$6f30324e,GetRectCenterX(VD),GetRectCenterY(VD),315.)
56212: set bg=CreateUnit(Player(6),$65303033,-1700.,1600.,bj_UNIT_FACING)
56213: if ay==4 then
56214: call UnitAddAbility(bg,$41305a31)
56215: elseif ay==5 then
56216: call UnitAddAbility(bg,$41305a32)
56217: set UUy=ptT(1,3)
56218: if UUy==1 then
56219: set OA=CreateUnit(Player(7),$6f303245,GetRectCenterX(ZD),GetRectCenterY(ZD),bj_UNIT_FACING)
56220: elseif UUy==2 then
56221: set OA=CreateUnit(Player(7),$6f303331,GetRectCenterX(ZD),GetRectCenterY(ZD),bj_UNIT_FACING)
56222: else
56223: set OA=CreateUnit(Player(7),$6f303332,GetRectCenterX(ZD),GetRectCenterY(ZD)-200.,bj_UNIT_FACING)
56224: endif
56225: if ay==6 then
56226: call SetUnitAbilityLevel(OA,$41313348,2)
56227: endif
56228: set yEy=OA
56229: set FEy=PercentToInt(0.,$ff)
56230: set gEy=PercentToInt(0.,$ff)
56231: call SetUnitVertexColor(yEy,FEy,gEy,PercentToInt(0.,$ff),$ff)
56232: set QA=CreateTextTag()
56233: call SetTextTagText(QA,"오로성은 신세계 이후에 등장합니다.",.023)
56234: call SetTextTagPos(QA,GetUnitX(OA),GetUnitY(OA),20.)
56235: call SetTextTagColor(QA,$8b,$bd,$ff,$ff)
56236: call SetTextTagPermanent(QA,true)
56237: call SetTextTagVisibility(QA,true)
56238: elseif ay==6 then
56239: call UnitAddAbility(bg,$41305a33)
56240: set EUy=ptT(1,3)
56241: if EUy==1 then
56242: set OA=CreateUnit(Player(7),$6f303245,GetRectCenterX(ZD),GetRectCenterY(ZD),bj_UNIT_FACING)
56243: elseif EUy==2 then
56244: set OA=CreateUnit(Player(7),$6f303331,GetRectCenterX(ZD),GetRectCenterY(ZD),bj_UNIT_FACING)
56245: else
56246: set OA=CreateUnit(Player(7),$6f303332,GetRectCenterX(ZD),GetRectCenterY(ZD)-200.,bj_UNIT_FACING)
56247: endif
56248: if ay==6 then
56249: call SetUnitAbilityLevel(OA,$41313348,2)
```

### 2.314 initialization

```jass
66735: set Jh=CreateUnit(Player(5),$6f30324e,GetRectCenterX(Jc),GetRectCenterY(Jc),315.)
66736: set lw=CreateUnit(Player(6),$65303033,-1700.,1600.,bj_UNIT_FACING)
66737: if AN==4 then
66738: call UnitAddAbility(lw,$41305a31)
66739: elseif AN==5 then
66740: call UnitAddAbility(lw,$41305a32)
66741: call ZbN()
66742: elseif AN==6 then
66743: call UnitAddAbility(lw,$41305a33)
66744: call ZbN()
66745: endif
```

### 2.314 entry and shared God selector

```jass
76946: function vTE takes nothing returns nothing
76947: local integer lTE
76948: local integer KTE
76949: local player ITE
76950: local player LTE
76951: local player VTE
76952: local player ZTE
76953: local player eTE
76954: local player DTE
76955: local player kTE
76956: local player OTE
76957: local player QTE
76958: local player nTE
76959: local player YTE
76960: local player iTE
76961: local integer ATE
76962: call TyG(0)
76963: if jF[sP]==0 then
76964: if AN>=5 then
76965: call SgN(sP,0,Fy)
76966: call DestroyTextTag(py)
76967: set lTE=sP
76968: call P1P(lTE,.03,jF[lTE]+1)
76969: else
76970: call P1P(sP,.03,12)
76971: endif
76972: elseif jF[sP]<=10 and jF[sP]>0 then
76973: call SetUnitVertexColor(LoadUnitHandle(gp,0,sP*$1f4),25*R2I(jF[sP]),25*R2I(jF[sP]),25*R2I(jF[sP]),$ff)
76974: set KTE=sP
76975: call P1P(KTE,.1,jF[KTE]+1)
76976: elseif jF[sP]==11 then
76977: if $6f303245==GetUnitTypeId(LoadUnitHandle(gp,0,sP*$1f4)) then
76978: call StartSound(Qbw("SE\\Saint_Warcury.mp3",null,$7f,1.,false,0.,0.))
76979: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|CFFC15AF4워큐리 : |r|cffdc143c악연으로 얽힌 자들이여, 그 녀석이 누구인지 알고 있는건가?|r")
76980: set Uw=Uw-10
76981: set dE[0]=dE[0]-10
76982: set dE[1]=dE[1]-10
76983: set dE[2]=dE[2]-10
76984: set dE[3]=dE[3]-10
76985: call UnitAddAbility(lw,$41313344)
76986: if AN==5 then
76987: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |CFFC15AF4워큐리 성|r 의 효과가 적용됩니다.\n모든 적 유닛들의 방어력이 10 증가합니다.\n모든 적 유닛들의 마법방어력이 10% 증가합니다.\n보스 몬스터의 체력이 1000만 증가합니다.")
76988: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |cffffd700신|r 추가 효과가 적용됩니다. 모든 적 유닛들의 체력이 500만, 체력재생이 25만 증가합니다.")
76989: set ITE=Player(6)
76990: call SetPlayerTechResearched(ITE,$52303053,5)
76991: set LTE=Player(6)
76992: call SetPlayerTechResearched(LTE,$52303157,15)
76993: call SetUnitAbilityLevel(lw,$41305a32,2)
76994: elseif AN==6 then
76995: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |CFFC15AF4워큐리 성|r 의 효과가 적용됩니다.\n모든 적 유닛들의 방어력이 15 증가합니다.\n모든 적 유닛들의 마법방어력이 15% 증가합니다.\n보스 몬스터의 체력이 1500만 증가합니다.")
76996: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |c00cc3337악몽|r 추가 효과가 적용됩니다. 모든 적 유닛들의 체력이 1000만, 체력재생이 50만 증가합니다.")
76997: set Uw=Uw-5
76998: set dE[0]=dE[0]-5
76999: set dE[1]=dE[1]-5
77000: set dE[2]=dE[2]-5
77001: set dE[3]=dE[3]-5
77002: call SetUnitAbilityLevel(lw,$41313344,2)
77003: call SetUnitAbilityLevel(lw,$41305a33,2)
77004: set VTE=Player(6)
77005: call SetPlayerTechResearched(VTE,$52303053,10)
77006: set ZTE=Player(6)
77007: call SetPlayerTechResearched(ZTE,$52303157,25)
77008: endif
```

### 2.314 Saturn selector

```jass
77009: elseif $6f303331==GetUnitTypeId(LoadUnitHandle(gp,0,sP*$1f4)) then
77010: call StartSound(Qbw("SE\\Saint_Saturn.mp3",null,$7f,1.,false,0.,0.))
77011: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|CFFC15AF4새턴 : |r|cffdc143c우리 세계정부에 거역한 것을 후회하며 죽어주길 바란다.|r")
77012: set gE[0]=gE[0]-7
77013: set gE[1]=gE[1]-7
77014: set gE[2]=gE[2]-7
77015: set gE[3]=gE[3]-7
77016: call UnitAddAbility(lw,$41313347)
77017: if AN==5 then
77018: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |CFFC15AF4새턴 성|r 의 효과가 적용됩니다.\n모든 아군 유닛들의 공격력이 20% 감소합니다.\n모든 아군 유닛들의 폭발형 데미지가 7% 감소합니다.\n모든 적 유닛들의 체력재생이 20만 증가합니다.")
77019: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |cffffd700신|r 추가 효과가 적용됩니다. 모든 적 유닛들의 체력이 500만, 체력재생이 25만 증가합니다.")
77020: call SetUnitAbilityLevel(lw,$41305a32,3)
77021: set eTE=Player(6)
77022: call SetPlayerTechResearched(eTE,$52303053,5)
77023: set DTE=Player(6)
77024: call SetPlayerTechResearched(DTE,$52303157,5)
77025: elseif AN==6 then
77026: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |CFFC15AF4새턴 성|r 의 효과가 적용됩니다.\n모든 아군 유닛들의 공격력이 30% 감소합니다.\n모든 아군 유닛들의 폭발형 데미지가 10% 감소합니다.\n모든 적 유닛들의 체력재생이 30만 증가합니다.")
77027: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |c00cc3337악몽|r 추가 효과가 적용됩니다. 모든 적 유닛들의 체력이 1000만, 체력재생이 50만 증가합니다.")
77028: set gE[0]=gE[0]-3
77029: set gE[1]=gE[1]-3
77030: set gE[2]=gE[2]-3
77031: set gE[3]=gE[3]-3
77032: call SetUnitAbilityLevel(lw,$41313347,2)
77033: call SetUnitAbilityLevel(lw,$41305a33,3)
77034: set kTE=Player(6)
77035: call SetPlayerTechResearched(kTE,$52303053,10)
77036: set OTE=Player(6)
77037: call SetPlayerTechResearched(OTE,$52303157,10)
```

### 2.314 Nasjuro selector

```jass
77038: endif
77039: elseif $6f303332==GetUnitTypeId(LoadUnitHandle(gp,0,sP*$1f4)) then
77040: call StartSound(Qbw("SE\\Saint_Nasjuro.mp3",null,$7f,1.,false,0.,0.))
77041: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|CFFC15AF4나스쥬로 : |r|cffdc143c언젠가 그 힘을 자유자재로 쓰게 될 것이다.|r")
77042: call UnitAddAbility(lw,$41313345)
77043: call UnitAddAbility(lw,$41313346)
77044: if AN==5 then
77045: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |CFFC15AF4나스쥬로 성|r 의 효과가 적용됩니다.\n모든 적 유닛들의 이동속도가 10% 증가합니다.\n모든 아군 유닛들의 공격속도가 10% 감소합니다.\n라인 몬스터의 체력이 1000만 증가합니다.")
77046: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |cffffd700신|r 추가 효과가 적용됩니다. 모든 적 유닛들의 체력이 500만, 체력재생이 25만 증가합니다.")
77047: set QTE=Player(6)
77048: call SetPlayerTechResearched(QTE,$52303053,15)
77049: set nTE=Player(6)
77050: call SetPlayerTechResearched(nTE,$52303157,5)
77051: call SetUnitAbilityLevel(lw,$41305a32,2)
77052: elseif AN==6 then
77053: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |CFFC15AF4나스쥬로 성|r 의 효과가 적용됩니다.\n모든 적 유닛들의 이동속도가 15% 증가합니다.\n모든 아군 유닛들의 공격속도가 15% 감소합니다.\n라인 몬스터의 체력이 1500만 증가합니다.")
77054: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |c00cc3337악몽|r 추가 효과가 적용됩니다. 모든 적 유닛들의 체력이 1000만, 체력재생이 50만 증가합니다.")
77055: call SetUnitAbilityLevel(lw,$41313345,2)
77056: call SetUnitAbilityLevel(lw,$41313346,2)
77057: call SetUnitAbilityLevel(lw,$41305a33,2)
77058: set YTE=Player(6)
77059: call SetPlayerTechResearched(YTE,$52303053,25)
77060: set iTE=Player(6)
77061: call SetPlayerTechResearched(iTE,$52303157,10)
77062: endif
77063: endif
77064: set ATE=sP
77065: call P1P(ATE,.03,jF[ATE]+1)
```

### 2.320 entry, timing and Emet suppression

```jass
76072: function vYb takes nothing returns nothing
76073: local boolean PYb=false
76074: local integer aYb
76075: local player sYb
76076: local player nYb
76077: local player NYb
76078: local player iYb
76079: local player qYb
76080: local player KYb
76081: local player ZYb
76082: local player fYb
76083: local player BYb
76084: local player RYb
76085: local player xYb
76086: local player MYb
76087: call jNy(0)
76088: if mv[Yg]==0 then
76089: if tA then
76090: set tA=false
76091: call hFF(Yg,0,OA)
76092: set OA=null
76093: call DestroyTextTag(QA)
76094: set QA=null
76095: call TYT(Yg,15.,11)
76096: elseif ay>=5 then
76097: call hFF(Yg,0,OA)
76098: call DestroyTextTag(QA)
76099: call ndF(Yg,.03)
76100: else
76101: call TYT(Yg,.03,14)
76102: endif
76103: elseif mv[Yg]<10 and mv[Yg]>0 then
76104: call SetUnitVertexColor(LoadUnitHandle(UO,0,Yg*$1f4),25*R2I(mv[Yg]),25*R2I(mv[Yg]),25*R2I(mv[Yg]),$ff)
76105: call ndF(Yg,.1)
76106: elseif mv[Yg]==10 then
76107: call SetUnitVertexColor(LoadUnitHandle(UO,0,Yg*$1f4),25*R2I(mv[Yg]),25*R2I(mv[Yg]),25*R2I(mv[Yg]),$ff)
76108: set aYb=0
76109: loop
76110: exitwhen aYb>=4
76111: if sg[aYb]==2 then
76112: set PYb=true
76113: endif
76114: set aYb=aYb+1
76115: endloop
76116: if PYb then
76117: call TYT(Yg,.1,11)
76118: else
76119: call TYT(Yg,.1,13)
76120: endif
76121: elseif mv[Yg]==11 then
76122: call StartSound(tyy("SE\\Emet.mp3",null,$78,1.,false,0.,0.))
76123: call lAF(Yg,0,GetUnitLoc(LoadUnitHandle(UO,0,Yg*$1f4)))
76124: call hFF(Yg,10,CreateUnitAtLoc(Vy,$6530414b,LoadLocationHandle(lO,0,Yg*$1f4),bj_UNIT_FACING))
76125: call SetUnitScale(LoadUnitHandle(UO,0,Yg*$1f4+10),5.,1.,1.)
76126: call UnitApplyTimedLife(LoadUnitHandle(UO,0,Yg*$1f4+10),$42487765,3.)
76127: call hFF(Yg,10,CreateUnitAtLoc(Vy,$65303858,LoadLocationHandle(lO,0,Yg*$1f4),bj_UNIT_FACING))
76128: call SetUnitVertexColor(LoadUnitHandle(UO,0,Yg*$1f4+10),$ff,$7d,$7d,$ff)
76129: call SetUnitScale(LoadUnitHandle(UO,0,Yg*$1f4+10),5.,1.,1.)
76130: call UnitApplyTimedLife(LoadUnitHandle(UO,0,Yg*$1f4+10),$42487765,3.)
76131: call SetUnitFlyHeight(LoadUnitHandle(UO,0,Yg*$1f4+10),30.,0.)
76132: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|CFFFFFA78에메트 : |r|CFFC15AF4너를... 죽게 놔두지 않아!|r")
76133: call TYT(Yg,4.,12)
76134: elseif mv[Yg]==12 then
76135: call hFF(Yg,10,CreateUnitAtLoc(Vy,$65304146,LoadLocationHandle(lO,0,Yg*$1f4),bj_UNIT_FACING))
76136: call hFF(Yg,10,CreateUnitAtLoc(Vy,$65303131,LoadLocationHandle(lO,0,Yg*$1f4),bj_UNIT_FACING))
76137: call SetUnitVertexColor(LoadUnitHandle(UO,0,Yg*$1f4+10),$ff,0,0,$ff)
76138: call SetUnitScale(LoadUnitHandle(UO,0,Yg*$1f4+10),4.,1.,1.)
76139: call UnitApplyTimedLife(LoadUnitHandle(UO,0,Yg*$1f4+10),$42487765,4.)
76140: call SetUnitTimeScale(LoadUnitHandle(UO,0,Yg*$1f4+10),.4)
76141: call hFF(Yg,10,CreateUnitAtLoc(Vy,$65304149,LoadLocationHandle(lO,0,Yg*$1f4),bj_UNIT_FACING))
76142: call SetUnitTimeScale(LoadUnitHandle(UO,0,Yg*$1f4+10),.4)
76143: call hFF(Yg,10,CreateUnitAtLoc(Vy,$6530414a,LoadLocationHandle(lO,0,Yg*$1f4),bj_UNIT_FACING))
76144: call SetUnitTimeScale(LoadUnitHandle(UO,0,Yg*$1f4+10),.4)
76145: call Yhb(LoadUnitHandle(UO,0,Yg*$1f4))
76146: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |CFFFFFA78에메트|r가 조이보이의 최대 패기를 방출하여 오로성을 퇴치합니다.\n|cffdc143c◈|r 오로성 효과가 적용되지 않습니다.")
76147: call TYT(Yg,.10,14)
76148: elseif mv[Yg]==13 then
```

### 2.320 common/Saturn/Nasjuro selectors

```jass
76149: if $6f303245==GetUnitTypeId(LoadUnitHandle(UO,0,Yg*$1f4)) then
76150: set hA=1
76151: call StartSound(tyy("SE\\Saint_Warcury.mp3",null,$7f,1.,false,0.,0.))
76152: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|CFFC15AF4워큐리 : |r|cffdc143c악연으로 얽힌 자들이여, 그 녀석이 누구인지 알고 있는건가?|r")
76153: set rF=rF-10
76154: set Qy[0]=Qy[0]-10
76155: set Qy[1]=Qy[1]-10
76156: set Qy[2]=Qy[2]-10
76157: set Qy[3]=Qy[3]-10
76158: call UnitAddAbility(bg,$41313344)
76159: if ay==5 then
76160: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |CFFC15AF4워큐리 성|r 의 효과가 적용됩니다.\n모든 적 유닛들의 방어력이 10 증가합니다.\n모든 적 유닛들의 마법방어력이 10% 증가합니다.\n보스 몬스터의 체력이 1000만 증가합니다.")
76161: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |cffffd700신|r 추가 효과가 적용됩니다. 모든 적 유닛들의 체력이 500만, 체력재생이 25만 증가합니다.")
76162: set sYb=Player(6)
76163: call SetPlayerTechResearched(sYb,$52303053,5)
76164: set nYb=Player(6)
76165: call SetPlayerTechResearched(nYb,$52303157,15)
76166: call SetUnitAbilityLevel(bg,$41305a32,2)
76167: elseif ay==6 then
76168: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |CFFC15AF4워큐리 성|r 의 효과가 적용됩니다.\n모든 적 유닛들의 방어력이 15 증가합니다.\n모든 적 유닛들의 마법방어력이 15% 증가합니다.\n보스 몬스터의 체력이 1500만 증가합니다.")
76169: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |c00cc3337악몽|r 추가 효과가 적용됩니다. 모든 적 유닛들의 체력이 1000만, 체력재생이 50만 증가합니다.")
76170: set rF=rF-5
76171: set Qy[0]=Qy[0]-5
76172: set Qy[1]=Qy[1]-5
76173: set Qy[2]=Qy[2]-5
76174: set Qy[3]=Qy[3]-5
76175: call SetUnitAbilityLevel(bg,$41313344,2)
76176: call SetUnitAbilityLevel(bg,$41305a33,2)
76177: set NYb=Player(6)
76178: call SetPlayerTechResearched(NYb,$52303053,10)
76179: set iYb=Player(6)
76180: call SetPlayerTechResearched(iYb,$52303157,25)
76181: endif
76182: elseif $6f303331==GetUnitTypeId(LoadUnitHandle(UO,0,Yg*$1f4)) then
76183: set hA=2
76184: call StartSound(tyy("SE\\Saint_Saturn.mp3",null,$7f,1.,false,0.,0.))
76185: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|CFFC15AF4새턴 : |r|cffdc143c우리 세계정부에 거역한 것을 후회하며 죽어주길 바란다.|r")
76186: set hy[0]=hy[0]-7
76187: set hy[1]=hy[1]-7
76188: set hy[2]=hy[2]-7
76189: set hy[3]=hy[3]-7
76190: call UnitAddAbility(bg,$41313347)
76191: if ay==5 then
76192: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |CFFC15AF4새턴 성|r 의 효과가 적용됩니다.\n모든 아군 유닛들의 공격력이 20% 감소합니다.\n모든 아군 유닛들의 폭발형 데미지가 7% 감소합니다.\n모든 적 유닛들의 체력재생이 22만 5000 증가합니다.")
76193: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |cffffd700신|r 추가 효과가 적용됩니다. 모든 적 유닛들의 체력이 500만, 체력재생이 25만 증가합니다.")
76194: call SetUnitAbilityLevel(bg,$41305a32,3)
76195: set qYb=Player(6)
76196: call SetPlayerTechResearched(qYb,$52303053,5)
76197: set KYb=Player(6)
76198: call SetPlayerTechResearched(KYb,$52303157,5)
76199: elseif ay==6 then
76200: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |CFFC15AF4새턴 성|r 의 효과가 적용됩니다.\n모든 아군 유닛들의 공격력이 30% 감소합니다.\n모든 아군 유닛들의 폭발형 데미지가 10% 감소합니다.\n모든 적 유닛들의 체력재생이 30만 증가합니다.")
76201: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |c00cc3337악몽|r 추가 효과가 적용됩니다. 모든 적 유닛들의 체력이 1000만, 체력재생이 50만 증가합니다.")
76202: set hy[0]=hy[0]-3
76203: set hy[1]=hy[1]-3
76204: set hy[2]=hy[2]-3
76205: set hy[3]=hy[3]-3
76206: call SetUnitAbilityLevel(bg,$41313347,2)
76207: call SetUnitAbilityLevel(bg,$41305a33,3)
76208: set ZYb=Player(6)
76209: call SetPlayerTechResearched(ZYb,$52303053,10)
76210: set fYb=Player(6)
76211: call SetPlayerTechResearched(fYb,$52303157,10)
76212: endif
76213: elseif $6f303332==GetUnitTypeId(LoadUnitHandle(UO,0,Yg*$1f4)) then
76214: set hA=3
76215: call StartSound(tyy("SE\\Saint_Nasjuro.mp3",null,$7f,1.,false,0.,0.))
76216: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|CFFC15AF4나스쥬로 : |r|cffdc143c언젠가 그 힘을 자유자재로 쓰게 될 것이다.|r")
76217: call UnitAddAbility(bg,$41313345)
76218: call UnitAddAbility(bg,$41313346)
76219: if ay==5 then
76220: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |CFFC15AF4나스쥬로 성|r 의 효과가 적용됩니다.\n모든 적 유닛들의 이동속도가 10% 증가합니다.\n모든 아군 유닛들의 공격속도가 10% 감소합니다.\n라인 몬스터의 체력이 1000만 증가합니다.")
76221: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |cffffd700신|r 추가 효과가 적용됩니다. 모든 적 유닛들의 체력이 500만, 체력재생이 25만 증가합니다.")
76222: set BYb=Player(6)
76223: call SetPlayerTechResearched(BYb,$52303053,15)
76224: set RYb=Player(6)
76225: call SetPlayerTechResearched(RYb,$52303157,5)
76226: call SetUnitAbilityLevel(bg,$41305a32,2)
76227: elseif ay==6 then
76228: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |CFFC15AF4나스쥬로 성|r 의 효과가 적용됩니다.\n모든 적 유닛들의 이동속도가 15% 증가합니다.\n모든 아군 유닛들의 공격속도가 15% 감소합니다.\n라인 몬스터의 체력이 1500만 증가합니다.")
76229: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cffF15F5F[시스템]|r |c00cc3337악몽|r 추가 효과가 적용됩니다. 모든 적 유닛들의 체력이 1000만, 체력재생이 50만 증가합니다.")
76230: call SetUnitAbilityLevel(bg,$41313345,2)
76231: call SetUnitAbilityLevel(bg,$41313346,2)
76232: call SetUnitAbilityLevel(bg,$41305a33,2)
76233: set xYb=Player(6)
76234: call SetPlayerTechResearched(xYb,$52303053,25)
76235: set MYb=Player(6)
76236: call SetPlayerTechResearched(MYb,$52303157,10)
76237: endif
76238: endif
76239: call ndF(Yg,.03)
76240: elseif mv[Yg]==14 then
76241: call ewb(Yg)
76242: endif
```

### 2.320 one-shot scheduler

```jass
6528: function TYT takes integer CwT,real SwT,integer bYT returns nothing
6529: if Gv[CwT]==null then
6530: return
6531: endif
6532: set Vv[CwT]=bYT
6533: set Xv[CwT]=false
6534: call TimerStart(Gv[CwT],SwT,false,UH)
6535: endfunction
```

### 2.320 next-stage helper

```jass
6787: function ndF takes integer PdF,real sdF returns nothing
6788: call TYT(PdF,sdF,mv[PdF]+1)
6789: endfunction
```

### 2.314 one-shot scheduler

```jass
4830: function P1P takes integer N1P,real E1P,integer w1P returns nothing
4831: if fF[N1P]==null then
4832: return
4833: endif
4834: set WF[N1P]=w1P
4835: set xF[N1P]=false
4836: call TimerStart(fF[N1P],E1P,false,Yf)
4837: endfunction
```

### 2.320 reset

```jass
23042: if hA!=0 then
23043: call UnitRemoveAbility(bg,$41313344)
23044: call UnitRemoveAbility(bg,$41313345)
23045: call UnitRemoveAbility(bg,$41313346)
23046: call UnitRemoveAbility(bg,$41313347)
23047: if ay==5 then
23048: call SetUnitAbilityLevel(bg,$41305a32,1)
23049: elseif ay==6 then
23050: call SetUnitAbilityLevel(bg,$41305a33,1)
23051: endif
23052: call SetPlayerTechResearched(Player(6),$52303053,0)
23053: call SetPlayerTechResearched(Player(6),$52303157,0)
23054: set hA=0
23055: set tA=true
23056: call TriggerExecute(rA)
23057: endif
```

### 2.320 story invocation

```jass
77142: if ig==null then
77143: set ig=CreateTimer()
77144: set qg=CreateTimerDialog(ig)
77145: endif
77146: set Kg=32
77147: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cff04ffc4[알림]|r 70초 뒤 미지의 바다인 |cffFF8200신세계|r로 출항합니다. |cffFF8200신세계|r는 라운드 타이머가 더욱 빨라집니다.")
77148: if ay!=6 then
77149: call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10.,"|cff04ffc4[알림]|r 난이도에 따른 전설위습 |cffFF0000"+I2S(ny)+"개|r가 지급됩니다.")
77150: endif
77151: call TimerDialogSetTitle(qg,"|cffFF0000신세계 시작까지|r")
77152: call TimerDialogDisplay(qg,true)
77153: call TimerStart(ig,70.,false,Q8)
77154: call TriggerExecute(rA)
```

### Trigger wiring

```jass
338: trigger rA=null
2047: code ej=null
4111: set rA=CreateTrigger()
4112: call mdy(rA)
4113: call TriggerAddAction(rA,ej)
23056: call TriggerExecute(rA)
76072: function vYb takes nothing returns nothing
77154: call TriggerExecute(rA)
80653: set ej=function vYb
```

Old function registration: `indexed-members/2314/war3map.j` L84101: `set JMG=function vTE`.

## Exact matching JSON evidence

Each row identifies the original JSON source, 1-based field-block line range, JSON pointer (zero-based indexes), binary field/value offsets, and decoded value. These are parsed source records, not values copied from the application.

### 2.314.w3u.json

EOF validated: `true`; parsed end `857425` equals byte length `857425`.

| Object (base) | Field / level | Value | JSON lines | JSON pointer | Field / value byte offset |
|---|---|---|---|---|---|
| o032 (otbk) | uabi / null | A13H,A0IQ,Avul | 3389–3402 | `/tables/1/objects/14/fields/0` | 3370 / 3378 |
| o032 (otbk) | uhpm / null | 10 | 3629–3642 | `/tables/1/objects/14/fields/15` | 3691 / 3699 |
| o032 (otbk) | unam / null | \|cffc15af4에단바론 V. 나스쥬로\|r - \|cffdc143c오로성\|r | 3693–3715 | `/tables/1/objects/14/fields/19` | 3755 / 3763 |
| o032 (otbk) | uhpr / null | 0 | 3750–3763 | `/tables/1/objects/14/fields/22` | 3811 / 3819 |
| o031 (otbk) | uabi / null | A13H,A0IQ,Avul | 3992–4005 | `/tables/1/objects/15/fields/0` | 4025 / 4033 |
| o031 (otbk) | uhpm / null | 10 | 4232–4245 | `/tables/1/objects/15/fields/15` | 4345 / 4353 |
| o031 (otbk) | unam / null | \|cffc15af4제이가르시아 새턴\|r - \|cffdc143c오로성\|r | 4296–4318 | `/tables/1/objects/15/fields/19` | 4409 / 4417 |
| o031 (otbk) | uhpr / null | 0 | 4353–4366 | `/tables/1/objects/15/fields/22` | 4465 / 4473 |
| o02E (otbk) | uabi / null | A13H,A0IQ,Avul | 12790–12803 | `/tables/1/objects/29/fields/0` | 14055 / 14063 |
| o02E (otbk) | uhpm / null | 10 | 13030–13043 | `/tables/1/objects/29/fields/15` | 14376 / 14384 |
| o02E (otbk) | unam / null | \|cffc15af4토프먼 워큐리\|r - \|cffdc143c오로성\|r | 13094–13116 | `/tables/1/objects/29/fields/19` | 14440 / 14448 |
| o02E (otbk) | uhpr / null | 0 | 13151–13164 | `/tables/1/objects/29/fields/22` | 14496 / 14504 |
| e003 (ewsp) | uabi / null | Avul | 753934–753947 | `/tables/1/objects/1331/fields/0` | 789420 / 789428 |
| e003 (ewsp) | uhpm / null | 15 | 754158–754171 | `/tables/1/objects/1331/fields/14` | 789643 / 789651 |
| e003 (ewsp) | unam / null | 라인몹이속더미 | 754222–754244 | `/tables/1/objects/1331/fields/18` | 789707 / 789715 |
| e003 (ewsp) | uhpr / null | 0 | 754295–754308 | `/tables/1/objects/1331/fields/22` | 789783 / 789791 |
### 2.314.w3a.json

EOF validated: `true`; parsed end `681032` equals byte length `681032`.

| Object (base) | Field / level | Value | JSON lines | JSON pointer | Field / value byte offset |
|---|---|---|---|---|---|
| A13G (ACac) | aare / 1 | 99999 | 102441–102454 | `/tables/1/objects/277/fields/0` | 121022 / 121038 |
| A13G (ACac) | aare / 2 | 99999 | 102457–102470 | `/tables/1/objects/277/fields/1` | 121046 / 121062 |
| A13G (ACac) | abuf / 1 | B07X | 102489–102502 | `/tables/1/objects/277/fields/3` | 121141 / 121157 |
| A13G (ACac) | abuf / 2 | B07X | 102505–102518 | `/tables/1/objects/277/fields/4` | 121166 / 121182 |
| A13G (ACac) | Cac1 / 1 | -0.20000000298023224 | 102553–102566 | `/tables/1/objects/277/fields/7` | 121239 / 121255 |
| A13G (ACac) | Cac1 / 2 | -0.30000001192092896 | 102569–102582 | `/tables/1/objects/277/fields/8` | 121263 / 121279 |
| A13G (ACac) | alev / 0 | 2 | 102617–102630 | `/tables/1/objects/277/fields/11` | 121335 / 121351 |
| A13G (ACac) | anam / 0 | [오로성]새턴 | 102633–102655 | `/tables/1/objects/277/fields/12` | 121359 / 121375 |
| A13G (ACac) | atar / 1 | air,invulnerable,enemies,ground | 102674–102687 | `/tables/1/objects/277/fields/14` | 121413 / 121429 |
| A13G (ACac) | atar / 2 | air,invulnerable,enemies,ground | 102690–102703 | `/tables/1/objects/277/fields/15` | 121465 / 121481 |
| A0Z3 (AUau) | aare / 1 | 999999 | 150023–150036 | `/tables/1/objects/420/fields/0` | 178777 / 178793 |
| A0Z3 (AUau) | aare / 2 | 999999 | 150039–150052 | `/tables/1/objects/420/fields/1` | 178801 / 178817 |
| A0Z3 (AUau) | aare / 3 | 999999 | 150055–150068 | `/tables/1/objects/420/fields/2` | 178825 / 178841 |
| A0Z3 (AUau) | abuf / 1 | B02F | 150071–150084 | `/tables/1/objects/420/fields/3` | 178849 / 178865 |
| A0Z3 (AUau) | abuf / 2 | B02F | 150087–150100 | `/tables/1/objects/420/fields/4` | 178874 / 178890 |
| A0Z3 (AUau) | abuf / 3 | B02F | 150103–150116 | `/tables/1/objects/420/fields/5` | 178899 / 178915 |
| A0Z3 (AUau) | Uau1 / 1 | 0.25 | 150119–150132 | `/tables/1/objects/420/fields/6` | 178924 / 178940 |
| A0Z3 (AUau) | Uau1 / 2 | 0.25 | 150135–150148 | `/tables/1/objects/420/fields/7` | 178948 / 178964 |
| A0Z3 (AUau) | Uau1 / 3 | 0.25 | 150151–150164 | `/tables/1/objects/420/fields/8` | 178972 / 178988 |
| A0Z3 (AUau) | Uau2 / 1 | 0 | 150167–150180 | `/tables/1/objects/420/fields/9` | 178996 / 179012 |
| A0Z3 (AUau) | Uau2 / 2 | 500000 | 150183–150196 | `/tables/1/objects/420/fields/10` | 179020 / 179036 |
| A0Z3 (AUau) | Uau2 / 3 | 800000 | 150199–150212 | `/tables/1/objects/420/fields/11` | 179044 / 179060 |
| A0Z3 (AUau) | anam / 0 | 모드 이동속도 증가 | 150231–150253 | `/tables/1/objects/420/fields/13` | 179092 / 179108 |
| A0Z3 (AUau) | atar / 1 | air,invulnerable,self,ground,vulnerable,friend | 150304–150317 | `/tables/1/objects/420/fields/17` | 179194 / 179210 |
| A0Z3 (AUau) | atar / 2 | air,invulnerable,self,ground,vulnerable,friend | 150320–150333 | `/tables/1/objects/420/fields/18` | 179261 / 179277 |
| A0Z3 (AUau) | atar / 3 | air,invulnerable,self,ground,vulnerable,friend | 150336–150349 | `/tables/1/objects/420/fields/19` | 179328 / 179344 |
| A0Z2 (AUau) | aare / 1 | 999999 | 150658–150671 | `/tables/1/objects/421/fields/0` | 179634 / 179650 |
| A0Z2 (AUau) | aare / 2 | 999999 | 150674–150687 | `/tables/1/objects/421/fields/1` | 179658 / 179674 |
| A0Z2 (AUau) | aare / 3 | 999999 | 150690–150703 | `/tables/1/objects/421/fields/2` | 179682 / 179698 |
| A0Z2 (AUau) | abuf / 1 | B02F | 150706–150719 | `/tables/1/objects/421/fields/3` | 179706 / 179722 |
| A0Z2 (AUau) | abuf / 2 | B02F | 150722–150735 | `/tables/1/objects/421/fields/4` | 179731 / 179747 |
| A0Z2 (AUau) | abuf / 3 | B02F | 150738–150751 | `/tables/1/objects/421/fields/5` | 179756 / 179772 |
| A0Z2 (AUau) | Uau1 / 1 | 0.25 | 150754–150767 | `/tables/1/objects/421/fields/6` | 179781 / 179797 |
| A0Z2 (AUau) | Uau1 / 2 | 0.25 | 150770–150783 | `/tables/1/objects/421/fields/7` | 179805 / 179821 |
| A0Z2 (AUau) | Uau1 / 3 | 0.25 | 150786–150799 | `/tables/1/objects/421/fields/8` | 179829 / 179845 |
| A0Z2 (AUau) | Uau2 / 1 | 0 | 150802–150815 | `/tables/1/objects/421/fields/9` | 179853 / 179869 |
| A0Z2 (AUau) | Uau2 / 2 | 200000 | 150818–150831 | `/tables/1/objects/421/fields/10` | 179877 / 179893 |
| A0Z2 (AUau) | Uau2 / 3 | 450000 | 150834–150847 | `/tables/1/objects/421/fields/11` | 179901 / 179917 |
| A0Z2 (AUau) | anam / 0 | 모드 이동속도 증가 | 150866–150888 | `/tables/1/objects/421/fields/13` | 179949 / 179965 |
| A0Z2 (AUau) | atar / 1 | air,invulnerable,self,ground,vulnerable,friend | 150939–150952 | `/tables/1/objects/421/fields/17` | 180051 / 180067 |
| A0Z2 (AUau) | atar / 2 | air,invulnerable,self,ground,vulnerable,friend | 150955–150968 | `/tables/1/objects/421/fields/18` | 180118 / 180134 |
| A0Z2 (AUau) | atar / 3 | air,invulnerable,self,ground,vulnerable,friend | 150971–150984 | `/tables/1/objects/421/fields/19` | 180185 / 180201 |
### 2.320.w3u.json

EOF validated: `true`; parsed end `917401` equals byte length `917401`.

| Object (base) | Field / level | Value | JSON lines | JSON pointer | Field / value byte offset |
|---|---|---|---|---|---|
| o032 (otbk) | uabi / null | A13H,A0IQ,Avul | 3389–3402 | `/tables/1/objects/14/fields/0` | 3370 / 3378 |
| o032 (otbk) | uhpm / null | 10 | 3629–3642 | `/tables/1/objects/14/fields/15` | 3691 / 3699 |
| o032 (otbk) | unam / null | \|cffc15af4에단바론 V. 나스쥬로\|r - \|cffdc143c오로성\|r | 3693–3715 | `/tables/1/objects/14/fields/19` | 3755 / 3763 |
| o032 (otbk) | uhpr / null | 0 | 3750–3763 | `/tables/1/objects/14/fields/22` | 3811 / 3819 |
| o031 (otbk) | uabi / null | A13H,A0IQ,Avul | 3992–4005 | `/tables/1/objects/15/fields/0` | 4025 / 4033 |
| o031 (otbk) | uhpm / null | 10 | 4232–4245 | `/tables/1/objects/15/fields/15` | 4345 / 4353 |
| o031 (otbk) | unam / null | \|cffc15af4제이가르시아 새턴\|r - \|cffdc143c오로성\|r | 4296–4318 | `/tables/1/objects/15/fields/19` | 4409 / 4417 |
| o031 (otbk) | uhpr / null | 0 | 4353–4366 | `/tables/1/objects/15/fields/22` | 4465 / 4473 |
| o02E (otbk) | uabi / null | A13H,A0IQ,Avul | 12790–12803 | `/tables/1/objects/29/fields/0` | 14055 / 14063 |
| o02E (otbk) | uhpm / null | 10 | 13030–13043 | `/tables/1/objects/29/fields/15` | 14376 / 14384 |
| o02E (otbk) | unam / null | \|cffc15af4토프먼 워큐리\|r - \|cffdc143c오로성\|r | 13094–13116 | `/tables/1/objects/29/fields/19` | 14440 / 14448 |
| o02E (otbk) | uhpr / null | 0 | 13151–13164 | `/tables/1/objects/29/fields/22` | 14496 / 14504 |
| e003 (ewsp) | uabi / null | Avul | 754446–754459 | `/tables/1/objects/1331/fields/0` | 790429 / 790437 |
| e003 (ewsp) | uhpm / null | 15 | 754670–754683 | `/tables/1/objects/1331/fields/14` | 790652 / 790660 |
| e003 (ewsp) | unam / null | 라인몹이속더미 | 754734–754756 | `/tables/1/objects/1331/fields/18` | 790716 / 790724 |
| e003 (ewsp) | uhpr / null | 0 | 754807–754820 | `/tables/1/objects/1331/fields/22` | 790792 / 790800 |
### 2.320.w3a.json

EOF validated: `true`; parsed end `699383` equals byte length `699383`.

| Object (base) | Field / level | Value | JSON lines | JSON pointer | Field / value byte offset |
|---|---|---|---|---|---|
| A13G (ACac) | aare / 1 | 99999 | 102441–102454 | `/tables/1/objects/277/fields/0` | 121033 / 121049 |
| A13G (ACac) | aare / 2 | 99999 | 102457–102470 | `/tables/1/objects/277/fields/1` | 121057 / 121073 |
| A13G (ACac) | abuf / 1 | B07X | 102489–102502 | `/tables/1/objects/277/fields/3` | 121152 / 121168 |
| A13G (ACac) | abuf / 2 | B07X | 102505–102518 | `/tables/1/objects/277/fields/4` | 121177 / 121193 |
| A13G (ACac) | Cac1 / 1 | -0.20000000298023224 | 102553–102566 | `/tables/1/objects/277/fields/7` | 121250 / 121266 |
| A13G (ACac) | Cac1 / 2 | -0.30000001192092896 | 102569–102582 | `/tables/1/objects/277/fields/8` | 121274 / 121290 |
| A13G (ACac) | alev / 0 | 2 | 102617–102630 | `/tables/1/objects/277/fields/11` | 121346 / 121362 |
| A13G (ACac) | anam / 0 | [오로성]새턴 | 102633–102655 | `/tables/1/objects/277/fields/12` | 121370 / 121386 |
| A13G (ACac) | atar / 1 | air,invulnerable,enemies,ground | 102674–102687 | `/tables/1/objects/277/fields/14` | 121424 / 121440 |
| A13G (ACac) | atar / 2 | air,invulnerable,enemies,ground | 102690–102703 | `/tables/1/objects/277/fields/15` | 121476 / 121492 |
| A0Z3 (AUau) | aare / 1 | 999999 | 150096–150109 | `/tables/1/objects/420/fields/0` | 178915 / 178931 |
| A0Z3 (AUau) | aare / 2 | 999999 | 150112–150125 | `/tables/1/objects/420/fields/1` | 178939 / 178955 |
| A0Z3 (AUau) | aare / 3 | 999999 | 150128–150141 | `/tables/1/objects/420/fields/2` | 178963 / 178979 |
| A0Z3 (AUau) | abuf / 1 | B02F | 150144–150157 | `/tables/1/objects/420/fields/3` | 178987 / 179003 |
| A0Z3 (AUau) | abuf / 2 | B02F | 150160–150173 | `/tables/1/objects/420/fields/4` | 179012 / 179028 |
| A0Z3 (AUau) | abuf / 3 | B02F | 150176–150189 | `/tables/1/objects/420/fields/5` | 179037 / 179053 |
| A0Z3 (AUau) | Uau1 / 1 | 0.25 | 150192–150205 | `/tables/1/objects/420/fields/6` | 179062 / 179078 |
| A0Z3 (AUau) | Uau1 / 2 | 0.25 | 150208–150221 | `/tables/1/objects/420/fields/7` | 179086 / 179102 |
| A0Z3 (AUau) | Uau1 / 3 | 0.25 | 150224–150237 | `/tables/1/objects/420/fields/8` | 179110 / 179126 |
| A0Z3 (AUau) | Uau2 / 1 | 0 | 150240–150253 | `/tables/1/objects/420/fields/9` | 179134 / 179150 |
| A0Z3 (AUau) | Uau2 / 2 | 500000 | 150256–150269 | `/tables/1/objects/420/fields/10` | 179158 / 179174 |
| A0Z3 (AUau) | Uau2 / 3 | 850000 | 150272–150285 | `/tables/1/objects/420/fields/11` | 179182 / 179198 |
| A0Z3 (AUau) | anam / 0 | 모드 이동속도 증가 | 150304–150326 | `/tables/1/objects/420/fields/13` | 179230 / 179246 |
| A0Z3 (AUau) | atar / 1 | air,invulnerable,self,ground,vulnerable,friend | 150377–150390 | `/tables/1/objects/420/fields/17` | 179332 / 179348 |
| A0Z3 (AUau) | atar / 2 | air,invulnerable,self,ground,vulnerable,friend | 150393–150406 | `/tables/1/objects/420/fields/18` | 179399 / 179415 |
| A0Z3 (AUau) | atar / 3 | air,invulnerable,self,ground,vulnerable,friend | 150409–150422 | `/tables/1/objects/420/fields/19` | 179466 / 179482 |
| A0Z2 (AUau) | aare / 1 | 999999 | 150731–150744 | `/tables/1/objects/421/fields/0` | 179772 / 179788 |
| A0Z2 (AUau) | aare / 2 | 999999 | 150747–150760 | `/tables/1/objects/421/fields/1` | 179796 / 179812 |
| A0Z2 (AUau) | aare / 3 | 999999 | 150763–150776 | `/tables/1/objects/421/fields/2` | 179820 / 179836 |
| A0Z2 (AUau) | abuf / 1 | B02F | 150779–150792 | `/tables/1/objects/421/fields/3` | 179844 / 179860 |
| A0Z2 (AUau) | abuf / 2 | B02F | 150795–150808 | `/tables/1/objects/421/fields/4` | 179869 / 179885 |
| A0Z2 (AUau) | abuf / 3 | B02F | 150811–150824 | `/tables/1/objects/421/fields/5` | 179894 / 179910 |
| A0Z2 (AUau) | Uau1 / 1 | 0.25 | 150827–150840 | `/tables/1/objects/421/fields/6` | 179919 / 179935 |
| A0Z2 (AUau) | Uau1 / 2 | 0.25 | 150843–150856 | `/tables/1/objects/421/fields/7` | 179943 / 179959 |
| A0Z2 (AUau) | Uau1 / 3 | 0.25 | 150859–150872 | `/tables/1/objects/421/fields/8` | 179967 / 179983 |
| A0Z2 (AUau) | Uau2 / 1 | 0 | 150875–150888 | `/tables/1/objects/421/fields/9` | 179991 / 180007 |
| A0Z2 (AUau) | Uau2 / 2 | 250000 | 150891–150904 | `/tables/1/objects/421/fields/10` | 180015 / 180031 |
| A0Z2 (AUau) | Uau2 / 3 | 475000 | 150907–150920 | `/tables/1/objects/421/fields/11` | 180039 / 180055 |
| A0Z2 (AUau) | anam / 0 | 모드 이동속도 증가 | 150939–150961 | `/tables/1/objects/421/fields/13` | 180087 / 180103 |
| A0Z2 (AUau) | atar / 1 | air,invulnerable,self,ground,vulnerable,friend | 151012–151025 | `/tables/1/objects/421/fields/17` | 180189 / 180205 |
| A0Z2 (AUau) | atar / 2 | air,invulnerable,self,ground,vulnerable,friend | 151028–151041 | `/tables/1/objects/421/fields/18` | 180256 / 180272 |
| A0Z2 (AUau) | atar / 3 | air,invulnerable,self,ground,vulnerable,friend | 151044–151057 | `/tables/1/objects/421/fields/19` | 180323 / 180339 |

### Uau2 raw field bytes

These preserve the exact encoded regeneration modifications for reproducibility.

```json
[
  {
    "file": "object-diff/2.314.w3a.json",
    "id": "A0Z3",
    "base": "AUau",
    "pointer": "/tables/1/objects/420/fields/9",
    "line": 150167,
    "endLine": 150180,
    "field": "Uau2",
    "level": 1,
    "value": 0,
    "offset": 178996,
    "valueOffset": 179012,
    "rawHex": "556175320200000001000000020000000000000000000000"
  },
  {
    "file": "object-diff/2.314.w3a.json",
    "id": "A0Z3",
    "base": "AUau",
    "pointer": "/tables/1/objects/420/fields/10",
    "line": 150183,
    "endLine": 150196,
    "field": "Uau2",
    "level": 2,
    "value": 500000,
    "offset": 179020,
    "valueOffset": 179036,
    "rawHex": "556175320200000002000000020000000024f44800000000"
  },
  {
    "file": "object-diff/2.314.w3a.json",
    "id": "A0Z3",
    "base": "AUau",
    "pointer": "/tables/1/objects/420/fields/11",
    "line": 150199,
    "endLine": 150212,
    "field": "Uau2",
    "level": 3,
    "value": 800000,
    "offset": 179044,
    "valueOffset": 179060,
    "rawHex": "556175320200000003000000020000000050434900000000"
  },
  {
    "file": "object-diff/2.314.w3a.json",
    "id": "A0Z2",
    "base": "AUau",
    "pointer": "/tables/1/objects/421/fields/9",
    "line": 150802,
    "endLine": 150815,
    "field": "Uau2",
    "level": 1,
    "value": 0,
    "offset": 179853,
    "valueOffset": 179869,
    "rawHex": "556175320200000001000000020000000000000000000000"
  },
  {
    "file": "object-diff/2.314.w3a.json",
    "id": "A0Z2",
    "base": "AUau",
    "pointer": "/tables/1/objects/421/fields/10",
    "line": 150818,
    "endLine": 150831,
    "field": "Uau2",
    "level": 2,
    "value": 200000,
    "offset": 179877,
    "valueOffset": 179893,
    "rawHex": "556175320200000002000000020000000050434800000000"
  },
  {
    "file": "object-diff/2.314.w3a.json",
    "id": "A0Z2",
    "base": "AUau",
    "pointer": "/tables/1/objects/421/fields/11",
    "line": 150834,
    "endLine": 150847,
    "field": "Uau2",
    "level": 3,
    "value": 450000,
    "offset": 179901,
    "valueOffset": 179917,
    "rawHex": "5561753202000000030000000200000000badb4800000000"
  },
  {
    "file": "object-diff/2.320.w3a.json",
    "id": "A0Z3",
    "base": "AUau",
    "pointer": "/tables/1/objects/420/fields/9",
    "line": 150240,
    "endLine": 150253,
    "field": "Uau2",
    "level": 1,
    "value": 0,
    "offset": 179134,
    "valueOffset": 179150,
    "rawHex": "556175320200000001000000020000000000000000000000"
  },
  {
    "file": "object-diff/2.320.w3a.json",
    "id": "A0Z3",
    "base": "AUau",
    "pointer": "/tables/1/objects/420/fields/10",
    "line": 150256,
    "endLine": 150269,
    "field": "Uau2",
    "level": 2,
    "value": 500000,
    "offset": 179158,
    "valueOffset": 179174,
    "rawHex": "556175320200000002000000020000000024f44800000000"
  },
  {
    "file": "object-diff/2.320.w3a.json",
    "id": "A0Z3",
    "base": "AUau",
    "pointer": "/tables/1/objects/420/fields/11",
    "line": 150272,
    "endLine": 150285,
    "field": "Uau2",
    "level": 3,
    "value": 850000,
    "offset": 179182,
    "valueOffset": 179198,
    "rawHex": "5561753202000000030000000200000000854f4900000000"
  },
  {
    "file": "object-diff/2.320.w3a.json",
    "id": "A0Z2",
    "base": "AUau",
    "pointer": "/tables/1/objects/421/fields/9",
    "line": 150875,
    "endLine": 150888,
    "field": "Uau2",
    "level": 1,
    "value": 0,
    "offset": 179991,
    "valueOffset": 180007,
    "rawHex": "556175320200000001000000020000000000000000000000"
  },
  {
    "file": "object-diff/2.320.w3a.json",
    "id": "A0Z2",
    "base": "AUau",
    "pointer": "/tables/1/objects/421/fields/10",
    "line": 150891,
    "endLine": 150904,
    "field": "Uau2",
    "level": 2,
    "value": 250000,
    "offset": 180015,
    "valueOffset": 180031,
    "rawHex": "556175320200000002000000020000000024744800000000"
  },
  {
    "file": "object-diff/2.320.w3a.json",
    "id": "A0Z2",
    "base": "AUau",
    "pointer": "/tables/1/objects/421/fields/11",
    "line": 150907,
    "endLine": 150920,
    "field": "Uau2",
    "level": 3,
    "value": 475000,
    "offset": 180039,
    "valueOffset": 180055,
    "rawHex": "5561753202000000030000000200000000efe74800000000"
  }
]
```

## Completion boundary

The requested coefficient and swapped-allocation audit is complete at static JASS + object level. Exact native engine tick cadence is not present in the supplied custom objects/JASS and remains unverified, explicitly not replaced by an assumed scripted period. No application, profile, production, or gameplay changes were made.
