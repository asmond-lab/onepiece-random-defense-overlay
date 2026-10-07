# ORDR 2.320 데이터 테스트 패치

빌드: `0.6.70-test.20260914.1` (Windows x64, self-contained single file)

## 지원 상태

2.320 오프라인 데이터와 앱 연결은 반영했다. **Warcraft III 3.0 실시간 인식·플레이 기록·자동 항법 점수는 승인하지 않았다.** 분석, 코드 회귀, 합성 모델, 격리 진단의 Ready는 운영 승인과 다르다.

일반 앱은 2.320 번들을 명시적으로 선택한다. 비라이브 fixture/replay는 기존 2.314 경로를 보존한다. 앱 설정 폴더는 기존판과 공유한다. 2.320의 자동 항법 비활성 설정이 종료 시 저장될 수 있으므로 샌드박스판으로 표현하지 않는다.

## 반영 내용

- `Map2320DataBundle`: 7개 파생 멤버를 정확한 경로·순서·정규화 길이·SHA로 검증하고, 검증한 동일 바이트를 파서에 전달한다. 알 수 없는/중복 manifest 속성, 변조 및 구버전 fallback을 거부한다.
- 원본 조합 등록 265행, 명령 81유닛/162별칭. TEST 행은 일반 제작 대상으로 취급하지 않는다. TMO 이후 2.320 원본 레시피를 적용하고 니카 선택·조건을 별도로 적용한다.
- 니카는 실제 H099/H0B2 중 하나, 나머지 재료, 플레이어 강화 플래그, boolean 토큰 및 확인된 목재를 요구한다. H08T나 물리적 아이템을 실제 선택 유닛/토큰으로 가장하지 않는다.
- 미확정 KING/UBAN/GOLD 등은 `UnresolvedSourceConditions`로 보류한다. 시간·판 세대·인식 revision이 다른 조건 관측을 재사용하지 않는다. exact 재료를 먼저 배분하고 조합 계획의 목재를 공통 예산에서 차감한다.
- 스토리 14단계, 항법 15종과 환전소 실행 비용을 반영했다. Exchange2320을 구 ContinuousBetting으로 재해석하지 않는다. 화면·추천 엔진은 공통 버전별 항법 해석을 사용한다.
- 불릿의 7개 연결 피해식, 실제 능력 레벨 3 상한, 특강 4레벨 설정값을 분리했다. 피해 래퍼 전 식이며 전투 DPS 실측이 아니다.
- 보드 221행 모델은 낮은 등록 레벨, 가족 최소 절댓값, 동률 최초 행을 따른다. 불완전/오래된 입력에는 합계를 만들지 않는다. 현재 패 실시간 보드 연결은 없다.
- TMO 48784 패 수치는 2.320 검증값이 아닌 이전 출처 참고로 표시하며, 구버전 클리어·학습 통계는 2.320 추천 순위에서 제외한다.

## 경계 검토와 수정

독립 검토에서 발견한 두 우회 경로를 수정하고 재검토했다.

1. Coach 경로가 일반 자동 항법 차단을 우회해 수동 계획을 바꾸던 문제: 2.320 자동 advice/confirmation/current observation과 fallback을 차단했다.
2. 승인 명령이 비어 있을 때 사용자 override 명령이 복구되던 문제: 2.320은 승인 catalog 명령 또는 빈 목록만 사용한다. 기존 2.314 fallback은 별도 보존한다.

2.320의 legacy native reader 사용은 `ReadOnlyProcessMemory.Open` 이전에 차단한다. 기존 enabled/verified/SHA/구조 검증은 그대로 유지한다. 명시적 실험 진단은 운영 추천·기록 승인으로 승격하지 않는다. 시작 시 구 cohort 전송도 차단하며 기존 queue를 버전 전환 이유로 삭제하지 않는다.

현재 동일 Warcraft 세션에서 GetLocalPlayer 연결 함수의 RVA 0xCA73D0은 PAGE_NOACCESS(보호 1)였고, 읽은 바이트는 0이다. 불변 로컬 identity, 생존/삭제/재접속 경계와 보조 리더 검증은 여전히 미완료다. 페이지 보호 변경, 메모리 쓰기, 게임 입력, MemoryDiagnostics 접근은 하지 않았다.

## 최종 검증

- `patch2320-release-final.trx`: 2,590 통과, 1 건너뜀, 0 실패. 기존 아카이브 evidence 테스트 1개는 skip 상태다.
- 비라이브 smoke 정상 종료. 해당 로그의 466 OK / 8 PASS 행은 개별 테스트 수로 재표현하지 않는다.
- MapProfileVerifier 기본 2.314 및 `--map-version 2.320` 모두 Valid. CurrentSessionReady=false. 2.320 live/scoring 지원도 false.
- carry policy canonical check 통과. 초기 실패는 CRLF와 LF의 바이트 차이뿐이었고, LF로 정규화한 내용은 생성 결과와 정확히 같았다. 수치나 source identity는 바뀌지 않았다.
- 배포 EXE: 776 bundle entries, Data 328파일 모두 작업본과 바이트 SHA 일치. 닫힌 2.320 번들을 추출한 EXE의 코드로 다시 로드했다.
- embedded public trust로 ECDSA 업데이트 envelope를 검증했다. 개인 키·설정·기록·로그·PDB·소스는 패키지에 없다. Windows Authenticode 코드서명은 아니다.
- ZIP 2개 entry를 전부 압축 해제해 원본 SHA와 비교했다.
- EXE에서 추출한 assembly의 패치 dialog를 렌더했다. MainWindow나 실시간 reader는 만들지 않았다.

## 전달 및 배포

파일: `OrandOverlay-0.6.70-test.20260914.1-map2320-offline-win-x64.zip`

- ZIP: 73,217,551 bytes
- ZIP SHA-256: `2B0AE6DEAE78E3E271B647104E5220C9284FCEBE754EE2E394CDA85A5D8A4A82`
- EXE: 78,767,624 bytes
- EXE SHA-256: `A2D401C24A72D6A88FC726EB8FB9233DCA2786A61D0736CC18511D81845AD898`

이 작업에서 Cloudflare 채널, 기존 .3 asset, stable release, 메모리 프로필을 게시하거나 변경하지 않았다. 수동 전달 테스트 ZIP이며 공식 승격이 아니다. 소스 csproj 기본 버전 0.6.70은 유지하고 publish에 명시적인 test informational version을 적용했다.

상세 원본 근거: `analysis-2320/README.md`. 검증 로그·공개 서명·배포 무결성 기록은 `patch-2320-verification/`에 보존한다.
