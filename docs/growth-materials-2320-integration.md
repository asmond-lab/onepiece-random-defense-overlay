# 2.320 성장형 재료 확보율 연결

## 반영 범위

성장형은 미래 보상이나 추가 카드가 아니라, 성장 중부터 **동일 rawcode의 기존 특별함 1기**로 보유 패와 재료 확보율에 포함한다. 성장 여부는 별도 `GrowthUnitIds`로 유지한다. QR 귀속은 현재 보기(current-view) 기준이며 불변 로컬 플레이어 신원 확정이 아니다.

이번 소스 통합은 3.0의 **명시적 검증 모드 전용 진단 경로**다. 일반 실행의 experimental 프로필 차단, 정확한 실행 파일/version/SHA/프로필 게이트, 2.320 기록·항법 차단을 해제하지 않았다. 기존 ZIP, 공개 테스트 채널, 공식 채널, 사용자 프로필을 교체하거나 새 배포판을 게시하지 않았다.

## 데이터 흐름

1. `Map2320GrowthSource`가 2.320 JASS에서 추출한 3,936개 전역 선언을 크기/SHA/schema/타입/이름으로 검증한다.
2. `Warcraft300Diagnostic.Inventory.Units`가 모든 WorldFrame 개체의 주소·rawcode·소유자·전체 allocation stamp를 제공한다.
3. `Warcraft300GrowthReader`가 현재 instance를 가리키는 aggregate를 매 관측마다 완전한 읽기 전용 private-region 스캔으로 재발견한다. 같은 context에서 추가 aggregate가 생겨도 캐시로 유일성 검사를 생략하지 않는다.
4. 현재 instance/script/native registry/manager/world/view, 모든 원본 전역 선언, QR와 배열, JASS handle와 native allocation을 검증한다. 명시적 QR=0만 부재다. 읽지 못함·빈/잘못된 배열·모호한 owner·변경된 핸들은 부재로 바꾸지 않는다.
5. QR 읽기 전후 WorldFrame 전체 개체/stamp 순서와 외부 world-locator context가 같아야 한다.
6. `GrowthMaterialInventory.Project`가 QR에 연결된 동일 물리 개체를 정확히 한 번만 처리한다. 현재 보기 소유 개체면 이미 집계되어 추가하지 않고, neutral-passive 27이면 해당 특별함을 1기 추가한다. 다른 소유자, 다른 stamp/rawcode/view, WorldFrame 누락·중복은 거부한다. 소유자 27 자체를 귀속 근거로 사용하지 않는다.
7. 일반 `RawcodeUnitMap` 결과가 기존 `RecipeCompletionCalculator` 및 추천/트리에 전달된다. 성장형 전용 가상 ID나 별도 보상 가산식은 없다.
8. `MainWindow`의 실제 조합 계획 호출은 성장 ID를 보호 목록에 포함한다. 확보율이 100%라도 성장형을 즉시 소비 가능한 재료로 취급하지 않는다. 현재 보호는 ID 단위이므로 같은 종류의 일반 개체까지 보수적으로 보호할 수 있다. 이는 정확한 개체별 craft eligibility를 새로 승인한 것이 아니다.

관측/인벤토리 결과를 캐시하지 않는다. 구조 metadata의 context에는 process 시작 시각, 프로필 revision/generation, source 지문과 원본 선언 지문을 연결하며, 소스·세대 변경에 이전 결과를 재사용하지 않는다. 기존 UI의 오류 시 '기존 패 유지'는 새 관측 성공을 뜻하지 않는다.

## 근거 구분과 비용

- World locator/native allocation/JASS manager→native pair의 독립 코드 근거와 VM node/array/aggregate의 실측 구조 후보를 구분한다. 후자를 VM accessor opcode로 입증했다고 하지 않는다.
- private scan 상한은 8 GiB/32초, metadata 읽기 상한은 8 MiB다. 짧은 읽기나 불완전 열거는 거부한다. 매번 전체 owner discovery를 수행하므로 아직 저비용 운영용 reader가 아니다.
- 이름은 최대 256바이트이며 4 KiB 경계를 넘겨 불필요하게 읽지 않는다.
- protected page 변경, 메모리 쓰기, DLL 인젝션, 게임 함수/입력 실행을 하지 않았다. `MemoryDiagnostics` 폴더에 접근하지 않았다.
- 성장 완료 별도 실측은 사용자 지시에 따라 요구하지 않았다. 원본의 동일 type 새 owned 개체 생성과 QR=null 규칙 및 합성 전환 회귀를 사용했다. 완료 실측을 수행했다고 주장하지 않는다.

## 통합 관측

`growth-materials-2320-evidence/integrated-growth.json`은 통합 `WarcraftMemoryRecognitionService.RecognizeAsync`의 실제 진단 결과다. 일반 실행에서 experimental 프로필이 거부되는 것을 먼저 확인하고, 격리 사용자 폴더와 명시적 검증 플래그로 한 번의 성공 관측을 수행했다.

- 현재 보기 0, WorldFrame slots 309, `Ready`이지만 source는 `WarcraftMemoryDiagnostic300CurrentView`.
- 나미 2, 루피 1, 쵸파 2, 프랑키 1, **헤르메포 특별함 1**, 합계 **7**.
- `GrowthUnitIds = [rawcode:I10h]`.
- 현재 전체 패의 키자루 재료 확보: 성장형 제외 **4/14 = 28.57%**, 포함 **8/14 = 57.14%**.
- `hasMapState=false`. 생존 의미·불변 로컬 신원·일반 실시간 지원·기록 지원 승인이 아니다.

첫 통합 시도는 discovery short-read로 거부되었다. 스캔 루프를 byte-pattern `Span.IndexOf`로 최적화한 뒤 다음 실행이 성공했다. 실패 영역을 건너뛰거나 완전성 조건을 완화하지 않았다. 첫 실패 로그를 별도로 보존했다.

## 회귀 및 검토

- 최종 전체 회귀: **2,798 통과 / 1 건너뜀 / 0 실패**, 약 3분 37초.
- 건너뜀: `NavigationMechanicsProfileTests.PinnedArchiveEvidenceMatchesAvailableSourceArchive`.
- 비라이브 smoke: **exit 0**, `PASS: 추천/메모리 연동 스모크 테스트 통과`.
- 초기 smoke는 artifact directory를 프로젝트 외부에 두어 소스 XAML 상대 경로 검사가 실패했다. 프로젝트 내 전용 artifact directory로 재실행하여 통과했다. 제품 코드 변경으로 우회한 것이 아니다.
- 헬메포만 있을 때 키자루 4/14, 모리아 1기 오즈 5/13, 일반 모리아+성장 모리아는 2기/10/13, 같은 물리 개체 중복 방지, 두 목표 간 같은 재료 재사용 방지, 명시적 QR 부재 후 새 owned 개체 1기, 세대/serial/소유/누락/중복 거부를 검증했다.
- 확보율 100%인 합성 fixture의 조합 계획이 평상시에는 존재하지만 성장 ID 보호 시 비어 있음을 검증했다. 기존 자원·조건 게이트는 유지한다.
- 정적 독립 검토에서 발견한 owner-cache 유일성 누락과 페이지 말미 이름 읽기 두 항목을 수정했고, 재검토는 해당 4개 파일 범위에서 material finding 없음이었다. 정적 검토는 라이브 승인이나 전체 코드 증명이 아니다.

## 파일

- `Data/map-growth-globals-2320.json`: 정규화 52,536 bytes, SHA256 `08D09CA1386CFD57E5935BBFA9C2E595644B410AE61F7054089357FAA0904909`.
- 원본 JASS SHA256 `6FDFC64BF8AD9463F5B5C8A351FFA7E1875D6CF9B51210129539375FA77A6C7C`.
- 위 데이터는 별도 진단 보조 데이터이며, 기존 폐쇄 2.320 오프라인 번들의 7개 member 계약을 변경하지 않았다.
- 새 구현: `Map2320GrowthSource.cs`, `ReadOnlyPrivateRegionScan.cs`, `Warcraft300GrowthReader.cs`, `GrowthMaterialInventory.cs`.
- 연결: `WarcraftMemoryRecognitionService.cs`, `Warcraft300Diagnostic.cs`, `MainWindow.xaml.cs`.
- 회귀: `Warcraft300GrowthReaderTests.cs`, `GrowthMaterialInventoryTests.cs`, `GrowthSourceAndRegionTests.cs`.
- 상세 로그와 변경 파일 SHA: `growth-materials-2320-evidence/`.
