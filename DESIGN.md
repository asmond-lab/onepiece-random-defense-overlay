## First-Legend purpose presentation (2026-09-22 approved)

Only the first-Legend stage presents the canonical groups `스토리`, `공중이동`, and
`가까운 조합`, in that order. Their membership and ordering come from the browser
snapshot; the view never reclassifies candidates. `가까운 조합` is visibly explained
as `전설 이상 전체` while its canonical group identity remains unchanged. Empty
Story or air groups stay visible and state that no matching candidates exist.

On first entry to the Legend stage, MAIN selects Story before considering material
proximity. UNIT overlay initially opens Story and leaves the other purpose groups
closed. Suggested-category ranking must not replace Story with a cheaper support
candidate. A category selection or fold made by the user remains authoritative through
same-hand, changed-hand, and stale refreshes, as do candidate and detail scroll offsets.
These presentation choices reset on a confirmed new session. Post-upper support groups
and every other stage keep their existing default behavior. Existing compact cards,
type, color, responsive sizing, detached craft selection, and craft shell are unchanged.

Native evidence uses the bundled 2.321 catalog/guide and populated cards on MAIN at
920/default 1080 width and UNIT at minimum 420/default 540 width. It covers all three
categories, empty matching tags, stale and changed hands, retained explicit category,
fold and scroll state, and candidate selection into the existing craft window.

## Resizable unit-check overlay (2026-09-22 superseding contract)

For `OverlayWindow` only, the fixed-size rule in **Overlay chrome** is superseded when the window presents `랜디픽 · 유닛 확인`. The user can resize both width and height through native edges/corners and a visible bottom-right grip while the rounded transparent shell, topmost/nonactivating behavior, drag area, click-through mode, content, adaptive columns and existing scroll ownership remain unchanged. Minimum dimensions are scaled from a readable 420×420 DIP content baseline. Automatic resolution scaling establishes the initial size but never overwrites a user size on hand refresh, drag completion, hide/show, DPI changes or display changes. DPI/display topology changes clamp the retained size and position to the nearest work area. Size retention is session-only; no settings/schema persistence is added. Stats and all other overlay windows retain their existing sizing contracts.

## Rounded craft outer shell (2026-09-22 approved option)

`NormalCraftWindow` adopts only the existing `OverlayWindow` neutral rounded outer-shell treatment and shared `OverlayTheme.ChromeRadius`. Preserve its current preferred/minimum dimensions, content layout, geometry restoration and persistence, header/body/footer structure, topmost/nonactivating behavior, drag/resize/close interactions, and all selection/reopen semantics. This is a shell-only visual alignment: add no palette token, inner layout, content, sizing, or behavior change.

## Overlay input and craft dismissal (2026-09-15 latest tester correction)

Latest user feedback overrides the no-close/no-hide rule below. Recommendation, stats and craft windows stay topmost and never activate on Show or mouse press. Preserve the mouse event so buttons, disclosures and scrolling remain interactive. Apply WS_EX_NOACTIVATE and return MA_NOACTIVATE from WM_MOUSEACTIVATE; detach the native hook on final close. No game input or foreground restoration tricks.

Craft has a visible Korean `닫기` button in the existing target header: 52x28 DIP, 8-DIP top/right inset, 12-DIP type, PlanSurface/PlanText/PlanLine colors and neutral 1-DIP border. Reserve 60 DIP at the right of the detached goal header; the button remains client-hit-testable inside the drag caption. No extra title row or topmost checkbox. Both the button and native close hide the same window. Only explicit selection or `조합창 열기` restores it, with Topmost=true and the prior size/position/scroll retained. Repeated or changed observations never reopen it. Keep the 700x320 preferred and 620x320 minimum lower HUD, recipe/material layout, stats geometry and shutdown semantics.

## Companion overlay CJK wrap correction (2026-09-15)

Final HUD QA found an existing orphaned Korean suffix in the narrow support recommendation card. In the compact overlay only, role phrases separated by a middle dot become separate lines; retain full role text and9-DIP type. Main cards and recommendation calculations are unchanged. Automation name retains the original full reason.

## Borderless fixed-topmost craft HUD (2026-09-15 latest user correction)

The user rejected both the topmost checkbox and the close X after unpinning hid the window behind the game. This overrides the previous native caption/toolbar contract. Keep the independent selection-opened700x320 HUD, always topmost. WindowStyle.None with WindowChrome: caption/drag region40 DIP, resize border4, no Aero caption buttons/glass. The target header is the only title row; no extra toolbar. Closing from native commands is canceled without hiding; main application Closed still destroys it. Explicit selection/reopen always restores Topmost, without resetting size/position or activating the game.

Use existing Plan colors and Craft typography. Detached shortage rail232 DIP with two96-DIP portrait slots and6-DIP gaps, stable existing tier/name order,32-DIP portraits,18-DIP count with explicit shortage label and full12-DIP name. Portrait and count share a compact row; name sits below; no new card borders. At minimum620px the two rail columns remain, recipes wrap. Detached action units use36-DIP portraits beside left-aligned14-DIP names and12-DIP selection/result labels; preserve22-DIP keycap, ingredient states and counts. First action receives subtle PlanSurface emphasis and Accent heading; subsequent actions remain Canvas. Same-hand render/scroll and current/stale data semantics are unchanged. No game input, reader, stats or progression edits.

Validate reopening after Topmost=false, no checkbox or close caption, drag caption and resize border configured, native close cannot lose the HUD, main minimize/shutdown, known/unknown/chat recipes, zero/one/two/many missing materials, minimum size, stale data and repeated hand. UI snapshots are synthetic, not live-game geometry confirmation.

## Selection opens craft overlay at lower game HUD (2026-09-15)

Latest user screenshot overrides startup visibility and the previous tall window. Craft remains a separate surface. Selecting a target in either exploration panel opens one shared craft overlay; before selection it is hidden, and its reopen button is disabled. The same selected target, planner, reference freshness and render-key cache feed the rehosted existing overlay craft workspace. Main has no duplicate craft controls. Closing craft hides it until explicit candidate selection or '조합창 열기'; observations never reopen, move, resize or activate it. Main minimization leaves it visible for gameplay; main Closed terminates it, canceled closure does not.

Reference: user codex-clipboard-028861bb-40e3-463b-a7ae-e0060f14252c.png (2559x1457). Red region is lower central unit-info HUD, approximately x40%/y76.5% of the game monitor. Use the recommendation overlay's monitor and clamp initial placement to its work area; preserve subsequent free placement for the session. Default700x320 DIP, minimum620x320, maximum width700, independently resizable height. These bounds supersede the prior480 maximum for the standalone HUD surface only. Keep32/36/28 portraits,8 inset,6 gap and current fonts. Use existing compact row spacing and160-DIP wrapping ingredient tiles with quantities below names, while keeping action names14 DIP. Preserve material-left/recipe-right C hierarchy, independent scrolls, resource footer, unknown key/data states. Compact goal header retains target name and step count. Native ToolWindow caption supplies drag/resize/close; the short toolbar has '항상 위', default true. ShowActivated=false. No decorative animation or game input.

Recommendation overlay uses a stable480-DIP normal-mode height after detachment; never resize it on hand refresh. Preserve stats geometry and recognition behavior. Verify actual selected/changed/same hand, initial hidden state, both click/reopen paths, position across refresh, close/minimize/shutdown/canceled closure, minimum size, long names, unknown/chat recipes, window-clamped screen placement. Synthetic evidence is not live-game confirmation.

## Selected C density refinement: same layout, smaller spacing (2026-09-15)

The user selected C and then approved a same-layout density reduction. Latest density reference: C:/Users/123/.codex/visualizations/2026/09/14/01a0a22c-bfc3-7ab0-8043-b89c6d4ded0e/craft-density-comparison.html. Structural alternatives were rejected. The original C selection came from the September 15 shortage concepts. Reference: C:/Users/123/.codex/visualizations/2026/09/14/01a0a22c-bfc3-7ab0-8043-b89c6d4ded0e/shortage-qa/c-few-736.png and c-many-736.png. Preserve the information hierarchy and adapt it to real main/overlay bounds; the surrounding example chrome and synthetic recipe are not product content.

- Target header above a persistent missing-material rail and recipe instructions. Resource requirements remain in a footer. Materials and recipes each own a bounded vertical scroll, so a long missing list never pushes the next action down. At pane width below 420 DIP, stack the material pane above the recipe pane, with 35/65 percent of the bounded body. Narrow overlay components reserve 70% of the view height (300–420 DIP) for the detail so both panes remain readable. Hide the rail when no additional units are required. Selection/session reset both scrolls; ordinary updates retain offsets and expanded state.
- Reuse PlanCanvas/Surface/Raised/Line/Text/Secondary/Accent/Warning tokens and Malgun Gothic/Consolas. Neutral border; no gold frame in this detail component. Selected density tokens: panel maximum width 480 DIP, inset 8, main gap 6, inner gaps 5/3, radius 4/8/12. Rail width 140 DIP; missing portrait 32, name 12, shortage count 18; target portrait 28; action portrait 36, ingredient portrait 20; key font 22 with minimum width 30, recipe name/body 14, metadata 12. Typography is unchanged. Main gives remaining horizontal space to exploration; regular overlays bound the detail to 45% of view height (220–290 DIP).
- Material rail rows: portrait, complete name, explicit N개 부족. Keep deterministic tier/name order and the existing expanded/collapsed preference. Craft action: centered selected unit / display-only key and number of combinations / result portrait and output count. Ingredients are quiet full-width rows with required quantities and separate owned/prior-step/missing status. No hypothetical outputs become owned.
- The real plan controls step order, hotkeys, chat commands, required units, quantities and conditions. Show unknown selection/key, unavailable graph and unobserved resources explicitly; never invent a ready/completed state. Candidate recommendation, first-upper progression, reference freshness and stats are untouched.
- Exploration cards show name/tier/material percentage/recommendation and role badges only. Do not duplicate material names or shortage counts there; the selected target’s material rail owns those details.
- Short main workspace (height below 380 DIP): 36 DIP action portraits and a 28 DIP target portrait, single-line goal header and 136 DIP wrapping ingredient tiles with quantities below the full name preserve the first full recipe at the existing 920x620 minimum window. Re-render only when this density breakpoint changes; normal observation caching is unchanged.
- Validate both surfaces, main 920/default and overlay540, zero/one/two/many shortages, long names, repeated/changed observations, fold and independent scrolling, expired state, unknown/chat recipes. Native WPF layout and user-approved adaptation replace pixel identity with browser font metrics; structure, tokens and visual priority must match C. No decorative animation.

# OrandOverlay Design System

## User-facing recognition copy (2026-09-15)

Use short Korean status/action labels on the 2.320 recognition-test surfaces: 유닛 확인 중, 인식 일시정지, 자동 인식 꺼짐, N라운드, 유닛 N개 인식. Do not expose REFERENCE, current-view indexes, native provenance, revisions or raw source IDs in primary labels. Keep existing automation IDs and internal evidence unchanged. One clear notice explains that recognized units may differ from actual holdings; combat stats may show explicitly labeled historical reference values; automatic recommendations remain unavailable. Detailed source/provenance stays in tooltips or the collapsed information section. Never label unknown values zero or call reference material percentages immediate crafting approval. Reuse the existing WPF text styles and wrapping; do not add colors or authority. Use the existing disclosure pattern to keep detailed patch analysis collapsed by default.

## Approved plan workspace / route overlay (2026-09-10)

### Left auxiliary panes

Inventory, guide profile and settings open one owned, non-modal utility window.
The active navigation item toggles it closed; a different item switches the same
window. Main remains visible, enabled, unchanged in size and on its current plan.
Closing a pane returns focus to its navigation item. Native ownership handles
owner shutdown, including canceled closes; no eager Closing cleanup is used.

The existing controls move between a hidden parking container and the pane host,
never copied. Inventory contains only its current recognition/origin labels and
the existing read-only list. Profile contains existing mode, guide, goal and
planning-context controls. Settings contains display, recognition, hotkey and
consent-information controls. Existing event handlers and enabled/visibility
restrictions remain authoritative. Stopped scans identify retained observations,
not an editable manual hand. The old ExpertSettings contract stays hidden and
cannot activate an inline drawer.

Use the existing neutral/teal system, 16px pane padding and 12px spacing. Preferred
utility size is 360x620 DIP, constrained to its owner's current monitor work area.
Placement uses native desktop coordinates consistently: left of main with 12 DIP
gap when space exists; otherwise clamped at the work-area edge, which may overlap
main rather than move it or put controls offscreen. Owner movement, monitor/DPI
changes and work-area notifications recompute placement. Inventory's list owns
its scroll; profile/settings have one body scroll. No yellow border or new input,
telemetry, gameplay or persistence authority is introduced.

### Product refinement: direction, not a mockup replica

The reference defines information hierarchy, not fixed whitespace or a poster.
Keep navigation/settings separate and the plan beside the current action. Use
natural-height headings and compact metrics, 12px panel/16px workspace insets,
36px component images and 8px card gaps. The 160px navigation rail leaves room
for both plan and action at a 920px window; below the main split threshold, the
same bounded scroll reflows rather than shrinking text. Default size is 1280x800.
Remove promotional copy, the mock milestone ribbon and repeated reserve totals.
Only live goal/recipe/ownership/count data supplies the display; idle hides metrics.

No yellow/gold borders remain on the main, coach, recommendation or consent
controls. `OverlayTheme.OutlineBrush` is a neutral divider; `SelectionBorderBrush`
is muted teal; `FocusBrush` is visible teal used only for interactive focus/hover.
Cards and final goals use restrained neutral borders, not bright replacement
outlines. Gold may remain in brand and numeric text. The native DWM frame is
neutralized by the UI load handler without changing startup/runtime policy.
Keyboard focus remains visible; production tests assert border roles and adaptive
visibility, not exact HTML coordinates or prose. Existing math and handlers stay.

This section supersedes the old coach composition only. Reference authority is
`design/overlay-concepts-20260910/main-plan.png` (main variant 1) and
`preview-desktop.png` (overlay B). This is an Operate surface used beside a dark
Warcraft match: opaque slate surfaces, teal route/action emphasis, gold branding.

- Main: compact navigation and separately expandable settings; full goal recipe
  and component ownership in the center; current action and real session notices
  on the right. Narrow workspaces place the action before the plan, without scaling text.
  Expanded recipe branches stay open across observation refreshes; idle/new goals reset them.
  Plan navigation scrolls the route host itself into view, including narrow layouts.
  Bullet alone displays U20h/930h/V20h in that order, naming U20h 검은수염;
  IDs and allocated counts remain untouched. Action titles remove only the catalog
  unit tier decoration. Compact route links follow actual recipe ancestors from
  the current target; unrelated targets never receive an invented recipe link.
- Overlay: goal/component strip, current action with observed additional craft
  completed/required/remaining counts, then next target. Long explanations stay
  in the existing details expander; safety constraints remain visible.
- One display projection reads the current frame, decision and catalog. Recipe
  ownership is distinct from observed additional craft receipts. No mock rounds,
  story completion, progress, timestamps, or historical actions ship as live data.
- Idle/stale observations show no ownership success, round/story metrics or craft
  progress. Recent notices contain only actual decision completion/change notices
  from this match; no notices means no history panel.
- Plan tokens in OverlayTheme: Canvas #090c11, Surface #111720, Raised #18212b,
  Line #2c3744, Text #f4f6f8, Secondary #adb9c7, Accent #80d8cf,
  Success #72dfb0, Warning #ffaf80. Gold is limited to text; focus and selection use their semantic teal tokens.
- Type: Malgun Gothic; title 24, action 20, heading 16, body 14, meta 12.
  Spacing: 4/8/12/16/24/32; radii 8/12; neutral card border 1.
  Main navigation 160 / separate settings drawer 360, supporting action 300,
  workspace split threshold 680; default window 1280x800, minimum 920x620.
- Shared Border/Grid/TextBlock/Expander/Button primitives and existing automation
  IDs remain. The coach owns one body scroll, with a fixed pause/review footer.
  No runtime, consent, input, persistence or recommendation permission changes.

## 1. Atmosphere & Identity

### Stats overlay extension (2026-09-10)

- Keep the existing 326x520 compact footprint in Full and StatsOnly modes (locked by StatsCompactBLayoutTests); do not change
  OverlayLayoutPolicy or scale calculations. Smaller capture widths use scrolling,
  not smaller type. Fixed drag/status header and footer surround one body scroll.
- Reuse the established compact StatsOverlayWindow XAML, StatsMetricView, StatChip and StatsRoleChip components. Their component palette and geometry are the existing stats surface contract (StatsCompactBLayoutTests), distinct from planner tokens. This restoration adds no visual tokens and preserves those components; consolidating their older literal styles into shared tokens is separate design work.
- Three compact primary metrics keep current value, target and status distinct.
  Follow with support effects and provider counts; retain all current aura,
  triggered/stacking breakdowns, physical/magic conditions and existing formatters.
  No new totals, thresholds or percentages are inferred by the view.
- Current-frame observation controls validity. Idle/stale hides numerical claims;
  a confirmed empty inventory shows real zero. Unknown difficulty/goal preserves
  observed values but labels targets unconfirmed, without success/failure coloring.
  Craft-result pending keeps the last current inventory values and a waiting label.
- Detailed readiness text remains available as a tooltip; visible readiness is a
  short support-status label, never a promise of winning. Existing cleanup controls
  stay in a separate disclosure, with unchanged handlers and data.


게임 위에서 즉시 읽히는 고밀도 다크 HUD다. 검은 반투명 셸과 금색 진행 강조를
시그니처로 유지하며, 장식보다 조합 순서와 상태 판독을 우선한다.

## 2. Color

| Role | Token / source | Value | Usage |
|---|---|---:|---|
| Chrome | `OverlayWindow` background | `#F2080A0E` | 전체 반투명 셸 |
| Surface | `OverlayTheme.Featured` | `#12151C` | 현재 조합 |
| Surface/low | `OverlayTheme.Row` | `#0C0E12` | 흐름·후보 보드 |
| Accent | `OverlayTheme.Gold` | `#F0C75E` | 선택·진행·핵심 수치 |
| Text/primary | `OverlayTheme.White` | `#FFFFFF` | 주요 정보 |
| Text/secondary | `OverlayTheme.Muted` | `#8B93A7` | 보조 정보 |
| Border | `OverlayTheme.Hairline` | `#242934` | 구획선 |
| Success | `OverlayTheme.Ok` | `#3DDC84` | 충족 상태 |
| Warning | `OverlayTheme.Warn` | `#F0B446` | 부족 상태 |

새 색은 `App.xaml` 또는 `OverlayTheme`에 의미 토큰으로 먼저 등록한다.

## 3. Typography

- 기본 글꼴: `Malgun Gothic`; 단축키 보조: `Consolas, Malgun Gothic`.
- 주요 제목 15–16px/굵게, 카드 제목 11–13px/준굵게, 보조 정보 9.5–11px.
- 한글 의미 단위가 잘리지 않도록 `TextWrapping=Wrap`을 사용하고 내부 코드·영문 키는
  사용자 문구에 노출하지 않는다.

| Planner type token | Value | Usage |
|---|---:|---|
| `OverlayTheme.PlannerTitleTypeSize` | `12` | 근거 블록 제목 |
| `OverlayTheme.PlannerStateTypeSize` | `9.5` | 헤더 우측 단계 상태 |
| `OverlayTheme.PlannerLabelTypeSize` | `10` | 근거 행 라벨 |
| `OverlayTheme.PlannerValueTypeSize` | `10.5` | 근거 행 값 |

## 4. Spacing & Layout

- 기준 단위는 4px이며 기존 4/6/8/10/12px 간격을 유지한다.
- 추천 창 설계 크기는 540×740, 패 수치 창은 326×520이며 `UiScale`로 화면에 맞춘다.
- 추천 창은 `Auto + Auto + Auto + * + Auto` 세로 셸이다.
- 상단·현재 조합·조합 흐름·하단 상태줄은 내용 높이를 보존한다.
- 후보 보드의 `ScrollViewer`가 남은 높이와 세로 오버플로를 단독 소유한다.
- 별표 행에 양수 `MinHeight`를 강제해 하단 상태줄을 창 밖으로 밀지 않는다.

| Planner layout token | Value | Usage |
|---|---:|---|
| `OverlayTheme.PlannerBlockMargin` | `0,0,0,8` | 근거 블록과 다음 후보 사이 |
| `OverlayTheme.PlannerHeaderMargin` | `0,0,0,6` | 헤더와 첫 근거 행 사이 |
| `OverlayTheme.PlannerRowMargin` | `0,0,0,4` | 근거 행 사이 |
| `OverlayTheme.PlannerBlockPadding` | `8` | 근거 블록 내부 패딩 |
| `OverlayTheme.PlannerLabelColumnWidth` | `88` | 행 라벨 고정 열 폭 |

## 5. Components

### Beginner coach

- `coach-clear-rewards`는 기존 Expander/CoachValue와 단일 스크롤을 사용한다.
  현재 맵에서 읽은 클리어 횟수와 10라·31라 보상 기준을 표시하되, 수령 완료나 현재
  잔량으로 취급하지 않는다. 미확인·오래된 관측·판 종료에서는 숨기고 다음 관측에서 복원한다.

- 플레이 모드는 `초보자 / 일반 / 대깨 / 매뉴얼` 네 개다. 초보자와 일반은 동일한 자동
  목표·추천·안전 검사를 사용하며 설명 밀도만 바뀐다. 두 모드 사이 전환은 진행 상태를 초기화하지 않는다.
- 매뉴얼은 서로 다른 상위 목표를 최대 두 개 지정한다. 목표별 진척도와 공동 재료를 표시하고,
  완성한 선택 목표는 다른 조합의 재료로 소모하지 않는다. 확정 항법과 충돌하면 목표를 지우지 않고 이유를 표시한다.
- 항법 추천은 모든 모드에서 항상 자동이다. 수동 추천 전환·수동 항법 설정은 제공하지 않는다.
  `게임에서 선택 완료`와 늦은 실제 항법 확인은 관측 입력이며 추천 정책을 수동으로 전환하지 않는다.
- 대깨는 `1 · 더글라스 불릿` 공략을 제공한다. 공유 Coach 헤더의 `coach-guide-choice`
  콤보는 메인/오버레이에서 동일 선택을 반영한다. 미등록 번호는 실행하지 않는다.
  공략 단계·현재 패·실제 항법 확인은 분리하며, 솔로 악몽 기준임을 표시한다.
  원문·출처·SHA 검증 자료는 내부에 보존하고 일반 안내 화면에서는 노출하지 않는다.
  운영 도움말은 직접 확인할 조작과 조합 후 대기만 짧게 안내한다.
  새 색/글꼴/간격 없이 기존 ComboBox/CoachLabel/CoachValue/Expander 토큰을 쓴다.
- 게임에서 관측한 난이도를 목표 표본·지원 추천·화면에 함께 반영한다. 미확인은 `난이도 미확인`으로
  표시하고 다른 난이도의 표본·기한·아이템을 대신 쓰지 않는다. 목표나 난이도 미확인 시 지원 목표
  수치를 숨긴다. 클리어 구간은 쉬움 40라·보통 50라·어려움 이상 65라이며, 35라 스토리 마감과
  라인 수 제한 경고는 악몽 전용이다. `1상위`는 조합 구성 조건이지 플레이어 수가 아니다.

- 기존 다크·금색 HUD 안에 초보자용 단일 행동 화면을 둔다. 기본 모드는 초보자 자동이며,
  고급 모드에서 기존 후보 보드, 수치, 가반 고정·수동 목표를 계속 사용할 수 있다.
- `BeginnerCoachView`를 메인 창과 오버레이가 공유한다. 상단은 현재 단계·목표·인식 상태,
  본문은 `지금 할 일 → 조작 → 이유 → 완료 확인 → 다음 관문 → 보존할 재료` 순서다.
- 즉시 행동 하나만 금색으로 강조한다. 원인·확인·대체 경로는 낮은 명도 표면에 둔다.
  한 화면에 선택 가능한 조합 후보를 동급으로 나열하지 않는다.
- 토큰: `CoachTitleTypeSize=20`, `CoachBodyTypeSize=14`, `CoachMetaTypeSize=12`,
  `CoachSectionSpacing=12`, `CoachPanelPadding=16`, `CoachIconSize=48`.
  글꼴·색·모서리는 기존 토큰을 쓴다.
- 초보자 영역의 세로 스크롤 소유자는 `coach-scroll` 하나다. 창 제목과 하단 상태줄은
  고정하고, 고급 보드와 초보자 보드를 동시에 노출하지 않는다. 긴 한글은 자연 높이로
  줄바꿈한다. 기본 영역에서 가로 스크롤·말줄임·고정 본문 높이를 사용하지 않는다.
- 재료 완성률은 `유닛 재료`로 표시한다. 역할 수치는 `지원 수치`이며 클리어 가능성,
  전투 승률, 보스 처치 보장으로 표시하지 않는다.
- 조합 행동에는 카탈로그의 실제 재료·수량·자원을 `coach-craft-recipe`로 조작 앞에 표시한다.
  남은 단계는 `coach-recipe-preview`에 번호·결과·재료 수량을 표시하되 참고용으로 명시하며,
  실행 키는 현재 허용된 행동에만 표시한다. 경로는 기존 `CoachLabel`/`CoachValue`와 단일
  스크롤을 사용하고 일반 모드에서도 숨기지 않는다. 최하위 재료 100%라도 실행 단계가 없으면
  재료 수집 대신 조합 보류와 재개 조건을 표시한다. 보상 대기·사용 안전 검사는 그대로 유지한다.
- 상태: 시작 대기, 인식 불안정, 첫 희귀, 스토리·보상, 항법 선택, 조합, 재료 대기,
  자원 확인, 강화·아이템, 전력 보완, 완료 확인, 클리어·실패 복기. 미확인은 숫자 0과 구분한다.
- 선택 구간이 지난 항법은 기본값을 추측하지 않는다. `coach-navigation-choice`에서
  실제 게임 항법을 고르기 전에는 확인 버튼을 비활성화한다. 클릭 통과 중에는 메인 창에서
  확인할 수 있음을 표시한다. 종료 화면에서는 재료 보존·계속 전투·일시정지 안내를 숨긴다.
- 고급 모드로 돌아갈 때 이전 자동 목표·항법 설정과 수동 목표를 복원한다.
- `coach-action`, `coach-controls`, `coach-reason`, `coach-confirmation`,
  `coach-milestone`, `coach-preserve`, `coach-unknown`, `coach-review`를 안정적인
  Automation ID로 제공한다. 이름과 상태는 보이는 문장과 일치한다.
- 버튼은 기존 hover·pressed·focus 스타일을 사용한다. `게임에서 선택 완료`는 사용자
  확인이며 자동 인식이나 게임 입력으로 기록하지 않는다. 조합 완료는 새 패에서 확인한다.
- 복기는 로컬 파일이며 원격 익명 집계와 분리한다. 중지·다시 시작·상세 보기 경로를
  제공하고 미지원 신호에 대해서는 확인할 내용과 보수적인 대체 행동을 명시한다.
- 100%·125%·150% 배율에서 모든 상태를 실제 WPF 하네스로 렌더링한다. 기본 안내,
  긴 문장, 빈 값, 인식 끊김, 모드 전환, 키보드 포커스와 스크롤을 검증한다.

### Overlay chrome
- 항법 미선택 제목에는 저장된 후보명을 넣지 않는다. 제목은 `목표 · 항법 미선택`,
  사용자 확인 후에만 `목표 · 항법명 (선택 확인)`이다. 추천 후보는 별도 줄이며 실제 선택과
  구분한다. 확인은 게임 입력이 아닌 사용자의 사실 신고이며 새 게임에서 초기화한다.
- 퀘스트 활성·완료 상태는 미확인/없음/진행 중/완료로 구분한다. 미확인 보상은 확정 수치로
  산입하지 않는다. 두 퀘스트 목표는 별도 선택이며 보상만 보고 자동으로 강제하지 않는다.
- 항로개척은 원본 맵의 17종 중 이번 판에 배정된 3개를 자동으로 표시한다. 수동 상태
  콤보박스는 두지 않는다. 각 항목은 이름·완료 여부·조건·보상 순서로 줄바꿈하며,
  읽기 실패 시 구체적인 미확인 이유를 표시하고 미배정/완료로 추정하지 않는다.
- 후보 비교와 퀘스트 근거는 기존 Planner 타입/여백 토큰과 보드 단일 스크롤을 사용한다.
- **Structure**: 둥근 외곽 Border 안의 5행 Grid.
- **States**: 기본, 클릭 통과, 숨김, DPI·모니터 변경.
- **Layout**: 창 자체는 고정 크기이며 내부 후보 보드만 세로 스크롤한다.

### Current craft well
- **Structure**: 현재 추천, 진행률, 역할 요약.
- **Layout**: 내용 높이를 보존하고 자체 스크롤하지 않는다.

### Craft flow well
- **Structure**: 수평 단계 카드와 단축키.
- **Layout**: 최대 높이 178, 가로 스크롤만 소유한다.

### Candidate board well
- **Structure**: 부족 재료, 후보 카드, 상세 진행.
- **States**: 선택, 현재 후보, 부족, 완료, 상위 추천 준비.
- **Layout**: 남은 별표 행을 차지하며 세로 스크롤을 단독 소유한다.

### Pending recommendation card
- **Purpose**: 첫 전설 이후 스토리 보상, 희귀위습 결과, 상위·항법 계산 사이에
  추천 카드가 잠시 0개가 되어도 추천 흐름이 끝나지 않았음을 명확히 전달한다.
- **Structure**: `추천은 계속됩니다` 제목, `상위 추천 준비 중` 상태, 현재
  `storySequence.ActionSummary` 또는 배너를 쓰는 `다음 행동`, 희귀 보상 결과가 상위
  경로를 바꿀 수 있다는 짧은 이유, 결과 반영 뒤 자동 재개된다는 안내 순서다.
- **Scope**: `sequence-story-reward`, `sequence-rare-reward`,
  `sequence-top-navigation`의 빈 추천 상태에만 표시한다. 일반 인식 대기의
  `패 인식 대기 중` 의미와 첫 전설 후보가 실제로 있는 상태는 바꾸지 않는다.
- **Token usage**: 바깥 `Current craft well`의 `Featured`/`OutlineBrush` 표면을
  공유 `CurrentCraftWell` 스타일로 사용한다. 이 스타일은 `Featured`/`OutlineBrush`,
  `WellBorderThickness`, `WellCornerRadius`, `PlannerBlockPadding` 토큰을 적용하며,
  내부는 `White`, `Muted`,
  `PlannerHeaderMargin`, `PlannerRowMargin`,
  `PlannerTitleTypeSize`, `PlannerStateTypeSize`, `PlannerLabelTypeSize`,
  `PlannerValueTypeSize`만 사용한다. 새 색상, 고정 폭, 글자 크기, 간격을 만들지 않는다.
- **Hierarchy and accessibility**: 메인 창과 오버레이 모두
  `제목 → 상태 → 다음 행동 → 이유 → 자동 재개 조건` 순서를 공유한다. 루트와 각
  의미 행은 안정적인 Automation ID, 보이는 라벨과 같은 Name, 보이는 값과 같은
  Value를 제공한다. 모든 값은 줄바꿈하며 후보 보드의 기존 단일 세로 스크롤만 쓴다.

### Planner evidence strip
- **Structure**: 후보 보드의 첫 블록에 다섯 단계 추천 순서, 현재 스토리와 클리어
  보상, 보상 기대값, 조합 판단을 먼저 표시하고 최종 단계에서 물리·마법 비교,
  목표·패키지, 항법 추천 근거를 이어 쌓는다. 별도 창이나 중첩 스크롤을 만들지
  않는다.
- **States**: `sequence-first-rare`, `sequence-story-reward`,
  `sequence-first-legend`, `sequence-rare-reward`, `sequence-top-navigation`과 기존
  `waiting`, `blocked`, `round20-preview`, `round21-actionable`, `committed`,
  `manual-override`, `round24-forced`, `unknown` 상태를 같은 프리미티브로 표시한다.
- **Layout**: 4px 리듬의 두 열 비교와 줄바꿈 가능한 단일 열 근거 행을 사용한다.
  후보 보드 `ScrollViewer`가 이 블록을 포함한 세로 오버플로를 계속 단독 소유한다.
- **Token usage**: 블록/헤더/행 spacing과 padding, 라벨 열 폭, 제목/상태/라벨/값
  글자 크기는 위 `OverlayTheme.Planner*` 토큰만 사용한다. 실제 WPF 트리의 Margin,
  Padding, ColumnDefinition, FontSize를 토큰과 비교해 우회와 값 드리프트를 차단한다.
- **Stable IDs**: `planner-phase`, `planner-sequence`, `planner-story-stage`,
  `planner-story-reward`, `planner-reward-value`, `planner-story-decision`,
  `planner-action`, `planner-blocker`,
  `planner-lane-comparison`, `planner-physical-route`, `planner-magic-route`,
  `planner-package`, `planner-first-legend`, `planner-navigation`,
  `planner-navigation-option`, `planner-recovery`, `planner-interval`,
  `planner-confidence`, `planner-unknown-signals`, `planner-restrictions`,
  `planner-forced-manual`을 UI 자동화와 캡처 행 식별에 공통 사용한다.
- **Semantics**: 각 행은 화면 라벨/값과 동일한 접근성 이름/값을 제공한다. 첫
  희귀함 이후에는 현재 한 단계의 스토리 보상 기대값으로 첫 전설을 지금 조합할지
  보상을 기다릴지 표시한다. 희귀 보상의 실제 결과가 반영되기 전에는 상위·항법
  필드를 `마지막 단계에서 공개`로 잠근다. 항법은 항상 "추천만 제공"으로 표현하며
  게임 내 선택이나 입력을 주장하지 않는다.
- **Sequential card scope**: 첫 전설 단계의 후보 보드는 목표 전설 카드 한 장만
  표시한다. 특별함·희귀함 등 하위 재료는 동급 추천 카드로 노출하지 않는다. 스토리
  보상 진행으로 조합 카드가 잠시 비는 단계는 `현재 단계 진행 중`으로 표시하며
  실제 인식 신호가 없을 때만 `패 인식 대기 중`을 사용한다.
- **Evidence harness**: 모든 상태를 100%로 캡처하고, 125%·150% 및 지원되는
  좁은 스케일에서 상태/ID/표시 필드/접근성 이름·값/DPI/한글 fixture/스크린샷/
  빌드 SHA/판정을 행 단위 JSON 매트릭스로 기록한다.

### Bullet operating board

- **Purpose and reference boundary**: `Bullet operating board`는 Bullet 전용의 다음
  전투 결정을 한 번에 읽게 하는 후보 보드 내부 프리미티브다. 제공된 Bullet 능력
  컴펜디움(SHA-256 `ef69ae6ed92ab0c44085604878c7fdb3612e6c23adf6a76174946d6188596412`)은
  단계·능력·장비·라운드 목표라는 정보 구조의 참고 자료일 뿐, 양피지 질감, 금속
  테두리, 다단 포스터, 캐릭터 초상 또는 픽셀 배치를 복제하는 지시가 아니다. 기존
  540×740 다크 HUD, `OverlayTheme` 색상, 낮은 명도 표면 계층을 보존한다.
- **Placement and scroll contract**: `candidate-board-scroll` 안에서만 렌더링한다.
  바깥 셸의 `Auto + Auto + Auto + * + Auto` 행과 하단 상태줄을 바꾸지 않으며,
  새 창·팝업·보조 세로 `ScrollViewer`·중첩 세로 스크롤을 만들지 않는다. 긴 내용은
  보드의 줄바꿈과 `candidate-board-scroll`의 단일 세로 오버플로로 해결한다.
- **Information order**: 위에서 아래 순서는 고정한다. (1) `Bullet` 이름, 현재
  라운드, 운영 단계와 신뢰도를 담은 한 줄 식별 헤더, (2) 한 문장 `즉시 할 일`, (3)
  이번 단계의 `목표`와 성공 조건, (4) 순서가 있는 최대 세 개의 `실행 경로`, (5)
  현재 우선 `능력/장비`와 이유, (6) 다음 단계로 넘기는 `다음 관문`, (7) 신호 부족
  시에만 `확인 필요`와 회복 행동이다. 1–3은 첫 화면에서 함께 보이게 하며, 4–7은
  같은 보드에서 아래로 이어진다. 비교표·범용 플래너의 기계 필드 나열·동급 후보
  카드 묶음으로 이 순서를 대체하지 않는다.
- **Anatomy**: 보드는 한 개의 `Border`/`Grid` 블록이며 그 안에 헤더, 강조된
  즉시-할-일 행, 목표 행, 번호가 붙은 실행 경로 1–3, 능력/장비 행, 다음 관문 행,
  조건부 확인 필요 행을 둔다. 각 행은 화면 라벨과 값을 한 번만 표시하는
  `label + wrapping value` 구조다. 행 사이에는 구획선 대신 `Hairline`과 기존 여백
  토큰을 사용하고, 금색은 단계/현재 행동/핵심 수치에만 쓴다. 성공은 `Ok`, 주의와
  미확인은 `Warn`에 라벨을 함께 쓰며 색이 의미를 독점하지 않는다.
- **Token contract**: 새 raw 색·spacing·폰트 크기·고정 폭을 추가하지 않는다.
  제목은 `OverlayTheme.PlannerTitleTypeSize`, 단계/신뢰도는
  `OverlayTheme.PlannerStateTypeSize`, 모든 라벨은
  `OverlayTheme.PlannerLabelTypeSize`, 값은 `OverlayTheme.PlannerValueTypeSize`를
  사용한다. 보드 패딩/헤더 간격/행 간격/다음 블록 간격/라벨 열은 각각
  `PlannerBlockPadding`, `PlannerHeaderMargin`, `PlannerRowMargin`,
  `PlannerBlockMargin`, `PlannerLabelColumnWidth`의 의미를 그대로 쓴다. 색은
  `Featured`/`Row`, `Gold`, `White`, `Muted`, `Hairline`, `Ok`, `Warn`만 사용한다.
- **State contract**: 상태 키는 안정적이며 화면 문구는 한국어로 표시한다. 각 상태는
  다음 내용을 반드시 채운다.

- **Locked Bullet sequence**: `early first-legend foundation` →
  `flying-capable legends 2/2 by story 12/round 50` (아직 부족하면 third legend가 air를
  채움) → `boss-kill 2/2` → `external slow 82 + Bullet unique +20` →
  `aura armor reduction target 100 including policy-declared Bullet contribution` →
  `stun target/status including Green Blood +0.3` → `round-50 Bullet craft` →
  `armor reduction → attack speed → attack power`; 항법은 `Bounty Hunter` 기본,
  `Emergency Call` 대체의 recommendation-only 정책이다.

  | State key | 화면 단계명 | 즉시 할 일과 목표 | 경로/능력 초점 | 다음 관문 |
  |---|---|---|---|---|
  | `early-foundation` | 첫 전설 기반 | 초반 첫 전설 기반을 확정한다. | 첫 전설의 현재/목표와 미확인 차단 사유를 표시한다. | 공중 가능 전설 2/2 준비 |
  | `air-mobility` | 공중 전설 | 스토리 12 또는 라운드 50 전 공중 가능 전설 2/2를 만든다. | 두 번째 전설까지 공중 가능 여부를 우선하고, 아직 부족하면 세 번째 전설이 공중 자리를 채운다. | 보스 처치 전설 2/2 |
  | `boss-kill` | 보스 처치 | 보스 처치 전설을 2/2로 맞춘다. | 보스 처치 현재/목표만 표시하고 부차 후보는 숨긴다. | 감속·방어력 감소·기절 제어 수치 확인 |
  | `control-armor` | 제어·방어 | 외부 감속 82와 Bullet 고유 +20, 오라 방어력 감소 100, 기절 상태를 충족한다. | Green Blood +0.3을 포함한 기절과 정책 선언 Bullet 방어력 감소 기여를 함께 표시한다. | 라운드 50 Bullet 조합 또는 명시적 차단 사유 |
  | `ready` | 준비 완료 | 강화는 방어력 감소, 공격 속도, 공격력 순서로 추천한다. | 항법은 Bounty Hunter 기본, Emergency Call 대체로만 추천한다. | 라운드 50 Bullet 조합 |
  | `round50` | 라운드 50 | 라운드 50 Bullet 조합을 즉시 목표로 둔다. | 현재 조합/목표, 강화 순서, 항법 추천을 유지한다. | 조합 결과 확인 또는 명시적 차단 사유 |

- **Todo 25 machine-field contract**: 아래 행은 상태와 관계없이 같은 순서로 표시한다.
  관측값이 있으면 Visible Value와 Automation Value를 같은 `현재/목표` 문장으로
  채우고, 관측값이 없으면 둘 다 `알 수 없음 — {구체적 인식 차단 사유}`로 채운다.
  숫자·기여값·대체 항법은 축약하거나 색만으로 전달하지 않는다.

  | Stable ID | Visible label | Visible value and Automation Value | Automation Name |
  |---|---|---|---|
  | `bullet-board-flying` | 공중 가능 전설 | `현재 {n}/2 · 목표 스토리 12 또는 라운드 50 전 2/2 · 부족 시 세 번째 전설이 공중을 채움` | 공중 가능 전설 |
  | `bullet-board-boss-kill` | 보스 처치 전설 | `현재 {n}/2 · 목표 2/2` | 보스 처치 전설 |
  | `bullet-board-slow` | 외부 감속 | `현재 {n}/82 · Bullet 고유 +20` | 외부 감속 |
  | `bullet-board-armor-reduction` | 오라 방어력 감소 | `현재 {n}/100 · Bullet 정책 선언 기여 +{n}` | 오라 방어력 감소 |
  | `bullet-board-stun` | 기절 | `현재 {상태/수치} · 목표 {상태/수치} · Green Blood +0.3` | 기절 |
  | `bullet-board-craft` | 라운드 50 Bullet 조합 | `현재 {조합 상태} · 목표 라운드 50 Bullet 조합` | 라운드 50 Bullet 조합 |
  | `bullet-board-enhancement` | 강화 순서 | `현재 {강화 상태} · 목표 방어력 감소 → 공격 속도 → 공격력` | 강화 순서 |
  | `bullet-board-navigation` | 항법 추천 | `현재 권장 Bounty Hunter · 목표 Bounty Hunter · 대체 Emergency Call · 추천만 제공` | 항법 추천 |

  `bullet-board-navigation`은 게임 내 이동·선택·입력을 수행하거나 주장하지 않는다.
  `bullet-board-armor-reduction`의 Bullet 기여값은 반드시 정책이 선언한 값을 그대로
  읽어 `{n}`에 넣는다. 각 행의 unknown Value에는 어떤 신호가 없거나 충돌했는지를
  명시해 추측값을 현재/목표처럼 보이게 하지 않는다.

- **Stable automation contract**: UI Automation 식별자는 상태가 바뀌어도 바꾸지
  않는다. 루트는 `bullet-operating-board`, 헤더/단계/라운드/신뢰도는
  `bullet-board-title`, `bullet-board-phase`, `bullet-board-round`,
  `bullet-board-confidence`; 실행 내용은 `bullet-board-action`,
  `bullet-board-objective`, `bullet-board-route-1`, `bullet-board-route-2`,
  `bullet-board-route-3`, `bullet-board-focus`, `bullet-board-gate`,
  `bullet-board-check-needed`, `bullet-board-recovery`, `bullet-board-flying`,
  `bullet-board-boss-kill`, `bullet-board-slow`, `bullet-board-armor-reduction`,
  `bullet-board-stun`, `bullet-board-craft`, `bullet-board-enhancement`,
  `bullet-board-navigation`을 사용한다. 비어 있거나 해당하지 않는 선택 경로 2·3과
  확인/회복 행은 자동화 트리에 빈 값으로 남기지 않고 숨긴다. 상태 키는
  `bullet-board-phase`의 Value에만 기록하며 ID에 붙이지 않는다.
- **Accessible names and values**: 루트 Name은 `Bullet 운영 보드`이고 Value는
  `라운드 {n}, {화면 단계명}, {신뢰도}`다. 각 행의 Name은 보이는 라벨과 정확히
  일치한다(`즉시 할 일`, `목표`, `실행 경로 1`–`3`, `능력/장비`, `다음 관문`,
  `확인 필요`, `회복 행동`). 각 Value는 해당 행의 보이는 값 전문이며, 줄바꿈·축약·색
  상태만으로 의미를 빼지 않는다. `bullet-board-phase` Name은 `운영 단계`, Value는
  화면 단계명이고, `bullet-board-round` Name은 `현재 라운드`, Value는 숫자만 쓴다.
  `bullet-board-confidence` Name은 `추천 신뢰도`, Value는 `높음`/`보통`/`낮음` 중
  하나와 그 근거를 함께 쓴다. 이는 스크린 리더와 자동화 캡처가 같은 결정 문장을
  읽게 하는 계약이다.
- **CJK and scale behavior**: 라벨은 고정 라벨 열에서 줄바꿈하지 않고, 값은
  `TextWrapping=Wrap`과 자연 높이로 한글 의미 단위를 보존한다. 값 열에는
  `TextTrimming`·고정 행 높이·가로 스크롤을 쓰지 않는다. 100%에서는 1–3의 헤더,
  즉시 할 일, 목표가 후보 보드 첫 화면에 함께 보이고 하단 상태줄이 보인다. 125%와
  150%에서는 창 크기나 토큰을 축소하지 않으며, 값이 더 감기면
  `candidate-board-scroll`만 더 스크롤한다. 세 배율 모두에서 라벨/값/아이콘의
  기준선, 전체 한국어 문장, 하단 상태줄, 모든 노출 행의 Automation Name/Value가
  잘리지 않아야 한다.
- **Interaction and debt gate**: 새 애니메이션·자동 게임 입력·클릭으로 인한 게임
  선택 주장을 추가하지 않는다. 키보드/휠은 기존 단일 보드 스크롤 문맥을 유지한다.
  Bullet 보드는 Critical 또는 Major 접근성·인지·색각·가독성 부채를 허용하지 않는다.
  해당 결함은 구현 전 계약 보완 또는 구현 후 수정·재검증의 차단 사유이며, Minor만
  위치·영향 사용자·수정 계획을 가진 채 명시적으로 기록할 수 있다.

### 독립 기본 패 갱신

- 유효한 기본/전체 패를 기존 패 수치 계산기에 표시 전용으로 연결한다. `참고 수치`와 `2.320 미검증`을 명시하고 자동 코칭·목표 충족 판단을 켜지 않는다. 스턴·이감·방깎, 단일·끝딜·폭뎀증·마젠은 기존 공통 컴포넌트로 함께 표시한다. 미확정 기여가 있으면 합계는 `?`, 관측 만료 때는 이전 숫자를 지운다.

- 기본 패가 새로 확인되면 성장 검사 완료를 기다리지 않고 목록을 갱신한다. 기본/성장 내부 검사 전환만으로 상태 문구나 카드 트리를 바꾸지 않는다. 같은 유닛 목록·선택·접힘 상태는 같은 표시 객체를 유지한다.
- 라운드는 `최근 확인 N라운드`라는 별도 표시 기록이다. 같은 연결의 유효한 기본 패가 유지되는 동안만 마지막으로 확인한 라운드를 표시하며, 연결/뷰 변경·기본 패 만료·중지 때 지운다. 새 전체 검사에서 라운드를 확인하지 못한 경우에도 지운다. 이 기록은 인식 계약·코칭·조합 판단 입력을 갱신하지 않는다.
- 성장 결과가 늦거나 만료되어도 유효한 기본 패와 창 위치, 접힘 상태를 유지한다. 기본 패 자체가 만료되거나 인식을 중지하면 기존 대기 상태를 사용한다.
- 기존 레이아웃과 `OverlayTheme` 토큰을 사용하고 창 표시·숨김 애니메이션을 추가하지 않는다.

## 6. Motion & Interaction

- Shared control hit targets use `OverlayTheme.ControlMinHitHeight=32` for ComboBox
  and Expander headers. ComboBox selection consumes its Padding; Expander headers
  forward the parent Foreground without changing keyboard or focus behavior.
- Coach uses a bounded Auto / * / Auto scroll-body-shell: `CoachScroll` owns body
  overflow; pause/review remain in the fixed footer in source/tab order.
  Pattern reference: https://github.com/changeroa/StyleGallery/blob/main/patterns/viewport-shell/scroll-body-shell.md
- Main settings retain the existing 330px expanded rail; closed settings use an
  Auto-sized rail capped at 330px so the main content recovers unused width.

- 새 장식 애니메이션을 추가하지 않는다.
- 버튼·체크박스·콤보박스는 `App.xaml`의 hover/pressed/focus 상태를 따른다.
- 창 드래그, 클릭 통과, 스크롤은 즉시 반응하며 레이아웃 크기를 애니메이션하지 않는다.

## 7. Depth & Surface

혼합 전략을 유지한다: 낮은 명도 차이로 층을 나누고, 핵심 현재 조합에만 얇은 금색
테두리를 쓴다. 그림자는 팝업에만 허용하며 HUD 본문에는 추가하지 않는다.

## 8. Accessibility Constraints & Accepted Debt

- 주요 본문 대비는 WCAG AA를 목표로 한다.
- 모든 긴 한글 문구는 줄바꿈 가능해야 하며 글리프·기준선이 잘리지 않아야 한다.
- 모든 해상도에서 하단 상태줄과 버전이 창 내부에 남아야 한다.
- 플래너 차단·미확인 사유는 색만으로 구분하지 않고 라벨과 접근성 값으로 함께
  전달한다. 후보 보드가 유일한 세로 스크롤 소유자라 키보드/휠 문맥이 바뀌지 않는다.

| Item | Location | Why accepted | Exit |
|---|---|---|---|
| 키보드 포커스 시각 검증 미자동화 | WPF 오버레이 | 기존 테스트 하네스 한계 | 전용 WPF 접근성 하네스 도입 시 |

## 일반 모드 진행과 조합 안내 (2026-09-15)

- 위의 자동 추천 제한 중 일반 모드의 표시 전용 진행은 이번 요청으로 대체한다. 새로 확인한 패에서 희귀함 → 전설·히든 → 상위 → 주력 지원 단계로 진행한다. 같은 판에서 재료 소비·일시 실패로 되돌아가지 않으며 수동 탐색에는 '현재 단계로' 복귀 버튼을 제공한다. 게임 인식·코칭·수치 검증 권한은 바꾸지 않는다.
- 카드의 기본 순서는 부족한 기본 재료 수와 남은 조합 거리다. 스토리 빠름은 원문의 깃발을 연결한 별도 라벨이다. 물리·마법·복합·유형 미확인을 명시한다. 민트는 추천과 짧은 이유, 메인 파랑/오버레이 금색은 직접 선택이라는 기존 구분을 유지한다.
- 첫 상위가 여러 개 함께 관측되면 시간 순서를 추측하지 않고 관측 후보에서 주력 기준을 선택하게 한다. 전투 수치의 우선순위는 주력의 근거 있는 유형으로만 바꾼다. 주력 전에는 물리·마법 미확정이며 모든 기존 항목과 참고/미확정 표시를 유지한다.
- 맵 이동 역할은 공중이동·지형무시이동·순간이동을 별도로 표시하고 인식된 제공 유닛을 함께 쓴다. 원문 역할은 수치로 변환하지 않는다. 긴 역할 설명은 접을 수 있다.
- 선택 상세의 첫 콘텐츠는 조합 순서다. 안흔함부터 목표까지 필요한 단계, 재료 수량, 선택 유닛, 키/채팅 명령, 횟수와 결과를 순서대로 표시한다. 키·선택 유닛 근거가 없으면 미확인으로 표시한다. 상세의 기존 독립 스크롤을 재사용하며 후보 목록 스크롤과 선택을 보존한다. 출처와 긴 역할 정보는 그 아래 접힌 설명에 둔다.
- 기존 NormalCandidateView 색/폰트/여백과 OverlayTheme의 수치 컴포넌트를 재사용한다. 새 창·자동 입력·애니메이션을 만들지 않는다. 현재 패·선택·단계가 같으면 카드와 조합 트리를 재생성하지 않는다.

- 메인에서 목표를 선택하면 폭 640 DIP 이상에서 후보(3):조합 순서(2)의 좌우 패널로 나눈다. 상세를 짧은 하단 창에 가두지 않고 첫 조합의 선택·키·재료를 함께 읽게 한다. 메인 후보와 상세는 각각 기존 스크롤을 유지한다. 오버레이는 기존 상하 구성을 유지한다. 주력 미확정/복합의 전투 핵심 지표는 동등한 2×2 배열, 물리/마법 확정은 기존 3열 배열이다.

## 일반 모드 조합 카드 가독성 보완 (2026-09-15 사용자 피드백)

- 사용 맥락: 게임을 보며 짧게 시선을 옮기는 플레이어. 첫 시선에 선택할 유닛, 키, 횟수, 결과를 판독해야 한다. 기존 9–11px 문장 목록은 사용자가 거절했다.
- 조합 단계를 재사용 가능한 카드로 표시한다. 번호와 재료 상태, 선택 유닛 초상 → 큰 키캡/횟수 → 결과 초상, 재료 타일 순서다. 번들 UnitImageFactory 초상만 사용하며 이름·수량도 함께 표시한다. 선택·결과·재료를 장식용 이미지로 대체하지 않는다.
- Craft typography: 제목/단계 핵심 16, 유닛명/행동 14, 메타/재료 12, 키캡 22 DIP; Malgun Gothic 유지. 숫자/키는 Consolas, Malgun Gothic. 간격 4/8/12/16, 카드/타일 반경 8/4, 초상 36/재료24/목표40, 키캡 최소36, 재료타일152 DIP. 긴 이름/명령/조건은 줄바꿈하고 고정 행 높이·가로 스크롤·말줄임을 쓰지 않는다.
- 기존 OverlayTheme.PlanSurface/PlanRaised/PlanLine/PlanText/PlanSecondary/PlanSuccess/PlanWarning 색을 사용한다. 인식 재료는 녹색과 보유 라벨, 부족은 주황과 부족 수량, 선행 단계 산출은 파랑과 앞 단계 라벨로 구분한다. 앞 단계 예상 수량을 실제 보유로 표시하지 않는다. 첫 카드만 다음 순서로 강조하며 조합 가능 확정을 뜻하지 않는다.
- 반복 면책 문단은 접힌 출처에 모으고, 키/선택 미확인 및 조건·자원 확인은 해당 카드에 남긴다. 조합키는 안내용 표시이며 클릭 입력 기능이 없다.
- 목표 선택 시 메인 좌우 폭을 후보2:조합3으로 조정한다. 오버레이 조합 영역은 전체 NormalView 높이의55%, 최소240/최대340 DIP를 사용한다. 각 영역의 기존 단일 스크롤과 창 크기, 패 갱신 시 객체/선택/스크롤 보존 계약은 유지한다.
- 검증: 실제 WPF의 희귀 단일 단계, 여러 조합, 장문 유닛/채팅, 부족/보유/예상/자원, 미확인키, 920폭 메인과540폭 오버레이. 키·횟수·재료가 사진/색상 없이도 접근성 트리에 남아야 한다. UI 기능 검증 점수와 사용자의 시인성 평가는 별개로 보고한다.

- 좁은 창 보완: 목표 헤더는32초상·유닛명·단계 수 한 줄을 기본으로 하며 긴 이름만 감싼다. 카드 padding8, 행동행 위아래4, 재료타일136 DIP로 조정한다. 920×620 메인의 첫 희귀 조합에서 선택/키/결과와 세 재료가 첫 화면에 함께 보여야 한다.

- 재료 타일 최종 폭144 DIP. 이름과 수량을 분리해 긴 유닛명이 수량 때문에 한 글자만 다음 줄로 밀리지 않게 한다. 수량은 상태 앞에 흰색으로, 보유/부족/예상 상태는 기존 의미 색과 라벨로 표시한다.

## 일반 모드 베타 배포 · 부족 재료 우선 (2026-09-15)

- 목표 헤더 다음에 먼저 모을 재료를 배치한다. 전체 조합에서 실제로 부족한 말단 재료만 초상24/이름12/부족수량16으로 표시하며, 선행 조합 결과를 구매할 재료처럼 중복 집계하지 않는다. 자원은 별도 줄에 필요량과 보유 미확인을 함께 표시한다. 모든 재료는 기본 펼침으로 보이고 조합 순서를 볼 때 접을 수 있다. 목표를 바꾸면 다시 펼친다. 기존 스크롤 하나를 유지한다.
- 후보에는 가장 많이 부족한 재료 이름·수량과 나머지 종류 수를 표시하고 선택 시 전체 목록을 보여준다. 미확인 조합은 재료 확보로 표시하지 않는다. 카드 배경/여백/글꼴은 기존 Craft 토큰을 재사용한다.
- 베타에서는 일반만 활성화한다. 초보자·대깨·매뉴얼은 잠금 라벨과 비활성 상태, 단계적 공개 설명을 제공한다. 저장된 다른 모드도 앱 시작 시 일반으로 열되 공략/수동 목표 설정은 보존한다. 잠긴 버튼·설정 선택·내부 모드 변경 요청을 모두 차단한다.
- 좁은 창에서는 부족 목록의 첫 화면 접근성이 조합 카드 전체 노출보다 우선한다. 전체 목록 및 조합 키/재료는 기존 스크롤로 접근하고, 부족 재료가 없을 때는 기존 첫 카드 노출을 유지한다. 새 애니메이션이나 게임 입력은 없다.

- 베타 카드에서는 중복된 ‘N개 더 필요’ 줄을 제거하고 이름 / 수량·다른 재료 종류의 두 줄 요약으로 대체한다. 오버레이 조합 영역은 높이50%, 최소240/최대320 DIP로 두어 후보의 첫 부족 수량까지 볼 공간을 확보한다. 전체 부족 목록은 목표 바로 아래의 기존 스크롤에 유지한다.

## 부족 재료 슬롯 · 로컬 시안 (2026-09-15 사용자 선택)

- 사용자가 큰 유닛 초상 + 부족 수량 배지를 선택했다. 작은 가로 텍스트 칩을 56 DIP 초상, 18 DIP 숫자 배지, 아래 12 DIP 유닛명으로 바꾼다. 슬롯 폭80, 아이콘/배지 영역64, 슬롯 간격8/하단8, 배지 padding4/2, 최소폭28, 반경4. 수량은 대비 높은 PlanWarning/PlanSurface, 이름은 PlanText, 보조 정보는 PlanSecondary를 사용한다.
- 슬롯에는 장식용 카드 배경을 두지 않는다. 초상 프레임과 수량 배지만 경계를 가지며 마우스 커서/hover를 버튼처럼 꾸미지 않는다. 이름·수량·접근성 이름 모두 실제 MissingMaterials에서 제공한다. 숫자만으로 의미를 추측하지 않도록 섹션은 ‘부족 재료’로 명명한다.
- 헤더는 부족 재료 / N종 · M개 두 정보만 보인다. 재료 순서는 등급/이름의 안정적 순서로 고정하며 수량 변화로 재정렬하지 않는다. 감소하여 없어지는 재료만 빠진다. 초상은 번들 UnitImageFactory를 사용한다. 긴 이름은 줄바꿈하고 미등록 초상에는 기존 대체 표시를 사용한다.
- 자원은 슬롯과 분리해 필요량 / 보유 미확인 줄로 표시한다. 전체 목록은 기본 펼침이며 기존 접기와 스크롤 계약, 같은 패 렌더 재사용, 조합 카드/모드 잠금/수치 창을 보존한다. 큰 초상 때문에 인식 권한이나 재료 계산을 바꾸지 않는다.
- 검증: 10종 많은 재료, 2종 소량, 1종, 추가재료 없음, 만료, 좁은 메인920와 오버레이540, 접고 조합 보기. 수량 배지·이름·초상이 서로 가리지 않고 주목표/재료 계층이 구분되어야 한다.
