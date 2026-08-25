# 초보 클리어 안전 추천과 패수치 컴팩트 모드

## 개요

초보 사용자가 TMO를 알트탭하지 않고도 55라운드 전후 생존 수치를 갖추도록
추천 순서를 바꾸고, 추천 보드를 숨긴 채 패수치만 유지하는 WPF 표시 모드를 추가한다.

기준 브리프: `.omo/drafts/beginner-clear-and-stats-compact.md`

## 고정밀 검토 기록

- Momus: 세션의 start-work gate가 먼저 열려 시스템상 실행 불가.
- architect 대체 검토: 조직 OAuth 제한으로 실행 불가.
- slow high-accuracy review 3회 수행.
- 1차/2차 지적: 표시 상태 중복, secondary top 결정성, carry fixture 생성 계약,
  마방깎 distinct semantics, smoke baseline, final HEAD 재검증.
- 모든 계획 지적 반영 완료.
- 최종 검토의 남은 요구는 구현 증거(xUnit/build/smoke/manual/visual/release SHA)뿐이며
  계획상 실행 blocker는 없음.

## 사용자 결과

1. 물딜/마딜 목표가 `1상위 권장`, `다상위 가능`, `다상위 필요`, `판단 보류`로 표시된다.
2. 방깎·이감·스턴 등 55라 준비 수치가 부족하면 두 번째 상위보다 지원 유닛이 먼저 나온다.
3. 핵심 수치가 충족된 뒤에만 허용된 다상위 추천이 열린다.
4. `패수치만` 모드에서 추천 창은 사라지고 실시간 패수치 창만 남는다.
5. 표시 모드·위치·클릭 통과 설정이 재시작 후 복원된다.

## 범위

### 포함

- 목표 상위의 carry 분류 정책과 권위 데이터
- 자동 항법의 표본 수 편향 제거
- 물딜/마딜 55라 준비 상태 및 부족 수치 계산
- 두 번째 상위 추천 잠금/해제
- 패수치와 추천 엔진 수치 계산의 공통 경로
- 추천 카드의 carry/준비 상태 표시
- `Full`, `StatsOnlyCompact`, `Hidden` 표시 모드
- 독립 Stats 창 visibility, 크기, 위치, 클릭 통과, 설정 영속화
- 순수 정책 테스트, WPF 실제 사용 QA, 버전/Release 검증

### 제외

- 55라 목표 수치 자체의 재밸런싱
- 새로운 클리어 표본 수집 계약이나 서버 스키마 변경
- TMO 조합식/명령어 변경
- 추천 보드 전체 디자인 개편
- 신규 기능과 무관한 일반 리팩터링

## 실행 공통 규칙

- 줄 번호는 탐색 힌트다. 각 작업 시작 시 파일의 명명된 symbol을 AST/LSP 또는 직접 읽기로
  다시 찾아 현재 위치를 확인한다.
- 테스트를 삭제·skip·완화하지 않는다.
- 기존 `비비 변화 폼` smoke 실패는 T0에서 제품 계약을 고쳐 assertion을 그대로 green으로
  만든 뒤 기능 작업을 시작한다.

## 결정 사항

### Carry 분류

- `GoalCarryMode`: `SoloPreferred`, `MultiAllowed`, `MultiRequired`, `Unknown`.
- 별도 `Data/goal-carry-policy.json`을 권위 레이어로 사용한다.
- TMO 42479의 명시 문구와 검증된 프로젝트 정책만 명시 분류에 사용한다.
- 명시 근거가 없으면 `Unknown`; 초보 안전 모드에서는 `Unknown`을 단일 상위 우선으로 취급한다.
- committed TMO snapshot에서 top-grade 목표를 전수 생성한다. description의 독립 단어
  `솔딜` 또는 `1상위`는 `SoloPreferred`, 독립 단어 `다상위`는 `MultiAllowed`,
  둘 다 있거나 어느 것도 없으면 `Unknown`이다.
- `MultiRequired`는 초기 fixture에서 0개로 고정하며 별도 권위 근거가 추가될 때만 허용한다.
- fixture에는 source snapshot SHA256과 생성 규칙 version을 기록하고 수동 편집 대신
  재생성 diff로 검토한다.
- 클리어 표본의 `SoloTop`/`MultiTop` 개수는 설명용 보조 증거이며 분류를 뒤집지 않는다.

### 55라 준비

- 물딜 기본 목표: 방깎 `211`, 이감 `102`, 안정 스턴 `1.4`.
- 마딜: 마방깎 공급원 보유 + 이감 `102` + 안정 스턴 `1.4`.
- 기존 목표별 override가 있으면 `GoalStrategyCalculator`의 값을 우선한다.
- 두 번째 상위 후보는 주딜 목표와 다른 top-grade 유닛으로 정의한다.
- 준비 미달이면 두 번째 상위 후보를 숨기지 않고 `보류`로 표시하되 지원 후보보다 아래에 둔다.
- `MultiRequired`도 생존 핵심 수치 전에는 두 번째 상위를 열지 않는다.

### 표시 모드

- `OverlayDisplayMode`: `Full`, `StatsOnlyCompact`, `Hidden`.
- 별도 `userHidden` 상태를 추가하지 않는다. 숨김 의도는 `OverlayDisplayMode.Hidden` 하나로 표현한다.
- `LastVisibleOverlayDisplayMode`는 `Full` 또는 `StatsOnlyCompact`만 저장한다.
- 이전 설정 파일에 필드가 없으면 `Full`.
- `StatsOnlyCompact`는 Stats 창만 표시하고 추천 창은 숨긴다.
- 컴팩트 Stats 창은 준비 상태, 스턴, 이감, 방깎/마방깎, 보조 패수치 칩만 유지한다.
- 리롤·긴급·특별함·그린블러드 패널은 컴팩트 모드에서 `Collapsed`.
- 인식 availability는 transient 입력이며 저장된 display mode를 절대 변경하지 않는다.

### 표시 상태 전이표

| 이벤트 | 저장 mode | last visible | 추천 창 | Stats 창 |
|---|---|---|---|---|
| `전체` 선택, available | Full | Full | 표시 | 표시 |
| `패수치만` 선택, available | StatsOnlyCompact | StatsOnlyCompact | 숨김 | 표시 |
| `숨김` 선택/현재 보이는 창 닫기 | Hidden | 이전 값 유지 | 숨김 | 숨김 |
| hotkey, 현재 visible | Hidden | 이전 visible mode | 숨김 | 숨김 |
| hotkey, 현재 Hidden | last visible | 이전 값 | mode대로 | mode대로 |
| 일시 인식 불가/세션 종료 | 변경 없음 | 변경 없음 | 숨김 | 숨김 |
| 인식 복귀, 저장 mode Full | Full | Full | 표시 | 표시 |
| 인식 복귀, 저장 mode StatsOnlyCompact | StatsOnlyCompact | StatsOnlyCompact | 숨김 | 표시 |
| 인식 복귀, 저장 mode Hidden | Hidden | 이전 값 유지 | 숨김 | 숨김 |

## 의존성 그래프

| 작업 | 의존성 | 병렬 가능 |
|---|---|---|
| T0 기존 smoke baseline 복구 | 없음 | 다른 작업 전 선행 |
| T1 Carry 정책 모델 | T0 | T2, T6와 병렬 |
| T2 공통 준비 수치 계산기 | T0 | T1, T6와 병렬 |
| T3 자동 항법 분류 적용 | T1 | T2와 병렬 |
| T4 두 번째 상위 게이트 | T1, T2 | 없음 |
| T5 준비 상태 추천 UI | T2, T4 | T7과 병렬 |
| T6 표시 모드 설정 모델 | T0 | T1, T2와 병렬 |
| T7 창 visibility 분리 | T6 | T5와 병렬 |
| T8 Stats 컴팩트 렌더링 | T2, T6, T7 | 없음 |
| T9 통합 회귀·실전 QA | T0, T3, T4, T5, T8 | 없음 |
| T10 버전·Release | T9 | 없음 |

## 실행 작업

### T0. 기존 비비 변화 폼 smoke baseline 복구

**참조**

- `SmokeTests/Program.cs`의 실패 메시지
  `비비 변화 폼은 메모리 인식만 희귀 비비로 통합하고 원래 레시피는 보존`
- `RawcodeAliases`
- `RawcodeCodec.DynamicUnitId`
- `DataCatalog.Unit`
- 계획 기준 commit `44c5e14`

**구현**

1. 현재 assertion을 그대로 둔 채 실패를 재현한다.
2. 비비 변화 rawcode `W50h`, 희귀 비비 canonical rawcode, 원래 레시피 보존의 세 계약 중
   어떤 제품 동작이 깨졌는지 맵/TMO 데이터와 현재 alias 로직으로 확정한다.
3. 실패 계약을 xUnit 회귀 테스트로 추가하고 제품 코드를 최소 수정한다.
4. smoke assertion을 삭제·조건부 통과·allowlist하지 않는다.

**테스트 — TDD**

- 변화 폼 메모리 ID canonicalization.
- 변화 폼 catalog tier/recipe 보존.
- full SmokeTests exit code 0.

**수용 기준**

- 기존 smoke assertion 문구와 조건을 유지한 상태로 전체 SmokeTests가 exit code 0이다.

**QA**

```powershell
dotnet test OrandOverlay.Tests/OrandOverlay.Tests.csproj --no-restore --filter "FullyQualifiedName~Vivi"
dotnet run -c Release --project SmokeTests/OrandOverlay.SmokeTests.csproj
```

**Commit: REQUIRED**

`비비 변화 폼 smoke baseline 복구`

---

### T1. Carry 정책 모델과 데이터 추가

**참조**

- `Models.cs:297-322`
- `GoalStrategyCalculator.cs:80-190`
- `DataCatalog.cs`
- `Data/tmo-unit-catalog.json`
- `OrandOverlay.Tests/PhysicalTopRecommendationPolicyTests.cs`

**구현**

1. `GoalCarryMode` enum과 `GoalCarryPolicyEntry`를 추가한다.
2. `Data/goal-carry-policy.json`을 만들고 `goalUnitId`, `mode`, `source`, `reason`을 저장한다.
3. `Tools/GoalCarryPolicyGenerator`는 effective `DataCatalog.AllUnits`를 읽고 기존
   top-grade 판정을 `TopGradePolicy.IsTopGrade` 공용 symbol로 추출해 그대로 사용한다.
   CLI는 canonical JSON을 stdout에만 출력하고 파일을 직접 쓰지 않으며 `--check <path>`는
   committed fixture와 byte 비교만 한다.
4. 정책 파일은 로드 시점의 모든 알려진 top-grade 목표 ID를 정확히 한 번 포함해야 한다.
   누락·중복·미등록 목표·알 수 없는 enum은 시작 실패다.
5. 명시 근거가 없는 목표도 파일에 `Unknown` entry로 materialize한다.
6. 생성 결과의 source SHA256·rule version을 검증하고, 재생성 결과와 committed fixture가
   다르면 테스트를 실패시킨다.
7. `Unknown`은 초보 안전 정책에서 `SoloPreferred`와 같은 제한을 적용하되 UI 문구는
   `판단 보류`로 유지한다.
8. 기존 사용자 목표/항법 설정과 하위 호환한다.
9. source identity는 `Data/tmo-unit-catalog.json`, `Data/tmo-unit-additions-42479.json`,
   `Data/game-data.demo.json` 각 파일의 raw bytes SHA256을 path ordinal 순서로 저장한다.
10. token regex는
    `(?:^|[\\s·,/()\\[\\]])(?:솔딜|1상위)(?=$|[\\s·,/()\\[\\]])`와
    `(?:^|[\\s·,/()\\[\\]])다상위(?=$|[\\s·,/()\\[\\]])`를 사용한다.
11. canonical JSON은 UTF-8 BOM 없음, LF, 2-space indent, property 순서
    `schemaVersion`, `generatorVersion`, `sourceFiles`, `entries`; entry property 순서
    `goalUnitId`, `mode`, `source`, `reason`; entries는 `goalUnitId` ordinal 정렬이다.
12. 같은 goal ID가 두 번 생성되면 즉시 실패하고, 생성 ID set이 effective top-grade ID set과
    정확히 같아야 한다.

**테스트 — TDD**

- 중복/미등록 목표/잘못된 enum이 실패한다.
- 명시 solo/multi/unknown 분류가 정확히 로드된다.
- 모든 top-grade 목표가 파일에 존재하며 한 항목을 삭제하면 로드가 실패한다.
- 근거 없는 목표는 명시 `Unknown` entry로 로드된다.
- 동일 snapshot으로 두 번 생성한 fixture가 byte-for-byte 동일하다.
- `--check Data/goal-carry-policy.json` exit code 0.
- TMO 설명 텍스트를 런타임에서 휴리스틱으로 재파싱하지 않는다.

**수용 기준**

- 모든 상위 목표가 네 가지 mode 중 하나로 결정된다.
- 표본 수만으로 mode가 바뀌지 않는다.

**QA**

```powershell
dotnet test OrandOverlay.Tests/OrandOverlay.Tests.csproj --no-restore --filter "FullyQualifiedName~GoalCarryPolicy"
```

**Commit: REQUIRED**

`추천 목표 carry 정책 모델 추가`

---

### T2. 공통 55라 준비 수치 계산기 추가

**참조**

- `GoalStrategyCalculator.cs:20-25,47-76,130-190`
- `InventoryStatsCalculator.cs:35-95`
- `RecommendationEngine.cs:747-914`
- `OrandOverlay.Tests/CompletedGoalRecommendationTests.cs:64-153`

**구현**

1. `CombatReadinessCalculator`와 immutable `CombatReadiness` 결과를 추가한다.
2. 결과에 damage type, 현재/목표 스턴·이감·방깎 또는 마방깎, deficit, `IsReady`를 둔다.
3. 마방깎은 현재 계약이 공급원 존재형이므로 `CurrentMagicArmorSources`,
   `RequiredMagicArmorSources = 1`, `MissingMagicArmorSource`로 표현하고 UI에는
   `마방깎 공급원 0/1`을 표시한다.
4. 인정 공급원은 기존 `StrategyMetrics.MagicArmorReductionSourceCount`가 세는 완성·보유
   유닛 중 official ability `마법 방어력 감소 > 0`인 유닛이다. 미완성 추천 후보는 제외하고,
   목표 자체도 실제 inventory에 완성 보유된 경우만 포함한다.
5. 조건부/발동형 여부는 기존 StrategyMetrics 판정을 그대로 재사용한다. combined inventory를
   canonical UnitId로 정규화한 뒤 qualifying UnitId의 distinct set을 만든다. 같은 유닛 복사본,
   manual/automatic 중복, rawcode alias는 한 공급원으로 세고 서로 다른 qualifying UnitId만
   증가한다. readiness는 distinct count `>= 1`에서 충족된다.
6. 기존 `GoalStrategyCalculator.StrategyMetricsFor`의 조건부 수치 규칙을 공통 계산기로
   이동하거나 공용 API로 노출한다.
7. `InventoryStatsCalculator`와 추천 엔진이 동일 결과를 소비하게 한다.
8. 베르고 액티브, 징베 조건부 방깎, 발동 이감 등 기존 합산 계약을 유지한다.

**테스트 — TDD**

- 물딜 `211/102/1.4` 경계 바로 아래/정확히/초과.
- 마딜은 마방깎 공급원 `0/1` + `102/1.4`.
- 마방깎 조건부/발동형, 미완성 후보, 보유 목표, 복사본, alias,
  manual/automatic 중복 경계 사례.
- 조건부 방깎·이감이 UI와 추천에서 같은 값.
- 완성된 목표 자체의 수치가 중복 합산되지 않는다.

**수용 기준**

- 동일 inventory/goal 입력에서 UI 값과 추천 readiness 값이 완전히 같다.
- 기존 물딜/마딜 정책 테스트가 유지된다.

**QA**

```powershell
dotnet test OrandOverlay.Tests/OrandOverlay.Tests.csproj --no-restore --filter "FullyQualifiedName~CombatReadiness|FullyQualifiedName~InventoryStats"
```

**Commit: REQUIRED**

`55라 준비 수치 계산 경로 통합`

---

### T3. 자동 항법에서 표본 수 강제 전환 제거

**참조**

- `MainWindow.xaml.cs:541-565`
- `ClearBuildStats.cs:69-78,129-148,205-263`
- `NavigationAdvisor.cs:103-176`
- `RecommendationMatrixInvariantTests.cs:113-120`

**구현**

1. `RecommendNavigationForGoal`의 `multi > solo` 직접 분기를 제거한다.
2. `GoalCarryMode`를 먼저 적용한다.
3. 현재 사용자가 고른 항법이 mode와 충돌하지 않으면 그대로 둔다.
4. `MultiAllowed`는 자동으로 다상위를 강제하지 않고 선택 가능한 설명만 제공한다.
5. `MultiRequired`만 다상위 계열을 자동 권장한다.
6. scoped sample이 12 미만이라 fallback을 사용한 경우 `학습됨`으로 표기하지 않는다.

**테스트 — TDD**

- multi 표본이 더 많아도 `SoloPreferred`가 단일 상위를 유지한다.
- `Unknown`이 표본 수 때문에 다상위로 바뀌지 않는다.
- `MultiRequired`는 다상위 항법을 권장한다.
- 사용자가 이미 호환 항법을 선택했으면 자동 변경하지 않는다.

**수용 기준**

- `multi sample > solo sample`만으로 다상위가 선택되는 경로가 없다.

**QA**

```powershell
dotnet test OrandOverlay.Tests/OrandOverlay.Tests.csproj --no-restore --filter "FullyQualifiedName~Navigation|FullyQualifiedName~GoalCarry"
```

**Commit: REQUIRED**

`carry 분류 기반 자동 항법 적용`

---

### T4. 두 번째 상위 추천에 준비 게이트 적용

**참조**

- `RecommendationEngine.cs:98-102,148-184,747-914`
- `GoalStrategyCalculator.cs:164-190`
- `PhysicalTopRecommendationPolicyTests.cs:18-150,286-300`
- `CompletedGoalRecommendationTests.cs:64-153`

**구현**

1. 현재 목표와 다른 top-grade 후보를 `secondary top`으로 분류한다.
2. `CombatReadiness.IsReady == false`면 secondary top은 active recommendation 결과에서 제외한다. UI에는 개별 top 카드 대신 단일 `2상위 보류` 요약만 전달한다.
3. 부족 수치별 지원 후보 순서는 안정 스턴 → 방깎/마방깎 → 이감 → 보잡/광잡의 기존 정책을 유지한다.
4. `SoloPreferred`는 준비 완료 후에도 secondary top을 자동 추천하지 않는다.
5. `MultiAllowed`는 준비 완료 후 secondary top을 허용한다. `MultiRequired`는 준비 완료 후 기존 점수 순위로 secondary top을 최소 한 장 포함한다.
6. 다상위 항법을 수동 선택해도 readiness gate는 우회할 수 없게 한다.
7. 준비 미달인데 deficit을 채우는 지원 후보가 0개면 secondary top으로 fallback하지 않고 `현재 패에서 보완 후보 없음` 상태를 반환한다.
8. 동점 지원 후보는 기존 결정적 정렬 키(완성도, 역할 기여, unit ID)를 유지하고 중복 제거 후에도 secondary top을 승격하지 않는다.
9. secondary top 후보는 기존 candidate pool에서 primary goal과 동일 ID, 이미 완성 보유한
   ID, cluster child, suppressed tier를 제거한 뒤 `GoalUnitId`로 중복 제거한다.
10. readiness 이후 secondary top 안정 정렬 키는 `Score desc`,
    `CompletionRatio desc`, `MissingLeafCount asc`, `GoalUnitId ordinal`이다.
11. `MultiRequired`는 위 정렬의 첫 secondary top 정확히 한 장을 모든 active support 추천
    뒤에 삽입한다. `take`가 가득 찼으면 primary goal과 support를 보존하고 가장 낮은 나머지
    비필수 후보 한 장을 제거한다. 제거 가능한 후보가 없으면 secondary top을 삽입하지 않고
    보류 요약을 유지한다.
12. `MultiAllowed`는 별도 슬롯을 예약하지 않고 readiness 이후 기존 결정 정렬에 참여한다.

**테스트 — TDD**

- 방깎, 이감, 스턴 각각 하나만 부족한 3개 경계 사례.
- 세 수치가 부족한 경우 지원 추천만 앞선다.
- 지원 후보가 없는 경우 추천 요약만 남고 secondary top이 나타나지 않는다.
- 정확히 목표를 달성하면 `MultiAllowed`의 두 번째 상위가 열린다.
- `SoloPreferred`는 목표 달성 후에도 자동 secondary top이 없다.
- 마딜 readiness가 물딜 방깎을 요구하지 않는다.
- 동일 입력 20회 반복에서 secondary top ID와 순서가 동일하다.
- `MultiRequired`의 삽입 슬롯과 `take` 초과 방지.

**수용 기준**

- 55라 준비 미달 상태에서 두 번째 상위가 지원 유닛보다 앞서는 결과가 없다.

**QA**

```powershell
dotnet test OrandOverlay.Tests/OrandOverlay.Tests.csproj --no-restore --filter "FullyQualifiedName~PhysicalTopRecommendationPolicy|FullyQualifiedName~SecondaryTopGate"
```

**Commit: REQUIRED**

`준비 수치 전 두 번째 상위 추천 보류`

---

### T5. 추천 화면에 carry·준비 상태 표시

**참조**

- `OverlayWindow.xaml:1-75`
- `OverlayWindow.xaml.cs:224-313`
- `RecommendationBoard.cs`
- `RecommendationPresentation.cs`

**구현**

1. 추천 헤더에 carry badge를 추가한다.
2. 준비 미달이면 `55라 준비 미달 — 2상위 보류`와 deficit을 표시한다.
3. secondary top 보류 카드는 지원 후보 뒤 별도 영역 또는 비활성 badge로 표시한다.
4. 물딜/마딜에 따라 방깎/마방깎 라벨을 바꾼다.
5. 기존 조합 흐름·첫 희귀함 포커스·클릭 선택을 변경하지 않는다.

**테스트 — tests-after**

- presentation helper의 carry/readiness 문자열.
- 물딜/마딜 deficit 라벨.
- 준비 완료 시 경고가 사라진다.

**수용 기준**

- 초보자가 왜 두 번째 상위가 보류됐는지 숫자로 알 수 있다.

**QA**

- 실제 WPF에서 FHD와 울트라와이드 캡처.
- `visual-qa`로 잘림, CJK 줄바꿈, 대비, badge 정렬 검토.

**Commit: REQUIRED**

`추천 카드에 carry와 55라 준비 표시`

---

### T6. 표시 모드 설정 모델 추가

**참조**

- `Models.cs:297-322`
- `SettingsStore.cs:5-68`
- `SettingsStoreTests.cs:10-83`
- `OverlayVisibilityPolicy.cs:1-69`

**구현**

1. `OverlayDisplayMode` enum을 추가한다.
2. `AppSettings`에 `OverlayDisplayMode = Full`과 `LastVisibleOverlayDisplayMode = Full`을 추가한다.
3. 오래된 JSON, 알 수 없는 값, 정상 round-trip 계약을 정의한다.
4. 기존 `HiddenByUser` runtime flag를 mode로 통합하고 중복 상태를 만들지 않는다.
5. 위 표시 상태 전이표를 pure policy 테스트의 data row로 그대로 사용한다.

**테스트 — TDD**

- Full/StatsOnlyCompact/Hidden 및 last-visible 저장·복원.
- 기존 JSON은 Full.
- 손상된 enum 값은 안전한 Full 또는 명시적 migration 규칙으로 복구.

**수용 기준**

- 재시작 후 선택 모드가 동일하다.

**QA**

```powershell
dotnet test OrandOverlay.Tests/OrandOverlay.Tests.csproj --no-restore --filter "FullyQualifiedName~SettingsStore|FullyQualifiedName~OverlayDisplayMode"
```

**Commit: REQUIRED**

`오버레이 표시 모드 설정 영속화`

---

### T7. 추천 창과 Stats 창 visibility 분리

**참조**

- `OverlayWindow.xaml.cs:8-67`
- `MainWindow.xaml.cs:1268-1351`
- `OverlayWindowBase.cs:14-238`
- `OverlayVisibilityPolicyTests.cs:8-123`

**구현**

1. `OverlayWindow`가 자신의 visibility로 `Stats.Show/Hide`를 강제하는 결합을 제거한다.
2. pure `OverlayDisplayPolicy`가 availability, display mode, last-visible mode, event를 입력받아 다음 저장 상태와 두 창 visibility를 반환하게 한다.
3. `ShowOverlayWindows`/`HideOverlayWindows`를 mode-aware 적용 함수로 교체한다.
4. 클릭 통과는 보이지 않는 창에도 상태를 보존하고, 표시될 때 동일하게 적용한다.
5. close-to-hide는 `OverlayDisplayMode.Hidden`으로 저장하고 last-visible은 유지한다.
6. availability 변화는 mode/last-visible을 수정하지 않는다.

**테스트 — TDD**

- Full은 둘 다 표시.
- StatsOnlyCompact는 Stats만 표시.
- Hidden/unavailable은 둘 다 숨기되, unavailable은 저장 mode를 변경하지 않는다.
- 일시적 인식 오류 후 저장 mode Full/StatsOnlyCompact는 각각 복원되고 Hidden은 계속 숨김.
- 실제 세션 종료에서만 availability가 숨김.

**수용 기준**

- 추천 창을 숨겨도 Stats 창이 독립적으로 유지된다.

**QA**

```powershell
dotnet test OrandOverlay.Tests/OrandOverlay.Tests.csproj --no-restore --filter "FullyQualifiedName~OverlayVisibility"
```

**Commit: REQUIRED**

`추천 창과 패수치 창 표시 상태 분리`

---

### T8. StatsOverlay 컴팩트 렌더링 추가

**참조**

- `StatsOverlayWindow.xaml:1-40`
- `StatsOverlayWindow.xaml.cs:8-18`
- `OverlayWindow.xaml.cs:224-313`
- `OverlayLayoutPolicy.cs:1-9`
- `OverlayLayoutPolicyTests.cs:6-13`
- `MainWindow.xaml:104-150`

**구현**

1. Stats XAML에 `ReadinessPanel`, core KPI, secondary chip, non-core section 이름을 부여한다.
2. compact mode에서 Emergency/Reroll/Special/GreenBlood 영역을 `Collapsed`.
3. compact 크기를 core KPI가 잘리지 않는 고정 폭 + 콘텐츠 높이로 정의한다.
4. Main 설정에 `전체 / 패수치만 / 숨김` 선택을 추가한다.
5. 기존 Stats 위치, DPI scale, monitor clamp, drag, click-through를 재사용한다.
6. 모드 변경은 창을 재생성하지 않고 visibility/layout만 바꾼다.
7. 위치 필드는 기존 `StatsOverlayLeft/Top`, 클릭 통과는 `ClickThroughOverlay`, 배율은 기존 overlay scale을 그대로 사용한다. compact/full 크기는 설정에 저장하지 않고 mode별 layout policy가 결정한다.
8. 위치 저장은 기존 `PositionCommitted` 시점만 사용한다. 재시작·모니터 제거·DPI 변경 시 기존 clamp 로직으로 현재 monitor work area 안에 복원한다.

**테스트 — tests-after**

- compact에서 핵심 KPI visible, 비핵심 panel collapsed.
- 물딜/마딜 readiness 라벨.
- compact/full 크기 정책.
- 모드 변경 후 Stats 위치 불변.
- 위치/클릭 통과/배율 round-trip과 monitor clamp.

**수용 기준**

- 사용자가 추천 보드 없이 패수치만 계속 볼 수 있다.
- 핵심 수치가 FHD에서 한 화면에 잘림 없이 표시된다.

**QA**

- Full → StatsOnlyCompact → Hidden → Full 전환.
- 재시작 후 모드/위치 복원.
- click-through on/off, drag, Caps Lock overlay hotkey.
- FHD, 울트라와이드, 혼합 DPI 캡처 후 `visual-qa`.

**Commit: REQUIRED**

`패수치 전용 컴팩트 오버레이 추가`

---

### T9. 통합 회귀와 실제 게임 QA

**참조**

- `OrandOverlay.Tests/*Recommendation*Tests.cs`
- `OrandOverlay.Tests/Overlay*Tests.cs`
- `SmokeTests/Program.cs`

**자동 검증**

```powershell
dotnet test OrandOverlay.Tests/OrandOverlay.Tests.csproj --no-restore
dotnet build OrandOverlay.csproj -c Release --no-restore
dotnet run -c Release --project SmokeTests/OrandOverlay.SmokeTests.csproj
```

T0 이후 SmokeTests는 allowlist 없이 exit code 0이어야 한다. T3/T4/T5/T7/T8의 새 smoke
assertion은 각각 명시적 PASS line을 출력하고, 누락·실패·exception은 통합 QA 실패다.

**실제 게임 QA**

1. 물딜 목표, 방깎/이감/스턴 미달 패에서 secondary top이 보류되고 deficit이 맞는지 확인.
2. 실제 패 변화 이벤트를 구독한 상태에서 지원 유닛을 확보하고 readiness가 갱신되는지 확인.
3. 세 목표를 충족한 뒤 `MultiAllowed`만 secondary top이 열리는지 확인.
4. 마딜 목표에서 물딜 방깎이 아니라 마방깎 공급원을 검사하는지 확인.
5. StatsOnlyCompact에서 추천 창 없이 패수치가 갱신되는지 확인.
6. 일시 인식 오류와 세션 종료를 구분하는지 확인.
7. 실제 화면 캡처를 `visual-qa`로 검토.

**수용 기준**

- 전체 xUnit green.
- Release build exit code 0.
- SmokeTests exit code 0이며 새 기능 PASS line이 모두 존재한다.
- 새 기능 관련 manual/visual QA는 모두 통과한다.

**Commit: REQUIRED**

`초보 안전 추천과 컴팩트 모드 통합 검증`

---

### T10. 버전·원격 Release 배포

**참조**

- `OrandOverlay.csproj`
- 저장소 최근 release tag 및 commit message 관례
- 기존 GitHub Release 자산명 `OrandOverlay.exe`

**절차**

1. 최신 원격 tag를 조회하고 재사용하지 않은 다음 patch version으로 올린다.
2. 검증된 변경만 atomic commit으로 커밋한다.
3. `origin/main`에 push한다.
4. 버전 commit 후 작업 트리가 clean하고 `HEAD == origin/main`인 상태에서 Release publish를
   다시 실행한다. T9의 이전 binary를 재사용하지 않는다.
5. 같은 final HEAD에서 전체 xUnit, Release build, 전체 SmokeTests를 다시 실행한다.
6. final Release binary로 T9의 물딜/마딜 readiness, compact mode, 실제 WPF/visual QA를
   다시 실행하고 evidence에 tested HEAD를 기록한다.
7. publish 직후 HEAD를 기록하고 그 binary로만 새 GitHub Release를 생성한다.
8. 원격 자산을 새 디렉터리에 재다운로드한다.
9. 로컬/원격 SHA256, tested/publish HEAD, tag target, release target, origin/main을 대조한다.
10. 작업 트리가 clean인지 확인한다.

**수용 기준**

- tag, commit, Release target이 동일하다.
- 검증·업로드된 binary가 버전 commit과 동일 HEAD에서 생성됐다.
- final HEAD 재검증의 xUnit/build/smoke/manual/visual QA가 모두 통과했다.
- 원격 다운로드 SHA256이 로컬과 동일하다.
- 기존 tag/자산을 덮어쓰지 않는다.

**QA**

```powershell
git fetch origin main
git rev-parse HEAD
git rev-parse origin/main
gh release view vX.Y.Z --json tagName,targetCommitish,assets,url
Get-FileHash .\release-vX.Y.Z\OrandOverlay.exe -Algorithm SHA256
Get-FileHash .\verify-vX.Y.Z\OrandOverlay.exe -Algorithm SHA256
git status --short
```

**Commit: REQUIRED**

`vX.Y.Z 초보 안전 추천과 패수치 컴팩트 모드`

## 최종 완료 정의

- carry 분류가 표본 수 우세와 분리됨.
- 생존 수치 미달에서 secondary top이 앞서지 않음.
- readiness와 UI 패수치가 같은 계산 결과를 사용함.
- StatsOnlyCompact가 독립적으로 표시·저장·복원됨.
- 전체 xUnit, Release build, 전체 SmokeTests와 새 smoke assertions가 모두 green.
- 실제 게임/시각 QA 증거가 남음.
- 신규 Release 원격 SHA256까지 일치함.
