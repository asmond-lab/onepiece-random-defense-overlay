# Guide 1 implementation vs authoritative map research

The map lane's `.omo/evidence/bullet-map-analysis.md` B01-B18/G01-G41 is the authoritative
research ledger. IDs here are its IDs, not the earlier 64-row image registration IDs.
The source transcription and original crops remain in `bullet-guide-1-source.md` and
`artifacts/bullet-guide-source/`. No original rule is deleted because a reader is absent.

## Implemented architecture

- `GuideCatalog`: number1, fixed Bullet goal; settings default1 does not rewrite manual goals.
- `BulletGuidePolicy`: source-ranked opening, second role unit, third air, fourth-after-story10,
  component legends, two boss units, post-craft support, round50 hold/craft, actual owned Bullet.
- `BulletGuideSupportPolicy`: 2.314 nominal aura rows, max-per-buff-group, confirmed Bounty7,
  no Bullet contribution, removal of component legends before evaluating support, source stun pairs.
  These are nominal formation potentials, not applied target debuffs or seconds of stun.
- `BulletGuideRuntimeReader`: production Guide1-only ag/Bg/Rg extension through the existing
  JASS quest-table anchor (fallback bounded discovery). Version/map-hash/actual-owned-Bullet/
  verified-local-player/type/domain/double-read/reset checks. Missing arrays are unknown, not0.
  Tier effects: armor5/20/40;slow0/20/20. Exact charges are not inferred from tiers.
- `RecommendationPipeline`/`RecommendationEngine`: a registered plan creates real recipe
  candidates. `AutoCombinePlanner` retains shared material/top-limit gates and uses the
  Guide's map-bound support-preservation gate instead of imposing another build's211/102 targets.
- `BulletGuideCraftSafety`: actual direct ingredients, round50, post-craft aura/stun/boss
  preservation and preparation commons. Resources still require actual observation in the coach;
  the guide does not print a craft key when its lumber is unknown.
- `BulletGuideUpgradePolicy`: only actual Bullet+verified tiers+complete source support;
  armor to30 then speed to30, one actually owned common at a time. The20 commons prepared
  at50 are spendable for their intended upgrades, not an invented permanent reserve.
- Shared coach: recognition/pause/reward/Finished gates, actual navigation confirmation,
  unchanged automatic/manual policies, source expander, map potential/tier explanation.

## Map G01-G41 crosswalk

| Map ID | Implemented behavior | Exact remaining work / status |
|---|---|---|
| G01 | Source explicitly labeled solo Nightmare; selected difficulty retained | PARTIAL: SE player participation and other-player assistance history not read; never use SoloTop as player count |
| G02 | First-legend stage and12-round source milestone | PARTIAL: e018 spending dispatch/helper availability and cumulative transformed-legend history not implemented |
| G03 | All4 source priority groups, recipe availability ranking | IMPLEMENTED; 검호 additionally resolved by actual h043 `검호조합` registration J:78900/85912 and map-combine-commands:12 |
| G04 | Second candidates confined to source slow/boss foundations | PARTIAL: optional first-two armor-only allowance/history is not a separately observed counter |
| G05 | Verified King/Shiki/Red/Karugara fly and Chopper blink identities, two-unit stage before story12 | PARTIAL: Caesar movement unresolved; Brulee paid mirror ability/trait not counted as free mobility |
| G06 | Existing air/Caesar ->Shiki;Shiki ->Red/King;King+Red ->Shiki/Smoker/Blackbeard | IMPLEMENTED source branches;material progress chooses among source alternatives;Caesar itself is not credited as verified mobility |
| G07 | Fourth new legend blocked before completed story10 | PARTIAL: e01A reserved Rayleigh/ship conversion dispatch is not executable; no phantom reward consumption |
| G08 | Source preparation text and real Brulee recipe remain available | GAP: e018/help-shop conversion, trait1 mirror payment/current ability availability |
| G09 | Source exception text retained | GAP: mission HP/reach and exact Perona/Peru naming reconciliation before selecting exception automatically |
| G10 | Two component stage before story12; component protection until50 | PARTIAL: selection-wisp execution and missed-deadline recovery not separate policies |
| G11 | Post-recipe support excludes Shiki/Smoker/Blackbeard, final material follows support | PARTIAL: Brulee teleport/use dispatch not implemented |
| G12 | Red first;Killer vsHibari by material progress;Red+Morgan/Karugara exception without pretending two primary bosses are owned | IMPLEMENTED roster policy;actual boss placement still needs R7 |
| G13 | Marco excluded from guide candidate set; source explanation retained | PARTIAL: dedicated Whitebeard+Hibari+Bon package construction not implemented |
| G14 | No automatic guide reroll/sale of retained rares; mapped aura losses block crafts | PARTIAL: XDrake/Aokiji active effects and conditional promotions need remaining bindings; no refund assumptions |
| G15 | Named specials' mapped auras deduplicated; common recipe safety | PARTIAL: Chopper special attack buff/use is not a live buff observation |
| G16 | Kid/Croc/Smoker shared aura maxima;Bon/Iva armor11 once; unallocated duplicate special sale one at a time | IMPLEMENTED sale advice from actual inventory and recipe allocation; A0BA/PtN map handler, no assumed random lumber. Actual game input remains manual |
| G17 | Source armor/slow rows and source-support candidates | PARTIAL: Cracker/Sengoku active boosts and BlackMaria selected form not read; both variants never added together |
| G18 | Guide prefers known aura candidates and excludes Vergo | IMPLEMENTED preference; applied targetA04M still not observed or credited |
| G19 | External82 nominal target;Moby40/Sabo25/confirmedBounty7;Bullet20 only verified armor>=2 | IMPLEMENTED planning/tier contract; actual coverage needs target-buff/position reader |
| G20 | External100 nominal target andtwo boss before source stun | IMPLEMENTED planning contract;100+40+75 never displayed as observed215;A04M not read |
| G21 | Source pair/single-control composition checks;no fake1.5sec simulation | PARTIAL: Queen+Whitebeard emergency active buff/control and precise stun availability not read |
| G22 | Source group assignment advice shown | GAP: client control-group membership and currentorders/targets |
| G23 | Source line/400-distance/high-current-HP advice shown | GAP: CUnit XY,spawnrect,order,targetHP,A04M;does not claim correct placement from ownership |
| G24 | Source DestructionKing advice and normal source progression | GAP: explicit mission flag and story11-before30 reward-race policy |
| G25 | Existing actual story/ship observations and source advice | GAP: executable AncientShip/helper conversion branch;never relabel stage9 Marineford in new guide logic |
| G26 | Real round50 Bullet recipe reaches AutoCombine and coach;unknown wood suppresses key | PARTIAL: native resource producer and grade-specific sale payouts/dispatch not implemented |
| G27 | Commons counted from9 rawcodes; after owned Bullet and support ready,30+ commons enable one rare Chopper supplement; recipe safety preserves20; allocated commons excluded from upgrades | PARTIAL: roster supplement and payment reservation verified; exact50 speed1..15 stopping requires CItem charges |
| G28 | Starting-armor160 attack-investment branch | EXCLUDED BY USER: user-managed, not an automatic condition or pending reader requirement |
| G29 | Verified armor/speed tiers drive sequential upgrades to3(30),60-round milestone | PARTIAL: exact-charge granularity and helper spell/resource opportunity costs |
| G30 | Poison use and timing | EXCLUDED BY USER: user-managed, no automatic cast condition or pending threshold |
| G31 | Boss-speed-dependent seastone/poison spending | EXCLUDED BY USER: user-managed, no automatic fast/tight threshold required for these uses |
| G32 | AfterMarineford8 observed GreenBlood: established stun pair/Mihawk, Nasjuro Barto/Dragon, otherwise Sengoku/Whitebeard/RedForce/Killer | IMPLEMENTED selection and shared safe item action; RedForce A07N eligibility map-confirmed. Applied recipient abilities still require R5; no assumed rewardat8 |
| G33 | Alternatives retained in source expander | GAP: Fuji/Aokiji conditional boss-control branch and skill-state reading |
| G34 | Source Nasjuro8:2/Toki/ship exception retained | GAP: guide item/ship branch and resource/conversion dispatch;no invented8:2 drop probability |
| G35 | Detected Warcury changes guide external-armor target to120 | PARTIAL: activation effects not inferred from identity;map actual10/15 debuff not replaced by guide20 |
| G36 | Observed Saturn with owned Bullet and required formation ready recommends rare Chopper; stops once owned | PARTIAL: Chopper roster branch implemented and real WPF tested; activated attack/regeneration and conditional Dragon alternative remain unobserved |
| G37 | Source Bounty default and actual user confirmation;slow7 only confirmed;wood2x3 corrected in source addendum | PARTIAL:JP/SP native confirmation and chest/pity resource events not integrated;random stock never credited |
| G38 | Source Emergency alternative and DoubleBenefit critique shown | PARTIAL: no invented numeric early-hand-difficulty selector;A09C charges not read;actual chosen alternative still accepted via normal confirmation |
| G39 | Author's MaximumOutput untested label retained | GAP: helper ability levels/cooldowns;no inventedone-boss navigation legality rule |
| G40 | Verified active Q006 produces conditional high-gamble cost-check advice; completed/unknown quests do not | PARTIAL: observation-to-guide wiring and actual WPF verified; no spending instruction without native affordability or remaining failure count |
| G41 | Actual traits and helper mana/cooldown gate round10+ A082 exchange; actual e018 fills missing common materials | IMPLEMENTED exchange/selection advice and production WPF transitions; preserves pending Brulee trait payment and does not precredit generated units |

## Concrete native verification boundary

The earlier Waiting/0-unit receipt is superseded by current read-only Ready observations.
`artifacts/helper-unit-reader-live/native-unit-probe.json` records helper mana18.261992/10000
and A07X/A07Z/A082/A0BY level1/cooldown0. `artifacts/native-position-live` records position
and life values; copied native coordinate functions independently produced the same helper
position(-5888,4464). `artifacts/native-combat-live` contains21 validated unit observations,
including shared bosses that are not assigned to the local lane.

Build-pinned CItem charges, generation checks, native regeneration/clock arithmetic,
coordinates and selected target ability levels are implemented. The114 arithmetic vectors
were evaluated against copied native functions in an isolated emulator. A live owned-Bullet
before/after upgrade and active lane debuff transition were not manufactured or observed.
These limitations must not be relabeled as zero values, complete roster coverage,
effective armor, current attack target or verified skill availability.

Latest native-integration suite:1043 tests passed. Later exchange/selection increments passed
their related suites and45 actual WPF scenarios. The rest of the crosswalk remains partial;
these increments do not complete all source conditions. See
`.omo/evidence/native-reader-progress.md` for current evidence and remaining scope.

## Validation artifacts

- `artifacts/bullet-guide-source/*-red.log`: failing-first selection/types,reader,support,
  common-reserve interpretation and real final-craft regressions.
- `guide-related.log`, `final-craft-green.log`: guide-specific regression runs.
- `full-suite.log` and `bullet-guide-full.trx`: integrated full suite (latest run replaces prior checkpoint).
- `isolated-release.log`: clean Release build in `C:/Users/123/AppData/Local/Temp/orand-guide1-release`.
- `wpf.log`: normal Release blocked by existing user OrandOverlay PID319396, left untouched.
- `wpf-final.log`, `artifacts/bullet-guide-ui/bullet-guide-ui.json`, PNGs: real WPF main/overlay
  production scan/pipeline with controlled observations, including actual recipe instructions,
  tier-only upgrade transition, source expansion, reward hold, Finished and new-session reset.
- Language-server diagnostics were attempted for changed C#/XAML/project files; csharp-ls
  is not installed and XAML/csproj have no configured server. Compiler validation is executed,
  not represented as LSP validation. No independent child reviewers were spawned (task prohibition).
