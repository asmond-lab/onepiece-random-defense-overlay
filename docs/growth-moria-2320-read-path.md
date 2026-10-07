# 2.320 성장형 모리아: 읽기 전용 경로 조사

## 결론과 범위

2026-09-13 22:02–22:03 UTC, 동일 3.0.0.24268 프로세스/현재 2.320 초기 맵에서 **기본 6기 + QR[0] 성장형 모리아 1기**를 반환하는 읽기 전용 진단 경로를 3회 대조했다. 매번 7기, 같은 모리아 native handle/serial, 같은 WorldFrame membership이 나왔다. 이는 현재-view 0과 사용자 확인 패에 대한 조사 결과다. **불변 로컬 identity, 생존 판별, 맵 재입장, 성장 완료·판매·조합 경계, 일반 앱 지원 승인은 아니다.** 제품 리더/프로필/배포판은 변경하지 않았다.

모리아 rawcode B00h(h00B), owner27, native handle25541/serial28029. owner7의 별도 모리아를 더하지 않는다. 소유자만 세거나 중립 유닛이 한 기라는 추측을 사용하지 않았다. 원본 p9T는 처음부터 neutral-passive로 만들고 QR[player]에 저장한다. w3T는 기존 개체를 숨김/죽임 처리하고 새 소유 개체를 만든 뒤 QR을 비운다. 소유권 이동이나 색상 보존 방식이 아니다.

## 관측된 경로

1. 정확한 EXE SHA 및 프로세스 시작 시각을 확인한다. 기존 opcode 기반 root/UI/WorldFrame decoder와 typed context를 전후 대조한다.
2. current root+25D0 → typed Jass2ScriptInstance_t. 읽기 가능한 데이터에서 이 인스턴스 포인터와 알려진 13F3070 포인터가 인접한 소유 aggregate 후보를 찾는다. 현재 일치 후보의 주소는 1610138833400이다. 과거 null 인스턴스 후보는 제외한다.
3. 해당 aggregate+16 → ScriptDataTable 후보 1609697873520. 테이블 vtable, 현재 인스턴스 포인터, 소유 aggregate 및 인스턴스 전체 관측 헤더가 전후 동일해야 한다.
4. 테이블+24에서 연결 목록을 순방향 순회한다. 각 node+24 역방향 링크를 직전 링크와 대조하고, table+17 sentinel 및 table+16 tail을 확인한다. 6196개 식별자에서 원본 JASS의 3936개 선언 모두 이름/타입이 일치했다. 원본 파일 SHA도 재검증했다. **선언 일치는 실행 중인 전체 스크립트 바이트/동작의 해시 검증이 아니다.**
5. 이름 QR, type pair12/12인 node → payload+56 → typed JassArray. 현재 배열의 관측 count+8=1, data+16, capacity+24=1. 원소 DWORD h=1058408.
6. M=Q(root+2620), i=h-0x100000, i<U32(M+290), entry=Q(M+298)+24*i, P=Q(entry+8). 이 JASS 핸들 → native object 연결은 별도의 3.0 covered 명령 근거가 있다.
7. P의 CUnit vtable 및 native index/serial, allocation marker, record type/backref를 대조하고 현재 WorldFrame에 존재하는지 검사한다. rawcode B00h, owner27이 나왔다. 현재-view 소유 6기와 중복 없이 합치면 7기다.

## 근거 강도와 정정

- JassArray의 unsigned-int 배열 base+8는 현 빌드 RTTI 근거가 있다. 그러나 구체적인 node/array/table/aggregate 필드 의미는 이번 실측 상호대조에 근거한 **실험적 구조 후보**다. 해당 VM accessor 본문은 여전히 읽기 불가이며 opcode로 완전히 입증하지 않았다.
- 13F0830 covered producer는 [instance pointer, callback] 순서로 쓴다. 앞서 [callback, table pointer]를 callback/context 쌍으로 해석한 가정은 폐기했다. table은 다음 필드의 관측값이다.
- root+2620의 manager+3A0이 2809D80 타입이라는 가정은 실제 vtable 불일치로 폐기했다. 이 가정을 핸들 판독에 사용하지 않았다.
- RTTI 탐색: 약5.056GB, 3.95초, read-short0. 321개 JassArray/14개 ScriptDataTable 후보. 참조 탐색 약5.056GB, 6.63초, short-read2. 따라서 참조 스캔으로 전 프로세스 유일성을 주장하지 않는다.
- QR 연결 목록의 긴 native 식별자 때문에 32바이트 이름 제한에서 한 번 중단했다. 이후 최대256바이트까지 점진적 exact-read/식별자 구문 검사로 대조했다. 불일치를 0이나 유효 값으로 바꾸지 않았다.
- 직접 VM resolver 1649A00/164B380, GetLocalPlayer CA73D0, World membership helper1271DC0의 페이지 보호는 변경하지 않았다. 함수 실행·게임 입력·메모리 쓰기·인젝션 없음. MemoryDiagnostics 접근/수정 없음.

## 반복 결과

| 표본 | 총 후보 | 일반 소유 | QR 성장형 | 읽기 bytes | 전체 목록 재대조 포함 시간 |
|---|---:|---:|---:|---:|---:|
| verified-1 | 7 | 6 | 1 | 1,071,755 | 241.675ms |
| verified-2 | 7 | 6 | 1 | 1,071,755 | 237.441ms |
| verified-3 | 7 | 6 | 1 | 1,071,755 | 245.568ms |

초기 발견은 heap 탐색을 사용했고 반복 표본은 **현재 판에 한정된 발견 주소**를 검증해 재사용했다. 다른 판에 주소를 강제 적용하는 범용 reader가 아니다. 단순 정지상태의 세 번 반복이며 장기/전투/성장 완료 관측이 아니다.

## 다음 검증과 배포 상태

- [blocked] 새 맵 세대에서 재탐색/오래된 소유 aggregate·배열 거부, 다인/시점 변경의 불변 로컬 identity.
- [blocked] 성장 완료 시 QR 해제와 player-owned replacement, 사망/삭제/판매/조합 경계.
- [blocked] 실험적 field layout의 추가 독립 근거와 fail-closed reader/test 통합.
- 제품 자동 인식은 이 실험 경로를 아직 사용하지 않는다. 기존 일반 모드 차단, 기록 차단, 서명 채널과 배포 ZIP 모두 유지.
- 이전 2695 passed/1 skipped는 WorldFrame/할당 변경의 전체 회귀이며 이 새 임시 프로브의 제품 통합 회귀로 재표시하지 않는다.

최소 증거: growth-moria-2320-evidence/verified-{1,2,3}.json 및 실행 코드를 비컴파일 Proof.cs.txt로 보존.

## 재입장 1회 추가 검증 (2026-09-13 22:07–22:11 UTC)

동일 프로세스79380/시작시각/EXE에서 사용자가 새 맵에 재입장했다. root, UI, WorldFrame, player, native registry, JASS instance/script, 소유 aggregate, ScriptDataTable, QR node/array와 growth CUnit가 이전 판과 달랐다. instanceId와 scriptId는 다시1이었다. ID1만으로 같은 세대를 판정하면 안 된다.

- 이전판 검증기는 `Map generation changed`로 거부했다. 모리아 관측을 새 패로 재사용하지 않았다.
- 새 프로브는 현재 root의 typed 인스턴스와 aggregate callback 값으로 readable private data를 탐색했다. 과거 root/aggregate/table/QR/growth 주소를 입력하지 않았다. 5,070,614,528bytes/2682.4724ms, short-read0, 전체 범위 종료, 일치 aggregate 후보1개. 이후 검증기는 발견 JSON을 읽어 현재 세대와 재대조했다.
- 새 root1610270394720, instance1611370303648, script1611342575328, owner aggregate1610441505560, table1610416467616, QR node1609643998480, array1610218614400.
- QR[view0] h1058415 → native unit1609423823632 → I10h 헤르메포, owner27, native index2741/serial27861. 이전 B00h 모리아 index25541/serial28029와 다르다.
- 나미100h×2, 쵸파800h×2, 루피300h×1, 프랑키L00h×1, QR 성장형 헤르메포I10h×1. 총7기.
- 3회 동일: 원본 선언3936개 이름/타입 모두 일치, 관측변수6196개, World309개. 전후 입력/역방향 목록/꼬리/sentinel/핸들·세대/WorldFrame 대조 통과. 각1,071,763bytes, 255.3492/243.5243/241.6939ms.
- 화면에 새로운 성장형 실루엣과 초기 패가 보인다. 겹친 개체 수량과 이름을 화면만으로 독립 확정한 것은 아니며 위 이름/수량은 메모리 진단 결과다.

동일 프로세스 내 한 번의 실제 재입장에서 이전 캐시 거부와 새 경로 재탐색을 확인했다. 재입장 전반, 다인, 불변 local identity, 성장 완료, 생존/판매/조합, 일반 제품 지원 승인은 아니다. node/array/aggregate field 해석은 실측 기반 후보이며 accessor opcode 입증은 별도다. 제품 코드/프로필/배포/기록 활성 상태 미변경.

증거: growth-moria-2320-evidence/reentry-1/ 하위 3개 표본, owner-discovery.json, old-cache-rejected.log, 비컴파일 프로브 코드.

## 사용자 범위 조정: 성장 완료 별도 실측 생략

사용자가 성장 완료 후에는 일반 소유의 특별함 헤르메포 1기로 집계되므로 별도 검증은 필요하지 않다고 명시했다. 해당 전환 규칙은 기존 원본 분석에서도 확인된 내용이다. 따라서 성장 완료 전후의 별도 실측을 필수 검증 항목에서 제외한다. 성장 중 QR 귀속으로 보완하고, 완료 후 일반 소유의 해당 특별함으로 집계하는 설계를 유지한다. 이는 실측을 수행했다는 뜻이 아니며, 같은 개체의 중복 집계 방지·현재 세대/핸들 검증·기존 안전 게이트를 생략하라는 요청으로 확대하지 않는다. 위 과거 항목 중 성장 완료 실측은 미완료 차단 항목이 아니라 사용자 요청으로 범위에서 제외된 항목이다.


## 후속: 성장 중부터 특별함 재료로 반영

`growth-materials-2320-integration.md`에 검증 전용 소스 통합 결과를 기록했다. 통합 진단에서 현재 헤르메포를 `rawcode:I10h` 특별함 1기로 포함하여 7기, 키자루 확보율 8/14(성장형 제외 4/14)를 확인했다. 전체 회귀 2,798 통과/1 건너뜀/0 실패 및 비라이브 smoke exit 0. 매 관측 완전 owner discovery와 WorldFrame 전후 stamp 비교를 요구하며 일반 실행·불변 로컬 identity·생존·기록 지원 승인은 여전히 별도다. 성장 완료 별도 실측은 사용자 요청대로 추가하지 않았다. 기존 배포 ZIP/채널은 변경하지 않았다.
