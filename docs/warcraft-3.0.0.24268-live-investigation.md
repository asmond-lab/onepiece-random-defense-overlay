# Warcraft III 3.0.0.24268 읽기 전용 실측

## 범위와 안전 경계

2026-09-13 실행 중인 원랜디 세션에서 조사했다. 실행 파일 SHA-256은 `BD2A0DC256289DE45287BB60F3725B88F1235216D5177984377FE1CA22840A12`이다.

- 게임 입력, 메모리 쓰기, 인젝션, 페이지 보호 변경 없음.
- `MemoryDiagnostics` 폴더 내용은 접근하지 않았다.
- 도구의 설치 경로 접근 차단 후 사용자에게 읽기 권한을 받아 격리된 분석본을 만들었다.
- 실제 개인 플레이 기록이나 개인 서명 키를 업데이트 서버에 올리지 않았다.
- 아래 실측은 새 빌드의 전체 지원 승인이나 `verified=true`를 의미하지 않는다.

## 확인된 유닛 구조

- CUnit vftable RVA: `0x02792E78`, RTTI COL RVA `0x029DDDF8`.
- rawcode: `unit + 0x178`. 실측 카탈로그 코드와 getter RVA `0x011E3520`의 `mov eax,[rcx+178h]`가 일치한다.
- 소유자: `uint32(unit + 0x1C0)`. CUnit 가상 getter RVA `0x011E5DD0`/`0x011E5DE0` 및 이 반환값을 플레이어 테이블 index로 넘기는 호출 경로를 확인했다. 새 빌드의 원래 필드 폭은 4바이트다.
- vtable만 남은 객체나 배열의 유효 개수를 넘는 잔여 포인터를 보유 패로 집계하면 안 된다.

## 확인된 게임 루트 계산

모든 연산은 unsigned 64비트 wraparound다.

```
e = uint64(moduleBase + 0x02E9AD00)
root = ((ROL64(e, 29) + 0x5BE06F37FC9B5B29)
        XOR 0x3A11C7B7EF67132B)
       + 0x2D2C27903E7F5D3D
```

- 실제 root RTTI가 `CGameWar3`와 일치했다.
- `uint32(root + 0x2698)`: 플레이어 테이블 개수 28.
- `root + 0x26A0 + index*8`: inline CPlayerWar3 포인터 배열. 별도 배열 포인터를 한 번 더 따라가는 구조가 아니다.
- helper RVA `0x008BBF50`는 index 0..27 및 테이블 개수를 검사한 뒤 해당 포인터를 반환한다.

### 아직 확정하지 않은 로컬 플레이어 경계

`uint16(root + 0x262C)`는 실측 0이었고, 커맨드 아트워크 경로에서 위 플레이어 테이블의 index로 사용됐다. 그러나 이 값이 관전/뷰 컨텍스트와 무관한 실제 `GetLocalPlayer()` identity라는 동등성은 아직 입증하지 못했다.

- 실제 JASS 등록 레코드에서 `GetLocalPlayer`의 함수 RVA `0x00CA73D0`을 찾았다.
- `.pdata` 함수 범위: `0x00CA73D0..0x00CA74DD`.
- 해당 페이지는 관측 시 `PAGE_NOACCESS`였다. 보호를 바꾸거나 읽지 못한 명령을 추정하지 않았다.
- 이 경계가 확정되기 전, 0번 슬롯을 고정하거나 해당 필드를 검증된 로컬 슬롯으로 승인하지 않는다.

## 월드 프레임 유닛 컬렉션

RTTI `CWorldFrameWar3`, vtable RVA `0x02764A20`에서 다음을 확인했다.

- `+0xC08`: uint32 유효 개수.
- `+0xC10`: CUnit 포인터 배열.
- `+0xC18`: capacity와 유사한 값. 개수로 사용하지 않는다.
- 관측된 유효 개수 309, capacity 유사 값 3200.
- 첫 309개는 서로 다른 CUnit이었다. 그 뒤의 메모리에는 잔여/중복 포인터가 있어 반드시 count로 한정해야 한다.
- 두 번의 안정된 관측에서 309개 모두 핸들 테이블 범위, 할당 marker `0xFFFFFFFE`, serial 일치 검사를 통과했다.
- 참조 증가/감소를 하는 append/destructor 및 렌더링 관련 순회 경로가 확인돼 allocator free-list와 구분했다.
- 모든 게임플레이 유닛의 완전한 포함, 실제 생존 상태, 안정된 전역 포인터 체인은 아직 별도 검증 대상이다.

해당 시점의 owner=0 카탈로그 코드 후보는 조로·나미·우솝·페로나 각 1, 버기 2였다. 이후 사용자가 직접 플레이 중이며 이 6기가 자신의 실제 패와 일치한다고 확인했다. 이는 현재 세션의 대조 증거이며 관전/리플레이까지 포괄하는 로컬 identity 증명이나 운영 인식 승인은 아니다.

## 실행 이미지 관측의 함정

원본 파일의 .text는 보호된 상태였다. 실행 중에도 8,799개 .text 페이지 중 1,304개만 읽을 수 있었고, 7,495개는 PAGE_NOACCESS였다.

기존 ReadImage의 16MiB 읽기에서 앞부분이 막히면 뒤의 읽을 수 있는 .rdata까지 0으로 취급할 수 있다. 따라서 RTTI를 못 찾았다는 결과가 타입 부재를 뜻하지 않는다. 읽기 가능한 모듈 구간을 개별적으로 확인하고 RVA 배치를 보존해야 한다. 다른 프로세스의 vftable 캐시 재사용도 차단해야 한다.

## 증거 보관

세션 tmp/live-3.0 아래의 격리된 probe, 관측 JSON, static-player 문서, pool-probe 문서에 상세 근거를 보관했다. 분석용 실행 코드 파일은 실행 또는 사용자 배포 용도가 아니다.

## 아직 미완료

1. 로컬 플레이어 identity의 독립적 검증.
2. 초기 6기 화면 대조 및 후속 16기 변화 관측은 완료. 후속 목록의 화면 수량별 재확인, 조합/판매/재접속과 실제 생존 상태 검증은 남아 있다.
3. 현재 라운드 UI의 시작 전 문구와 7라운드 문구를 격리 검증했다. 운영 리더 이식, 자원·선위·도박·능력·전투 등 다른 보조 리더 검증은 남아 있다.
4. 새 빌드 지원 프로필 활성화 및 공식판 승격.

## 반영한 안전 수정과 검증 결과

- `StructuralUnitPoolScanner.cs`: 읽을 수 있는 모듈 구간만 RVA 위치를 보존하여 읽고, 짧은 읽기는 페이지 단위로 복구한다. 프로세스 식별 없는 정적 vftable 캐시는 제거했다.
- `ReadOnlyProcessMemory`: 정확한 모듈 범위의 VirtualQueryEx 열거와 취소/한도 검사를 추가했다. 요구 권한은 조회와 읽기뿐이다.
- 실제 게임에서 수정한 앱의 RTTI 읽기 코드만 독립적으로 호출해 CUnit RVA `0x2792E78` 및 CWorldFrameWar3 RVA `0x2764A20`을 다시 찾았다. 보조 vftable `0x2764C18`/`0x2764C38`도 별개로 발견됐다.
- 새 테스트와 관련 기존 회귀 993개 통과. 전체 비라이브 스모크 통과.
- 기존 운영 인식의 버전·SHA·enabled·verified 게이트와 번들 프로필은 유지했다. 아래 실험 경로는 명시적 격리 검증에서만 동작한다. 새 3.0 지원판 배포나 공식 승인으로 해석하지 않는다.

## 후속 격리 검증과 1028개 회귀

- `Warcraft300Diagnostic.cs`, schema 2 `Warcraft30024268Diagnostic` 레이아웃을 추가했다. 정확한 버전/SHA/필드 폭/구조를 고정한다. 일반 실행에서는 enabled/verified가 모두 true여도 거부한다.
- 명시적인 `ORAND_VERIFY_DIR`에서만 동작하며 enabled=false는 여기서도 거부한다. 실제 임시 프로필은 session tmp의 별도 user root에만 두었고 enabled=true, verified=false이다. 설치된 사용자 캐시와 번들/원격 프로필은 변경하지 않았다.
- CGameUI vtable RVA `0x275ED08`. 전역 `0x2F5EF00`와 `0x2F85360`은 각각 native getter RVA `0x1CC880`, `0x2D0340`으로 확인했다. 한쪽을 추정 선택하지 않고 두 값의 일치와 타입을 요구한다. 월드 프레임 `+0x40`의 부모도 이 UI와 같아야 한다.
- typed frame, 중복 없는 CUnit, DWORD owner 0..27, count/vector/view/UI 전후 일치를 검사한다. 관측된 private allocation이 4GiB를 넘으므로 진단 전용 한도는 8GiB/30초이다. 1MiB 단위 정확한 읽기와 불완전 읽기/한도 초과/복수 유효 프레임 거부를 적용했다. 매 틱 실사용용 최적화나 일반 인식 경로는 아니다.
- 기존 `ReadConsistentSnapshot`의 격리 호출은 초기 6기를 3회 및 추가 30회 안정적으로 관측했다.
- 이후 앱의 전체 `RecognizeAsync`를 별도 호스트에서 실행했다. 일반 모드는 먼저 `UnverifiedProfile`을 반환했고, 명시적 진단 모드만 `Ready`와 `CURRENT-VIEW (not proven local identity)`를 반환했다. 절대 힙 주소를 프로필에 지정하지 않고 typed frame을 재발견했다.
- 후속 프레임 count=321, 매핑 합계=16: 나미1, 조로1, 루피2, 상디2, 버기3, 버기 마기탄1, 해군 칼병1, 우솝1, 브룩1, 스모커1, 페로나1, 우솝 화염탄1. 이 후속 목록은 판독 결과이며 화면 수량별 사용자 재확인은 없다. 6→16 변화를 특정 조합/뽑기 행동으로 추정하지 않는다.
- 실험 결과는 검증된 로컬 슬롯, native unit pointer, 중립 성장형 소유권, map/round, session-boundary를 제공하지 않는다. 기존 보조 리더·성장 캐시·MainWindow·GameplayTelemetryClient를 실행하지 않았다. 진단 JSON만 session tmp에 보관했다.
- 관련 회귀 **1028/1028**, 전체 비라이브 스모크 통과. 독립 검토에서도 운영 게이트/격리 경계의 구체적 회귀가 발견되지 않았다.
- 근거: `full-recognition-host.log`, `full-recognition-diagnostic.json`, `tests/diagnostic300-final.trx`, `diagnostic300-tests.log`, `diagnostic300-smoke.log`.

## 라운드 UI 읽기 전용 실측

- 새 handle registry RVA `0x2F807F0`. 할당 marker, index 범위, serial, 객체의 역방향 handle을 검증했다.
- vtable RVAs: CTimerDialogWar3 `0x2730B08`, CTimerDialog `0x2768190`, CTextFrame `0x228F5D0`, CTimerWar3 `0x26E04E0`.
- agent `+0x58` → UI, UI `+0x298` → `TimerDialogTitle` text frame. frame `+0x40` → UI, UI `+0x40` → 위 두 전역의 CGameUI. timer도 별도 handle과 UI `+0x2D8`의 일치를 검증했다.
- title 포인터 `+0x4C8`과 `+0x4D0`의 일치를 요구한다. 구 `+0x350`을 강제 재사용하지 않았다. canonical 필드/전역을 단정하지 않으므로 전환 중 정상 상태도 안전하게 거부할 수 있다.
- 초기 제목 `|cffFF0000 1라운드 시작까지|r`는 current round가 아니므로 기존 parser가 null을 반환한다.
- 진행 후 제목은 `|cffFF0000현재 라운드|r : 7|r`, timer `00:00:01`이었다. 두 관측과 registry/전역/alias/소유 관계가 일치했다. 해당 표본은 2,000 bytes, 42ms였다. 이는 관측 당시의 7라운드 UI이지 이후 현재 라운드의 단정이 아니다.
- 격리 round-probe 결과만 있으며 운영 `WarcraftCurrentRoundReader`의 3.0 이식은 하지 않았다.
- 근거: `round-probe/findings.md`, `dual-root-findings.md`, `dual-root-world-final.json`, `after-progression.json`.

## 배포 판단

현재 세션의 기본 패 판독과 진행 변화는 확인했으나 **3.0 운영 지원 승인은 보류**한다. 불변 로컬 identity, 생존/삭제·재접속 경계와 미확인 보조 리더를 먼저 검증해야 한다. 새 프로필 게시, `.3` asset 교체, 새 후보판/공식판 승격은 수행하지 않았다.
