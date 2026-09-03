# OrandOverlay Design System

## 1. Atmosphere & Identity

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
- 추천 창 설계 크기는 540×740, 패 수치 창은 228×700이며 `UiScale`로 화면에 맞춘다.
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

### Overlay chrome
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
- **Token usage**: 바깥 `Current craft well`의 `Featured`/`Gold`/`Hairline` 표면을
  공유 `CurrentCraftWell` 스타일로 사용한다. 이 스타일은 `Featured`/`Gold`,
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

## 6. Motion & Interaction

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
