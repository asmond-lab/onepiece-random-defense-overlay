# 랜디픽 BETA 1.0.1-test.1: 자동 업데이트 전환 빌드

## 공개 배포
- 실행파일: https://orand-updates.epic42121.workers.dev/downloads/1.0.1-test.1/RandyPick.exe
- 크기: 78,512,186 bytes
- SHA-256: `679EBDD21839A58E4538276670C1F9B0B737030975DC8F92550AB5BF802BE108`
- test 채널만 갱신. stable 및 profiles 응답 바이트/상태 유지.
- 1.0.0-test.1, 0.6.70-test.20260913.3의 기존 OrandOverlay.exe 경로 유지.
- Cloudflare Worker 버전: `09eb4031-14c8-499e-b5ba-3ed4ea41646b`.

## 기능 범위
이 릴리스는 **Warcraft III 3.0/ORD 2.320 정식 실시간 코칭 완료 릴리스가 아니다.**

- 배포 파일명만 RandyPick.exe로 변경. 내부 OrandOverlay 어셈블리, 리소스, 단일 실행/IPC 식별자, 사용자 데이터 폴더 유지.
- 기존 %LOCALAPPDATA%\OrandOverlay 설정과 기록 유지.
- 같은 test 채널의 서명된 업데이트 지원. 자동 업데이트 기본 켜짐, 사용자 끄기 설정 저장.
- 게임 중 또는 판 상태 미확인일 때 설치 유예. 개발 PC도 예외 없음.
- 설치 시 서명된 제안과 다운로드 경로, 실제 파일 크기/해시를 재검증.
- 예전 1.0.0-test.1에는 업데이트 조회 차단 코드가 있으므로 최초 1회 수동 전환 필요.
- ECDSA 업데이트 서명은 Windows Authenticode 서명이 아님.

## 3.0 읽기 검증과 미완료 범위
현재 판에서 읽기 전용 진단으로 관측 16개 중 카드 매핑 7개, 출처가 입증된 보조 개체 9개를 분리했다. 카드 인식률 기준 0.6은 낮추지 않았다. 알려진 보조 개체만 제외한 분모는 7이며 나머지 미분류 개체는 계속 포함한다.

실제 관측값 7개를 변경하지 않고 격리된 WPF 검증 호스트의 일반 후보 화면과 참고 스탯 창에 전달했다. 3초 신선도, 실패/만료 시 숨김, 자동 코칭/기록/텔레메트리 부작용 차단을 검증했다. 이것은 설치판 정식 인식 활성화와 다르다.

미완료: 생존/사망, 내 플레이어, 완전한 현재 패, 실행 맵, 일관된 라운드·스토리·항법 및 네이티브 스탯 능력 증명. 3.0 활성 프로필은 0개이며 LiveRecognitionSupported와 AutomaticNavigationScoringSupported는 false로 유지했다.

추가 조사: JASS의 현재 라운드 전역 `pb`와 다음 카운터 `Eb`를 확인했지만 3.0 스칼라 디코딩/일관성 판독은 아직 구현·승인하지 않았다. 로컬 플레이어를 저장한 전역 변수는 찾지 못했다. 지속 조합 인덱스는 죽은 개체도 보존하므로 생존 검증을 대체할 수 없다.

## 검증
- 전체: 2,996 통과, 0 실패, 기존 1개 건너뜀 / 2,997.
- 스모크: 467 assertions.
- 맵 2.314/2.320 오프라인 검증, carry 정책 통과.
- 스탯 UI: 36 상태/크기 사례 통과.
- Cloudflare Worker: 138 테스트 통과.
- 공개 파일 재다운로드 후 778 번들 항목, 330 Data 해시, 서명/크기/해시 일치 확인.
- 실제 새 EXE의 `--verify-package` 점검 exit 0. 파일명/버전/자동 업데이트/자기 교체 가능/로고 확인, 일반 런타임·네트워크·게임 읽기·사용자 설정 접근 없음.
- 최초 비공개 후보의 WPF StartupUri=null 종료 오류는 기존 안전 초기화 도우미를 사용하도록 수정 후 재검증했다. 실패 후보는 배포하지 않았다.

## 배포 ZIP
`RandyPick-BETA-1.0.1-test.1.zip`: RandyPick.exe + README.txt만 포함.
- 크기: 72,958,005 bytes
- SHA-256: `F83F85FA39B827BD1C3866F1666C7F0F1F3133B8E254775B66A767C3E30C9858`
- 설정, 기록, 로그, 소스, 비밀 키 미포함.

검증 증거는 Aside 세션 IFNrohE9w9cRfhLp의 tmp/bridge-release-r2, tmp/live-verification/nonlive-final-r2-20260914-160020, tmp/live-native-ui-proof에 보관했다.
