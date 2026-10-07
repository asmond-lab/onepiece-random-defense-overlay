# 로컬 활동 로그

RandyPick은 인식 결과와 화면 반영, 사용자의 RandyPick 조작, 현재 화면에서 관측 가능한 게임 상태 변화를 시간순 JSONL로 기록합니다. 이 기록은 로컬 진단용이며 원격 텔레메트리로 전송되지 않습니다.

## 파일 위치와 보존

- 설치·게시된 앱: 실행 파일이 있는 폴더의 `activity-logs` 디렉터리
- 파일 이름: `activity-<applicationSessionId>-<index>.jsonl`
- 운영 설정: 파일당 최대 16 MiB, 한 앱 실행 세션에서 최근 32개 파일 보존
- 보존 한도는 **현재 앱 실행 세션이 만든 파일**에만 적용됩니다. 이전 실행 세션의 파일을 자동으로 삭제하지 않습니다.
- 각 줄은 독립 JSON 객체입니다. `receivedAtUtc`가 절대 시각, `elapsedMilliseconds`가 해당 앱 실행 시작 후 경과 시간이며, `sequence`는 같은 실행 세션 안의 순서입니다.

로그에는 패 정보와 앱 조작 내역이 포함될 수 있습니다. 공유하기 전에 내용을 확인하십시오.

## 증거 수준 읽는 법

로그의 관측값은 Warcraft의 **현재 진단 화면(current view)** 에서 읽은 값입니다.

- `observed-current-view`: 해당 시점의 현재 화면에서 직접 관측한 값
- `validated-current-view-counts`: 현재 화면 객체를 검증해 집계한 수량
- `observed-current-view-counter-delta`: 현재 화면에서 읽힌 누적 카운터의 두 시점 차이
- `actionConfirmed: false`: 변화는 관측했지만 그것만으로 특정 사용자의 단일 행동을 확정할 수 없음
- `unattributed` 또는 후보 목록: 결과 변화는 보이지만 어떤 행동·사용자·원인에 속하는지 하나로 귀속할 수 없음
- `game.evidence-missing` 또는 `observation.gap`: 필요한 입력이나 관측 경계가 끊긴 상태. `lane`, `laneReset`, `correlationReset`으로 영향을 받은 범위를 확인함

현재 화면의 슬롯은 불변의 로컬 플레이어 신원이 아닙니다. 런타임 맵 바이트의 동일성도 별도로 확정되지 않은 상태이므로, 맵 소스 기반 규칙과 실제 관측값을 구분합니다. 누락되거나 초기화되지 않은 카운터는 `0`이 아니라 **사용 불가/모름**입니다.

카운터 값 `0`은 해당 배열 키가 현재 관측의 `counters`에 실제 존재할 때만 관측된 0입니다. 키가 없거나 `activityUnavailableCounterNames`에 있으면 값은 모름이며, 0으로 보충하거나 누락 구간을 가로질러 delta를 계산하지 않습니다.

패 수량 변화만으로 위습 사용, 조합, 도박 또는 특정 결과 유닛을 확정하지 않습니다. 확정 가능한 소스 규칙·카운터·동일 문맥의 전후 관측이 함께 있을 때만 파생 활동을 기록하며, 모호한 결과는 후보 또는 미귀속 상태로 남깁니다.

기본 패(`basic`)와 전체 패(`full`)의 이력은 독립적입니다. 기본 패만 거부되어 `lane: basic`, `laneReset: true`, `correlationReset: false`가 기록돼도, 별도로 정상 검증된 전체 패의 이력까지 지우지는 않습니다. 전체 패 거부·만료·문맥/판 변경·저장 누락은 계속 게임 행동 연결을 초기화합니다. 거부된 입력 자체를 정상 관측으로 사용하는 것은 아닙니다.

### 게임 활동 event kind

- `game.wisp`: rawcode 소비 또는 연속 관측된 위습 카운터 변화. `candidateOutputs`는 후보이며 `outputsAttributed: false`, `actionConfirmed: false`입니다.
- `game.wisp-evidence`: 서로 다른 관측에서 온 raw 소비/카운터 증거의 상관관계·만료 정보입니다. 값이 그대로인 정상 전체 패 관측을 사이에 두더라도, 원본 완료 시각 기준 기존 3초 유효기간 안에서는 보완 증거를 연결합니다. 무관한 변화·실제 전체 패 단절·문맥 변경·시간 역전·만료에서는 끊습니다. `countsAsAction: false`이므로 위습을 한 번 더 사용한 것으로 세지 않습니다.
- `game.craft`: 관측된 전체 added/removed가 핀된 2.321의 단일 active recipe와 정확히 일치할 때만 기록합니다. 현재 화면에서 직접 볼 수 없는 요구사항은 `unobservedRequirements`에 보존합니다.
- `game.gamble`: 필요한 카운터의 관측 범위만 기록합니다. `outcomeComplete`가 `true`일 때만 성공/실패 방정식이 완결됐고, partial이면 `successes`/`failures` 또는 derivation label이 `null`일 수 있습니다. `missingCounterNames`와 `observedOutcomeCounterDeltas`를 함께 확인해야 하며 결과 후보는 특정 시도에 귀속하지 않습니다.
- `game.counter`: 연속 관측된 양의 카운터 변화 원문입니다. 그 자체는 추정 행동이 아닙니다.
- `game.counter-observed`: 새 누적값은 읽혔지만 이전 값이 없어 delta를 알 수 없는 관측입니다. `deltaKnown: false`이며 행동으로 세지 않습니다.
- `game.ambiguity`: 둘 이상의 조합 규칙이 정확히 일치해 하나로 확정할 수 없는 상태입니다.
- `game.evidence-missing`: rawcode 증거 자체가 없어 상관관계를 초기화한 상태입니다.
- `game.counter-reset`: 명시적 reset, 문맥·revision 변경 또는 카운터 rollback으로 이전 baseline을 폐기한 상태입니다.

`ActivityProjectedRawcodes`를 우선하며, rawcodes가 명시적으로 제공된 경우에만 `ActivityRawcodes`로 대체합니다. 누락된 카운터 키는 재등장할 때 baseline만 새로 잡고 gap을 가로지르는 delta를 만들지 않습니다.

### 네이티브 카운터 진단 필드

다음 항목은 로컬 JSON에 camelCase로 기록됩니다.

- `counters`: 실제로 읽힌 카운터 키와 값만 포함
- `activityUnavailableCounterNames`: 현재 관측에서 구조적으로 초기화되지 않았거나 슬롯이 없어 읽을 수 없는 이름
- `counterStatus`: `ready`, `partial`, `not-requested` 또는 fail-closed 원인
- `activityCounterReadCalls`, `activityCounterReadBytes`, `activityCounterReadDurationMs`: 추가 카운터 읽기의 비용

이 필드는 로컬 진단용이며 원격 텔레메트리나 게임플레이 권한 신호가 아닙니다. `partial`의 unavailable 이름은 `0`이 아닙니다.

## 손실과 실패

기록은 인식·UI 스레드를 막지 않도록 용량이 제한된 큐와 단일 백그라운드 writer를 사용합니다.

- 큐 과부하로 레코드가 빠지면 JSONL에 `record-loss`가 기록되고 `firstSequence`, `lastSequence`, `count`로 손실 범위를 표시합니다.
- 로컬 기록 손실이나 I/O 오류가 관측되면 행동 상관관계를 끊어 손실 이전과 이후를 이어서 추정하지 않습니다.
- 파일 생성·쓰기·flush·close 오류는 앱 동작을 막지 않습니다. 오류는 내부 `LastError`로 유지되고 종료 시 진단 trace에 기록됩니다.
- 실행 중 로컬 기록 이상은 기존 `GameplayRecordingStatus`에 표시됩니다. 경고 우선순위는 `LastError` → 누락 개수와 `record-loss` 확인 안내 → backpressure이며, 1초마다 백그라운드 dispatcher에서 갱신됩니다. 원격 observed/gameplay 상태는 이 로컬 경고를 덮어쓰지 않습니다.
- 정상 종료는 그 시점까지 수락된 레코드의 flush를 기다립니다. flush 이후에 새로 수락된 작업 때문에 이전 flush가 무기한 지연되지 않습니다.
- 종료 순서는 상태 갱신 timer 중지 → 진행 중 scan 완료 대기 → `app.stop` 기록 → writer dispose → terminal 오류의 `Trace` 기록입니다.

`sequence`가 연속인지, `record-loss`/`observation.gap`이 있는지, 각 행의 `evidence`와 `actionConfirmed` 값을 함께 확인하십시오.

## 네이티브 GUI 증거 도구

다음 명령은 사용자의 실행 중인 RandyPick이나 Warcraft를 조작하지 않습니다. 외부 효과가 꺼진 소유 fixture 창을 만들고, 핀된 2.321 데이터로 구성한 completion-only READY full 관측을 실제 `MainWindow` 경로에 전달한 다음, Windows UI Automation `WindowPattern`으로 최소화·복원하고 화면과 로그를 캡처합니다.

프로젝트 루트에서 새 출력 디렉터리 이름으로 실행합니다.

```powershell
dotnet run --project Tools/ActivityLogCapture/ActivityLogCapture.csproj -c Release -- artifacts/activity-logging-20260921/qa-tooling/full-path-final --native
```

PASS 조건:

- 콘솔 JSON의 `success`가 `true`
- `nativeWindows`가 `1`, `remainingWindows`가 `0`
- `native-actions.json`에 실제 HWND와 `Minimized`, `Normal` UIA 관측이 모두 존재
- `fixture.png`가 읽을 수 있는 PNG
- JSONL의 다섯 full `memory.read`가 모두 `accepted: true`
- JSONL에 `game.wisp`, `game.craft`, `game.gamble`이 있고 각 `actionConfirmed`가 `false`; 위습/도박의 `outputsAttributed`도 `false`
- JSONL에 `ui.presentation`, `ui.window-state`, `scan.completed`가 존재
- 명령 종료 뒤 보고서의 `processId`가 더 이상 실행 중이지 않음

출력 파일:

- `activity-*.jsonl`: fixture의 로컬 활동 로그
- `native-actions.json`: HWND, UIA 요청·관측 상태, screenshot 경로, 창 정리 결과
- `fixture.png`: 복원·렌더 완료 뒤 캡처한 fixture 화면

증거 도구가 만든 창과 프로세스만 정리 대상입니다. 사용자가 실행한 RandyPick과 Warcraft 프로세스는 종료하지 않습니다.

이 시나리오는 알려진 rawcode/counter 전이를 정적으로 주입해 **MainWindow accepted-full 배선과 상관관계 규칙**을 검증합니다. 실제 Warcraft 메모리에서 사용자의 위습·조합·도박 행동을 관측한 live proof가 아니며, 실제 행동 시점·로컬 플레이어 신원·결과 유닛 귀속을 확정하지 않습니다.

## C# 진단 환경

프로젝트 로컬 서버는 `Tools/.lsp/csharp-ls.exe`에 설치되어 있습니다. `.omo/lsp.json`은 builtin `csharp` 서버를 우선하도록 설정합니다.

현재 Senpi built-in LSP 클라이언트는 프로젝트 설정을 적용하기 전에 부모 프로세스 `PATH`에서 bare `csharp-ls`를 검사합니다. 따라서 프로젝트 로컬 바이너리는 `--version`이 성공해도 이 세션의 `lsp_diagnostics`에서는 `missing_dependency`가 됩니다. 해결에는 Senpi를 시작한 환경의 전역/사용자 `PATH` 또는 사용자 LSP 설정 변경이 필요하며, 프로젝트 설정만으로는 우회할 수 없습니다.

이 경계가 있는 동안 변경 파일의 대체 진단은 명명된 빌드 뮤텍스 아래 scoped `dotnet build`로 수행합니다.
