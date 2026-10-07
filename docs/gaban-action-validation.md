# 현재 행동 추천 검증

기준 소스: `6a09688` (`v0.6.70`).

## 변경 범위

- 완성 빌드의 지원 구성과 현재 패의 다음 행동을 별도로 평가한다.
- 라운드 정보가 있는 추천은 실제 보유한 직접 재료와 조합 후 잔여 패로 평가한다.
- 생존 보완 후보를 완성 빌드 목록 밖에서도 찾되, 기존 항법·기물 제한을 통과한 후보만 사용한다.
- 같은 역할 우선순위에서는 목표에 필요한 하위 패의 추가 손실이 적은 후보를 먼저 선택한다.
- 생존·딜 역할을 잃는 조합은 하위 조합 단계에서도 보류한다.
- 가반 조합이 보류되면, 목표 재료를 예약한 잔여 패를 기준으로 대체 기물을 고른다.
- 특수 재료 부족과 일반 경고는 별도 목록이다.

라운드 정보 없는 `RecommendNearestCrafts` 호출은 기존 완성 빌드 순서를 유지한다.
실제 앱과 `RecommendationPipelineRequest`는 라운드·완료 스토리 단계를 전달한다.

## 재현 명령

저장소 루트에서 실행:

```powershell
dotnet test OrandOverlay.Tests/OrandOverlay.Tests.csproj --configuration Release
dotnet build OrandOverlay.csproj --configuration Release
dotnet run --project tools/PlannerEvidenceCapture/PlannerEvidenceCapture.csproj --configuration Release -- --output artifacts/gaban-action-qa --build-sha 6a09688 --gaban-actions
```

화면 검증은 실제 WPF `OverlayWindow`와 공통 추천 파이프라인,
`AutoCombinePlanner`를 사용한다. 앱의 자동 업데이트·텔레메트리 시작은 비활성화한다.

최종 실행 결과: Release 테스트 705개 통과, 실패·건너뜀 0개.
Release 빌드 경고·오류 0개. WPF 6개 상태, PNG 12장 검증 통과.

## 회귀 및 화면 시나리오

- 가반 미완성, 킬러 히든 직접 재료 확보: 29·30라에서 킬러를 다음 행동으로 안내.
- 킬러 완성: 이미 보유한 킬러를 다시 추천하지 않음.
- 가반 재료 확보: 재료 소모로 사라지는 역할의 대체 기물을 먼저 안내.
- 대체 기물 확보: 가반 조합 안내 재개.
- 가반 완성: 가반 중복 제작 대신 지원 구성으로 진행.
- 유일한 필수 스턴을 소모하는 하위 조합: 실제 조합 단계 목록에서 제외.
- 공유 재료 사용: 목표에 추가로 필요한 하위 패 수 계산.
- 자원 잔량 0과 미확인 상태 구분: 확인된 부족은 조합 불가, 미확인은 확인 경고.
- 일반 안전 경고를 특수 재료 부족으로 오인하지 않음.

실패를 먼저 관측한 회귀:
`UnfinishedGabanDoesNotDisplaceReadyBossSupportAtRound30`,
`GabanPipelineGeneratesReadySurvivalActionBeforeFinalBuildCandidates`,
`BossCraftCannotSpendTheOnlyRequiredStun`,
`GoalMaterialLossCountsReplacementCardsAfterCraft`,
`ActualCraftPlanDoesNotConsumeLastStunForAnIntermediate`,
`GabanConversionNamesReplacementSupportsAndResumesAfterTheyArrive`,
`TacticalWarningsDoNotBecomeMissingSpecialMaterials`.

화면별 추천 ID·직접 조합 단계·표시 제목은
`artifacts/gaban-action-qa/gaban-actions.json`에 기록한다.
동일 디렉터리에 6개 상태의 상단·하단 PNG 12장을 저장한다.

## 검증 한계

고정 패와 대체 기물 도착을 넣는 결정적 시나리오이며, 실제 게임 리플레이가 아니다.
같은 입력의 기존 완성 빌드 순서와 새 행동 순서를 비교하지만 클리어율을 측정하지 않는다.
검증 당시 Warcraft 프로세스는 실행 중이지 않았다.

유닛 능력 표의 역할·수치가 판단 근거다. 실제 라인 DPS, 보스 남은 HP,
배치·스킬 사용·남은 시간까지 측정하는 전투 시뮬레이터로 해석하면 안 된다.
목재·골드·특포가 관측되지 않으면 확인 경고를 표시하며 잔량을 추정하지 않는다.
따라서 신 난이도 클리어가 보장되거나 실전 승률이 개선됐다고 주장하지 않는다.

C# LSP가 설치되지 않아 LSP 진단은 실행하지 못했으며 .NET 컴파일러로 검증한다.
