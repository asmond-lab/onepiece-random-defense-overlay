# 현재 배포: BETA 1.0.17 · 서명된 test 채널 1.0.17-test.1 (2026-09-25 UTC)

**ORDR 2.323 test 배포 PASS.** 실제 새 맵 압축파일 SHA256 `eac74a90a967473196c84446b880387857f50a828083c0e1b5fc02b325663535`와 독립 추출 자료를 반영했다. 266개 조합은 새 원본에서 다시 확인해 별도 2.323 출처로 묶었고, 환전소 선택위습의 35% 2개 / 나머지 1개 분기를 구분했다. 2.322 자료와 기록은 유지하며 현재 보기 진단 참고·알 수 없는 조건·세션 맵 검사·게임 자동 조작 금지 경계를 유지한다. 추가 베리의 새 `not Ss` 조건은 관측할 수 없어 확정 보상으로 안내하지 않는다. 클래식/TMO의 9월 25일 23시 제한은 패치노트 주장이지 랜디픽의 저장 제한이 아니며 시간대는 알 수 없다. 디스코드 공지 본문은 사용자 요청에 따라 이번 범위에서 제외됐다. 실게임 스킬 수정·TMO 등록·워커 정상 설치/시작은 검증하지 않았다.

- 공개 다운로드: https://orand-updates.epic42121.workers.dev/downloads/1.0.17-test.1/RandyPick.exe · **78,916,695 bytes** · SHA256 `da86714abc29c12dae216b851d12bbb1cad2aa93e544f1c5683dff8c24df600a` · PE FileVersion `1.0.17.0`, ProductVersion `1.0.17-test.1`.
- 서명 test 피드: https://orand-updates.epic42121.workers.dev/v1/channels/test/win-x64. 로컬·전체 공개 파일 바이트/해시/PE 일치, raw 피드 원문과 서명 매니페스트 동일, P-256 서명 검증. stable/profile 원문 동일, 남긴 현재+직전 3개 자산 확인; 창 밖으로 밀린 단일 이전 키만 정리했다.
- 최종 전체 **3,890 PASS / 0 FAIL / 1 기존 SKIP**; Release build exit 0 (경고/오류 0), 별도 publish 경고 CS8601/CS9113 기록. 합성 WPF는 실제 비주 디스플레이 DISPLAY2에서 새 버전/출처 화면만 14장 확인, 5개 소유 HWND 파괴·fixture PID 종료 확인. 정상 패키지 설치·실제 게임 테스트와 구분한다.
- 전체 근거: `artifacts/ordr-2323/{BASELINE,PATCH-RESULTS,TEST-GATE,GUI,RELEASE,FINAL}.md`, 배포 안내 `docs/beta-1.0.17-test.1.md`. Git 저장소 메타데이터가 없어 커밋하지 않았다.

## 아래 1.0.16 및 이전 기록은 당시 판정 그대로 보존

# 현재 배포: BETA 1.0.16 · 서명된 test 채널 1.0.16-test.1 (2026-09-24 UTC)

**공개 test PASS.** 현재 서명 피드는 `application/test/win-x64` `1.0.16-test.1`을 가리킨다. 전체 공개 파일 GET과 선정된 로컬 attempt-5의 바이트·PE 일치 확인: **78,843,344 bytes**, SHA256 `234a3bc4232b6540513288ec6cb6d23fc1c0421bb2f95be889b57d0d8d8320c3`, FileVersion `1.0.16.0`, ProductVersion `1.0.16-test.1`.

- 다운로드: https://orand-updates.epic42121.workers.dev/downloads/1.0.16-test.1/RandyPick.exe
- test 피드: https://orand-updates.epic42121.workers.dev/v1/channels/test/win-x64
- recovery-3 동일 소스 게이트: 집중 **1,229/1,229 PASS**, 전체 **3,880 PASS / 0 FAIL / 1 기존 SKIP**, 앱·캡처 도구 Release 빌드 둘 다 exit 0. 적격 921파일 지문 `ad088c7cda5dac69397775436d39b49206b1a8bb9dd125ef7267da4534bbb544` 유지. 선정 패키지는 `artifacts/user-copy/recovery-3/package/1.0.16-test.1/attempt-5/publish/RandyPick.exe`; publish·비런타임 package probe exit 0, 번들 Data 346개 바이트 확인. publish 경고 CS8601/CS9113 총 4건은 숨기지 않았다.
- 동일 DISPLAY2 비런타임 WPF 59 PNG / 47 기본 UIA / 44 동작과 독립 시각·한글·구조 판정 PASS. 실제 UIA로 유지로 카드 선택, TOP에 선택 제목·부족 재료, 스크롤 BOTTOM에 요구 골드 10000·목재 7·미확인 조건을 확인했다. KING/PICK 및 실제 보유·조합 가능은 미확인; 이전 TMO 능력치는 참고값, 246행 유틸리티 보드는 오프라인/프로덕션 소비자 없음. 정상 스크롤은 잘림이 아니다. 소스·실제 UIA의 툴팁 문구만 확인했고 실제 포인터 hover 이미지는 얻지 못했다.
- 기존 서명된 `1.0.15-test.1` 피드와 새 키 404를 쓰기 직전에 확인하고 새 EXE와 test 포인터만 게시했다. 공개 전체 파일/해시/PE 및 원문 피드=서명 manifest/P-256 P1363 서명을 사후 검증했다. stable=404 유지, 프로필 피드 `1.20260913.1` 원문 동일, 이전 세 test 파일 및 프로필 자산 HEAD 200·ETag/크기 보존. 삭제 없음.
- 이는 **공개 파일·피드 및 합성 GUI** 검증이지 정상 패키지 설치·시작·실게임 플레이·실제 조합·동의 저장 실패 주입 증명이 아니다. 안전하지 않은 오류 경로는 소스 감사 후 미실행으로 기록했다. 사용자 앱·게임·프로필을 조작하지 않았다. 근거: `artifacts/user-copy/recovery-3/release-documentation.md`, `remote/release-verdict.md`; 배포 글: `docs/beta-1.0.16-test.1.md`.

## 아래 recovery-2 및 이전 기록은 당시 판정 그대로 보존

# ORDR 2.322 / 전체 사용자 문구 recovery-2: BETA 1.0.16 / 1.0.16-test.1 · 로컬 후보, 미게시 (2026-09-24/25)

**현재 판정: BLOCKED, 게시 없음.** 마지막으로 검증된 공개 test 배포는 `1.0.15-test.1`(아래 과거 공개 기록)이다. 새 공개 URL·해시·버전은 없다. `artifacts/user-copy/recovery-2/stable/gate.md`의 집중 1,229 PASS와 달리 전체는 **3,863 PASS / 17 FAIL / 1 기존 SKIP, exit 1**이었다. 실패 게이트는 역사로 보존하며, 통합 수정 `integration/fixes.md`의 목표 테스트 8/8은 마지막 테스트-only 수정 및 전체 게이트를 대체하지 못한다. 게이트 후 Release build/fingerprint PASS 없음. `gui/{structure-verdict,cjk-verdict}.md` 둘 다 BLOCKED/FAIL, 새 게이트 캡처 **0장**; `package/attempt-selection.json` 및 선정 패키지/PE/해시와 recovery-2 일곱 담당자 새 영수증도 없다. `remote/release-verdict.md`는 서명·업로드·피드 쓰기 없음, 현재 원격 상태 미조회라고 기록한다. **공개 쓰기는 수행하지 않았다.** 상세: `artifacts/user-copy/recovery-2/release-documentation.md`.

원본 일곱 담당자와 recovery-1 일곱 담당자의 범위 기록 및 원본 fixture RED/콜백 소유권 증거는 유지한다. 새 recovery-2 **게이트 전** 합성 WPF fixture에서는 앱 소유 브라우저의 실제 Yujiro 카드를 UIA Invoke했다. TOP 픽셀은 선택된 `한마 유지로` 제목과 부족 재료 행, 스크롤된 BOTTOM은 **골드 10000 / 목재 7 / 미확인 조건 안내**를 그렸다. 자원이 두 뷰포트 모두에 보일 필요는 없고 정상 스크롤은 결함이 아니다. 표시 문구에서 개발자 용어/프레임워크 UIA 이름을 줄였지만 게이트 후 전 화면 증명은 없다. KING/PICK 적격성과 실제 조합·보유는 미확인이다. 동의·개인정보의 opt-in/거절, 수집 제외 및 보존 정책은 그대로이며 과거 TMO 능력치는 참고값(실제 수치 미확인), 246행 보드는 오프라인/프로덕션 소비자 없음. 과거 프로필 `다.` 및 2.320 도움말 줄바꿈의 수정은 게이트 후 픽셀로 증명되지 않았다. 서명 서비스/사용자 프로필 없이 안전하게 실행할 수 없는 설치·시작·동의 저장 오류 전이는 소스 감사상 미실행 한계이지 그 자체로 문구 배포 자동 차단 사유가 아니다. 정상 패키지 GUI와 실게임 플레이는 미검증이다.

## 이전 recovery-1 후보 상태 기록 (아래 원문 보존)

# ORDR 2.322 / 전체 사용자 문구: BETA 1.0.16 / 1.0.16-test.1 · 로컬 후보, 미게시 (2026-09-24)

**현재 공개 test 채널의 마지막 검증된 게시 버전은 1.0.15-test.1이다.** BETA 1.0.16 / 내부 `1.0.16-test.1` 전체 사용자 문구는 로컬 후보이며 게시되지 않았다. 원래 실패한 `artifacts/user-copy/stable/gate.md`(집중 1,234/1,242 PASS, 8 FAIL)는 역사적 영수증으로 보존한다. **새 recovery-1 게이트** `artifacts/user-copy/recovery-1/stable/gate.md`는 새 fixture의 실제 UIA 기술명 16개가 남아 있어 집중/전체 테스트, Release build, fingerprint 확인 **전에 중단**했다. `recovery-1/gui/{structure-verdict,cjk-verdict}.md` 두 독립 판정도 FAIL/BLOCKED: 최종 `gui/capture-1/` 없음, 새 게이트 PNG **0장**. `recovery-1/package/attempt-selection.json` 및 선정 패키지/PE/해시 없음; `recovery-1/remote/release-verdict.md`는 서명·원격 쓰기 전 중단을 기록한다. 새 후보의 공개 URL/해시나 현재 원격 상태를 주장하지 않는다. 과거 1.0.15 공개 다운로드 및 이전 상태 기록은 아래 보존한다. 상세 결과는 `artifacts/user-copy/recovery-1/release-documentation.md`, 25항목별 역사·근거는 `docs/local-ordr-2322-candidate.md`를 참조한다.

- 2.322 아카이브 119,516,025 bytes / SHA256 `93baeb58a6d7ad7e25c7e8a44a345f0c39b5907aa9c1774995730d4fbd101d9b`의 오프라인 데이터 및 아카이브 일치 수동 진단 참조만 허용된다. 일반 네이티브 리더/게임플레이/자동 활동 인식은 CODE상 `not approved general reader`이다. 실제 2.322 Warcraft 플레이, 공식 TMO 등록/종료 시각, 실행 중인 게임의 맵 식별은 미검증. 9–12/20의 보드는 직접 224 + 생성 11행 루프 2개 = 246행의 오프라인 모델로, pjass 우선순위상 A0KY 냉철함은 관측 능력 레벨이 필요하다. 프로덕션 보드 소비자는 없고 공유/특성/보드 합계의 실게임 적용은 미검증. 현재 패 능력치는 이전 TMO 프로필/카탈로그 참고값이며 일반 통계 경계 수정으로 참고 합계는 표시하되 목표 준비 판정은 막는다. 2.322 프로필의 **조합 자료** 안내는 선택된 2.322 레시피 소스를 표시하도록 고쳤다. 2.321이 쓰는 2.320 레시피 소스와 별개인 과거 TMO 손수치 경고를 새 2.322 수치로 바꾸지 않았다 (`recovery-3/profile-label/{RED,GREEN}.md`).
- **문구 변경 전 recovery-3 직렬 게이트 (과거):** 집중 194/194 PASS (exit 0; 기존 테스트 프로젝트 경고 14건 기록), 전체 3,892 PASS / 0 FAIL / 1 기존 NotExecuted (총 3,893, exit 0), Release build exit 0 (경고/오류 0). 전후 887파일 fingerprint `3d0624060609945d052f9d876e6b3642421dfd143436effa2da70c457c5c7fb8` (`recovery-3/stable/{gate.md,source-fingerprint.json}`). recovery-3 Yujiro RED 2 FAIL/4 PASS에서 GREEN 87/87 PASS: 2.322 `신비` 등급을 유지하며 정확한 `rawcode:2C0h`에만 일반 상위/전설 가까운 조합 후보 경로를 열었다. KING/PICK와 게임 조합 준비 여부는 여전히 해결되지 않았다. fixture 빌드 exit 0, 계약 4/4 PASS (`recovery-3/harness/GREEN.md`). 이전 root/recovery-1/-2 실패·성공 영수증은 역사로 유지한다.
- **문구 변경 전 attempt-4 로컬 패키지 (과거):** `qa-release/recovery-3/package/1.0.16-test.1/attempt-4/publish/RandyPick.exe`, **78,840,199 bytes / SHA256 `79cb9abfbd899cf94b699d1e56f729d496582c90b0a45e064a537dcddc9d14d7`** (공개 해시 아님). publish exit 0 (기존 CS8601/CS9113 경고 4건), 비런타임 `--verify-package` exit 0, PE FileVersion `1.0.16.0` / ProductVersion `1.0.16-test.1`, 번들 Data 346개 바이트 검증. 이전 attempt 1–3 패키지는 보존 (`recovery-3/package/1.0.16-test.1/attempt-4/package.md`).
- **문구 변경 전 capture-4 (과거):** 합성 입력 실제 WPF HWND **49 PNG**, 페인트/크기/SHA 영수증 일치, action 39개 적용. 일반 후보로 실제 생성된 Yujiro 버튼을 UIA Invoke로 선택했다 (기존 recovery-2의 직접 `model.Select` 증거와 다름). 탐색 15개는 콤보 선택 핸들러로 각각 **caption과 수동 계획 본문**을 함께 칠했으나 15개 UIA 클릭, 설정 저장 또는 게임 선택을 증명하지 않는다. 독립 CJK **44 PASS / 4 기존 REVISE / 1 FAIL**; 구조 판정도 FAIL: `default-yujiro-empty-top.png`에는 Yujiro/재료/조건 대신 `카드를 선택하면 조합 순서를 볼 수 있어요.`만 보인다. 이어지는 빈 패 bottom은 Yujiro, 부족 재료, GOLD 10000/WOOD 7 및 별도 KING/PICK를 칠하지만 top 증거를 대체하지 못한다. 원인은 확정되지 않아 제품 조합 결함으로 단정하지 않는다. 기존 프로필 `다.` 3장과 2.320 도움말 줄바꿈 1장은 비회귀 cosmetic 잔여다. 6개 관측 HWND **2624018, 42405196, 7997752, 123145796, 2492618, 4196014** 모두 사후 `IsWindow=false`, fixture PID **103204** 부재 (2026-09-24T12:46:46.385707+00:00); 즉시 modal 종료 증거는 아니다 (`recovery-3/gui/{visual-inspection,structure-verdict,cjk-verdict}.md`, `capture-4/process-after.json`). 일반 패키지 GUI와 실제 Warcraft는 미검증.
- **전체 문구 단계의 실제 결과:** 일곱 담당자의 원본 및 recovery-1 범위 영수증은 일반 사용자 본문·상태·사유·툴팁·UIA·도움말·동의·업데이트의 표시 경계 수정과 원시 ID/개발자 용어 축소를 기록한다. 전 화면에서 제거됐다는 판정은 아니다: 새 fixture UIA에서 조합 펼침 헤더 Button 16개의 Name이 `System.Windows.Controls.Grid`/`System.Windows.Controls.StackPanel`로 남았다. Yujiro의 별도 KING/PICK 적격성과 실제 조합 가능 여부는 미확인으로 안내하며 골드 10000/목재 7을 보유·준비됐다고 주장하지 않는다. 동의/개인정보 수집·보존·opt-in/거절 동작은 그대로다. 예전 프로필 `다.`와 2.320 도움말 줄바꿈은 문구 수정 대상이지만 최종 게이트 픽셀 증명은 없다. 이전 TMO 능력치는 참고값, 보드는 오프라인 246행이며 프로덕션 소비자가 없다. 정상 패키지 GUI·실게임 플레이 미검증.
- **recovery-1 실제 차단:** 원본 집중 1,234 PASS / 8 FAIL (총 1,242, exit 1)은 이전 실패 기록이다. 그 후 소유자 후속 수정과 fixture 재실행은 있었으나 `recovery-1/fixture/green.md`의 GREEN-2는 빌드/실행 exit 0, 합성 WPF 59 PNG·44 action에도 접근성 GREEN **아님**: 원래 프레임워크 UIA Name 26개 중 후보 10개만 정리됐고 조합 헤더 Button 16개가 남았다. 원본 fixture RED는 비동기 callback이 fixture 자체 브라우저를 앱 소유 브라우저로 교체했음을 증명한다. 새 fixture는 실제 바인딩된 브라우저에서 Yujiro 카드를 UIA Invoke했고 빈 패 상단 픽셀에 선택 제목과 부족 유닛 행, 하단 픽셀에 골드 10000/목재 7을 그렸다. **상단 픽셀에는 이 자원 둘이 보이지 않는다** (상단 UIA에는 있음). 이는 게이트 후 캡처 또는 일반 패키지 GUI 증명이 아니다. recovery-1 직렬 집중/전체/Release는 fixture 차단 때문에 **시작하지 않았고** 새 통과 fingerprint도 없다. recovery-3 집중 194/194·전체 3,892 PASS·build·attempt-4·capture-4는 문구 변경 전 역사다.
- **게시 중단:** recovery-1에 선정된 패키지·PE·로컬 해시가 없고 `package/attempt-selection.json`도 없다. 현재 후보 feed/HEAD 조회, 서명/DPAPI·Wrangler 인증, R2 재고/업로드, 공개 전체 다운로드/해시, 서명 피드 사후 검증, stable/프로필/롤백 사후 확인, 보존 삭제 모두 미실행/미검증 (`artifacts/user-copy/recovery-1/remote/release-verdict.md`). 예전 feed HTTP 200/후보 HEAD 404 및 DPAPI 결과는 현재 상태나 후보 서명을 증명하지 않는다. 로컬 과거 attempt-4를 공개 패키지라 부르지 않는다. 사용자 앱·게임·프로필·원격 미조작; 저장소 Git 메타데이터가 없어 청결/커밋 주장 없음.

---
# 현재 배포: BETA 1.0.15 · test 채널 (2026-09-22)

로컬 `1.0.15-test.1`을 공개 test 채널에 올렸다. stable 채널과 프로필 피드는 그대로다. 같은 버전 키는 덮어쓰지 않았다.

- 다운로드: https://orand-updates.epic42121.workers.dev/downloads/1.0.15-test.1/RandyPick.exe
- 업데이트 피드: https://orand-updates.epic42121.workers.dev/v1/channels/test/win-x64
- Windows x64 / 78,727,455 bytes / SHA256 `8c9fa3ab7f2d3553a146e5a919120eb9724aac3af2626254c09be5e7251d3d24`
- 공개 다운로드 전체 해시가 로컬 `artifacts/growth-recognition-20260922/v115/publish/RandyPick.exe`와 같다. 피드 원문은 `artifacts/release-1.0.15-test.1/sign/manifest.json`과 같고, 내장 공개키 `orand-p256-20260913`으로 P-256 P1363 서명을 확인했다.
- 롤백 키 `1.0.2-test.14`, `1.0.2-test.13`은 200이다. `1.0.2-test.12` 이하는 원래 404라 이번에 지운 키는 없다. stable=404, 프로필 피드는 `1.20260913.1` 그대로 200.
- 배포 글: docs/beta-1.0.15-test.1.md
- 이미 실행 중인 로컬 PID 121396은 이 빌드다. 기존 test.14 클라이언트는 게임 종료 후 수동 설치로 이 버전을 받는다.

---
# 현재 로컬 실행: BETA 1.0.15 · test 채널 게시 (2026-09-22)

- test 채널 `1.0.15-test.1` 게시 완료. 피드 버전·크기·SHA256이 로컬 패키지와 같다.
- 공개 다운로드 해시 일치: SHA256 `8c9fa3ab7f2d3553a146e5a919120eb9724aac3af2626254c09be5e7251d3d24`, 78,727,455 bytes.
- 피드: https://orand-updates.epic42121.workers.dev/v1/channels/test/win-x64
- 다운로드: https://orand-updates.epic42121.workers.dev/downloads/1.0.15-test.1/RandyPick.exe
- 이전 채널 헤드 `1.0.2-test.14` 키는 유지. wrangler 이 버전은 `r2 object list`가 없어 추가 삭제는 하지 않았다. 프로필 키 삭제 없음.
- 증거: `artifacts/release-1.0.15-test.1/{sign/manifest.json,remote-before.json,remote-after.json}`.

---
# 이전 로컬 실행: BETA 1.0.15 · 미등록 rawcode 델타 제외 (2026-09-22)

- 월드 벡터의 미등록 헬퍼 rawcode(실측 `5C0h` 해적 기관총병 표기, `unknownRawcodes`)가 붙었다 빠질 때 `rawcode.delta`를 남기던 것을 막았다. 패·카운터 변화는 그대로 기록한다.
- 전체 **3,831 PASS / 0 FAIL / 1 SKIP**. RED `unknown-rawcode-red2.trx` 0/1 → GREEN `unknown-rawcode-green.trx` 7/7.
- 실행 파일: `artifacts/growth-recognition-20260922/v115/publish/RandyPick.exe`, `1.0.15-test.1`, 78,727,455 bytes. SHA256 `8C9FA3AB7F2D3553A146E5A919120EB9724AAC3AF2626254C09BE5E7251D3D24`.
- PID 121396, 06:54:50Z, BETA 1.0.15. 이전 1.0.14 PID 135800 정상 종료. Warcraft 24704 유지. 공개 채널 미게시.

---
# 이전 로컬 실행: BETA 1.0.14 · 일시 전체검사 실패 시 성장 유지 (2026-09-22)

- 이미 인정한 QR 성장(헤르메포)은 핸들 표/vtable/QR 레이스 같은 **일시 전체 검사 실패**로 빼지 않는다. 판이 바뀌거나 성공한 전체가 성장을 없으면 바로 뺀다.
- 하단 「기록 저장 공간이 부족합니다」는 원격 관측 큐 백프레셔 문구였다. 「원격 진단 전송 대기 · 인식은 계속합니다」로 바꿨다. 원격 큐는 삭제하지 않았다.
- 전체 **3,830 PASS / 0 FAIL / 1 SKIP**, WPF 23프레임 104/104(거절 프레임에도 헤르메포 유지). Codex 담당은 한도 실패, 리드가 적용.
- 실행 파일: `artifacts/growth-recognition-20260922/publish/RandyPick.exe`, `1.0.14-test.1`, 78,727,201 bytes. SHA256 `A35719746919806B0813C1193056923541DA6B1A7323B5E0DD23A30F8FCDA0D8`.
- PID 135800, 06:26:44Z, BETA 1.0.14. 이전 1.0.13 PID 115896 정상 종료. Warcraft 24704 유지. 공개 채널 미게시.
- 교체 시점 보드는 76기·I10h 없음(지원 단계). 13↔12 실재현은 이 프로세스에서 불가. 증거는 WPF 61881 + 단위 테스트.
- 노트패드: `artifacts/growth-recognition-20260922/ulw-notepad.md`

---
# 이전 로컬 실행: BETA 1.0.13 · 성장 증인 주소·소유·rawcode (2026-09-22)

- 성장 유지 증인에서 핸들/할당 17필드를 버렸다. 같은 뷰 QR 개체(주소·owner 27·rawcode)면 월드 오브젝트가 늘어도 헤르메포를 빼지 않는다. 다른 사람의 owner-27 특별함은 주소가 달라 대체되지 않는다.
- 4인: QR은 1~4칸 배열이고 전체 검사는 `QR[현재 뷰 슬롯]`만 본다. WC3 owner 27만으로는 내 것/남 것을 가릴 수 없다. 그 슬롯이 가리킨 개체 주소로 가릴 수 있다. CURRENT-VIEW가 이 PC 로컬 플레이어라는 불변 증명은 아니다.
- 관련 66/66, 전체 **3,828 PASS / 0 FAIL / 1 SKIP**, WPF 23프레임 104/104. Codex 담당은 한도로 실패해 리드가 같은 계약을 적용했다.
- 실행 파일: `artifacts/unit-list-stability-20260922/witness-v2/publish/RandyPick.exe`, `1.0.13-test.1`, 78,727,153 bytes. SHA256 `E534E339B8613B292F8D0E0FCCCF064C31AF7E892109B60977AC034FF8A69295`.
- 1.0.12 PID 132152 정상 종료, `app.stop` droppedRecords=0. 새 PID 115896, 05:07:18Z, BETA 1.0.13. Warcraft 24704 유지. 오버레이 숨김 유지.
- 교체 후 두 번째 전체 검사 이후 표시 1311회가 I10h를 유지했고 드롭 0. 월드 지문은 한 개라 1.0.12에서 보인 외래 오브젝트 흔들림은 이 프로세스에서 재현되지 않았다.
- 증거: `artifacts/unit-list-stability-20260922/witness-v2/FINAL-EVIDENCE.md`.

---
# 이전 로컬 실행: BETA 1.0.12 · 유닛 목록 성장 표시 유지 (2026-09-22)

- 유닛 확인 목록이 같은 내 패인데 헤르메포 성장 참조가 빠졌다 복원되던 회귀를 고쳤다. 전체 검사가 잡은 QR 성장 개체를 기본 스냅샷에서 같은 개체로 다시 보면, 관련 없는 월드 지문 변화만으로는 빼지 않는다. 실제 추가·제거·인식 실패는 그대로 반영한다.
- 관련 649/649, 실제 WPF 23프레임 104/104, 전체 **3,828 PASS / 0 FAIL / 1 기존 SKIP**. 패키지 자체 검사 exit 0.
- 실행 파일: `artifacts/unit-list-stability-20260922/publish/RandyPick.exe`, 내부 `1.0.12-test.1`, 78,727,153 bytes. SHA256 `7AEC6471C1E285C88BE2FBDC659E177CFD07665105887EF1E66AF6AB6301BBB1`.
- 이전 1.0.11 PID 126536은 정상 종료, 마지막 `app.stop` droppedRecords=0. 새 앱 PID 132152, 시작 UTC `2026-09-22T04:20:29.4845254Z`, 제목 BETA 1.0.12, 실게임 인식·실행 파일 옆 로그를 확인했다. Warcraft PID 24704는 유지했다. 오버레이 숨김 설정도 유지됐다.
- 교체 후 활성 맵에서 전체 검사가 헤르메포를 잡은 뒤, 기본 875회가 그 카드 없이도 표시에서 한 번도 빼지 않았다. 이 구간 월드 지문은 안 바뀌어, 원래 결함의 월드 변화 조건은 실게임에서 재현되지 않았다. 그 조건의 증거는 WPF 재현이다.
- 증거: `artifacts/unit-list-stability-20260922/{FINAL-EVIDENCE,live-startup,live-verify,packaged-main.png}`. 공개 채널·원격 릴리스·프로젝트 커밋 없음. 사용자 오버레이 체감·이전 실제 도박 대조는 별도다.

---
# 이전 로컬 실행: BETA 1.0.11 · 기본 패 독립 인식 (2026-09-22)

- **사용자 실사용 회귀 수정 중:** 유닛 확인 목록이 반복해서 바뀐다는 보고를 받았다. 실제 로그에서 같은 내 패인데 월드 지문 변화로 헤르메포 성장형 참조가 기본 표시에서 빠지고 전체 표시에서 복원되는 반복을 확인했다. 정지 상태의 주기 측정만으로 잡지 못한 결함이며, `artifacts/unit-list-stability-20260922/WORK.md`에서 별도 재현·수정을 진행한다.
- 기본 패 확인 기회를 약 100ms, 무거운 전체·성장 검사를 기존 약 250ms로 분리했다. 동일 네이티브 리더를 직렬 사용하며 기존 검증·중지·판 경계·로그 이력을 유지한다. 긴 읽기 중 100ms를 보장하는 방식은 아니다.
- 관련 707개와 실제 WPF 경로의 합성 입력 확인 55개를 통과했다. 기본 패 변경이 다음 전체 검사 전에 화면에 표시되는 것, 느린 기본 읽기에서도 전체 검사가 계속 밀리지 않는 것을 검증했다.
- **최종 전체 3,808 PASS / 0 FAIL / 1 기존 SKIP**, Release publish 및 패키지 자체 검사 exit 0. 기존 연결 테스트 2개는 공통 인식 함수 호출 연결까지 검사하도록 보강했다. LSP PATH 제한은 유지한다.
- 실행 파일: `artifacts/basic-cadence-20260922/publish/RandyPick.exe`, 내부 `1.0.11-test.1`, 78,726,273 bytes. SHA256 `4d77fd96161f5ddd95ba716f300b31f68da9211e9b64f76b883781d6f7a5c341`.
- 이전 1.0.10은 정상 종료했다. 새 앱 PID 126536, 시작 UTC `2026-09-22T03:28:04.4547983Z`, 실제 창·정상 응답·실행 파일 옆 로그를 확인했다. Warcraft PID 24704는 유지했다.
- 실제 맵 데이터가 읽히는 75초 측정에서 기본 간격 중앙값 107.97ms/p95 113.97ms, 전체 265.95ms/p95 272.22ms였다. 1,121개 읽기 모두 수락, 오류·손실 0, 앱·게임 응답 75/75다. 앱 CPU 평균/최대 1.7315%/2.0541%(32 논리 프로세서), 작업 세트 평균/최대 419.32/495.80MiB.
- 이전 활성 로그의 기본 간격 중앙값은 250ms였다. 직전 자원 기준은 맵 미인식 상태이고 최초 새 버전 측정은 로딩이 섞였으므로 같은 판의 CPU/FPS 개선 비교로 주장하지 않는다. 사용자의 위습 1개 결과·체감 응답은 대기 중이며, 클릭부터 픽셀까지의 지연은 측정하지 않았다.
- 증거: `artifacts/basic-cadence-20260922/FINAL-EVIDENCE.md`, `live-startup.json`, `packaged-main.png`, `measurement/final-active-75s.json`. 공개 채널·원격 릴리스·프로젝트 커밋은 하지 않았다.

---
# 이전 로컬 실행: BETA 1.0.10 · 첫 전설 목적별 분류 (2026-09-22)

- 첫 전설 단계를 **스토리 / 공중이동 / 가까운 조합**으로 변경했다. 스토리·공중이동은 해당 공략 표식이 있는 전설·히든, 가까운 조합은 조건에 맞는 전설 이상 전체를 포함한다. 기존 특수 획득 조건 필터는 유지한다.
- 메인은 스토리 기본 선택, 유닛 확인 창은 스토리만 기본 펼침이다. 각 분류는 부족 재료 수·남은 조합 단계 순이며, 이후 직접 고른 분류/접힘/스크롤은 패 변경·인식 만료 때 유지한다. 첫 상위 이후 물딜·마딜 지원 분류와 다른 단계의 기본 표시는 보존했다.
- 실제 진단 참조 입력을 넣은 WPF 화면 20개를 확인했다. 메인 920/1080, 유닛 창 420/540, 세 분류, 빈 표식, 단계 전환, 변경/만료 유지, 모두 펼치기 및 기존 조합 창 연결을 검증했다. 사용자의 실제 플레이 테스트와는 구분한다.
- **최종 전체 3,797 PASS / 0 FAIL / 1 기존 SKIP**, Release publish와 `package-probe-final.json` exit 0. LSP PATH 제한과 기존 컴파일러/분석기 경고는 그대로 기록했다.
- 실행 파일: `artifacts/first-legend-categories-20260922/publish-final/RandyPick.exe`, 내부 `1.0.10-test.1`, 78,723,177 bytes. SHA256 `ac284f79049be83b1c127cd969102bd5c5a9fb1b380bb0c89c34691de3f6bf21`.
- 1.0.9는 정상 종료했으며 마지막 `app.stop` droppedRecords=0이다. 새 앱 PID 118752, 시작 UTC `2026-09-22T02:11:50.8482932Z`, BETA 1.0.10 창과 정상 응답·실게임 인식·실행 파일 옆 로그를 확인했다. Warcraft PID 24704는 유지했다.
- 현재 판은 `상위 탐색` 단계다. 새 분류를 즉시 보려면 단계 메뉴에서 `전설·히든`을 선택한다. 다음 첫 전설 진입 시 스토리가 자동 기본 표시된다. 사용자의 오버레이 숨김 설정도 유지했다.
- 증거: `artifacts/first-legend-categories-20260922/FINAL-EVIDENCE.md`, `live-startup.json`, `packaged-main.png`. 현재 로그 세션은 `90360e02-d795-4d4f-8030-5fe911b554a2`.
- 공개 채널·원격 릴리스·프로젝트 커밋은 하지 않았다. 사용자 플레이 테스트와 이전 활동 기록의 실제 도박 대조는 아직 수행되지 않았다.

---
# 이전 로컬 실행: BETA 1.0.9 · 유닛 확인 창 크기 조절·조합식 둥근 외곽 (2026-09-22)

- `랜디픽 · 유닛 확인` 오버레이에 가장자리/모서리 크기 조절과 오른쪽 아래 손잡이를 추가했다. 최소 크기는 배율 기준 420×420이며, 같은 실행 중 패 갱신·이동·숨김/재표시 후에도 크기를 유지한다. 재실행 시에는 기존 기본 크기로 시작한다. 능력치 창과 보유 패 보조 창은 변경하지 않았다.
- 사용자 승인대로 조합식 창의 크기·비율·내용 배치와 저장된 위치/크기는 유지하고, 외곽만 공통 반경 16의 둥근 모서리·중립 테두리로 맞췄다.
- 실제 WPF 내용이 채워진 유닛 창 420×420/540×480/700×640, 조합식 창 320×260/326×440/420×584를 직접 확인했다. Windows UI Automation 크기/위치 조절, 모서리 판정, 스크롤, 숨김/재표시, 비활성·최상위 동작을 검증했다. 실제 마우스 드래그나 게임 입력을 주입한 검증은 아니다.
- **전체 3,787 PASS / 0 FAIL / 1 기존 SKIP**, Release publish와 패키지 자체 검사 exit 0. LSP는 프로젝트 설치 파일은 있으나 진단 도구의 PATH에서 해석되지 않는다. 기존 `UpdateService.cs` CS9113 경고는 유지했다.
- 실행 파일: `artifacts/window-usability-20260922/publish/RandyPick.exe`, 내부 `1.0.9-test.1`, 78,722,839 bytes. SHA256 `6b02efa97e8e69929c5991201d7ce9f9d508431bb477ed094b8cae159084ad70`.
- 기존 1.0.8은 정상 종료했고 최종 `app.stop`의 droppedRecords=0을 확인했다. 새 앱 PID 110128, 시작 UTC `2026-09-22T01:12:09.8595786Z`, 실제 제목 BETA 1.0.9/응답 정상/실게임 인식과 새 로그를 확인했다. Warcraft PID 24704는 그대로다.
- 사용자 기존 오버레이 숨김 설정을 유지했다. 메인 창의 `오버레이 보이기`로 다시 표시할 수 있다. 새 로그는 위 publish 폴더의 `activity-logs/activity-b687c56d29644c7d8afd6d348a431d11-0001.jsonl`부터 저장된다.
- 증거: `artifacts/window-usability-20260922/FINAL-EVIDENCE.md`, `live-startup.json`, `packaged-main.png`. 공개 채널·원격 릴리스 게시와 프로젝트 커밋은 하지 않았다.
- 아래 활동 기록의 실제 도박 대조는 아직 대기 중이다. 기존 30분 성능 측정은 1.0.8 결과이며 1.0.9 측정으로 바꿔 주장하지 않는다.

---
# 이전 로컬 실행: BETA 1.0.8 · 활동 기록 검증 (2026-09-22)

> 최종 수정 패키지를 실제 실행했다. **30분 연속 기록·실제 위습·실제 조합 대조는 통과했고, 실제 도박 결과 응답만 대기 중**이다. 공개 채널에는 게시하지 않았다.

- **실사용 RED→GREEN:** 첫 위습 "해군총병"에서 발견한 basic/full 이력 혼선과 늦은 소비 증거 연결을 수정했다. 실제 재검증 "나미 흔함"은 `f=14→15`, `100h=2→3`, 위습 1개 감소와 연결됐고 중복 없이 한 번만 기록됐다. `live-final/wisp-user-red.json`, `wisp-user-green.json`.
- **실제 조합 PASS:** 사용자가 보고한 총병+칼병→타시기 안흔함과 `game.craft` A01R, 재료 `900h`/`600h` 각 1개, 결과 `E00h` 1개가 일치한다. `live-final/craft-user-green.json`.
- **최종 바이너리 30분 PASS:** 1,800.057초, 읽은 로그 57,618개, 저장 누락/오류 0, 응답 1,800/1,800, 최대 수신 간격 1.066초. CPU 평균 0.999%/최대 2.711%(32 논리 프로세서 기준), 최대 작업 세트 523.82 MiB. `soak-tooling/live-30m-livefix.json`. 게임 일시정지 여부를 확정하지 않아 활성 플레이 성능 개선으로 주장하지 않는다.

- 실행 파일: `artifacts/activity-logging-20260921/publish-livefix/RandyPick.exe`, 내부 `1.0.8-test.1`, 78,721,863 bytes.
- SHA256: `db8f6b1c5fcd672cf9acac706e3070eaf32cc458ce3a0661fa6a0a8972aad330`.
- 최종 전체 테스트: **3,786 PASS / 0 FAIL / 1 기존 SKIP**, `tests/full-livefix-isolated-final.trx`. publish와 `package-probe-livefix.json` 자체 검사 exit 0. 실제 WPF 재현은 별도 프로세스로 격리해 기존 Stats/Startup 테스트의 전역 Application 오염도 해결했다.
- 실제 WPF 시험 데이터에서 위습·조합·도박 로그, OS 최소화/복원, 화면 및 정리 검증 통과. 근거 `native-qa/lead-verdict.json`; 실제 사용자 행동 대조와는 구분한다.
- 단일 파일 앱의 `AppContext.BaseDirectory`가 임시 압축 해제 폴더를 가리키는 문제를 실제 패키지에서 발견했다. 활동 로그만 `Environment.ProcessPath` 기준으로 고쳤으며 자산·데이터·원격 텔레메트리 경로는 보존했다.
- 현재 로그: `publish-livefix/activity-logs/activity-d0e11ffeb4e3468bb4c6a0cfd3dd3e1d-0001.jsonl`부터 순서대로 저장된다. 실제 `app.start`의 경로와 일치하고 `synthetic=false`, 2.321 규칙과 실게임 읽기를 확인했다. 파일이 회전하면 뒤 번호가 증가한다.
- 최신 실제 앱 화면: `live-postfix/packaged-window.png`. 표시되는 "기록 저장 공간이 부족합니다"는 기존 `ObservedTelemetryClient.IsBackpressured` 전송 큐 경고다. 새 로컬 활동 로그 경고와는 별개이며 기존 큐를 삭제하지 않았다.
- 이전 바이너리의 첫 30분 측정은 파일 변경 알림 지연을 기록 중단으로 오판했다. 원본 실패 보고서를 보존했고, 원본 57,585개 연속 레코드 감사와 관측기 수정 증거를 별도 보존했다. 최종 수치는 수정 관측기로 새 바이너리에서 다시 측정한 결과다.
- 최초 전체 실행의 rotation 테스트 1회 실패는 실행기 중단으로 상세를 잃어 원인이 미확정이다. 같은 입력의 전체 재실행 및 독립 100회는 통과했고, 회전 조건과 실패 시 정리를 강화한 후 최종 전체 테스트도 통과했다. `writer-final-ready.json`에 제한을 보존한다.
- 첫 패키지와 잘못된 경로의 최초 실게임 로그는 `publish/`, `live-initial/`에 보존했다. 첫 앱은 정상 종료했고 6,808개 연속 레코드와 `app.stop`, droppedRecords=0을 확인했다.

위 상대 증거 경로의 기준은 `artifacts/activity-logging-20260921/`이다.

---
# 이전 준비 단계: LOCAL CANDIDATE BETA 1.0.8 · 내부 1.0.8-test.1

> 로컬 소스·Release build 후보만 준비됐다. **실제 사용자 행동 대조 검증은 아직 대기 중**이며, 공개 릴리스·업데이트 채널 게시·패키징·후보 EXE 실행은 하지 않았다.

- `docs/VERSIONING.md` 정책대로 사용자 표시는 숫자 세 자리 `BETA 1.0.8`, 내부/업데이터 식별자는 `1.0.8-test.1`이다. `AssemblyVersion`/`FileVersion`은 `1.0.8.0`이며 기존 test 채널 비교·서명 호환 형식을 유지한다.
- 버전 기대값 RED: `artifacts/activity-logging-20260921/tests/version-108-red-final.trx` 5건 중 버전 불일치 2 FAIL(실제 1.0.7), 나머지 3 PASS. csproj 변경 후 동일 범위 `version-108-green.trx` **5/5 PASS**.
- 비패키지 Release build: `dotnet build OrandOverlay.csproj -c Release --no-restore` exit 0. 증거 `artifacts/activity-logging-20260921/version-108-build.log`.
- 평가된 속성: `Version=1.0.8`, `AssemblyVersion=1.0.8.0`, `FileVersion=1.0.8.0`, `InformationalVersion=1.0.8-test.1`. 증거 `artifacts/activity-logging-20260921/version-108-properties.txt`.
- 앱 활동 기록 집중 회귀는 직전 입력에서 24/24 PASS였고 gameplay 후속 계약도 15/15 PASS 후 API가 유지됐다. 최종 accepted-READY-full 실제 MainWindow fixture와 `game.wisp-evidence`/`game.counter-observed`/부분 `game.gamble` JSONL 증거는 qa-tooling 통합 결과에 의존한다. 사용자 실제 위습·조합·도박 행동과 후보 로그 대조는 아직 수행하지 않았다.
- 이번 후보 준비에서는 전체 테스트를 실행하지 않았고, 패키지·self-probe·사용자 앱 실행/종료도 실행하지 않았다.

## 최종 게이트에서만 실행할 정확한 절차 (이번 단계 미실행)

```powershell
# 1) 새 디렉터리에 단일 파일 publish. 기존 산출물을 덮어쓰지 않는다.
dotnet publish OrandOverlay.csproj -c Release -r win-x64 `
  -p:PublishSingleFile=true --self-contained true `
  -o artifacts/activity-logging-20260921/publish

# 2) package self-probe. 출력 JSON은 존재하지 않는 새 절대 경로여야 한다.
& 'C:\Users\123\Desktop\dev\RandyPick\artifacts\activity-logging-20260921\publish\RandyPick.exe' `
  --verify-package `
  'C:\Users\123\Desktop\dev\RandyPick\artifacts\activity-logging-20260921\package-probe-1.0.8-test.1.json'
```

- self-probe는 `FileMode.CreateNew`로만 보고서를 만들며 정상 런타임·네트워크·게임 읽기·사용자 설정을 시작하지 않는다. 성공 보고서는 `version=1.0.8-test.1`, `assetName=RandyPick.exe`, `assemblyName=OrandOverlay`, `selectedChannel=test`, `supportsAutomaticUpdates=true`, `runtimeStarted=false`, `noNetwork/noGameReads/noUserSettingsOpened=true`여야 한다.
- 정상 실행은 기존 RandyPick 프로세스 신원을 먼저 재확인한다. 최종 게이트에서 허용된 경우에만 기존 앱에 `CloseMainWindow()`를 요청하고 자연 종료를 기다린다. 강제 종료하지 않는다. 이후 새 `RandyPick.exe`를 `Start-Process -PassThru`로 실행해 창 제목 `랜디픽 BETA 1.0.8`, HWND, Responding을 확인한다.
- 정상 종료 검증은 새 후보 메인 창에 `CloseMainWindow()`를 요청한다. `Gameplay_OnClosing`이 스캔 취소 → 관측/게임플레이 기록 종료 → 로컬 활동 writer flush/dispose → 재호출 Close 순서로 끝낸 뒤 프로세스가 자연 종료하는지 확인한다. `Stop-Process`는 사용하지 않는다.

---
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
# 현재 로컬: 패 인식 완료 결과 동시 반영·제한 재시도 검증 (2026-09-21)

- 최신 실측: 사용자 유닛 갱신 후 이전7개→현재24개·6라운드 인식. 기본37/37·전체6/6 성공. 실제 게임 프레임을 숨김 WPF 화면 처리에 전달해 추천·부족재료·선택 유지까지 확인했다. 쵸파 혼 포인트 기준 루피1·해군 칼병1 부족. `live-updated-units.json`, `live-updated-ui.json`. 변화가 끝난 뒤 시작한 측정이므로 변화 순간의 무중단 증거는 아니다.
- 로컬 후보 버전: `1.0.2-test.14.recognition.1`. 공개 test 채널은 `1.0.2-test.14`이며 이번 작업에서 변경하지 않았다.
- 성장 탐색 후 최종 검증한 동일 스냅샷에서 기본 패와 전체 패를 함께 만들어 한 번에 반영한다. 이전 기본 패와 시점이 달라 정상 전체 결과가 거부되는 경로를 수정했다. 추가 네이티브 읽기·시간 갱신으로 신선도를 속이는 처리 없음.
- 유효한 유닛 코드/목록 변경에는 기본 스냅샷을 딱 한 번 다시 읽는다. 문맥·소유자·할당 세대·등록 상태·취소 오류는 재시도로 숨기지 않는다. 기존 전달 순서·전체 owner 유일성 탐색·3초 신선도·마지막 인식 표시를 보존했다.
- 코드리뷰 2개 완료. 리뷰 중 발견한 거부 상태 진단 문구를 수정했다. 회귀: 전체 3600 PASS/1버전기대값FAIL/1기존SKIP → 버전 검증 보정 후 관련225 PASS/0FAIL. 원래 소비자에서 새 회귀 실패를 재현했고 수정 후 통과했다.
- 실제 실행 중인 Warcraft 3.0.0.24268에서 12.484초, 기본41/41·전체6/6 정상. 기본6개+성장1개. 선택 데이터2.321. 관측 유닛 변화가 없어 이전 방식도6/6 통과했으며 실전 끊김 재현/해결 증거로 확대하지 않는다.
- 실제 WPF 소비 경로에 합성 프레임을 넣어 이전기본1→완료전체3 동시 반영, 중간빈패/기본2 미표시, 선택 유지, 오래된값 거부·복구·게임경계 초기화를 확인했다. 화면/게임 입력 없이 숨겨진 창 객체를 모두 정리했다.
- 남은 항목: 성장 탐색의 약3.46GB 반복 읽기 비용, 과거25라운드 실패 원인 확정, 관측 중 실제 조합 변화 순간·장시간·4인·새 게임 경계 검증. 사용자 갱신 이후 상태와 추천 반영은 확인했다.
- 증거·리뷰·로컬 빌드: `C:/Users/123/Downloads/randypick-recognition-followup-20260921/` (`EVIDENCE.md`, `CODE-REVIEW.md`, `publish/RandyPick.exe`).

---
# 현재 로컬 작업: 메모리 인식 갱신 보호 · 실게임 검증 대기 (2026-09-21)

- 기본 패를 새로 읽기 전에 전달 공간을 확인하도록 수정했다. 화면 소비가 밀리면 아직 시작하지 않은 선택적 읽기를 미루고, 이미 읽은 관측과 최종 결과는 순서대로 보존한다.
- 단일 읽기 작업, 전체 owner 유일성 탐색, 3초 신선도, 마지막 인식 표시 정책을 유지한다. 단순 주소 캐시는 추가하지 않았다.
- 실제 리더 회귀 RED→GREEN, 전체3563 PASS/0FAIL/1기존SKIP, 리드 관련154 PASS. WPF 창에서 전달 지연·복구·관측 순서·새 문맥/판 경계 초기화를 확인했다.
- Warcraft·랜디픽이 실행되지 않아 실게임 CPU/메모리/프레임 지연 비교와 실제4인 소유권 검증은 미실행이다. 직접 탐색 경로의 안전성 근거도 추가 확보가 필요하다.
- 새 버전 게시 없음. 공개 test 채널은 아래의1.0.2-test.14 그대로다. 성능 향상·완료를 주장하지 않는다.
- 증거/이어가기: `C:/Users/123/Downloads/randypick-recognition-20260921/EVIDENCE.md`, `notepad.md`.

---

# 현재 배포: 코드 리뷰 네 결함 수정 test.14 배포 완료 (2026-09-19)

추천 전략과 준비 판정·조합 보호·KPI 목표를 연결하고, 신 난이도 방깎 기준을 지원 보드에도 적용했다. 설정 마이그레이션은 저장 실패 시 읽은 값을 유지하며 원자적으로 저장한다. 진단 탐색 뒤 실제 값을 다시 읽어 3초 신선도를 판정하고, 초기화 실패로 닫힌 창을 다시 Show하지 않는다.

- 다운로드: https://orand-updates.epic42121.workers.dev/downloads/1.0.2-test.14/RandyPick.exe
- Windows x64 / 78,646,787 bytes / SHA256 `76025483f5f2bfc1fac771066110e7540f3c2fdb58f7266e94119e29133638d2`.
- 검증: 최종 전체 3558 PASS / 0 FAIL / 1 기존 SKIP, 통합 집중 회귀 182 PASS, Release 빌드 및 게시 빌드 성공.
- 시작 오류 안내 후 정상 종료 및 정상 창 표시를 격리된 데스크톱 하네스로 확인했다. 실제 Warcraft 게임·CPU 성능 측정은 하지 않았다.
- 패키지 자체 검사와 제품 매니페스트 서명 검증, 공개 다운로드 전체 SHA256/크기, test 피드의 서명 매니페스트 원문 일치를 확인했다.
- test.13 다운로드와 프로필 피드 본문을 보존했다. 알려진 test.1~12 다운로드 키는 게시 전 이미404였으므로 이번에 삭제한 키는 없다. 현재 + 직전3개 보존 정책 유지.
- 증거: `C:/Users/123/Downloads/randypick-fixes-20260919/EVIDENCE.md`, `notepad.md`, `integration-results/final-test14.trx`, `release/package-probe.json`.
- 현재 폴더에는 Git 메타데이터가 없어 프로젝트 커밋·푸시는 하지 않았다. 기존 버전 키나 프로필 자산을 덮어쓰지 않았다.

---

# 이전: 관측 텔레메트리 묶음 전송 test.13 배포 완료 (2026-09-18)

워커 일일 요청(1027) 절감을 위해 관측 기록을 디스크에 묶어 보내고 네이티브 OPTIONS를 제거한 1.0.2-test.13을 test 채널에 게시했다. 채널이 새 실행 파일을 가리킨 뒤 이전 test.12 다운로드 키는 삭제했다. 프로필 피드와 프로필 자산은 보존했다.

- 다운로드: https://orand-updates.epic42121.workers.dev/downloads/1.0.2-test.13/RandyPick.exe
- 업데이트 피드: https://orand-updates.epic42121.workers.dev/v1/channels/test/win-x64
- Windows x64 / 78,644,976 bytes / SHA256 `3d1aebd884c1569aa1874096b878d12aa4d3d20af7883c791b7414a0e816e484`.
- 로컬 빌드: `C:/Users/123/Downloads/RandyPick-validation-20260918/telemetry-batch-test13/publish-final/RandyPick.exe`.
- 검증: 메인 회귀 3543 PASS / 1 SKIP. 공개 다운로드 SHA256 일치, test 피드 payload가 test.13. 게시 후 test.13=200, test.12=404, profiles=200, stable=404.
- 배포 글: docs/beta-1.0.2-test.13.md
- 이후 게시 규칙: 현재 채널 버전 + 직전 3개 실행 파일만 R2에 남긴다. 프로필은 보존. `docs/r2-release-retention.md`.
- 실제 게임 한 판의 요청 절감 측정은 하지 않았다.

---

# 이전: 조합 재클릭 고정 해제 test.12 배포 완료 (2026-09-16)

같은 조합을 다시 누르면 선택이 풀리고 조합창이 닫히도록 1.0.2-test.12를 Cloudflare test 채널에 게시했다. 기존 test.11 이하 실행 파일과 버전 키는 보존했다.

- 다운로드: https://orand-updates.epic42121.workers.dev/downloads/1.0.2-test.12/RandyPick.exe
- 업데이트 피드: https://orand-updates.epic42121.workers.dev/v1/channels/test/win-x64
- Windows x64 / 78,631,686 bytes / SHA256 `e7e3c54ee18c83caead60ef3873ca9f474bf2028bb1bc6528a9d02a3fe1b18e3`.
- 로컬 빌드: `C:/Users/123/Downloads/RandyPick-validation-20260915-01/unpin-test12/publish-final/RandyPick.exe`.
- 검증: 메인 회귀 3508 PASS / 1 SKIP. 환경 실패였던 GameplayStats Python 테스트는 제외. 번들 780 payload / 332 Data, 공개 다운로드 해시 일치, test 피드가 서명 manifest와 byte 동일. stable=404, profiles=200 본문 전후 동일, test.11 키 200 유지.
- 배포 글: docs/beta-1.0.2-test.12.md

---

# 현재: 일반 모드 유틸 세분류 test.11 배포 완료 (2026-09-16)

TMO식 물딜/마딜+유틸 칸, 보스 잡기/광폭화 잡기 분리, 유틸 있는 전 등급 분류, 세라핌 4기 역할 보강을 1.0.2-test.11로 패키징하고 Cloudflare test 채널에 게시했다. 기존 test.8/test.9/test.10 실행 파일과 버전 키는 보존했다.

- 다운로드: https://orand-updates.epic42121.workers.dev/downloads/1.0.2-test.11/RandyPick.exe
- 업데이트 피드: https://orand-updates.epic42121.workers.dev/v1/channels/test/win-x64
- Windows x64 / 78,631,472 bytes / SHA256 `c8e98a631c2d4ec9523bde91bc5754632c0670719d2ffed4e72686829350ba7a`.
- 로컬 빌드: `C:/Users/123/Downloads/RandyPick-validation-20260915-01/utility-test11/publish-final/RandyPick.exe`.
- 검증: 메인 회귀 3507 PASS / 1 SKIP. `GameplayStatsTests` 1건은 Aside Python SRE mismatch로 환경 실패이며 이번 유틸 변경과 무관. 번들 780 payload / 332 Data, 서명 envelope, 공개 다운로드 해시 일치. 게시 후 test 피드가 서명 manifest와 byte 동일. stable=404, profiles=200 본문 전후 동일, test.10 키 200 유지.
- 실제 Warcraft III 3.0 / 원랜디 2.320 한 판의 유틸 칸 확인은 이번 검증에서 실행하지 않았다.
- 배포 글: docs/beta-1.0.2-test.11.md

---

# 현재: 일반 모드 인식 연속성·F1·업데이트 대기 test.10 배포 완료 (2026-09-15)

테스터 피드백으로 확인된 일반 모드 핫픽스를 1.0.2-test.10으로 패키징하고 Cloudflare test 채널에 게시했다. 기존 test.8/test.9 실행 파일과 버전 키는 보존했다.

- 다운로드: https://orand-updates.epic42121.workers.dev/downloads/1.0.2-test.10/RandyPick.exe
- 업데이트 피드: https://orand-updates.epic42121.workers.dev/v1/channels/test/win-x64
- Windows x64 / 78,626,777 bytes / SHA256 `0eaf854593c0aff743a5f7c95bb294985eff9383962047fffdf7f4870fa10b76`.
- 패 인식이 순간적으로 실패해도 마지막 정상 패·추천·조합 흐름을 유지하고, 현재 패가 다시 확인될 때만 최신 상태로 갱신한다. 현재 패가 확정되지 않은 동안 부족 수량·보유 상태를 새 값으로 꾸며내지 않는다.
- F1을 기본 오버레이 키로 고정하고 `MOD_NOREPEAT`를 사용한다. 현재 패 읽기 실패 때문에 F1 토글이 무시되지 않도록 세션 표시 가능성과 최신 읽기 상태를 분리했다. 사용자가 닫은 조합창은 F1/새로 고침으로 다시 열리지 않는다.
- 오버레이·통계·조합창은 마우스 클릭 시 게임 포커스를 빼앗지 않는 `WS_EX_NOACTIVATE` 경로를 사용한다. 조합창에는 명시적인 닫기 버튼이 있다.
- 업데이트는 자동 설치하지 않는다. 새 버전이 있으면 메인·오버레이에 `업데이트 대기 중`을 표시하고, 게임 종료 후 사용자가 버튼을 눌러 설치한다.
- 일반 모드만 활성화하며 초보자·대깨·매뉴얼 모드는 잠금 상태를 유지한다.
- 로컬 빌드: `C:/Users/123/Downloads/RandyPick-validation-20260915-01/tester-hotfix-test10/release/publish-final/RandyPick.exe`. 번들 780 payload/332 Data 해시, 서명 envelope, self-probe를 통과했다.
- 검증: 관련 회귀 297개 PASS 및 추가 집중 테스트 PASS, WPF 합성 2,924 assertion/70 capture PASS, 전용 조합창 fixture PASS, 네이티브 포커스·F1 harness 0 failures. 전체 수천 건 테스트는 현재 실행이 장시간 지속되어 재실행하지 않았다.
- 배포 전후 test feed byte 동일성, public download의 서명·크기·SHA256·PE 버전, 현재 test.10 및 이전 test.8/test.9 updater의 업그레이드·재검증, 같은 버전 제안 억제를 확인했다. stable=404/profile=200 본문은 전후 동일하다. 설치·재시작은 호출하지 않았다.
- 증거: `C:/Users/123/Downloads/RandyPick-validation-20260915-01/tester-hotfix-test10/release/release-verification.json`, `package/package-verification.json`, `public-download/public-asset-check.json`, `public-updater/public-channel-checks.json`, `previous-client-updater.json`, `test8-client-updater.json`, `remote-before.json`, `remote-after.json`.
- 실제 Warcraft III 3.0 / 원랜디 2.320 한 판의 연속 인식은 이 검증에서 실행하지 않았다. 실제 게임에서는 test.10으로 한 판을 플레이하고, 인식이 다시 끊기는 시점과 업데이트 대기 버튼 동작을 알려 달라.

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

# 현재: 일반 모드 베타 test.8 업데이트 배포 완료 (2026-09-15)

사용자가 검증한 로컬 test.8의 공개 업데이트 배포와 글 작성을 명시적으로 승인했다. 같은 실행 파일을 기존 Cloudflare Worker/R2로 게시하고 test 채널을 test.3 → test.8로 전환했다.

- 다운로드: https://orand-updates.epic42121.workers.dev/downloads/1.0.2-test.8/RandyPick.exe
- 업데이트 채널: https://orand-updates.epic42121.workers.dev/v1/channels/test/win-x64
- 버전 1.0.2-test.8 / Windows x64 / 78,605,501 bytes
- SHA256 e57c81ff78d309172bba99aff42143db5e31b106d3aa84d47aa8323b41b475fa
- 승인 파일: C:/Users/123/Downloads/RandyPick-validation-20260915-01/craft-hud-refinement/publish-final/RandyPick.exe. 다시 빌드하거나 기존 버전 키를 덮어쓰지 않았다.
- R2 신규 키 없음 확인 → 서명·번들 검증 → EXE 업로드 → 실제 공개 다운로드 검증 → 서명된 test 채널 게시 순서로 진행했다. 서명된 새 manifest가 공개 feed와 byte 동일함을 확인했다.
- 실제 패키지 내 업데이트 코드를 사용한 공개 다운로드에서 HTTPS 경로·서명·버전·크기·SHA256 확인. 현재 test.8 패키지와 이전 공개 test.3 패키지의 UpdateService 코드 각각에 이전/current 버전 identity를 넣어 실제 feed 조회·업데이트 제공·서명 재검증을 확인했다. test.8에는 업데이트 재제안 없음. 설치나 재시작은 호출하지 않았다.
- stable은 전후404 동일, profiles는 전후200 및 본문 byte 동일. Worker 재배포/프로필/정식 채널 변경 없음.
- 주요 변경: 체크박스·X 없는 항상 위 조합창, 탐색 선택 시 하단 중앙에 표시, 부족 재료2열, 선택→키·횟수→결과의 가로 흐름, 중복 제목·문구 정리, 좁은 탐색 카드 한글 줄바꿈 수정. 일반만 활성화하며 다른 모드 잠금 유지.
- UI/기능 검증은 이전 턴의 관련154테스트·합성2,609assertion·59PNG·독립2검토 PASS를 사용했다. 이번 배포 검증은 전송/서명/업데이트 경로이며 실제 게임 연속 플레이 검증을 대체하지 않는다.
- 배포 글: docs/beta-1.0.2-test.8.md. 전달용 사본: C:/Users/123/Downloads/RandyPick-validation-20260915-01/release-test8/베타테스터-배포글.md. 테스터에게 메시지를 직접 보내지는 않았다.
- 증거: C:/Users/123/Downloads/RandyPick-validation-20260915-01/release-test8/release-verification.json, remote-before.json, remote-after.json, test-manifest-before.json, sign/manifest.json, package/package-verification.json, public-download/public-asset-check.json, public-updater/public-channel-checks.json, previous-client-updater/public-channel-checks.json 및 R2 로그.
- 공개된 downloads/1.0.2-test.8/RandyPick.exe는 불변이다. 후속 수정은 새 버전과 새 서명 manifest로 게시한다.

---

## 이전 로컬 검증 기록 (보존)

# 현재: 항상 위 고정·가로 조합창 개선 — 로컬 test.8 (2026-09-15)

사용자 요청대로 조합창의 항상 위 체크박스와 X를 제거했다. 탐색 선택으로 여는 단일 조합창은 항상 위에 고정되며, 새 로컬 빌드를 실행했다.

- 원인 재현: Topmost=false인 채 IsVisible=true이면 이전 ShowWorkspace가 다시 위로 올리지 않았다. red.log의 detached-reopen-restores-always-on-top 실패 후 수정했고 최종 같은 검사가 통과했다.
- 창은 WindowStyle.None + WindowChrome. 중복 제목줄/툴바 제거, 목표 헤더40 DIP는 드래그, 가장자리4 DIP는 크기 조절. 체크박스/네이티브 X 없음. 일반 닫기 요청은 창을 숨기지 않고 취소한다. 메인 앱 종료에서는 정상 종료한다. 선택/열기마다 Topmost를 복구하며 위치·크기·스크롤은 보존한다.
- 기본700×320, 최소620×320 유지. 부족 재료는232-DIP 영역의2열(슬롯96/초상32), 조합은36-DIP 초상 옆 이름·역할 → 키 → 결과를 가로로 표시한다. 다음 순서를 조용한 배경으로 강조한다. 같은 창에서 조합 뷰포트199→269 DIP, 첫 단순 조합 카드160→133 DIP(해당 합성 사례)로 공간 효율이 개선됐다.
- 최종 검토 중 기존 탐색 카드의 순간이동 보/완 줄바꿈도 수정했다. 좁은 오버레이만 역할 구분점에서 줄바꿈하고 원래 접근성 이름을 유지한다. 기존 일반 진행·인식·조합 계산·수치 동작은 유지한다. 보존10파일 중9개 바이트 동일, NormalCandidateView.Progression.cs는 위 표시 코드만 변경.
- 최종 WPF 합성 실행 exit0/PASS:2,609 assertion PASS,0FAIL +184관측 기록(총2,793entries),59PNG. 관련 xUnit154/154 PASS. 독립 구현/시각 검토 모두 PASS, 시각59/59 직접 검토. 네이티브 WM_NCHITTEST로 드래그HTCAPTION=2 및 좌측 크기조절HTLEFT=10 확인.
- 최종 실행 파일: C:/Users/123/Downloads/RandyPick-validation-20260915-01/craft-hud-refinement/publish-final/RandyPick.exe
- 버전1.0.2-test.8,78,605,501bytes,SHA256 e57c81ff78d309172bba99aff42143db5e31b106d3aa84d47aa8323b41b475fa. 이전 publish 폴더는 마지막 한글 수정 전 패키지이므로 최종본으로 전달하지 않는다.
- self-probe: 설치/업데이트 지원 및 기존test채널 연결, RuntimeStarted=false/NoNetwork=true/NoGameReads=true/NoUserSettingsOpened=true. 이번 작업에서 공개 업로드·채널 변경 없음.
- 실행 교체: test.7 PID102064를 정상 종료 후 test.8 PID42796,MainWindowHandle14026214,Responding=true 확인(2026-09-15 15:57 KST). 강제 종료 없음. 이후 프로세스 ID는 재확인한다.
- 합성·네이티브 창 검증과 실제 게임 위 동작은 구분한다. 실제 게임에서의 배치/앞뒤 순서/연속 조합은 이번 턴에서 직접 확인하지 않았다. 컴퓨터 유즈와 게임 입력을 사용하지 않았다.
- 증거: craft-hud-refinement/wpf-complete,capture-manifest-complete.json,source-manifest-final.json,preservation-final.json,unit-tests-complete.log,exe-package-final.json,package-final.json,local-launch.json,final-review.json.

---

## 이전 기록 (보존)

# 현재: 선택 시 여는 하단 중앙 조합창 — 로컬 test.7 (2026-09-15)

탐색 패널에서 목표 유닛을 선택하면 별도 조합창을 연다. 최신 사용자 스크린샷의 빨간 하단 중앙 유닛 정보 영역을 초기 배치 기준으로 삼았다. 선택 전에는 숨기며 자동 추천만으로 열지 않는다.

- 메인·추천 오버레이의 기존 조합 패널을 단일 독립 WPF 창으로 이동했다. 좌측 부족 재료 / 우측 선택 유닛 → 조합 키·횟수 → 결과의 기존 C안과 작은 초상을 유지한다. 탐색 쪽 중복 부족 문구 제거도 유지한다.
- 기본 크기 700×320 DIP, 최소 620×320. 탐색 오버레이가 있는 모니터의 가로40%·세로76.5% 위치에 최초 배치하고 작업 영역 안으로 제한한다. 사용자 드래그·크기 조정 후에는 재선택·갱신으로 위치와 크기를 초기화하지 않는다. 다른 해상도/배율 및 실제 게임 HUD 위치는 실사용 확인이 필요하다.
- 다른 후보 선택 시 같은 창의 내용을 갱신한다. 닫기는 조합창만 숨기고, 후보 선택 또는 조합창 열기 버튼으로 다시 연다. 메인을 최소화해도 조합창은 유지되며 앱 종료 시 함께 종료한다. 항상 위를 켜고 끌 수 있다. 메인·추천 패널에는 중복 조합 영역이 없다. 일반 추천 오버레이 기본 높이는480 DIP로 줄였다.
- 인식·조합 계산·일반 진행·전투 수치 핵심10파일 해시10/10 동일. 같은 패 재렌더 억제, 선택/스크롤 보존, 실제 관측 기반 단계 전환,3초 유효 시간과 권한 경계를 보존했다.
- 최종 네이티브 WPF 합성 실행 exit0/PASS: 2,602 assertion PASS,0 FAIL 및184 관측/상태 기록(총2,786 entries),59 PNG. 관련 xUnit92/92 PASS. 앞선 wpf-final 2,759 entries는 모두 assertion인 수치가 아니므로 구분한다.
- 독립 구현 검토 PASS 및 시각 검토59/59 PASS. 앞선 시각 검토의 빈 화면/제목 잘림 의심은 각각 단독 이미지 재확인과 의도된 스크롤 위치 확인으로 철회됐다. 스크롤 맨 위2장과 읽기 만료 조합창1장을 추가해 증거를 보완했다. 제품 문제 수정으로 허위 기록하지 않는다.
- 실행 파일: C:/Users/123/Downloads/RandyPick-validation-20260915-01/detached-craft/publish/RandyPick.exe
- 버전1.0.2-test.7,78,605,207 bytes, SHA256 caa0ce97e31ea8364c1b107e5ffb1971962b3efee571dcd10dd4d599f8e94e9a.
- self-probe에서 설치/자동 업데이트 지원 및 기존 test 채널 연결 확인. 패키지 검사 자체는 RuntimeStarted=false, NoNetwork/NoGameReads/NoUserSettingsOpened=true. 이번 작업에서 공개 업로드와 업데이트 채널 변경은 수행하지 않았다.
- 로컬 실행 완료: PID102064, MainWindowHandle15336952, Responding=true (2026-09-15 15:41 KST). 실행 직전 이전 RandyPick 프로세스는 없었다. 이후 PID는 다시 확인할 것. 실제 게임의 카드 선택·배치·연속 조합 검증과 합성 검증을 혼동하지 않는다.
- 증거: detached-craft/wpf-complete, capture-manifest-complete.json, unit-tests-final.log, preservation-after.json, package.json, exe-package.json, local-launch.json, local-process-final.json, final-review.json. 컴퓨터 유즈/게임 입력을 사용하지 않았다.

---

## 이전 기록 (보존)

# 현재: C안 크기·간격 축소 적용 — 로컬 test.6 (2026-09-15)

현재 배치(좌측 부족 재료 / 우측 조합)를 유지한 압축안을 메인·오버레이에 적용했다. 글자 크기는 유지했다.

- 부족 재료 초상32 DIP, 조합 초상36, 목표 초상28, 기본 패딩8, 기본 간격6, 전용 패널 최대폭480. 메인 탐색 영역에 남는 폭을 돌려주고 오버레이 전용 패널의 높이 여유를 줄였다.
- 위 탐색 카드의 부족 재료 문구 제거를 유지했고, 상단 추천 안내의 중복 수량도 제거했다. 부족 수량·조합 재료·키·횟수는 전용 패널에서 확인한다.
- 같은 패의 재렌더 억제, 사용자 선택/스크롤, 실제 관측 기반 일반 모드 진행, 전투 수치 창, 3초 인식 유효 시간과 권한 경계는 보존했다. 핵심10파일 해시10/10 동일.
- 최종 실제 WPF 합성 검증: exit0, 2,966 assertion PASS, 55 PNG. 관련 xUnit82 PASS. 화면 캡처는 합성 입력이며 실제 게임 연속 플레이 검증이 아니다.
- 독립 기능/시각 검토 모두 최종 PASS. 55/55 원본 화면 검토 후 최종 캡처55개가 동일 바이트임을 재확인했고, 마지막 상단 문구 제거는 해당 소스 경로에서 확인했다. 기록: density-applied/final-review.json.
- 실행 파일: C:/Users/123/Downloads/RandyPick-validation-20260915-01/density-applied/publish-final/RandyPick.exe
- 버전 1.0.2-test.6, 크기 78602785 bytes, SHA256 68fff42fab5efa308fbea55510f251e1f38ab86f4c473992c1156ecc18df3821.
- 단일 실행 파일 self-probe 통과: 설치/자동 업데이트 지원, test 채널 연결. RuntimeStarted=false, NoNetwork/NoGameReads/NoUserSettingsOpened=true. 공개 업로드와 업데이트 채널 변경은 이번 작업에서 하지 않았다.
- 증거: C:/Users/123/Downloads/RandyPick-validation-20260915-01/density-applied/wpf-final, capture-manifest-final.json, preservation-after.json, source-evidence-final.json, exe-package-final.json, local-package-final.json. 앞선 publish 폴더와 c-layout/publish test.5는 최종본이 아니며 publish-final만 전달한다.
- 컴퓨터 유즈와 게임 입력을 사용하지 않았다. 창은 합성 픽스처가 닫고 종료했다.

---

## 이전 기록 (보존)

# 현재: 전용 패널 압축 시안 선택 대기 (2026-09-15)

> 최신 사용자 정정: 새로운 배치 시안이 아니라 현재 C안 배치를 그대로 유지하면서 아이콘·간격·패딩과 불필요한 폭을 줄이는 방향이다. 최신 전후 비교: C:/Users/123/.codex/visualizations/2026/09/14/01a0a22c-bfc3-7ab0-8043-b89c6d4ded0e/craft-density-comparison.html. 이전 3가지 구조 비교안은 적용 기준으로 사용하지 않는다. 아직 시안 비교 단계이며 제품에 새 압축값을 적용하지 않았다.

사용자가 C안 구현 중 전용 패널을 더 작고 읽기 쉽게 만든 시안을 요청했다. 현재는 디자인 비교 단계다.

- C안 좌측 부족 재료 / 우측 조합 배치는 소스에 구현했다. 최신 요청대로 메인·오버레이 탐색 카드의 중복 부족 재료 문구도 제거했다.
- 짧은 메인 창에서 재료 이름 끝 글자가 혼자 줄바꿈되던 문제는 수량을 상태 행으로 옮겨 수정했다.
- 최신 합성 캡처: C:/Users/123/Downloads/RandyPick-validation-20260915-01/c-layout/wpf-final-visual. JSON은 2,780 assertion PASS / 55 PNG를 기록했다. 셸 도구는 60초 제한을 반환했으므로 프로세스 종료 성공과 동일시하지 않는다. 이후 실행 중인 해당 픽스처 프로세스 없음 확인.
- 이전 C 빌드 관련 xUnit 82 PASS. 최신 탐색 문구 제거·좁은 레이아웃 변경 후 최종 독립 시각 검토와 배포 패키지 재생성이 남았다. 앞선 시각 검토에서 발견된 한글 줄바꿈은 수정했고 새 검토는 압축 시안 선택 후 진행한다.
- c-layout/publish/RandyPick.exe test.5는 위 마지막 UI 변경 전 패키지다. 최신 배포본으로 전달하거나 업로드하지 말 것.
- 인식·플래너·일반 진행·수치 표시 핵심 10파일의 보존 해시는 c-layout/preservation-after.json에 기록(10/10 동일). 읽기 유효 시간 3초 변경 없음.
- 초기 전체 픽스처에서 선택 후 idle 대기 3.5초로 정보 만료가 발생했다. 추가 측정은 Select 약38ms / 화면 대기 포함451ms였고 전체 검증 통과. 원인 확정이나 성능 수정 완료로 주장하지 않는다.
- 압축 시안: C:/Users/123/.codex/visualizations/2026/09/14/01a0a22c-bfc3-7ab0-8043-b89c6d4ded0e/compact-craft-concepts.html
- 공개 배포는 test.3 상태이며 이번 C/압축 시안 작업에서는 업데이트 채널을 변경하지 않았다. 컴퓨터 유즈는 사용하지 않았다.

---

## 이전 기록

# 부족 재료 초상 슬롯 — 로컬 검토본

- 사용자 선택: 큰 유닛 초상 + 부족 수량 배지.
- 로컬 버전: **1.0.2-test.4**
- 실행 파일: C:/Users/123/Downloads/RandyPick-validation-20260915-01/missing-material-redesign/publish/RandyPick.exe
- 크기: 78602340 bytes / SHA256: 47e6537d9308cccda98b696f3f5dbd798c7228b1606587327971d08e86fcb44d
- 현재 공개 배포는1.0.2-test.3. 이번 시안의 외부 업로드/채널 변경은 수행하지 않았다.

## 변경

56 DIP 초상, 오른쪽 아래18 DIP 부족 수량 배지, 아래12 DIP 이름. 작은 가로 카드 배경을 제거하고80 DIP 슬롯으로 배치했다. 재료는 등급/이름 순으로 고정해 수량 변경으로 순서가 뒤섞이지 않는다. 자원 필요량과 보유 미확인 표시는 별도로 유지한다. 기존 조합 카드·일반만 활성화 정책·패 인식·전투 수치 계산은 보존했다.

## 검증

- 관련 xUnit82 PASS /0 FAIL.
- 실제 WPF 합성2673 assertion PASS /0 FAIL,54 PNG. 많은 재료/2종/1종/없음, 만료/미확인, 접기·조합,920 메인/540 오버레이 포함. 실제 게임 데이터 검증은 아니다.
- 두 독립 시각 검토자가 각각54/54 원본 PNG를 열어 최종 PASS. 한글/배지 겹침/상태 구분 차단 문제 없음.
- 단일 실행 파일 self-probe: version test.4, CanSelfInstall=true, RuntimeStarted=false, NoNetwork/NoGameReads/NoUserSettingsOpened=true.
- 인식 코어5파일은 공개test.3 준비 시 기록한 해시와 동일.
- 첫 실패는 새 픽스처가 ‘없는 유닛’을0개 관측으로 넣은 입력 오류로 수정했다. 별도 기존 미확인 재료 화면은 선행 만료 테스트 다음에 자체 fresh 입력을 주도록 분리했다. 제품의3초 신선도 및 유효 수량 제한은 변경하지 않았다. 실패/최종 증거를 별도 경로에 보존했다.

## 증거

- C:/Users/123/Downloads/RandyPick-validation-20260915-01/missing-material-redesign/wpf-final
- C:/Users/123/Downloads/RandyPick-validation-20260915-01/missing-material-redesign/visual-evidence.json
- C:/Users/123/Downloads/RandyPick-validation-20260915-01/missing-material-redesign/tests/focused.trx
- C:/Users/123/Downloads/RandyPick-validation-20260915-01/missing-material-redesign/local-preview.json
- C:/Users/123/Downloads/RandyPick-validation-20260915-01/missing-material-redesign/recognition-core-preservation.json
- C:/Users/123/Downloads/RandyPick-validation-20260915-01/missing-material-redesign/fixture-diagnosis.md

컴퓨터 유즈와 게임 입력은 사용하지 않았다. 원본 인계 본문과 이전 배포 기록은 아래 및 before/에 보존되어 있다.

---

## 이전 공개 배포 기록

# 랜디픽 작업 상태 — 일반 모드 베타 배포 완료

## 현재 배포

- 버전: **1.0.2-test.3** / Windows x64 단일 실행 파일
- 다운로드: https://orand-updates.epic42121.workers.dev/downloads/1.0.2-test.3/RandyPick.exe
- 테스트 채널: https://orand-updates.epic42121.workers.dev/v1/channels/test/win-x64
- 로컬 파일: C:/Users/123/Downloads/RandyPick-validation-20260915-01/normal-beta-release/publish/RandyPick.exe
- SHA-256: 1faf6dbf40dd4390c961812eb5c0843e41bae86d0ac6841dcbf41d0a314e09cb
- 크기: 78,602,186 bytes
- 기존 Cloudflare Worker/R2를 사용했고 테스트 채널만 1.0.1-test.1 → 1.0.2-test.3으로 전환했다. stable/profile/Worker 코드는 변경하지 않았다. 배포와 테스트 업데이트 연결은 최신 사용자 요청으로 명시적으로 승인됐다.
- 배포용 글: [docs/beta-1.0.2-test.3.md](docs/beta-1.0.2-test.3.md). 테스터에게 직접 메시지는 보내지 않았다.

## 이번 변경

- 목표 바로 아래 기본 펼침 ‘먼저 모을 재료’: 전체 조합에서 실제 부족한 말단 유닛의 초상·이름·정확한 수량. 이미 보유한 중간 재료와 공통 재료 차감은 기존 플래너를 그대로 사용한다.
- 자원은 필요량 / 보유량 미확인을 별도 표시한다. 앞 단계 예상 결과는 실제 보유로 바꾸지 않는다. 미확인 그래프 사유를 유지한다.
- 후보 카드는 대표 부족 재료와 다른 종류 수를 두 줄로 표시한다. 외 N종의 분리 줄바꿈을 수정했고 중복 총계 문구를 제거했다.
- 조합 순서의 선택 유닛 → 키·횟수 → 결과, 단계별 재료 타일을 유지한다. 긴 부족 목록을 접고 순서를 볼 수 있다. 메인 기존 좌우 배치, 오버레이50%/240–320 DIP 조합 영역, 기존 스크롤을 사용한다.
- 일반만 활성화. 초보자·대깨·매뉴얼 탭은 잠금 표시/비활성이고, 설정 복원·콤보 선택·내부 모드 요청도 일반으로 제한한다. 기존 다른 모드 구현과 수동 목표/공략 설정은 보존한다.
- 인식 코어·새 게임 판단·3초 신선도·게임플레이 권한·전투 수치 계산은 바꾸지 않았다.

## 검증

- 전체 xUnit 실행: 3418건 중3416 PASS /1 FAIL /1 SKIP. FAIL은 버전 기대값 test.2였으며 test.3으로 수정한 뒤 최종 관련58건 모두 PASS. 전체 원본 행과 재검증을 대조한 최종 결과는 **3417 PASS /0 미해결 /1 SKIP**. 단일 전체 재실행이 모두 성공했다고 주장하지 않는다. 생략1건은 기존 원본 아카이브 부재 검사.
- 실제 WPF 합성: **2551 assertion PASS /0 FAIL /48 PNG**. 모드 잠금의 UI와 내부 요청, 부족 이름·수량·자원 미확인, 같은 패의 카드/조합 트리/스크롤 보존, 단계·주력·이동 역할, 만료/복구와 수치 창 검증.
- 두 독립 시각 검토자가 각각48/48 원본 PNG를 열어 최종 PASS.
- self-contained EXE:780/780 payload +332 Data 파일, 소스 해시 일치, 포함 런타임, 서명/자산 일치 검증. --verify-package: CanSelfInstall=true, test feed, NoNetwork/NoGameReads/NoUserSettingsOpened=true.
- R2 해당 버전 키가 없음 확인 → 불변 EXE 업로드 → 실제 공개 다운로드 검증 → 서명된 test 채널 게시 순서 준수.
- 공개 파일을 패키지의 UpdateTransport + VerifyDownloadedBodyAsync 코드로 받아 서명·크기·SHA256·PE 버전을 검증했다.
- 공개 feed를 패키지의 UpdateService에 이전 버전 identity1.0.1-test.1 /1.0.2-test.2로 주입해 실제 HTTP 조회: 둘 모두 새 버전 제공/서명 재검증 PASS. 현재1.0.2-test.3에는 재제안 없음. 이전 EXE 자체 실행이나 자동 교체/재시작의 실기 검증은 아니며, 이번 작업에서는 설치를 호출하지 않았다.
- 공개 test manifest와 로컬 서명 manifest는 byte 동일. stable은 전후404로 동일.

## 실제 게임 검증 범위

이번 변경은 실제 게임 데이터로 동적 조합을 검증하지 않았다. 직전 로컬 후보는 Warcraft III3.0.0.24268 / 원랜디2.320[R],27라운드 일시정지 상태에서65유닛 및 오버레이/전투 창을 확인했다. 그 결과를 이번 빌드의 연속 플레이 검증으로 간주하지 않는다.

기존 실행 앱을 강제로 종료하거나 업데이트 설치를 강행하지 않았다. 이번 단계에서는 Computer Use를 시작하지 않았으며 이전 세션은 정리된 상태다. 베타 업데이트는 기존 동의/자동 업데이트 설정과 게임 중 설치 보류 정책을 따른다.

## 후속 작업시 보존

- 작업 폴더에는 .git이 없다. reset/stash/clean/기존 파일 일괄 덮어쓰기 금지.
- MemoryDiagnostics 읽기/순회 금지. 설치 게임 파일은 기존 승인된 EXE 버전·SHA256 읽기만 허용. 기존 정상 앱의 범위 제한 읽기 인식 외 추가 스캔/덤프/주입/게임입력 금지.
- 진단 자료를 GameplayReady/코칭/실제 소유 확정 권한으로 승격하지 않는다. 전투 참고/미확정 값과 실제 확인값을 구분한다.
- 이미 공개한 downloads/1.0.2-test.3/RandyPick.exe는 덮어쓰지 않는다. 수정 배포에는 새 버전과 새 서명 manifest를 사용한다.

## 증거

C:/Users/123/Downloads/RandyPick-validation-20260915-01/normal-beta-release

- before/: 이번 변경 전 파일 백업
- release-source-hashes.json / final-preservation-check.json / recognition-core-preservation.json
- test-verification-summary.json / full/beta-full.trx / final-ui-tests/final-ui.trx
- wpf-verified/diagnostic-checks.json / visual-evidence.json / visual-review-final.md
- package/package-verification.json / exe-package.json / sign/manifest.json
- r2-preflight.log / r2-asset-upload.log / r2-channel-publish.log
- public-download/public-asset-check.json / public-updater/public-channel-checks.json
- remote-before.json / remote-after.json

직전 상태와 원래 인계 본문은 before/WORK-STATUS.md 및 CODEX-HANDOFF.md에 보존했다.
