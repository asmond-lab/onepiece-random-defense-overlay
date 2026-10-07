# 랜디픽 라운드·유닛 표시 작업 인수인계

## 사용자의 현재 목표
기존 플레이어별 유닛 판독은 재구현하지 않고 유지한다. Warcraft III 3.0.0.24268 / 원랜디 2.320에서 라운드 표시를 연결하고, 실제 앱에서 유닛 목록과 함께 동작하는지 검증한 뒤 Cloudflare test 채널 배포와 배포 글 작성을 마친다.

사용자 최신 피드백: **“패 인식 자체가안되고있음.”**
그 뒤 누락된 프로필과 보유 패 창 표시 연결을 발견해 수정 중이며, 사용자가 다른 CLI 에이전트로 작업을 넘기기로 했다. 이 문서를 작성한 에이전트는 소스 수정을 멈췄다.

## 작업 위치
- 프로젝트: `C:\Users\123\Desktop\dev\orand-overnight-overlay-20260907-111725`
- 세션: `C:\Users\123\.aside\u\0\sessions\2026-09-14_IFNrohE9w9cRfhLp`
- 장기 체크포인트: 위 세션의 `tmp\live-integration-checkpoint.md`
- 이번 검증: `tmp\round-only-integration`
- 기존 작업물을 reset/clean/checkout으로 덮어쓰지 말 것. 다른 UI·배포·진단 작업이 함께 들어 있다.

## 꼭 지킬 작업 범위
- 기존 유닛 판독/분류를 유지하고, 라운드 및 화면 연결 누락만 고친다.
- `MemoryDiagnostics`는 접근·순회하지 않는다. 전체 디렉터리 재귀 탐색으로 들어가지 않도록 주의한다.
- 설치된 Program Files 게임 EXE/DLL 파일을 직접 읽지 않는다. 승인된 복사본과 PID/시작 시각/runtime binding만 진단에 사용한다.
- 게임 입력/자동화, 대상 함수 실행, DLL 로드, 쓰기·인젝션·원격 스레드, 디버깅, 보호 변경, 전체 메모리 덤프 금지.
- PAGE_NOACCESS를 만나면 해당 접근을 중단한다. 우회나 별칭 탐색 근거로 삼지 않는다.
- 새 전역 분석, native loader, 생존/본인 판별, 전체 게임 상태 역공학으로 확대하지 않는다.
- 관측 목록을 검증된 내패·생존·완전한 인벤토리라고 부르지 않는다. 자동 코칭/craft-now/게임플레이 권한은 계속 꺼져 있어야 한다.
- WarcraftProbe는 별도 진단 도구다. Tools의 미완성 scalar R7 확장은 이번 기능의 승인 근거가 아니다.

## 현재 중요한 사실
### 1. 라운드 공용 판독과 UI 연결
- `Warcraft300ObservedRoundReader`가 기존 Growth 관측의 pb/Eb 및 소유 타이머 제목을 비교한다.
- `DiagnosticInventoryObservation.ObservedRound`에 참고용 라운드를 넣고 메인 상태/통계 창에 표시한다.
- 기존 `RecognitionResult.Round`나 코칭 권한에는 넣지 않는다.
- 마지막 승인 복사본 기반 실측: pb=1, Eb=2, 타이머 제목=1, Round=1. 라운드 단계 약 1.9851ms / 2458bytes / 252reads, 전체 scan age2708.2623ms로 fresh.
- 실제 다음 라운드로 넘어가는 변화는 아직 관측하지 못했다. 1→2/unknown/expiry는 테스트 입력으로 검증했다.

### 2. 사용자 실행 앱에서 발견한 첫 원인과 보완
- 사용자가 실행한 후보 앱은 `RandyPick` PID103916, 창 제목 `랜디픽 BETA 1.0.2`, 표시 버전 `1.0.2-test.1`이었다. PID는 재확인할 것.
- 실제 UI는 `지원하지 않는 실행 파일 · 관측개체 참고 사용 불가`를 표시했다. 실시간 패 인식 체크는 On이었다.
- 원인: **소스 및 후보 EXE의 Data/memory-profiles.json에는 2.0.4 프로필만 있고 3.0 프로필이 아예 없었다.** 도구의 명시 프로필/합성 테스트만으로는 이 누락을 잡지 못했다.
- `Data\memory-profiles.json`에 정확히 pin된 3.0.0.24268 참고 프로필을 추가했다. **enabled=false / verified=false**를 유지한다.
- 정상 서비스는 `MapDatasetRuntimePolicy.AllowsReferenceObservation(...)`의 별도 참고 경로로 이 프로필을 선택한다. 일반 운영 프로필 활성화와 다르며, `MemoryProfileValidator.Validate` 검증은 계속 수행한다.
- 기존 2.0.4.23745 rev5 프로필 값은 유지했다.
- 새 설치에서 optional 사용자 캐시가 없을 때 `FileStamp`가 FileInfo.Length를 읽어 실패하는 별도 문제도 재현하고 고쳤다.

### 3. 실행 중인 사용자 앱에 적용한 임시 캐시 보완
- `C:\Users\123\AppData\Local\OrandOverlay\memory-profiles.json`에 같은 disabled/unverified 3.0 참고 프로필을 추가했다.
- 기존 legacy 프로필은 값 그대로 유지했고, 원본 바이트 백업은 `tmp\round-only-integration\user-profile-before-reference.json`에 있다.
- 보완 후 **현재 실행 앱의 실제 UI에서 `1라운드 (관측) · 현재뷰 0 관측개체 참고 대조 · 생존·내패·실제제작가능 미검증`을 확인했다.**
- 하지만 보유 패 창은 여전히 `패 확인 대기 / 보유 패 없음`이었다.
- 원인: 현대 관측 경로는 `_automatic`에 넣지 않는 것이 맞지만, **보유 패 창 InventoryList에도 관측 Entries를 보여주는 연결이 빠져 있었다.**

### 4. 현재 마지막 수정, 아직 빌드/검증 안 됨
아래 UI 변경을 마지막으로 작성했고, 그 이후 빌드/테스트는 아직 하지 않았다.
- `MainWindow.DiagnosticInventory.cs`
  - `RenderDiagnosticInventoryList()` 추가.
  - fresh인 `_diagnosticInventory.Entries`를 기존 InventoryList에 이름×개수로 표시.
  - `현재뷰 N 관측 유닛 · 생존·내패 미검증 · 읽기 전용`으로 출처 표시.
  - unavailable/stale이면 목록을 지우고 `관측 유닛 확인 대기`로 표시.
  - `_automatic` 및 코칭 입력에는 절대로 합치지 않음.
  - `RenderDiagnosticInventoryReference()`에서 이 목록 렌더러 호출.
- `MainWindow.PlanUi.cs`
  - 2.320의 `UpdateAuxiliaryContext`는 위 참고 목록 렌더러를 호출하고 legacy origin 판정으로 덮어쓰지 않도록 return.
- `Tools\RandypickBetaCapture\DiagnosticCapture.cs`
  - Fresh마다 실제 InventoryList 내용이 관측 Entries의 이름/개수와 일치하는지 검사 추가.
  - Unavailable마다 목록이 비워지고 대기행만 남는지 검사 추가.
  - 실제 보유 패 내비게이션 버튼을 눌러 별도 창을 캡처하는 합성 WPF 검증 추가.
  - 기존 라운드1→2→unknown 및 기존 no-coach/no-automatic 검사 유지.

## 현재 소스 버전과 관련 변경 파일
현재 csproj InformationalVersion은 **1.0.2-test.2**다. 코어/Assembly/FileVersion은 1.0.2 / 1.0.2.0 유지.

이번 범위 관련 파일:
- `Warcraft300ObservedRoundReader.cs`
- `Warcraft300GrowthReader.cs`
- `WarcraftMemoryRecognitionService.cs`
- `DiagnosticInventoryObservation.cs`
- `MapDatasetRuntimePolicy.cs`
- `MainWindow.DiagnosticInventory.cs`
- `MainWindow.PlanUi.cs`
- 통계 창의 관측 라운드 표시 관련 기존 변경
- `Data\memory-profiles.json`
- `MemoryRecognitionProfiles.cs`의 FileStamp optional-file 처리
- `OrandOverlay.csproj`
- `OrandOverlay.Tests\ShippedReferenceProfileTests.cs` (새 파일)
- `OrandOverlay.Tests\BrandPresentationTests.cs`의 기대 InformationalVersion
- `OrandOverlay.Tests\CoachDisclosureTests.cs`의 기대 BETA1.0.2 표기
- `Tools\RandypickBetaCapture\DiagnosticCapture.cs`
- `Tools\RandyPickLiveValidation\StandaloneScalarRunner.cs`의 ReferenceRound 출력

## 검증 상태, 시점 구분 필수
### 예전 1.0.2-test.1 소스 상태
- 전체 3262pass / 0fail / 1skip / 3263total.
- `tmp\round-only-integration\full-gates-r2`의 full.log/status.json/full.trx.
- legacy 및 diagnostic WPF 합성, smoke, maps2.314/2.320, goal carry, stats9x4 통과.
- 이 결과는 **현재 마지막 UI 수정 이후의 검증이 아니다.**

### 프로필 보완 후, 마지막 UI 수정 전
- `ShippedReferenceProfileTests`는 실제 빌드 출력의 Data/memory-profiles.json을 사용한다.
- missing user cache / legacy-only cache 두 경우를 검사한다.
- missing cache 경우 실패를 재현한 뒤 FileStamp를 고쳤다.
- focused **386pass / 0fail**: ShippedReferenceProfileTests, MemoryProfileRepositoryMergeTests, Warcraft300, DiagnosticInventory, BrandPresentationTests.
- 로그 `tmp\round-only-integration\shipped-profile-r2.log`.
- 이후 위 3개 UI 파일을 수정했으므로 이 386pass를 최신 완료로 주장하지 말 것.

## 공개 배포 상태
- 현재 자동 업데이트 test 채널: **1.0.1-test.1**. 승격하지 않았다.
- stable 응답404, profiles 응답200이며 마지막 비교에서 본문이 이전과 동일했다.
- 이전 유효 공개 EXE:
  `https://orand-updates.epic42121.workers.dev/downloads/1.0.1-test.1/RandyPick.exe`
- 문제가 있던 **후보** 1.0.2-test.1은 immutable 파일만 올려 두었다. 이 URL을 덮어쓰지 말 것.
  `https://orand-updates.epic42121.workers.dev/downloads/1.0.2-test.1/RandyPick.exe`
  SHA256 `4DD856C01E4DE7FF4F57C3E4C2DC651612C89E8EABC862F783DB03E1860AC93E`
  size78536061.
- 후보 파일의 서명·내용·공개 재다운로드 해시는 일치했지만, 위 두 연결 누락이 있었으므로 배포 완료로 취급하면 안 된다.
- 새 test.2 EXE는 아직 publish/package/sign/upload하지 않았다.

## 다음 수행 순서
1. 마지막 UI 변경 빌드 및 focused 테스트 실행. 필요하면 최소 수정.
2. RandypickBetaCapture diagnostic/legacy WPF를 실행해 보유 패 창의 실제 목록, 라운드, 만료/정지/재개를 확인. 합성 화면은 반드시 합성이라고 표시한다.
3. **패키지 검증 강화**: `Tools\ReleaseVerification\Verify-RandypickBundle.ps1`는 지금 3.0 enabled 프로필이 없는지만 검사해서, 3.0 프로필 자체 누락을 놓쳤다. extracted Data에 정확한 disabled/unverified 3.0 참고 프로필이 1개 존재하고 기존 legacy 프로필이 유지됨을 검사하도록 보완할 것.
4. 최종 전체 회귀 + smoke + map verifiers + carry + stats를 직렬 수행. 같은 출력 디렉터리를 재사용하지 않는다.
5. 새 immutable `1.0.2-test.2`로 로컬 publish/sign/verify. EXE --verify-package도 실제 실행하되 이 모드를 정상 native 앱 검증이라고 부르지 않는다.
6. 사용자에게 이미 실행 중인 앱의 라운드 표시가 살아난 것은 확인했다. 새 UI가 들어간 실제 앱에서 관측 유닛 목록까지 확인하고, 가능하면 실제 다음 라운드 변화를 확인한다. 설치 게임 바이너리 직접 읽기/게임 입력 금지는 계속 지킨다.
7. 필요한 실제 앱 검증 전에는 test feed를 승격하지 않는다. 완료 후 새 asset 업로드→공개 재다운로드 해시 확인→현재 feed 변경 여부 확인→서명 manifest 승격→공개 manifest/EXE 재검증 순서.
8. 이전 EXE, stable, profiles는 건드리지 않는다. 게시글은 실제 통과한 기능/제한만 설명하고 자동 코칭이나 정확한 내패·생존 판별을 주장하지 않는다.

## 실행 명령 참고
PowerShell에서 아래 환경을 정리한 뒤 실행했다:
```powershell
${env:ProgramFiles(x86)}='C:\Program Files (x86)'
$env:PYTHONHOME=$null
$env:PYTHONPATH=$null
$env:ORAND_VERIFY_DIR=$null
```

```powershell
dotnet test .\OrandOverlay.Tests\OrandOverlay.Tests.csproj -c Release --no-restore --logger 'trx;LogFileName=full.trx' --results-directory '<새 결과 폴더>'
dotnet build .\Tools\RandypickBetaCapture\RandypickBetaCapture.csproj -c Release --no-restore
dotnet .\Tools\RandypickBetaCapture\bin\Release\net8.0-windows\RandypickBetaCapture.dll --diagnostic '<새 폴더>'
dotnet .\Tools\RandypickBetaCapture\bin\Release\net8.0-windows\RandypickBetaCapture.dll '<다른 새 폴더>'
dotnet run --project .\SmokeTests\OrandOverlay.SmokeTests.csproj -c Release --no-restore
dotnet run --project .\Tools\MapProfileVerifier\MapProfileVerifier.csproj -c Release --no-restore -- --root '<프로젝트>'
dotnet run --project .\Tools\MapProfileVerifier\MapProfileVerifier.csproj -c Release --no-restore -- --root '<프로젝트>' --map-version 2.320
dotnet run --project .\Tools\GoalCarryPolicyGenerator\GoalCarryPolicyGenerator.csproj -c Release --no-restore -- --check .\Data\goal-carry-policy.json
dotnet run --project .\Tools\StatsUiCapture\StatsUiCapture.csproj -c Release --no-restore -- '<새 폴더>'
```

### 승인된 live 진단 입력
- 마지막 관측 대상 PID50744, start `2026-09-14T05:21:07.0906646Z`. 재시작 여부부터 확인한다.
- 승인 복사본:
  `C:\Users\123\.aside\u\0\sessions\2026-09-13_X2j2h7HlhcnGq0XN\tmp\live-3.0\Warcraft III.exe`
- 복사본 SHA256:
  `BD2A0DC256289DE45287BB60F3725B88F1235216D5177984377FE1CA22840A12`
- 지도 JASS:
  `docs\analysis-2320\modern-reader\members-ko-2.320\war3map.j`
  SHA256 `6FDFC64BF8AD9463F5B5C8A351FFA7E1875D6CF9B51210129539375FA77A6C7C`.
- `Tools\RandyPickLiveValidation\Program.cs`의 일반 경로는 installed file을 읽는다. **그 경로를 실행하지 말 것.** 기존 승인 copy binding은 `--probe-scalars-only --authorized-exe-copy ...` 분기다. 단, 미완성 광범위 scalar 조사로 확장하지 않는다.

## 배포 도구 참고
- 서비스 설정: `ops\update-service\wrangler.toml`.
- **모든 Wrangler R2 명령에 이 --config 경로를 명시**한다. 설정을 빼면 잘못된 접근 상태로10042가 났고, 설정 명시 후 기존 저장소 접근이 정상화됐다. R2/결제 설정은 변경하지 않았다.
- 마지막 사용 CLI:
  `C:\Users\123\AppData\Local\npm-cache\_npx\d77349f55c2be1c0\node_modules\wrangler\bin\wrangler.js`
- 서명: `ops\update-publisher\New-SignedManifest.ps1`. 개인키나 인증 토큰을 출력하지 말 것.
- 기존 로컬 절차 템플릿은 세션 tmp의 `round-publish-local-r1.ps1`, `round-probe-parent.ps1`, `round-upload-asset.ps1`, `bridge-promote-test.ps1`, `bridge-verify-public.ps1`.
- 버전·fresh output directory·public candidate path·명시 config를 새 test.2에 맞춰 검토 후 재사용한다. 이전 결과 파일은 덮어쓰지 않는다.

## 인계 시 주의
- '프로브가 읽었다'와 '정상 앱에서 표시된다'를 구분한다.
- '서명/패키지 검증 통과'와 '실제 유닛 목록 표시'를 구분한다.
- 마지막 UI 변경은 아직 컴파일조차 확인하지 않았다.
- 사용자에게 검증을 다시 떠넘기기 전에 CLI에서 할 수 있는 source/config/UI 검증을 모두 끝낸다.
