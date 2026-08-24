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

## 4. Spacing & Layout

- 기준 단위는 4px이며 기존 4/6/8/10/12px 간격을 유지한다.
- 추천 창 설계 크기는 540×740, 패 수치 창은 228×700이며 `UiScale`로 화면에 맞춘다.
- 추천 창은 `Auto + Auto + Auto + * + Auto` 세로 셸이다.
- 상단·현재 조합·조합 흐름·하단 상태줄은 내용 높이를 보존한다.
- 후보 보드의 `ScrollViewer`가 남은 높이와 세로 오버플로를 단독 소유한다.
- 별표 행에 양수 `MinHeight`를 강제해 하단 상태줄을 창 밖으로 밀지 않는다.

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

| Item | Location | Why accepted | Exit |
|---|---|---|---|
| 키보드 포커스 시각 검증 미자동화 | WPF 오버레이 | 기존 테스트 하네스 한계 | 전용 WPF 접근성 하네스 도입 시 |
