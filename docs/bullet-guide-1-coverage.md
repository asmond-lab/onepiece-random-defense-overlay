# Guide 1 requirements - registered before implementation

Source: [transcription](bullet-guide-1-source.md), high-resolution crop evidence there.
PENDING means mapping/implementation pending, not discarded or unobservable.
Map research must separate static rule from live observation. No fabricated thresholds.
Current status is below; PARTIAL is not completion. The map lane uses a separate G01-G41
numbering system: see [the complete crosswalk](bullet-guide-1-map-coverage.md).
All source advice is readable in the shared Guide1 source expander, but displaying advice
does not count as implementing its conditional execution policy.

| ID | Distinct actionable source requirement | Policy/UI connection | Status |
|---|---|---|---|
| G01 | Guide1 selection/persistence/main+overlay | settings/shared coach | IMPLEMENTED |
| G02 | Actual owned Bullet, never recipe completion as ownership | stage selection | IMPLEMENTED |
| G03 | Solo Nightmare differs from public game; selected difficulty retained | player/difficulty | PARTIAL |
| G04 | First legend best list and 4 priority groups | opening ranking | IMPLEMENTED |
| G05 | First legend before12; subsequent slow/stun/boss | ordered stage | PARTIAL |
| G06 | At most one armor-only early legend | candidate filter | PARTIAL |
| G07 | Third legend air; King/Red preferred, Caesar/Kalgara disfavored | ordered air stage | IMPLEMENTED |
| G08 | Two flying/blink units before story12 | observed unit abilities | PARTIAL |
| G09 | Existing air -> Shiki, Shiki -> Red/King | owned branch ranking | IMPLEMENTED |
| G10 | King+Red -> Shiki/Smoker/Blackbeard; preferred pairs | owned branch ranking | IMPLEMENTED |
| G11 | Avoid Marco; preserve Whitebeard/Hibari/Bon | exclusions/reservation | PARTIAL |
| G12 | Fourth legend after story10, Rayleigh+ship/Brulee | story/reward/resources | PARTIAL |
| G13 | Prepare Brulee after2; Peru/Morgan/Iceburg/Bulgori exception | ownership/Brulee | MAP/READER GAP |
| G14 | Use selection wisps to secure air2/story12 | resource conversion | MAP/READER GAP |
| G15 | Two Bullet legends by story12, final before50 | recipe progression | PARTIAL |
| G16 | Keep component legends on field before50 | reservation/early top hold | IMPLEMENTED |
| G17 | Brulee Caesar and Blackbeard for story | conversion/story | MAP/READER GAP |
| G18 | Two boss units; Red first, Killer unless Hibari materials stronger | support ranking | IMPLEMENTED |
| G19 | One-boss exception Red+Morgan/Kalgara | support exception | IMPLEMENTED |
| G20 | Slow story King->Queen0.5; distortion after quests | quest/story speed | MAP/READER GAP |
| G21 | Destruction King via fast first2/four afterPunk/shop wisps | quest/legend gates | MAP/READER GAP |
| G22 | Ancient ship and legend story attackers | ship/story | MAP/READER GAP |
| G23 | Round50 Bullet; sell leftover uncommons then upgrade | recipe/economy | PARTIAL |
| G24 | Only armor15 gives slow20, external82 | upgrade/live aura | IMPLEMENTED |
| G25 | Only armor30 gives40, external aura100 | upgrade/live aura | IMPLEMENTED |
| G26 | Boss2/aura100/slow82 before stun1..1.5 | ordered support | IMPLEMENTED |
| G27 | Four stun pairs, Queen+Whitebeard attack-speed fallback | support/spells | PARTIAL |
| G28 | Avoid Vergo/stack armor helpers, prefer aura | candidate exclusions | IMPLEMENTED |
| G29 | Cracker/Sengoku/Chopper/Sabo synergies | support candidates | PARTIAL |
| G30 | Black Maria stun/slow form when Kaku/Luffy2 | ownership/form | MAP/READER GAP |
| G31 | Preserve rare slow sources and conditional promotions | recipe/economy protection | PARTIAL |
| G32 | Preserve rare Chopper/Usopp/Burgess/Jozu | recipe/economy protection | PARTIAL |
| G33 | Preserve named special armor/slow/boost sources | recipe/economy protection | PARTIAL |
| G34 | Duplicate rare+special effects: sell only special | map A0BA, current recipe allocation, one-sale advice | IMPLEMENTED |
| G35 | Keep common20 at50; optional Chopper with30..40 | count/resource policy | PARTIAL |
| G36 | At50 armor30/speed1..15 | separate upgrade levels | PARTIAL |
| G37 | High starting armor: armor/speed30,power1..15,save | user-managed attack investment | EXCLUDED BY USER |
| G38 | Low starting armor: power30 then save | user-managed attack investment | EXCLUDED BY USER |
| G39 | At60 full armor/speed; armor->speed->power | upgrade cost/levels | PARTIAL |
| G40 | Seastone/poison/sail preferred; fast boss power15 | poison/seastone spending is user-managed; sail source guidance retained | USER EXCLUSION APPLIED |
| G41 | Poison after debuff stacks | user-managed timing | EXCLUDED BY USER |
| G42 | After50 separate boss+legends/Bullet/stun groups | unit selection | PARTIAL |
| G43 | Boss group attacks boss/enrage; Bullet highHP line | live targets/spells | PARTIAL |
| G44 | BigMom60 speed -> stop upgrade/seastone65 or spend rest | user-managed seastone spending | EXCLUDED BY USER |
| G45 | AfterMarineford feasible2stun -> Mihawk Green Blood boss | observed item/host, pair and shared material safety | IMPLEMENTED |
| G46 | Nusjuro Green Blood Bartolomeo/Dragon | observed Gorosei/item/owned recipient | IMPLEMENTED |
| G47 | Other Gorosei Green Blood Sengoku/Whitebeard/Red/Killer | observed item/owned recipient; Red A07N verified | IMPLEMENTED |
| G48 | Iva/Bon/Queen leakage, Fuji/Aokiji hidden alternatives | stun capability | MAP/READER GAP |
| G49 | Nusjuro Toki/MihawkSeraphim ship exception,wood8/Ray2 | conversion/items | MAP/READER GAP |
| G50 | Warcury extra20 armor | Gorosei/support | PARTIAL |
| G51 | Saturn Chopper+smallarmor or Dragon stun | observed Saturn Chopper supplement after support ready; active Dragon conditions pending | PARTIAL |
| G52 | Bounty Hunter default,slow7,wood6 at30/40/50boss | navigation effects | PARTIAL |
| G53 | Emergency Call when early hand difficult | navigation fallback | MAP/READER GAP |
| G54 | DoubleBenefit disfavored/MaximumOutput untested | navigation alternatives | PARTIAL |
| G55 | Low gamble for clear/high gamble quest exception | verified active Q006 cost-check advice, no assumed affordability | PARTIAL |
| G56 | Suggested navigation != actual;21..23/24 confirmation | existing confirmation | IMPLEMENTED |
| G57 | No trait upgrade, shop exchange from10 | map shop/resources | MAP/READER GAP |
| G58 | Diagram recipe vs authoritative current catalog | source reconciliation | IMPLEMENTED |
| G59 | Recognition/reward/finished/pause gates precede instructions | shared safety | IMPLEMENTED |
| G60 | Session reset/mode changes/automatic and manual unchanged | regression tests | IMPLEMENTED |
| G61 | Basic hit600k,211armor except59,160startingarmor power1..15 | starting-armor160 branch excluded by user; damage facts retained as source | USER EXCLUSION APPLIED |
| G62 | Position400 from spawn,not extremes,new mobs avoided | position/target geometry | PARTIAL |
| G63 | Attack-speed30 radius450 and fast armorbreak75 application | skill upgrade/buffs | PARTIAL |
| G64 | 7% proc failure/stacks and speed30 before power15 | live buff/proc/upgrade | PARTIAL |
