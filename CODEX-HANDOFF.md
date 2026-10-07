# 현재 로컬: BETA 1.0.7 · 중복 패 읽기 수정·보조 주소 재사용 (2026-09-21)

- 사용자 ‘현재 패 확인 중’ 실측:13.086초 기본94중35성공,58회Duplicate CUnit·1회vtable거부; 전체6중3성공. 관측15라운드29개, 선택데이터2.321. 단순 성장형 탐색 대기가 아니라 기본 목록의 중복 주소 때문에도 패가 만료됐다.
- 동일 주소가 여러 슬롯에 있어도 슬롯별identity/등록stamp/owner/rawcode를 검증하고 물리유닛은1회만 센다. 원본슬롯벡터 전후비교와binding해시를 보존한다. 세대/등록/소유자변경은 계속 거부한다.
- 일반앱은 최초owner탐색 후 동일문맥/헤더/전체globals/QR/native검증으로 기존주소를재사용한다. 매번3~4GB를읽지않는다. 기본진단/감사호출은strict전체탐색유지. cached모드는새로등장한다른owner후보를전역탐색하지않으며 이보장차이는진단필드/테스트에명시했다. 권한/freshness확대없음.
- 성장형부재를 영구캐시하거나 특정라운드에무조건0처리하지않고 현재슬롯을새로읽는다. reader검증실패/문맥변경/세션초기화는폐기·재탐색. 일반inventory후속실패만으로owner주소를폐기하던외부Reset제거.
- 실측보조리더:최초1585.184ms·전체탐색3,655,896,450bytes→재사용6회33.444~51.339ms·각1,019,588bytes/전체탐색0. 앱전체화면지연수치가아님.
- 최종실게임24.112초 기본74/74·전체60/60수락,error0,mismatch0. 관측23라운드60개, 첫FullDiscovery1회+Reuse59회, 성공프레임최대간격337.156ms. baseline과다른게임상태이며표본내수량동일; 실제조합순간/장시간/다인/새판실측완료주장은아님.
- 집중276PASS,전체3735PASS/0FAIL/1기존SKIP,독립코드리뷰GREEN. E세로창·리사이즈·능력치·테마·UI연속성등14/14소스SHA동일. CUA사용없음.
- BETA1.0.7(내부1.0.7-test.1)Release·패키지검사통과. BETA1.0.6정상종료후새로컬앱실행,제목/응답확인. 공개서버·업데이트채널게시없음.
- 증거/빌드: `C:/Users/123/Downloads/randypick-growth-scan-diagnosis-20260921/`의 `EVIDENCE.md`, `CODE-REVIEW.md`, `live-final-summary.json`, `native-cache-comparison.json`, `tests/full-final.trx`, `user-launch.json`, `publish/RandyPick.exe`.

---
# 현재 로컬: BETA 1.0.6 · 세로 조합식 E안·창 크기 조절 (2026-09-21)

- 현재 능력치 창 아래에 E안 세로 조합식을 처음 배치한다. 기본 326×440, 최소 320×260. 부족 재료 초상·수량 배지와 선택 유닛/조합 키·횟수/결과를 압축 행으로 표시한다. 그레이·화이트 테마 유지.
- 창 가장자리/모서리로 크기 조절하고 제목으로 이동한다. 전용 `craft-window-vertical-e-v1.json`에 위치/크기를 저장해 다시 열기/다음 실행에 복원한다. 다른 모니터/작업영역 변경 시 화면 안으로 보정한다. 비활성 오버레이 동작 유지.
- 패 갱신 때 단계 펼침을 유지하고 같은 패는 컨트롤을 재생성하지 않는다. 부족 재료와 단계 목록은 독립 스크롤한다. 기본 높이에서는 첫 상세+두 번째 행+세 번째 일부가 보이며 더 많은 단계는 스크롤/창 확대가 필요하다.
- 실제 WPF 합성 입력 및 격리 native 창 검증160 PASS/0FAIL,19캡처. 실제 아래 배치·이동/크기 변경 후 숨김/종료 저장·새 창 복원·포커스 유지·hook/timer/HWND 정리 확인. native5개 모두 종료. CUA 사용 없음.
- 전체3687 PASS/0FAIL/1기존SKIP. 새 테스트의 입력/리소스 fixture 보정 후 통과했고, 마지막 테스트 정리 예외 경로 보완 후 집중4 PASS. 독립 코드리뷰 완료. 제품 DLL은 QA160개 통과 산출물과 동일.
- 인식 안정화·속도·현재 능력치·테마·조합 계산·비활성 동작17개 소스 해시 동일. 실게임 조합/장시간 인식 연속성을 새로 검증한 결과는 아니며 아래 실측/남은 항목은 유지한다.
- Release 게시 빌드·패키지 자체 검사 성공. 이전 BETA1.0.5를 정상 종료하고 새 BETA1.0.6 실행, 제목/응답 확인. 내부1.0.6-test.1, 기존 updater 호환 유지. 공개 서버·업데이트 채널 변경 없음.
- 증거/빌드: `C:/Users/123/Downloads/randypick-vertical-craft-e-20260921/`의 `EVIDENCE.md`, `CODE-REVIEW.md`, `tests/full-green.trx`, `tests/vertical-final.trx`, `ui/QA-RESULT.md`, `user-launch.json`, `publish/RandyPick.exe`.

---
# 현재 로컬: BETA 1.0.5 · 기본 패 갱신 속도 개선 (2026-09-21)

- 전체 성장 탐색 중 기본 패 재확인 대기를 250ms에서 100~250ms로 조절한다. 읽기 비용이 커지면 대기를 늘리고 완료 시각부터 계산해 연속 읽기 폭주를 막는다. 기존 단일 읽기 작업자·수신 여유 확인을 유지한다.
- 실제 주소 기준 8바이트 정렬된 포인터 검색으로 CPU 비용을 줄였다. 전체 메모리 순회·소유자 유일성·후보 헤더 재읽기·취소/시간/용량 제한은 보존한다.
- 실제 게임의 기본 프레임 간격 중앙값 275.282→120.548ms, p95 296.123→145.322ms. 수정 후 12.807초 기본89/89·전체6/6 수락, 오류0. 정지된 3라운드12개 패 및 선택 데이터2.321에서 측정했으며 실제 뽑기/조합의 화면 지연이나 장시간 끊김 해소 검증은 아니다.
- 전체 성장 탐색은 여전히 약1.8초이고 약3.58GB 읽기가 대부분이다. 같은 실제 버퍼의 후보 검색 CPU는67.7~83.6→29.6~35.4ms. 기본 패를 전체 탐색 사이에 더 자주 전달한다.
- 관련196 PASS, 전체3659 PASS/0 FAIL/1기존SKIP, 독립 코드리뷰 결함 없음. 정렬 경계 차등33개·비용별 대기 및 밀린 호출 폭주 방지 회귀 포함.
- C안 현재 능력치·그레이 테마·BETA1.0.4 순간 오류 보호 등12개 소스 해시 동일. 기존 관찰 기록과 서버 설정 보존.
- BETA 1.0.5 (내부1.0.5-test.1) Release·패키지 검사 통과. 이전 앱 정상 종료 후 새 로컬 앱 실행, 제목/응답 확인. 공개 서버와 업데이트 채널 변경 없음.
- 증거/빌드: `C:/Users/123/Downloads/randypick-recognition-speed-20260921/`의 `EVIDENCE.md`, `CODE-REVIEW.md`, `tests/full-final.trx`, `user-launch.json`, `publish/RandyPick.exe`.

---
# 현재 로컬: BETA 1.0.4 · 일시 오류 시 정상 패 유지 (2026-09-21)

- 새 기본 프레임 이전의 `TransientReadError`가 기존 정상 패를 즉시 지우던 실제 화면 처리 경로를 수정했다. 원래3초 유효기간/리비전/세션 경계를 유지한다. 읽기 시각을 갱신하거나 성장 정보를 임의 재사용하지 않는다.
- 합성 프레임을 실제 MainWindow에 전달해 RED7실패 → GREEN30/30 확인. 전체3622 PASS/0 FAIL/1기존SKIP, 독립 코드리뷰 완료. 기존 메모리 리더·C안 능력치·테마 관련9개 소스 해시 동일.
- 수정 전 실제 게임 정지 패에서123.9초 기본409/409·전체60/60 성공, 3라운드12개 동일. 사용자 확인: 현재 패는 맞지만 플레이 중 끊김. 게임 진행/실제 조합 중 재현 및 수정 후 해소는 아직 미검증이다. 게임 진행 요청은 전달했다.
- BETA 1.0.4 (내부1.0.4-test.1) Release/패키지 검사 통과, 새 로컬 앱 실행 및 정상 제목/응답 확인. 공개 서버/채널 변경 없음.
- 별도 관찰 기록 큐256개 한도 도달. 진단 저장/전송 문제이며 코드상 인식 자체를 차단하지 않는다. 기존 기록 보존, 서버/큐 변경 없음.
- 증거/실행 파일: `C:/Users/123/Downloads/randypick-recognition-fix-20260921/`의 `EVIDENCE.md`, `CODE-REVIEW.md`, `transient-fix/green/proof.json`, `publish/RandyPick.exe`.

---
# 현재 로컬: BETA 1.0.3 · 숫자 버전 표기 (2026-09-21)

- 사용자 버전은 `BETA 1.0.3 → BETA 1.0.4 → BETA 1.0.5`로 올린다. 메인·오버레이·업데이트 안내의 긴 접미사를 제거했다. `docs/VERSIONING.md` 참조.
- 내부 식별자는 `1.0.3-test.1`이다. 기존 test.8/test.14 클라이언트의 test 채널·서명 검증·버전 비교 호환을 보존한다. 진단 기록에는 원래 식별자를 남긴다.
- 관련 테스트143 PASS/0 FAIL, 독립 코드리뷰 완료, Release 패키지 자체 검사 성공. 기존 테스터 버전→1.0.3 및1.0.3→1.0.4의 오프라인 서명 업데이트 검증 포함.
- 새 로컬 앱의 창 제목 `랜디픽 BETA 1.0.3` 및 정상 응답 확인. C안 능력치·그레이 테마·패 인식 변경을 보존했다. 서버 게시/업데이트 채널 변경은 하지 않았다.
- 증거/실행 파일: `C:/Users/123/Downloads/randypick-beta-1.0.3-20260921/`의 `EVIDENCE.md`, `user-launch.json`, `publish/RandyPick.exe`.

---
# 현재 로컬: 현재 능력치 C안 · 그레이/화이트 테마 (2026-09-21)

- 개발 버전 `1.0.2-test.14.recognition.3` 실행. 현재 능력치 창은 C안(물리/마법별3핵심, 중립4핵심, 나머지3열/기본역할4열), 326x440으로 정리했다. 0·부분합 표시와 미확인값 목표 판정 차단을 보존했다.
- 메인·탐색·조합·능력치·공통 컨트롤/팝업/제목바를 중앙 RandyPickTheme의 그레이/화이트로 통일했다. 선택과 추천은 테두리·배경·문구로 구분하고, 원래 유닛 초상과 경고/오류 표시는 보존한다.
- 최종 전체3612 PASS/0FAIL/1기존SKIP, 독립 테마·능력치 리뷰 완료. 합성 패를 실제 WPF 컨트롤로 표시해300확인/16캡처 통과. 소형창·긴 수치·0·물리/마법·상세 펼침·배율 검증. 네이티브 테스트 창0, 남은 창 객체0.
- 인식 관련 기존10개 소스/테스트 해시 동일. 실제 게임 연속 플레이 무중단/수치 정확성을 새로 입증한 결과는 아니다. 인식 측정 근거와 미완료 사항은 아래 이전 기록을 유지한다.
- Release 게시 빌드·패키지 검사 통과. 공개 서버와 업데이트 채널은 변경하지 않았다.
- 로컬 실행 파일/증거: `C:/Users/123/Downloads/randypick-gray-c-20260921/`의 `publish/RandyPick.exe`, `EVIDENCE.md`, `CODE-REVIEW.md`, `ui/evidence-final/proof.json`.

---
# 현재 로컬: 패 수치 0·부분합 표시 (2026-09-21)

- 로컬 개발 버전 `1.0.2-test.14.recognition.2`. 미확정 효과가 있어도 유한한 계산 합계를 표시하며, 더할 수치가 없으면 `0`으로 표시한다. 부분합·참고 안내를 유지하고 미확정 자료의 목표 충족·진행률 판정은 보류한다.
- 개별 진단 KPI가 창과 다르게 목표 충족을 판단하던 불일치도 수정했다. 현재 관측 없음/비정상 수치의 숨김·미확인 처리는 유지한다. 수치 계산기·원본 자료·패 인식 코드는 변경하지 않았다.
- Stats·브랜드 집중 테스트26 PASS, 독립 코드리뷰 완료, 숨김 WPF에서 기록된24개 패를 재생해 네 KPI·지원 효과0과 부분합 표시 확인. 네이티브 창 생성0, 종료 후 창 객체0. 합성 화면 검증이며 실제 게임 수치 정확성 검증은 아니다.
- Release 게시 빌드·패키지 자체 검사 성공. 이전 인식 후보의 관련 소스/테스트10개 해시 동일. 기존 로컬 앱을 정상 종료하고 새 개발 빌드를 실행했다. 공개 업데이트 채널 변경 없음.
- 증거: `C:/Users/123/Downloads/randypick-stats-zero-20260921/EVIDENCE.md`, 로컬 실행 파일은 같은 폴더의 `publish/RandyPick.exe`.

---
# 최신 인계: 패 인식 로컬 후보 test.14.recognition.1 (2026-09-21)

최신 상태는 [WORK-STATUS.md](WORK-STATUS.md) 맨 위를 따른다. 아래 test.13 이하 배포 기록은 과거 기록이다.

- 기존 OmO Native의 전달 지연 방지 수정을 보존하고, 동일 최종 스냅샷의 기본/전체 결과 동시 반영과 일시적인 목록 변경의1회 재시도를 추가했다.
- 코드리뷰와 자동·숨김 WPF 검증 완료. 현재 게임에서 기본41회/전체6회 정상 읽기를 확인했지만, 유닛 변화가 없어 실제 조합 중 끊김 해결이나 성능 향상을 단정할 수 없다.
- 사용자가 유닛 갱신을 알려줘 후속 실측 완료: 기존7개→현재24개·6라운드, 기본37/37·전체6/6 성공. 실제 게임 프레임을 숨김 WPF에 넣어 추천·부족재료·선택 유지 확인. 변화 순간은 측정 시작 전이어서 아직 미검증이다. 후속 증거는 `live-updated-units.json`, `live-updated-ui.json`이다.
- 전체 탐색 비용과4인/새 게임 식별은 미완료다. 근거 없는 이전 owner 주소 재사용으로 전체 유일성 검증을 생략하지 않는다.
- 증거: `C:/Users/123/Downloads/randypick-recognition-followup-20260921/EVIDENCE.md`, `CODE-REVIEW.md`, `live-pair.json`, `ui/evidence/proof.json`.
- 로컬 빌드: 같은 증거 폴더의 `publish/RandyPick.exe`. 공개 업데이트 채널은 기존test.14를 보존했다.

---
# 현재: 관측 텔레메트리 묶음 전송 test.13 배포 완료 (2026-09-18)

1.0.2-test.13을 Cloudflare test 채널에 게시했다. 관측 기록을 묶어 보내고 네이티브 OPTIONS를 제거했다. test.12 다운로드 키는 채널 전환 후 삭제했다.

- 다운로드: https://orand-updates.epic42121.workers.dev/downloads/1.0.2-test.13/RandyPick.exe
- SHA256: `3d1aebd884c1569aa1874096b878d12aa4d3d20af7883c791b7414a0e816e484` / 78,644,976 bytes.
- 증거: `C:/Users/123/Downloads/RandyPick-validation-20260918/telemetry-batch-test13`.

> 최신 상태는 [WORK-STATUS.md](WORK-STATUS.md)를 먼저 확인하세요. 아래 test.12 이하 기록은 보존합니다.

---

# 이전: 조합 재클릭 고정 해제 test.12 배포 완료 (2026-09-16)

1.0.2-test.12를 Cloudflare test 채널에 게시했다. 같은 조합을 다시 누르면 선택이 풀리고 조합창이 닫힌다.

- 다운로드: https://orand-updates.epic42121.workers.dev/downloads/1.0.2-test.12/RandyPick.exe
- SHA256: `e7e3c54ee18c83caead60ef3873ca9f474bf2028bb1bc6528a9d02a3fe1b18e3` / 78,631,686 bytes.
- 증거: `C:/Users/123/Downloads/RandyPick-validation-20260915-01/unpin-test12`.

> 최신 상태는 [WORK-STATUS.md](WORK-STATUS.md)를 먼저 확인하세요. 아래 test.11 이하 기록은 보존합니다.

---

# 현재: 일반 모드 유틸 세분류 test.11 배포 완료 (2026-09-16)

1.0.2-test.11을 Cloudflare test 채널에 게시했다. 물딜/마딜+유틸 칸, 보잡/광잡 분리, 유틸 있는 전 등급, 세라핌 4기 역할을 포함한다.

- 다운로드: https://orand-updates.epic42121.workers.dev/downloads/1.0.2-test.11/RandyPick.exe
- SHA256: `c8e98a631c2d4ec9523bde91bc5754632c0670719d2ffed4e72686829350ba7a` / 78,631,472 bytes.
- 증거: `C:/Users/123/Downloads/RandyPick-validation-20260915-01/utility-test11`.
- 실제 한 판의 유틸 칸 확인은 아직이다. `MemoryDiagnostics`, 게임 파일 직접 읽기, 게임 입력·주입은 계속 금지한다.

> 최신 상태는 [WORK-STATUS.md](WORK-STATUS.md)를 먼저 확인하세요. 아래 test.10 이하 기록은 보존합니다.

---

# 현재: 일반 모드 인식 연속성·F1·업데이트 대기 test.10 배포 완료 (2026-09-15)

테스터 피드백 핫픽스 1.0.2-test.10을 Cloudflare test 채널에 게시했다. 패 인식의 순간 실패는 마지막 정상 추천·조합 흐름을 유지하고, F1은 기본키와 반복 방지로 동작한다. 오버레이·조합창은 게임 포커스를 빼앗지 않으며 조합창은 닫기 버튼으로 숨길 수 있다. 새 업데이트는 자동 설치하지 않고 메인·오버레이의 `업데이트 대기 중` 버튼에서 게임 종료 후 사용자가 진행한다.

- 다운로드: https://orand-updates.epic42121.workers.dev/downloads/1.0.2-test.10/RandyPick.exe
- 피드: https://orand-updates.epic42121.workers.dev/v1/channels/test/win-x64
- SHA256: `0eaf854593c0aff743a5f7c95bb294985eff9383962047fffdf7f4870fa10b76` / 78,626,777 bytes.
- 증거 루트: `C:/Users/123/Downloads/RandyPick-validation-20260915-01/tester-hotfix-test10/release`.
- 합성 UI·네이티브 입력·패키지·공개 업데이트 경로는 검증했지만, 실제 게임 한 판의 연속 인식은 아직 검증하지 않았다. `MemoryDiagnostics`, 게임 파일 직접 읽기, 게임 입력·주입은 계속 금지한다.

> 최신 상태는 [WORK-STATUS.md](WORK-STATUS.md)를 먼저 확인하세요. 아래 test.9 이하 기록은 보존합니다.

---

# 현재: 2.320 자동 기록·인식 진단 test.9 배포 완료 (2026-09-15)

사용자가 2.320 플레이 관측 기록·패 인식 끊김 진단 수집, 새 동의, 실제 서버 수신 검증과 배포를 명시 승인했다. 기존 test.8 위에 기능을 추가하여 test 채널을 1.0.2-test.9로 게시했다.

- 다운로드: https://orand-updates.epic42121.workers.dev/downloads/1.0.2-test.9/RandyPick.exe
- 78,620,717bytes / SHA256 b50e487b62589e8ba6499c296003c3a40aac7373fd161255e5356bbf33b37d54.
- 기존2.314 v2/v3 수집·학습을 켜지 않고 /v4/observations, telemetry_v4_packets에 참고 관측을 분리했다. 실제 소유·생존·완전한 패·조합 실행·승패로 승격하지 않는다. 알 수 없는 라운드는 누락값이며 0으로 기록하지 않는다.
- 수집: 관측 유닛·수량·변화, 확인된 라운드, 실제 화면 추천과 사용자가 선택한 목표, basic/full/표시/scan 상태, 읽기 시간·표시 만료·끊김·복구 간격, 앱·지원 게임·데이터 버전. raw reason은 허용된 코드로만 바꾼다. PID·메모리 주소·파일경로·닉네임·채팅·화면·고정설치ID 없음.
- 동의4 재요청: 동의3으로는 새 런타임이 시작되지 않는다. 거절/닫기 종료. 동의 창 기본/최소너비 상·중·하6PNG와 기존 메인1PNG, 독립2검토 모두 PASS. 모든 개인정보·보존 안내 유지.
- UI/읽기 스레드에는 디스크·HTTP를 넣지 않는다. 큐는 별도 쓰기/전송 작업,30초 재시도, 최대256디스크패킷/32MiB·256메모리패킷/30일. 동의를 디스크·전송·ACK 후삭제 전에 확인하며 파일은 정확한 schema/packetId ACK 후삭제. 새앱/게임구간은 랜덤ID. HTTP리디렉션 차단.
- 원격추가migration0005_observed_telemetry.sql 및 Worker127466f9-6268-4143-a534-d935e196f050 배포. 기존 v3 OPTIONS204 유지. v4 OPTIONS204/schema4/acceptedtrue 확인. 30일 정리, 비공개 rawGET, 합성 source 분리.
- 실제 WPF controlled observation → 동일제품클라이언트 → 실제 Cloudflare ACK → 별도 D1 SELECT로 수신 확인. 최종 시나리오30패킷/2세션(전체합성54패킷/4세션), 기본읽기실패·표시만료·복구gap·패수량변경·목표선택·추천·새세션·종료 확인. 모두 synthetic-validation; 실제 테스터 live 기록은 당시0건. 실제 테스터의 끊김 원인을 고쳤다고 말하지 않는다.
- 회귀: 관련메인341tests PASS, 클라이언트단독44tests PASS(중복범위 포함), 서버전체45tests PASS. WPF합성2609assertion PASS/0FAIL +184관측기록,59PNG. 기존 read/core/조합/수치9파일 hash동일.3초 freshness/게임권한/자동입력금지/모드잠금/분리조합창 유지.
- 공개키와 번들780payload/332Data해시 검증, 실행파일 self-probe no network/game/settings. 불변새키없음확인 → asset업로드 → 실제packaged updater로 공개파일 digest/size/version검증 → 서명된testfeed게시. 기존test.8 및 새test.9 업데이터 모두 이전버전업데이트 제공/같은버전억제 확인. stable404·profiles200본문 보존, 업데이트Worker자체변경없음.
- 소스/로컬빌드: C:/Users/123/Downloads/RandyPick-validation-20260915-01/telemetry-test9/publish-final/RandyPick.exe. 공개된 test.8 및 test.9키를 덮어쓰지 않는다.
- 증거: telemetry-test9/release-verification.json, server-final-query.json, e2e-final/server-receipt.json, regression-tests.log, client-green.log, server-full-suite.log, wpf-regression/diagnostic-checks.json, consent-final2, final2-ui-integrity-review.md, evidence/final2-ui-visual-review.md, package, sign, public-download, public-updater, previous-client-updater, preservation-final.json.
- 컴퓨터 유즈·게임입력·신규게임파일/프로세스읽기 사용없음. 기존 사용자 앱을 강제종료하거나 새동의를 대신 클릭하지 않았다. 베타테스터에게 메시지를 직접 전송하지 않았다.
- 다음 진단은 test.9로 업데이트·재동의한 테스터의 source=live 기록을 먼저 조회한다. 이전 test.8의 플레이는 소급수집할 수 없다. 앱의 제보용 정보복사는 최종1회 상태여서 시계열을 대체하지 않는다.

---

## 이전 기록 (보존)

> 최신 상태는 [WORK-STATUS.md](WORK-STATUS.md)를 먼저 확인하세요. 사용자 승인으로 1.0.2-test.8 EXE와 서명된 test 업데이트 채널 배포를 완료했습니다. 공개 다운로드 및 이전/현재 패키지 업데이트 코드 검증을 통과했습니다. 배포 글은 docs/beta-1.0.2-test.8.md입니다. stable/profile/Worker는 유지했습니다. 아래 로컬 전용 상태 기록은 과거 기록입니다.

> 최신 상태는 [WORK-STATUS.md](WORK-STATUS.md)를 먼저 확인하세요. 조합창 체크박스·X 제거, 항상 위 복구, 단일 헤더와 가로 조합/부족2열을 적용한 로컬1.0.2-test.8을 실행했습니다. 최종 파일은 craft-hud-refinement/publish-final/RandyPick.exe입니다. 관련154테스트·합성2,609assertion·59화면·독립2검토 PASS. 실제 게임 검증/공개 업로드/채널 변경은 이번 턴에서 수행하지 않았습니다. 아래 과거 기록은 보존합니다.

> 최신 상태는 [WORK-STATUS.md](WORK-STATUS.md)를 먼저 확인하세요. 최신 사용자 요청에 따라 탐색 유닛 선택 시 하단 중앙에 별도 조합창을 여는 로컬1.0.2-test.7을 적용·실행했습니다. 검증: xUnit92 PASS, 합성2,602 assertion PASS 및184 기록,59 PNG, 독립 구현/시각 검토 PASS. 최종 파일은 detached-craft/publish/RandyPick.exe입니다. 실제 게임 내 위치·조합 갱신은 미검증이며 공개 업로드/채널 변경은 하지 않았습니다. 아래 이전 인계 기록은 보존합니다.

> 최신 상태는 [WORK-STATUS.md](WORK-STATUS.md)를 먼저 확인하세요. C안 배치를 유지한 크기·간격 축소를 적용했고 로컬 1.0.2-test.6 패키지 및 합성 화면 검증을 마쳤습니다. 최종 파일은 density-applied/publish-final/RandyPick.exe입니다. 이 작업에서는 공개 업로드·채널 변경·실제 게임 연속 검증을 하지 않았습니다. 아래 과거 인계 본문은 보존합니다.

> 최신 상태는 [WORK-STATUS.md](WORK-STATUS.md)를 먼저 확인하세요. C안 적용과 탐색 카드 중복 문구 제거 뒤, 전용 패널 압축 시안을 비교하는 단계입니다. 공개 배포는 test.3이고 현재 소스의 최종 패키지는 아직 준비하지 않았습니다. 아래 인계 본문은 보존합니다.

# Codex 앱 작업 인수인계: 랜디픽 유닛 목록·라운드 표시

## Codex 앱에서 시작하는 방법
1. Codex 앱에서 아래 폴더를 기존 프로젝트로 엽니다. 새 빈 프로젝트나 별도 복제본이 아니라, 현재 변경사항이 들어 있는 이 작업 폴더를 사용합니다.
   `C:\Users\123\Desktop\dev\orand-overnight-overlay-20260907-111725`
2. 프로젝트 루트의 `CODEX-HANDOFF.md`를 읽도록 지시합니다. 파일 접근 확인이 필요하면 이 프로젝트와 아래에 명시된 검증 자료 경로만 허용합니다. 금지된 게임 바이너리나 MemoryDiagnostics 접근은 허용하지 않습니다.
3. 다음 지시문을 새 작업에 붙여 넣습니다.

```text
CODEX-HANDOFF.md를 먼저 읽고 랜디픽 작업을 이어서 진행해줘.
현재 작업 폴더의 기존 변경사항을 보존하고 reset/clean/checkout으로 덮어쓰지 마.
마지막 유닛 목록 UI 수정은 아직 빌드·검증되지 않았으니 그 부분부터 확인해줘.
실제 번들 프로필, 기존 유닛 목록, 라운드 표시 연결을 검증하고 문서의 순서대로 마무리해줘.
금지된 게임 접근과 분석 범위 제한을 유지해.
프로브·합성 UI·패키지 검증을 정상 앱 실측과 혼동하지 말고, 필요한 검증 전에는 업데이트 채널을 승격하지 마.
```

## 가장 중요한 현재 상태
- 기존 에이전트는 수정을 멈췄습니다. 이 문서 이후 추가 빌드나 배포를 하지 않았습니다.
- 소스 버전: **1.0.2-test.2**, 아직 새 패키지 없음.
- 실제 실행 앱은 누락 프로필 보완 후 **1라운드 관측 표시**가 살아났습니다.
- 보유 패 창에 관측 유닛을 보여주는 마지막 소스 수정은 **아직 미검증**입니다.
- 공개 자동 업데이트 test 채널은 **1.0.1-test.1 유지**. 문제 있는 test.1 후보를 승격하거나 덮어쓰면 안 됩니다.

---

# 상세 작업 기록

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
