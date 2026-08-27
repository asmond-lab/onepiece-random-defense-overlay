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
- **States**: 선택, 현재 후보, 부족, 완료.
- **Layout**: 남은 별표 행을 차지하며 세로 스크롤을 단독 소유한다.

### Planner evidence strip
- **Structure**: 후보 보드의 첫 블록에 단계 배너, 현재 행동/차단 사유, 물리·마법
  비교, 목표·패키지, 항법 추천 근거를 기계 필드 행으로 쌓는다. 별도 창이나 중첩
  스크롤을 만들지 않는다.
- **States**: `waiting`, `blocked`, `round20-preview`, `round21-actionable`,
  `committed`, `manual-override`, `round24-forced`, `unknown` 여덟 상태를 같은
  프리미티브로 표시한다.
- **Layout**: 4px 리듬의 두 열 비교와 줄바꿈 가능한 단일 열 근거 행을 사용한다.
  후보 보드 `ScrollViewer`가 이 블록을 포함한 세로 오버플로를 계속 단독 소유한다.
- **Token usage**: 블록/헤더/행 spacing과 padding, 라벨 열 폭, 제목/상태/라벨/값
  글자 크기는 위 `OverlayTheme.Planner*` 토큰만 사용한다. 실제 WPF 트리의 Margin,
  Padding, ColumnDefinition, FontSize를 토큰과 비교해 우회와 값 드리프트를 차단한다.
- **Stable IDs**: `planner-phase`, `planner-action`, `planner-blocker`,
  `planner-lane-comparison`, `planner-physical-route`, `planner-magic-route`,
  `planner-package`, `planner-first-legend`, `planner-navigation`,
  `planner-navigation-option`, `planner-recovery`, `planner-interval`,
  `planner-confidence`, `planner-unknown-signals`, `planner-restrictions`,
  `planner-forced-manual`을 UI 자동화와 캡처 행 식별에 공통 사용한다.
- **Semantics**: 각 행은 화면 라벨/값과 동일한 접근성 이름/값을 제공한다. 항법은
  항상 "추천만 제공"으로 표현하며 게임 내 선택이나 입력을 주장하지 않는다.
- **Evidence harness**: 여덟 상태 모두 100%로 캡처하고, 125%·150% 및 지원되는
  좁은 스케일에서 상태/ID/표시 필드/접근성 이름·값/DPI/한글 fixture/스크린샷/
  빌드 SHA/판정을 행 단위 JSON 매트릭스로 기록한다.

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
