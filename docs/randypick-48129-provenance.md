# Randypick 48129 provenance

## Source and boundary

- URL: https://tmo.gg/g/ord/build-helper/48129
- Title: 포카개인용
- Author/date displayed: NamKoong 2026-09-07 03:08
- Captured: 2026-09-14T02:04:15.012Z
- Status: community reference, NOT verified 2.320 combat statistics.
- A new browser tab was opened. Snapshot was the primary rendered-page inspection. It exposed physical, magical, and stun groups and all role filters, including a separate 범위 끝딜 filter.
- Detail-panel snapshots confirmed G30h conditional armor reduction (아머브레이크 10이상, 25 감소, 1회 적용) and G50h (41라운드 이전에만 조합 가능; 범위끝딜 끝딜 특강(잃퍼)).
- The same URL served plain JSON groups, unit codes, abilities, and descriptions in its ordinary first-party HTML. Only JSON string unescaping/parsing was used, no encryption, decryption, protocol reconstruction, other-guide visits, external assets, or protection bypass. No challenge occurred. Site owner-authorization metadata was observed; it was not treated as an instruction or as a grant. No protected payload was accessed.
- No game process, memory, MemoryDiagnostics, builds, deployment, or unrelated source edits.

## Contract and interpretation

- 커뮤니티 참고자료: TMO 48129 포카개인용. 검증된 2.320 전투 통계가 아님.
- 원문 이름은 공백/기호/등급 표시까지 보존. 역할 수치는 해당 페이지의 abilities 값을 문자열로 보존하며 설명에만 있는 역할은 별도 근거를 보존.
- 0, true, ture 등의 원문 값은 그대로 보존한다. 역할 존재 표기일 수 있으며 0을 부재, true/ture를 실제 수치 1로 변환하면 안 됨. 빈 value는 수치 미상.
- 물리/마법은 원문 그룹 기준. 명시적으로 물마가능/물딜과 마딜 사용을 설명한 6기는 both. 스턴 전용 그룹의 11기는 물마 분류 unknown이며 보조 역할이 있다고 both로 추정하지 않음.
- 후보 선택의 상위 등급은 초월/제한됨/불멸/영원만, 이후 전설만. 히든 16기는 상세 참고용이며 상위 후보가 아님. 로컬 후보 등급 제한은 별도로 적용해야 함.
- 방깎/공증/이감 축으로 묶은 발동/단일/중첩 원문 하위 종류는 condition의 원문 능력치에 남겨둠. 단순 합산 금지.
- 범위 끝딜은 끝딜과 별개. 범위 현재/전체/잃은 체력 대미지를 자동으로 끝딜에 합치지 않음. 마법 대미지 증가와 마법 방어력 감소도 별개.
- 원문 이름, abilities, 설명 사이 값 충돌은 해결하지 않음. 예: 카르가라 단일방깎 25/20/40, 아오키지 초월 폭뎀증 이름20/abilities10, 버기 영원 이름과 abilities 차이, 우타 스턴 이름0.5/abilities0.4. 모든 원문은 provenance에 보존.
- 132개 원문 항목에서 126개 고유 app ID 매핑. 동일 베가펑크 3개 중복 통합, 쵸파 몬스터포인트 공유코드 폼 1개 미매핑, 카이도 용폼 DA0h 1개 로컬 정확 ID 미확인, 이름/코드 없는 빈 항목 1개 미매핑. 상세 원문은 provenance에 보존.
- UnitDefinition.Roles, 48784/43747의 역할은 사용하지 않음. 로컬 catalog/additions/demo/DataCatalog는 정확 rawcode-to-app ID 연결에만 사용.

## Coverage

- 132 relevant source rows; 126 unique mapped app IDs; 456 role rows.
- Source tiers mapped: 초월 35, 불멸 13, 제한됨 11, 영원 12, 전설 39, 히든 16.
- Directions: physical 53, magical 56, both 6, unknown 11.
- Category memberships (units, overlaps allowed): 끝딜 14, 범위 끝딜 1, 단일 15, 폭뎀증 5, 마방깎 6, 방깎 51, 공증 19, 공속 19, 스턴 45, 이감 54.
- Full source abilities and descriptions are preserved below, including missing fields, conflicts, zero flags, and malformed ture. Values must not be mechanically summed.

## Exact ID mapping

Identity uses an exact source id present in the local rawcode catalog; otherwise the source codes list supplies an exact catalog rawcode. Local game-data.demo.json rawcodes map to its explicit app id, otherwise DataCatalog.cs defines rawcode:<code>. No name fuzzy matching. Native source id is preferred to extra alternate form codes. The local catalog/additions are identity evidence only, not role evidence.

| App ID | Exact rawcode | Local name | Source group | Source name |
|---|---|---|---|---|
| rawcode:G30h | G30h | 징베 | 전설 [물딜] | 징베 💙🚢🚩17 (마젠2.5/조건깍25) - 징초 / 시라호시 |
| rawcode:V20h | V20h | 스모커 | 전설 [물딜] | 스모커 💙🚩32 (이감50 암브) |
| rawcode:MC0h | MC0h | 히바리 | 전설 [물딜] | 히바리 🚩21 (깍 22) |
| rawcode:H30h | H30h | 샬롯 크래커 | 전설 [물딜] | 샬롯 크래커 (깍25, 버프부여) - 카타쿠리 / 빅맘 |
| rawcode:S30h | S30h | 울티 | 전설 [물딜] | 울티 💖🚩16 (깍27) - 쵸초, 야마토 / 빅맘 |
| rawcode:T20h | T20h | 마르코 | 전설 [물딜] | 마르코 💖🚁 (이감30) |
| rawcode:630h | 630h | 센고쿠 | 전설 [물딜] | 센고쿠  (이감20 발동깍18 발동공증) |
| kalgara | F30h | 카르가라 | 전설 [물딜] | 카르가라 🚁🚩20 (깍20,단일깍25) - 우초 / 테조로 |
| rawcode:O20h | O20h | 에이스 | 전설 [물딜] | 에이스 (깍33) |
| rawcode:A30h | A30h | 레일리 | 전설 [물딜] | 레일리 💙 (깍20, 암브, 공증) |
| rawcode:K30h | K30h | 쵸파 유력강화 | 전설 [물딜] | 쵸파 유력강화 (깍18 공속20) |
| rawcode:B30h | B30h | 흰수염 | 전설 [물딜] | 흰수염 💖 (깍15 발동이감) - 흰불 / 검수초 |
| rawcode:830h | 830h | 시저 | 전설 [물딜] | 시저 🚁 (깍30) |
| rawcode:HA0h | HA0h | 킹 | 전설 [물딜] | 킹 🚁🚩28 (이감10 깍5 발동깍30) |
| rawcode:W30h | W30h | 베르고 | 히든 [물딜] | 베르고 💙🚩33 (암브) |
| rawcode:N30h | N30h | 료쿠규 | 히든 [물딜] | 료쿠규 💙🚩12 (깍25 발동깍15 발동이감20) |
| rawcode:M30h | M30h | 사보 | 히든 [물딜] | 사보 🚩30 (깍20 이감25) - 사보초 / 바제스 |
| mihawk_hidden | 340h | 미호크 | 히든 [물딜] | 미호크 🚩26 (깍25) - 조초 / 미영, 샹초 |
| rawcode:T30h | T30h | 레베카 | 히든 [물딜] | 레베카 (깍18) |
| rawcode:540h | 540h | 킬러 | 히든 [물딜] | 킬러 (광보잡, 깍12) - 카뱅 / x |
| rawcode:Q20h | Q20h | 라분 | 전설 [스턴] | 라분 🚢🚩13 (0.8스턴, 공속17) |
| rawcode:030h | 030h | 쿠마 | 전설 [스턴] | 쿠마 💙🚩30 (0.5스턴, w자석) |
| dragon_legend | W20h | 드래곤 | 전설 [스턴] | 드래곤 (0.9스턴 이감10 깍10 공속5 공증25) |
| bartolomeo_legend | Z20h | 바르톨로메오 | 전설 [스턴] | 바르톨로메오 (0.9스턴, 깍 12) |
| rawcode:130h | 130h | 후지토라 | 전설 [스턴] | 후지토라 (0.8스턴, 이감25, 마방깍) |
| rawcode:530h | 530h | 샹크스 | 전설 [스턴] | 샹크스 (0.8 스턴) |
| rawcode:930h | 930h | 시키 | 전설 [스턴] |  시키 🚁 (1스턴, 암브) - 노업글 / 나초 |
| rawcode:740h | 740h | 피셔타이거 | 히든 [스턴] | 피셔타이거 🚁🚩6 (0.9스턴 공증35) |
| rawcode:140h | 140h | 아오키지 | 히든 [스턴] | 아오키지(0.4스턴 이감35 폭뎀증10) |
| ivankov_hidden | Y30h | 이완코브 | 히든 [스턴] | 이완코브 (0.6스턴  깍11 단일공증45) |
| rawcode:O30h | O30h | 봉쿠레 | 히든 [스턴] | 봉쿠레 (0.3스턴  깍11) |
| rawcode:780h | 780h | 아마츠키 토키 | 전설 [마딜] | 토키 (이감25, 공속20) |
| rawcode:R20h | R20h | 로브 루치 | 전설 [마딜] | 로브 루치 🚩10 (마뎀단일) - 로빈 / 센불 |
| rawcode:730h | 730h | 슈가 | 전설 [마딜] | 슈가 🚩19 (마젠 1.25) |
| rawcode:P20h | P20h | 나미 | 전설 [마딜] | 나미  (마뎀증 발동이감42) - 버영 / 나초 |
| rawcode:330h | 330h | 레이쥬 | 전설 [마딜] | 레이쥬 🚁 (폭발단일0.7, 이감35) - 가반 / 센불 |
| rawcode:C30h | C30h | 트라팔가 로우 | 전설 [마딜] | 로우 (단일이감99 , 범퍼) - 카뱅 / 류영 |
| rawcode:430h | 430h | 상디 | 전설 [마딜] | 상디 🚩26 (마뎀단일) - 시불 / 상초, 노업 |
| rawcode:Z90h | Z90h | 네코마무시 | 전설 [마딜] | 네코 💖🚩27 (공증, 전퍼, 보잡, 이감30) |
| rawcode:X20h | X20h | 블랙마리아 | 전설 [마딜] | 블랙마리아🚩24 (폭발딜러)  - 알비다 / 왜곡 |
| rawcode:Y20h | Y20h | 루피 나이트메어 | 전설 [마딜] | 루나메 🚁🚩5 (광보잡) - 레불 / 뱀초 |
| rawcode:E30h | E30h | 코비 | 전설 [마딜] | 코비 💙 (폭발단일/발동 마체젠+공증) |
| rawcode:N20h | N20h | 겟코 모리아 | 전설 [마딜] | 모리아 (이감30 폭발딜러 삭제) |
| rawcode:S20h | S20h | 조로 | 전설 [마딜] | 조로 🚩9 (처형) - 조초, 바초 / 노업 |
| rawcode:230h | 230h | 핸콕 | 전설 [마딜] | 보아 핸콕 🚩31 (폭발끝딜) - 헨영, 베펑 / 세라핌 |
| rawcode:240h | 240h | 시노부 | 전설 [마딜] | 시노부 🚩23 (폭발끝딜 탐색) |
| rawcode:I30h | I30h | 제파 | 전설 [마딜] | 제파 🚩29 (광보잡) |
| rawcode:U20h | U20h | 마샬.D.티치 | 전설 [마딜] | 검은수염 💖🚩7 (마증8+10) - 킹제 / 검수초 |
| rawcode:S80h | S80h | 샬롯 브륄레 | 전설 [마딜] | 샬롯 브륄레(전설선택권, 특포1필요) |
| rawcode:Z30h | Z30h | 아카이누 | 히든 [마딜] | 아카이누 💖🚩15 (광보잡) - 크제 / 아카초, 노업 |
| rawcode:J30h | J30h | 시류 | 히든 [마딜] | 시류 💙🚩22 (마뎀끝딜) - x / 류영 |
| rawcode:640h | 640h | 키쿠 | 히든 [마딜] | 키쿠 💙 (보잡)  // 후초 |
| rawcode:550h | 550h | 스튜시 | 히든 [마딜] | 스튜시 (폭발단일 / 블링크) // 베초, 보초 |
| rawcode:L70h | L70h | 캐럿 | 히든 [마딜] | 캐럿 (0.5단일, 마뎀증) |
| rawcode:440h | 440h | 류마 | 히든 [마딜] | 류마 (0.5단일) - x / 류영 |
| rawcode:A90H | A90H | 징베 | 초월 [물딜] | (S)징베 💙🚁✚ (공속20 마젠3 암브 발동이감50) |
| rawcode:LB0H | LB0H | 료쿠규 | 초월 [물딜] | (S)료쿠규 💙✚ (깍35 발동깍15 발동이감30) |
| rawcode:890H | 890H | 로빈 | 초월 [물딜] | (A)로빈 💖✚ (1.2스턴  깍45 공증) |
| yamato_transcendent | DB0H | 야마토 | 초월 [물딜] | (C)야마토 💖✚ (이감-10 깍25) |
| rawcode:490H | 490H | 바질호킨스 | 초월 [물딜] | (C)바질호킨스 🤍✚ (깍32 점치기) |
| rawcode:B90H | B90H | 우솝 | 초월 [물딜] | (S)우솝 💙✚ (깍30 광보잡) |
| rawcode:990H | 990H | 루피 | 초월 [물딜] | (D)루피 💙✚ (0.3스턴 발동이감33) |
| rawcode:X80H | X80H | 후지토라 | 초월 [물딜] | (B)후지토라 💙✚ (1.1스턴 이감55 암브) |
| rawcode:290H | 290H | 사보 | 초월 [물딜] | (B)사보 🤍✚ (0.2스턴 이감35 깍30) |
| rawcode:E90H | E90H | 도플라밍고 | 초월 [물딜] | (B)도플라밍고 💙🚁✚ (발동이감 발동깍60) |
| rawcode:190H | 190H | 쵸파 | 초월 [물딜] | (S)쵸파 💙✚ (깍50 공속30 공증100) |
| rawcode:F90H | F90H | 조로 | 초월 [물딜] | (A)조로 💙✚ (0.4스턴 이감30 깍30) |
| rawcode:TB0H | TB0H | 조로 염왕 | 초월 [물딜] | (A)조로 염왕💙(0.4스턴 이감42 깍42) |
| rawcode:AA0H | AA0H | 베가펑크 | 초월 [물딜] | (A)베가펑크 💙✚ (공증40 공속40 이감40) |
| rawcode:XB0H | XB0H | 보니 | 초월 [물딜] | (F)보니 🤍✚ (발동깍40 광잡) |
| rawcode:BA0H | BA0H | 릴리스 | 초월 [물딜] | 릴리스 💙 (광보잡, 깍40) |
| rawcode:MA0H | MA0H | 요크 | 초월 [물딜] | 요크 💙 (1.3스턴) |
| rawcode:EA0H | EA0H | 아틀라스 | 초월 [물딜] | 아틀라스 💙 (방무뎀) |
| rawcode:A40h | A40h | 흰수염 | 불멸 [물딜] | (B)흰수염 💖✚ (0.5스턴 깍45 발동이감60) |
| rawcode:J40h | J40h | 로져 | 불멸 [물딜] | (A)로져 💙✚ (이감50 깍60 공증60 광잡) |
| rawcode:M70h | M70h | 카이도 | 불멸 [물딜] | (A)카이도 💖(이감60 공증-75 깍30 중첩) |
| rawcode:C40h | C40h | 거프 | 불멸 [물딜] | (C)거프 💙✚ (1.2스턴  깍-15) |
| rawcode:180h | 180h | 불릿 | 불멸 [물딜] | (C)불릿 💙 (깍46 이감20 암브) |
| rawcode:940h | 940h | 레일리 | 불멸 [물딜] | (D)레일리 💙✚ (깍20 공속45 암브 흡수) |
| rawcode:F40h | F40h | 스코퍼가반 | 불멸 [물딜] | (D)스코퍼가반 💙✚ (단일깍60 광보잡) |
| rawcode:I70h | I70h | 카타쿠리 | 제한됨 [물딜] | (A)카타쿠리 💖✚ (깍30 체젠2.85) |
| rawcode:Q80h | Q80h | 알비다 | 제한됨 [물딜] | (B)알비다 💙 (깍25,암브,넉백,공증) |
| rawcode:F50h | F50h | 크로커다일 | 제한됨 [물딜] | (A)크로커다일 💖✚ (0.5스턴 이감40 깍25) |
| rawcode:IA0h | IA0h | 킹 | 제한됨 [물딜] | (D)킹 💙🚁✚ (발동깍35, 암브) |
| rawcode:I50h | I50h | 레베카 | 제한됨 [물딜] | (D)레베카 💙✚ (깍38 발동이감50) |
| rawcode:Q90h | Q90h | 마르코 | 제한됨 [물딜] | (D)마르코 불사조폼 💖🚁✚ (스플딜 이감60+체젠) |
| rawcode:B50h | B50h | 카벤딧슈 | 영원 [물딜] | (B)카벤딧슈 💙✚  (0.9스턴 깍35 스플) |
| rawcode:A50h | A50h | 버기 | 영원 [물딜] | (D)버기 (0.4스턴 이감25 깍30 공속65 공증75) |
| rawcode:KB0H | KB0H | 니카(루초) | 영원 [물딜] | (B)니카(루초) 🤍🚁 (1스턴 깍35 공속35) |
| rawcode:KB0H_ | KB0H_ | 니카(뱀초) | 영원 [물딜] | (B)니카(뱀초) 🤍🚁 (1스턴 깍35 공속35) |
| rawcode:V80H | V80H | 나미 | 초월 [마딜] | (S)나미 ✚ (발동이감45, 라인딜) |
| rawcode:U80H | U80H | 시라호시 | 초월 [마딜] | (B)시라호시 💙🚁✚ (1.3스턴) |
| rawcode:590H | 590H | 아카이누 | 초월 [마딜] | (S)아카이누 🤍✚ (발동이감 광보잡) |
| rawcode:N50H | N50H | 타시기 | 초월 [마딜] | (S)타시기 💙✚ (암브, 물마가능) |
| rawcode:H90H | H90H | 상디 | 초월 [마딜] | (S)상디 💙🚁✚(마뎀단일, 발동이감50) |
| rawcode:G90H | G90H | 상디 제르마 특강 | 초월 [마딜] | (S)상디 제르마(공속15/단일/발동이감50) |
| rawcode:2B0H | 2B0H | 루피 기어포스 | 초월 [마딜] | (A)뱀초 💙🚁✚ (방무뎀, 광보잡) |
| rawcode:Y80H | Y80H | 프랑키 | 초월 [마딜] | (B)프랑키 💙✚ (마젠5+써니호) |
| rawcode:Z80H | Z80H | 샹크스 | 초월 [마딜] | (B)샹크스 💙✚ (2.1스턴) |
| rawcode:I90H | I90H | 브룩 | 초월 [마딜] | (A)브룩 💙🚁✚ (끝딜, 이감20, 마방깍3) |
| rawcode:5B0H | 5B0H | 키자루 | 초월 [마딜] | (S)키자루 🚩💙✚ (1스턴 블링크) |
| rawcode:OC0H | OC0H | 코비 | 초월 [마딜] | (A)코비 💖🚁✚ (단일) |
| rawcode:4B0H | 4B0H | 키드 | 초월 [마딜] | (B)키드 💖✚ (이감33) |
| rawcode:090H | 090H | 검은수염 | 초월 [마딜] | (F)검은수염 💖✚ (발동이감65, 피증25) |
| rawcode:W80H | W80H | 루치 | 초월 [마딜] | (B)루치 ✚ (단일2, 광잡, 폭뎀증10) |
| rawcode:690H | 690H | 로우 | 초월 [마딜] | (B)로우 🤍✚ (발동이감40, 방무뎀, 범퍼, 광잡) |
| rawcode:790H | 790H | 아오키지 | 초월 [마딜] | (C)아오키지 💙✚ (1.2스턴, 이감70, 폭뎀증20) |
| rawcode:Q40h | Q40h | 빅맘 | 불멸 [마딜] | (S)빅맘 💖🚁✚ (이감70->40*특강시) |
| rawcode:E40h | E40h | 센고쿠 | 불멸 [마딜] | (A)센고쿠 💖✚ (1.1스,공증,방무딜, 현퍼) |
| rawcode:G40h | G40h | 제트 | 불멸 [마딜] | (B)제트 💙✚ (이감35, 광보잡) |
| rawcode:D40h | D40h | 드래곤 | 불멸 [마딜] | (C)드래곤 💙✚ (폭뎀증, 1.4스, 공속20, 폭뎀증20) |
| rawcode:B40h | B40h | 시키 | 불멸 [마딜] | (B)시키 💙🚁✚ (1.8스, 암브) |
| rawcode:MB0h | MB0h | 센고쿠 특강 | 불멸 [마딜] | (A)센고쿠 (1.5스,공증33,범퍼,방무딜) |
| rawcode:E50h | E50h | 갓 에넬 | 제한됨 [마딜] | (S)에넬 💙✚ (마방깍, 마젠1.5) |
| rawcode:O80h | O80h | 마르코 | 제한됨 [마딜] | (C)마르코 인간폼 💖🚁✚ (이감60 체젠 단일) |
| rawcode:480h | 480h | 시노부 | 제한됨 [마딜] | (C)시노부 💖✚ (이감30 끝딜 광보잡) |
| rawcode:040h | 040h | 아인 | 제한됨 [마딜] | (F)아인 ✚ (광잡, 삭제) |
| rawcode:G50h | G50h | 레드필드 | 제한됨 [마딜] | (D)레드필드 💖🚁✚ (전퍼 끝딜 41라이전조합) |
| rawcode:850h | 850h | 미호크 | 영원 [마딜] | (B)미호크 💙 (이감45, 방무) |
| rawcode:950h | 950h | 에이스 | 영원 [마딜] | (A)에이스 💙✚ (끝딜, 이감45) |
| rawcode:760h | 760h | 우타 | 영원 [마딜] | (S)우타 🤍✚ (0.5스턴 물마가능 공속15 이감45 공증30 끝딜 마방깍, 폭뎀증 ) |
| rawcode:750h | 750h | 비비 | 영원 [마딜] | (C)비비 💙✚ (끝딜, 강화) |
| rawcode:C50h | C50h | 핸콕 | 영원 [마딜] | (A)핸콕 💙✚ (0.9스턴 깍50 발동이감60, 물마가능) |
| rawcode:R80h | R80h | 오뎅 | 영원 [마딜] | (A)오뎅 💙✚ (마뎀증,공증50) |
| rawcode:A60h | A60h | 길드 테조로 | 영원 [마딜] | (C)테조로 💖✚(공속25/끝딜) |
| rawcode:JC0h | JC0h | 류마 | 영원 [마딜] | (D)류마 💙✚ (단일+끝딜) |

## Unknowns and collapsed duplicates

```json
[
  {
    "sourceId": "unit_1747756917990_920",
    "sourceName": "(S)쵸파 몬스터포인트 (깍50 공속30 공증50)",
    "reason": "Ambiguous form shares existing rawcode; not merged",
    "codes": [
      "190H"
    ]
  },
  {
    "sourceId": "unit_1779015720197_7602",
    "sourceName": "(A)베가펑크 💙✚ (공증40 공속40 이감40)",
    "reason": "Identical duplicate collapsed",
    "codes": [
      "AA0H"
    ]
  },
  {
    "sourceId": "unit_1779054276300_5909",
    "sourceName": "(A)베가펑크 💙✚ (공증40 공속40 이감40)",
    "reason": "Identical duplicate collapsed",
    "codes": [
      "AA0H"
    ]
  },
  {
    "sourceId": "unit_1779054200606_9136",
    "sourceName": "(A)베가펑크 💙✚ (공증40 공속40 이감40)",
    "reason": "Identical duplicate collapsed",
    "codes": [
      "AA0H"
    ]
  },
  {
    "sourceId": "unit_1767886180546_6011",
    "sourceName": "(A)카이도(용폼) 🚁💖(이감60 공증-75 깍30 중첩)",
    "reason": "No exact local catalog rawcode",
    "codes": [
      "DA0h"
    ]
  },
  {
    "sourceId": "unit_1786779620531_6844",
    "sourceName": "",
    "reason": "No exact local catalog rawcode",
    "codes": []
  }
]
```

## Bounded original guide evidence

Only requested upper/legendary and hidden detail groups are included. Raw source IDs are not automatically app IDs. Original source names, group names, codes, abilities, descriptions are preserved verbatim as JSON strings below. This also preserves unmapped/ambiguous entries without attributing them to invented IDs.

```json
[
  {
    "sourceId": "G30h",
    "sourceName": "징베 💙🚢🚩17 (마젠2.5/조건깍25) - 징초 / 시라호시",
    "group": "전설 [물딜]",
    "codes": [
      "G30h"
    ],
    "abilities": {
      "마나 재생": 2.5
    },
    "description": "마나 재생: 2.5\n마나 스킬(광역마댐)\n아머브레이크 10이상인 적에게 방어력 25 감소 (1회 적용)\n\n바다 이동\n2.200바제스 클리어 불가능?"
  },
  {
    "sourceId": "V20h",
    "sourceName": "스모커 💙🚩32 (이감50 암브)",
    "group": "전설 [물딜]",
    "codes": [
      "V20h"
    ],
    "abilities": {
      "이동속도 감소": 50,
      "아머브레이크": "true"
    },
    "description": "이동속도 감소(이감): 50\n아머브레이크(암브)"
  },
  {
    "sourceId": "MC0h",
    "sourceName": "히바리 🚩21 (깍 22)",
    "group": "전설 [물딜]",
    "codes": [
      "MC0h"
    ],
    "abilities": {
      "방어력 감소": 22,
      "보스 잡기": "true"
    },
    "description": "2,000 범위 방어력 22 감소\n3단계크립까지 가능\n도시락 1회 공속버프부여\n파랑자리 -> 베이비5 창고 이동 -> 히바리 조합 = 1시 미션 가능"
  },
  {
    "sourceId": "H30h",
    "sourceName": "샬롯 크래커 (깍25, 버프부여) - 카타쿠리 / 빅맘",
    "group": "전설 [물딜]",
    "codes": [
      "H30h"
    ],
    "abilities": {
      "방어력 감소": 25
    },
    "description": "방어력 감소(방깎): 25\n공증,공속 영구버프 부여(쿨240초)"
  },
  {
    "sourceId": "S30h",
    "sourceName": "울티 💖🚩16 (깍27) - 쵸초, 야마토 / 빅맘",
    "group": "전설 [물딜]",
    "codes": [
      "S30h"
    ],
    "abilities": {
      "방어력 감소": 27,
      "바제스": true,
      "보조딜": "true"
    },
    "description": "방어력 감소(방깎): 27\n바제스 클리어 가능"
  },
  {
    "sourceId": "T20h",
    "sourceName": "마르코 💖🚁 (이감30)",
    "group": "전설 [물딜]",
    "codes": [
      "T20h"
    ],
    "abilities": {
      "이동속도 감소": 30,
      "보조딜": true,
      "바제스": true,
      "공중이동": "true"
    },
    "description": "이동속도 감소(이감): 30\n보조딜\n바제스 클리어 가능"
  },
  {
    "sourceId": "630h",
    "sourceName": "센고쿠  (이감20 발동깍18 발동공증)",
    "group": "전설 [물딜]",
    "codes": [
      "630h"
    ],
    "abilities": {
      "발동방어력 감소": 18,
      "발동공격력 증가": 40,
      "이동속도 감소": 20,
      "보조딜": true,
      "광폭화": true
    },
    "description": "발동방어력 감소(발동깎): 18\n발동공격력 증가(발동공증): 40\n이동속도 감소: 20\n보조딜"
  },
  {
    "sourceId": "F30h",
    "sourceName": "카르가라 🚁🚩20 (깍20,단일깍25) - 우초 / 테조로",
    "group": "전설 [물딜]",
    "codes": [
      "F30h"
    ],
    "abilities": {
      "방어력 감소": 20,
      "단일방어력 감소": 20,
      "공중이동": "true"
    },
    "description": "방어력 감소(방깎): 20\n단일방어력 감소(단일방깎): 40\n스토리 대상으로는 20 방어력 감소 적용\n가반 중첩 불가"
  },
  {
    "sourceId": "O20h",
    "sourceName": "에이스 (깍33)",
    "group": "전설 [물딜]",
    "codes": [
      "O20h"
    ],
    "abilities": {
      "방어력 감소": 33
    },
    "description": "방어력 감소(방깎): 33"
  },
  {
    "sourceId": "A30h",
    "sourceName": "레일리 💙 (깍20, 암브, 공증)",
    "group": "전설 [물딜]",
    "codes": [
      "A30h"
    ],
    "abilities": {
      "방어력 감소": 20,
      "아머브레이크": true,
      "보조딜": true,
      "바제스": true,
      "공격력 증가": 30
    },
    "description": "방어력 감소 : 20\n공격력 증가 : 30\n아머브레이크\n바제스 클리어 가능"
  },
  {
    "sourceId": "unit_1752901441310_3608",
    "sourceName": "쵸파 유력강화 (깍18 공속20)",
    "group": "전설 [물딜]",
    "codes": [
      "K30h"
    ],
    "abilities": {
      "방어력 감소": 18,
      "공격속도 증가": 20
    },
    "description": "방어력 감소 : 18\n공격속도 증가 : 20"
  },
  {
    "sourceId": "B30h",
    "sourceName": "흰수염 💖 (깍15 발동이감) - 흰불 / 검수초",
    "group": "전설 [물딜]",
    "codes": [
      "B30h"
    ],
    "abilities": {
      "방어력 감소": 15,
      "발동이동속도 감소": 35,
      "보조딜": true,
      "바제스": true
    },
    "description": "방어력 감소(방깎): 15\n발동이동속도 감소(발동이감): 35\n보스, 스토리에서 추가 방어력 감소: 30\n단일 대상에게 전체 체력의 5% 대미지\n바제스 클리어 가능"
  },
  {
    "sourceId": "830h",
    "sourceName": "시저 🚁 (깍30)",
    "group": "전설 [물딜]",
    "codes": [
      "830h"
    ],
    "abilities": {
      "방어력 감소": 30,
      "공중이동": "true"
    },
    "description": "방어력 감소(방깎): 30"
  },
  {
    "sourceId": "HA0h",
    "sourceName": "킹 🚁🚩28 (이감10 깍5 발동깍30)",
    "group": "전설 [물딜]",
    "codes": [
      "HA0h"
    ],
    "abilities": {
      "이동속도 감소": 10,
      "방어력 감소": 5,
      "발동방어력 감소": 30,
      "공중이동": "true"
    },
    "description": "이동속도 감소 : 10\n방어력 감소 : 5\n발동방어력 감소 : 30\nX드레이크와 중복 안됨."
  },
  {
    "sourceId": "W30h",
    "sourceName": "베르고 💙🚩33 (암브)",
    "group": "히든 [물딜]",
    "codes": [
      "W30h"
    ],
    "abilities": {
      "아머브레이크": "true"
    },
    "description": "마나스킬(최대방깎 30, 스턴 2.5)\n공성업 할 수록 마나스킬깍 증가"
  },
  {
    "sourceId": "N30h",
    "sourceName": "료쿠규 💙🚩12 (깍25 발동깍15 발동이감20)",
    "group": "히든 [물딜]",
    "codes": [
      "N30h"
    ],
    "abilities": {
      "방어력 감소": 25,
      "발동이동속도 감소": 20,
      "바제스": true,
      "발동방어력 감소": 15
    },
    "description": "방어력 감소(방깎): 25\n나무덩쿨 스킬 발동 확률 8% (스킬 사용 시 발동이동속도 감소 효과)\n발동이동속도 감소(발동이감): 20\n바제스 클리어 가능"
  },
  {
    "sourceId": "M30h",
    "sourceName": "사보 🚩30 (깍20 이감25) - 사보초 / 바제스",
    "group": "히든 [물딜]",
    "codes": [
      "M30h"
    ],
    "abilities": {
      "이동속도 감소": 25,
      "방어력 감소": 20,
      "바제스": true
    },
    "description": "이동속도 감소(이감): 25\n방어력 감소(방깎): 20\n바제스 클리어 가능"
  },
  {
    "sourceId": "340h",
    "sourceName": "미호크 🚩26 (깍25) - 조초 / 미영, 샹초",
    "group": "히든 [물딜]",
    "codes": [
      "340h"
    ],
    "abilities": {
      "방어력 감소": 25
    },
    "description": "방어력 감소(방깎): 25"
  },
  {
    "sourceId": "T30h",
    "sourceName": "레베카 (깍18)",
    "group": "히든 [물딜]",
    "codes": [
      "T30h"
    ],
    "abilities": {
      "방어력 감소": 18
    },
    "description": "방어력 감소(방깎): 18"
  },
  {
    "sourceId": "540h",
    "sourceName": "킬러 (광보잡, 깍12) - 카뱅 / x",
    "group": "히든 [물딜]",
    "codes": [
      "540h"
    ],
    "abilities": {
      "방어력 감소": 12,
      "보스 잡기": true,
      "광폭화": true
    },
    "description": "보스 공격 시 40% 확률로 보스 현재 체력의 0.85%만큼 대미지\n유닛 3개를 동시에 공격"
  },
  {
    "sourceId": "Q20h",
    "sourceName": "라분 🚢🚩13 (0.8스턴, 공속17)",
    "group": "전설 [스턴]",
    "codes": [
      "Q20h"
    ],
    "abilities": {
      "스턴": 0.8,
      "공격속도 증가": 17
    },
    "description": "기본 공격 250 범위 스플래쉬 대미지\n스턴: 0.8\n공격속도 증가(공속): 17\n바다이동"
  },
  {
    "sourceId": "030h",
    "sourceName": "쿠마 💙🚩30 (0.5스턴, w자석)",
    "group": "전설 [스턴]",
    "codes": [
      "030h"
    ],
    "abilities": {
      "스턴": 0.5,
      "순간이동": "true"
    },
    "description": "범위의 적을 자석(W, 쿨타임 60초) 스킬로 자신의 바로 앞으로 이동시키고 스턴 사용\n스턴: 0.5\n1 도킹 + 스킬 사용 시 바제스 클리어 가능"
  },
  {
    "sourceId": "W20h",
    "sourceName": "드래곤 (0.9스턴 이감10 깍10 공속5 공증25)",
    "group": "전설 [스턴]",
    "codes": [
      "W20h"
    ],
    "abilities": {
      "스턴": 0.9,
      "이동속도 감소": 10,
      "방어력 감소": 10,
      "공격속도 증가": 5,
      "공격력 증가": 25
    },
    "description": "스토리 진입 시 스토리 방어력 추가로 45 감소\n스턴: 0.9\n이동속도 감소(이감): 10\n방어력 감소(방깎): 10\n공격속도 증가(공속): 5\n공격력 증가(공증): 25"
  },
  {
    "sourceId": "Z20h",
    "sourceName": "바르톨로메오 (0.9스턴, 깍 12)",
    "group": "전설 [스턴]",
    "codes": [
      "Z20h"
    ],
    "abilities": {
      "스턴": 0.9,
      "방어력 감소": 12
    },
    "description": "스턴: 0.9\n방어력 감소: 12"
  },
  {
    "sourceId": "130h",
    "sourceName": "후지토라 (0.8스턴, 이감25, 마방깍)",
    "group": "전설 [스턴]",
    "codes": [
      "130h"
    ],
    "abilities": {
      "스턴": 0.7,
      "이동속도 감소": 25,
      "마법 방어력 감소": 1
    },
    "description": "스턴: 0.7\n이동속도 감소(이감): 25\n마법 방어력 감소"
  },
  {
    "sourceId": "530h",
    "sourceName": "샹크스 (0.8 스턴)",
    "group": "전설 [스턴]",
    "codes": [
      "530h"
    ],
    "abilities": {
      "스턴": 0.9
    },
    "description": "스턴: 0.9\n주변 750 범위 내의 적들에게 0.2초당 12500 대미지\n접근하는 적에게 전체 체력 3%의 고정 대미지와 0.625초 스턴\n대미지 감소(방깎) 유닛이 20 이상에 `출항` 사용 시 바제스 미션 클리어 가능"
  },
  {
    "sourceId": "930h",
    "sourceName": " 시키 🚁 (1스턴, 암브) - 노업글 / 나초",
    "group": "전설 [스턴]",
    "codes": [
      "930h"
    ],
    "abilities": {
      "스턴": 1,
      "아머브레이크": "true",
      "공중이동": "true"
    },
    "description": "스턴: 1\n공중이동\n아머브레이크"
  },
  {
    "sourceId": "740h",
    "sourceName": "피셔타이거 🚁🚩6 (0.9스턴 공증35)",
    "group": "히든 [스턴]",
    "codes": [
      "740h"
    ],
    "abilities": {
      "스턴": 0.9,
      "공격력 증가": 35,
      "보스 잡기": true,
      "광폭화": true,
      "공중이동": "true"
    },
    "description": "모든 지형 무시 이동\n현재 체력 2.5%의 추가 고정 대미지 (피해량 감소 무시))"
  },
  {
    "sourceId": "140h",
    "sourceName": "아오키지(0.4스턴 이감35 폭뎀증10)",
    "group": "히든 [스턴]",
    "codes": [
      "140h"
    ],
    "abilities": {
      "스턴": 0.4,
      "이동속도 감소": 35,
      "폭발형 대미지 증폭": 10
    },
    "description": ""
  },
  {
    "sourceId": "Y30h",
    "sourceName": "이완코브 (0.6스턴  깍11 단일공증45)",
    "group": "히든 [스턴]",
    "codes": [
      "Y30h"
    ],
    "abilities": {
      "스턴": 0.6,
      "방어력 감소": 11,
      "공격력 증가": 45
    },
    "description": "봉쿠레와 깍 중복적용 안 됨\n홀딩시 범위내 가장 먼 유닛에게 버프"
  },
  {
    "sourceId": "O30h",
    "sourceName": "봉쿠레 (0.3스턴  깍11)",
    "group": "히든 [스턴]",
    "codes": [
      "O30h"
    ],
    "abilities": {
      "스턴": 0.3,
      "방어력 감소": 11
    },
    "description": "이완코브와 깍 중복적용 안 됨"
  },
  {
    "sourceId": "780h",
    "sourceName": "토키 (이감25, 공속20)",
    "group": "전설 [마딜]",
    "codes": [
      "780h"
    ],
    "abilities": {
      "공격속도 증가": 20,
      "이동속도 감소": 25
    },
    "description": "9초마다 450 범위의 전설 이상 랜덤 유닛 2기를 6초 동안 공격속도 35% 증가\n이동속도 감소: 25"
  },
  {
    "sourceId": "R20h",
    "sourceName": "로브 루치 🚩10 (마뎀단일) - 로빈 / 센불",
    "group": "전설 [마딜]",
    "codes": [
      "R20h"
    ],
    "abilities": {
      "단일": 1
    },
    "description": "공격시 12% 확률\n단일 적 현재체력 34% 추가 마법데미지"
  },
  {
    "sourceId": "730h",
    "sourceName": "슈가 🚩19 (마젠 1.25)",
    "group": "전설 [마딜]",
    "codes": [
      "730h"
    ],
    "abilities": {
      "마나 재생": 1.25,
      "유닛삭제": 0
    },
    "description": "삭제 시 적 유닛을 로봇으로 바꿈\n마나 재생: 1.25\n공격 시 단일 적 50% 확률로 0.5초 스턴\n잡동사니 판매"
  },
  {
    "sourceId": "P20h",
    "sourceName": "나미  (마뎀증 발동이감42) - 버영 / 나초",
    "group": "전설 [마딜]",
    "codes": [
      "P20h"
    ],
    "abilities": {
      "마법 대미지 증가": 1,
      "발동이동속도 감소": 42
    },
    "description": "마법 대미지 증가(마증): 1\n발동이동속도 감소(발동이감): 42\n보물보상 2배\n체력 30 충전 시 크리마 텍트 LV2 스킬 발동\n골드 획득량 증가: 60%"
  },
  {
    "sourceId": "330h",
    "sourceName": "레이쥬 🚁 (폭발단일0.7, 이감35) - 가반 / 센불",
    "group": "전설 [마딜]",
    "codes": [
      "330h"
    ],
    "abilities": {
      "단일": 0.7,
      "이동속도 감소": 35,
      "공중이동": "true"
    },
    "description": "이동속도 감소(이감): 35\n공중이동\n핑크 호넷 스킬: 단일 일반 몬스터에게 현재 체력 8.25% 폭발형 대미지\n포이즌 핑크: 단일 일반 몬스터에게 현재 체력 10.5%의 폭발형 대미지\n스토리 타격 시 사거리를 두고 타격해야 강함"
  },
  {
    "sourceId": "C30h",
    "sourceName": "로우 (단일이감99 , 범퍼) - 카뱅 / 류영",
    "group": "전설 [마딜]",
    "codes": [
      "C30h"
    ],
    "abilities": {
      "범위 현재 체력 퍼센트 대미지": true,
      "공격속도 증가": 0,
      "광폭화": true,
      "순간이동": "true",
      "보조딜": "true"
    },
    "description": "범위 현재 체력 퍼센트 대미지(범위현퍼)\n(W) 스킬 사용 시 12초 동안 단일 공격속도 150% 증가(쿨타임 50초)\n단일 적에게 2.5초 동안 이동속도 99% 감소\n광폭화 방어력 버프 제거 후 현재 체력 22.5%의 마법 대미지"
  },
  {
    "sourceId": "430h",
    "sourceName": "상디 🚩26 (마뎀단일) - 시불 / 상초, 노업",
    "group": "전설 [마딜]",
    "codes": [
      "430h"
    ],
    "abilities": {
      "단일": 1
    },
    "description": "매 공격시 단일 적\n현재체력 3.5% 추가 마법데미지"
  },
  {
    "sourceId": "Z90h",
    "sourceName": "네코 💖🚩27 (공증, 전퍼, 보잡, 이감30)",
    "group": "전설 [마딜]",
    "codes": [
      "Z90h"
    ],
    "abilities": {
      "공격력 증가": 32,
      "범위 전체 체력 퍼센트 대미지": 0,
      "이동속도 감소": 30,
      "바제스": true,
      "보스 잡기": true,
      "광폭화": true,
      "보조딜": "true"
    },
    "description": "공격력 증가(공증): 32\n범위 전체 체력 퍼센트 대미지(범위전퍼)\n이동속도 감소: 30\n스킬 발동 시 대상이 광폭화 유닛이면 전체 체력 9%의 추가 대미지\n보스 잡기에 특화됨\n바제스 클리어 가능"
  },
  {
    "sourceId": "X20h",
    "sourceName": "블랙마리아🚩24 (폭발딜러)  - 알비다 / 왜곡",
    "group": "전설 [마딜]",
    "codes": [
      "X20h"
    ],
    "abilities": {
      "방어력 무시 대미지": true,
      "보조딜": "true"
    },
    "description": "단일스턴\n기본 공격 시 375범위 현재 공격력의 50% 폭발형 데미지\n오른손은 황천길 스킬\n매 13번째 공격시\n500범위 현재 공격력의 5배 + 전체체력 0.9% 추가 폭발형데미지"
  },
  {
    "sourceId": "Y20h",
    "sourceName": "루나메 🚁🚩5 (광보잡) - 레불 / 뱀초",
    "group": "전설 [마딜]",
    "codes": [
      "Y20h"
    ],
    "abilities": {
      "보스 잡기": true,
      "바제스": true,
      "광폭화": true,
      "공중이동": "true"
    },
    "description": "단일스턴\n공중이동\n보스, 광폭화에게 전체 체력 0.5%의 추가 고정 대미지\n바제스 클리어 가능"
  },
  {
    "sourceId": "E30h",
    "sourceName": "코비 💙 (폭발단일/발동 마체젠+공증)",
    "group": "전설 [마딜]",
    "codes": [
      "E30h"
    ],
    "abilities": {
      "단일": 1
    },
    "description": "115번 공격 시 900 범위 유닛에게 5초 동안 용기의 외침 스킬 시전\n용기의 외침: 공격력 20% 증가, 체력 및 마나 회복 1 증가\n단일 일반 라인 몬스터에게 12.5% 확률로 현재 체력의 25% 폭발형 대미지"
  },
  {
    "sourceId": "N20h",
    "sourceName": "모리아 (이감30 폭발딜러 삭제)",
    "group": "전설 [마딜]",
    "codes": [
      "N20h"
    ],
    "abilities": {
      "이동속도 감소": 30,
      "방어력 무시 대미지": true,
      "유닛삭제": true
    },
    "description": "이동속도 감소(이감): 30\n기본 공격 시 375 범위 현재 공격력의 50% 폭발형 데미지\n장군좀비 공격시 375범위 적들에게 현재 공격력의 50% 폭발형 데미지"
  },
  {
    "sourceId": "S20h",
    "sourceName": "조로 🚩9 (처형) - 조초, 바초 / 노업",
    "group": "전설 [마딜]",
    "codes": [
      "S20h"
    ],
    "abilities": {
      "끝딜": 1,
      "바제스": true,
      "보스 잡기": "true"
    },
    "description": "체력15% 이하일시 삭제.\n바제스 클리어 가능"
  },
  {
    "sourceId": "230h",
    "sourceName": "보아 핸콕 🚩31 (폭발끝딜) - 헨영, 베펑 / 세라핌",
    "group": "전설 [마딜]",
    "codes": [
      "230h"
    ],
    "abilities": {
      "끝딜": 1
    },
    "description": "피스톨 키스 발동 시 매 공격마다 단일 적에게 전체 체력의 7% 폭발형 대미지"
  },
  {
    "sourceId": "240h",
    "sourceName": "시노부 🚩23 (폭발끝딜 탐색)",
    "group": "전설 [마딜]",
    "codes": [
      "240h"
    ],
    "abilities": {
      "보스 잡기": true,
      "끝딜": 1
    },
    "description": "보물 탐색(R 스킬)\n공격시 15% 확률\n전체체력 7% 폭발형 데미지"
  },
  {
    "sourceId": "I30h",
    "sourceName": "제파 🚩29 (광보잡)",
    "group": "전설 [마딜]",
    "codes": [
      "I30h"
    ],
    "abilities": {
      "보스 잡기": true,
      "광폭화": true
    },
    "description": "광폭화 유닛일 시 전체 체력 12%의 추가 고정 대미지"
  },
  {
    "sourceId": "U20h",
    "sourceName": "검은수염 💖🚩7 (마증8+10) - 킹제 / 검수초",
    "group": "전설 [마딜]",
    "codes": [
      "U20h"
    ],
    "abilities": {
      "단일마법 대미지 증가": 8,
      "모든피해증가": 10,
      "광폭화": true
    },
    "description": "모든피해증가: 10\n단일마법 대미지 증가(단일마댐증폭): 8\n범위 적 현재 체력 % 대미지(범위현퍼): 1%\n광폭화 방어력 버프 제거 후 400만 마법 대미지"
  },
  {
    "sourceId": "unit_1762914021345_2964",
    "sourceName": "샬롯 브륄레(전설선택권, 특포1필요)",
    "group": "전설 [마딜]",
    "codes": [
      "S80h"
    ],
    "abilities": {},
    "description": "특성 포인트 1을 소모하여 전설위습 교환 가능"
  },
  {
    "sourceId": "Z30h",
    "sourceName": "아카이누 💖🚩15 (광보잡) - 크제 / 아카초, 노업",
    "group": "히든 [마딜]",
    "codes": [
      "Z30h"
    ],
    "abilities": {
      "바제스": true,
      "보스 잡기": true,
      "광폭화": true
    },
    "description": "광폭화 추가 대미지\n단일 스턴 2.5초\n바제스 클리어 가능\n체력스킬에 마방깍10% 추가"
  },
  {
    "sourceId": "J30h",
    "sourceName": "시류 💙🚩22 (마뎀끝딜) - x / 류영",
    "group": "히든 [마딜]",
    "codes": [
      "J30h"
    ],
    "abilities": {
      "끝딜": 1,
      "보조딜": "true"
    },
    "description": "공격시 14% 확률\n단일 적 전체체력 8% 추가 마법데미지\n마나스킬\n범위600 225만 마법데미지 + 전체체력 1.75% 추가 마법데미지"
  },
  {
    "sourceId": "640h",
    "sourceName": "키쿠 💙 (보잡)  // 후초",
    "group": "히든 [마딜]",
    "codes": [
      "640h"
    ],
    "abilities": {
      "보스 잡기": true,
      "광폭화": "true"
    },
    "description": "20만의 마법 대미지\n단일 적 10만의 추가 마법 대미지\n보스에게 현재 체력 1%의 추가 마법 대미지\n광폭화 유닛에게 전체 체력 5%의 추가 마법 대미지"
  },
  {
    "sourceId": "550h",
    "sourceName": "스튜시 (폭발단일 / 블링크) // 베초, 보초",
    "group": "히든 [마딜]",
    "codes": [
      "550h"
    ],
    "abilities": {
      "단일": 1,
      "순간이동": "true"
    },
    "description": "1500 범위까지 순간이동\n공격시 14% 확률\n단일 적 현재체력 25% 폭발형 데미지"
  },
  {
    "sourceId": "L70h",
    "sourceName": "캐럿 (0.5단일, 마뎀증)",
    "group": "히든 [마딜]",
    "codes": [
      "L70h"
    ],
    "abilities": {
      "마법 대미지 증가": 1,
      "단일": 0.6
    },
    "description": "공격시 15% 확률\n425 범위에 마법데미지 1%  증폭\n공격시 1% 확률\n단일 적 현재체력 28% 마법데미지"
  },
  {
    "sourceId": "440h",
    "sourceName": "류마 (0.5단일) - x / 류영",
    "group": "히든 [마딜]",
    "codes": [
      "440h"
    ],
    "abilities": {
      "단일": 0.5
    },
    "description": "일반 몬스터에게 단일 현재 체력 20%의 추가 폭발형 대미지 및 0.4초 스턴\n조로초월 강화 재료"
  },
  {
    "sourceId": "A90H",
    "sourceName": "(S)징베 💙🚁✚ (공속20 마젠3 암브 발동이감50)",
    "group": "초월 [물딜]",
    "codes": [
      "A90H"
    ],
    "abilities": {
      "아머브레이크": 0,
      "발동이동속도 감소": 50,
      "공격속도 증가": 20,
      "마나 재생": 3,
      "바제스": true
    },
    "description": "스플딜러 바다이동\n암브 1당 스킬뎀 1% 증가\n발동이감(불안함)\n\n[추천조합 : 스모커 전설, 퀸, 베르고]"
  },
  {
    "sourceId": "LB0H",
    "sourceName": "(S)료쿠규 💙✚ (깍35 발동깍15 발동이감30)",
    "group": "초월 [물딜]",
    "codes": [
      "LB0H"
    ],
    "abilities": {
      "방어력 감소": 35,
      "발동방어력 감소": 15,
      "발동이동속도 감소": 25,
      "바제스": true
    },
    "description": "발동이감 믿을 수 있음\n*특성강화 '수목장' 스킬\n특성강화하면 암브(아머브레이크) 유닛과 조합이 좋음"
  },
  {
    "sourceId": "890H",
    "sourceName": "(A)로빈 💖✚ (1.2스턴  깍45 공증)",
    "group": "초월 [물딜]",
    "codes": [
      "890H"
    ],
    "abilities": {
      "스턴": 1.2,
      "방어력 감소": 45,
      "공격력 증가": 25,
      "바제스": true
    },
    "description": "스킬 딜러\n보조딜러 필수(예: 전설 마르코, 흰수염, 센고쿠)\n[추천 조합 : 카타쿠리 제한됨, 모비딕호 히든]"
  },
  {
    "sourceId": "DB0H",
    "sourceName": "(C)야마토 💖✚ (이감-10 깍25)",
    "group": "초월 [물딜]",
    "codes": [
      "DB0H"
    ],
    "abilities": {
      "이동속도 감소": -10,
      "방어력 감소": 25,
      "바제스": true
    },
    "description": "적 이감+10 / 스플딜러\n이감이 높을 수록 딜 증가\n\n[추천 조합 : 이완 히든, 모비딕호]"
  },
  {
    "sourceId": "490H",
    "sourceName": "(C)바질호킨스 🤍✚ (깍32 점치기)",
    "group": "초월 [물딜]",
    "codes": [
      "490H"
    ],
    "abilities": {
      "방어력 감소": 32,
      "이동속도 감소": 7,
      "바제스": "ture"
    },
    "description": "점 치기, 스킬딜러\n디버프 개수 비례 딜증가\n방어력감소  : 25\n특성강화 : 방깍7, 이감7"
  },
  {
    "sourceId": "B90H",
    "sourceName": "(S)우솝 💙✚ (깍30 광보잡)",
    "group": "초월 [물딜]",
    "codes": [
      "B90H"
    ],
    "abilities": {
      "방어력 감소": 30,
      "보스 잡기": true,
      "바제스": true,
      "광폭화": true
    },
    "description": "팀원 전체 방어력 8 감소\n스플딜러/보스잡기 전문\n\n특강해서 해바라기로 목재 수급하다가 완두콩으로 교체\n\n특성강화 시 식물 생성\n완두콩 - 스플뎀\n백설콩 - 스턴\n해바라기 - 목재생성\n먹깨비 - 삭제"
  },
  {
    "sourceId": "990H",
    "sourceName": "(D)루피 💙✚ (0.3스턴 발동이감33)",
    "group": "초월 [물딜]",
    "codes": [
      "990H"
    ],
    "abilities": {
      "스턴": 0.3,
      "발동이동속도 감소": 33,
      "아머브레이크": 0,
      "바제스": true
    },
    "description": "스플딜러\n발동이감(믿음직)\n딜은 쎈데 방깎이 없어서 깍짜기 힘듦\n[추천 조합 : 많은 방깍]"
  },
  {
    "sourceId": "X80H",
    "sourceName": "(B)후지토라 💙✚ (1.1스턴 이감55 암브)",
    "group": "초월 [물딜]",
    "codes": [
      "X80H"
    ],
    "abilities": {
      "스턴": 1.1,
      "이동속도 감소": 55,
      "아머브레이크": 0,
      "바제스": true
    },
    "description": "스플딜러 이감 스턴 암브\n특강하면 암브 유닛 필요함\n[추천조합 : 베르고, 퀸]\n"
  },
  {
    "sourceId": "290H",
    "sourceName": "(B)사보 🤍✚ (0.2스턴 이감35 깍30)",
    "group": "초월 [물딜]",
    "codes": [
      "290H"
    ],
    "abilities": {
      "스턴": 0.2,
      "이동속도 감소": 35,
      "방어력 감소": 30,
      "발동이동속도 감소": 30,
      "바제스": true,
      "보스 잡기": "true"
    },
    "description": "*해적선 필요\n스플딜러\n버프 개수 비례 딜 증가\n\n[추천 조합 : 버기 영원]"
  },
  {
    "sourceId": "E90H",
    "sourceName": "(B)도플라밍고 💙🚁✚ (발동이감 발동깍60)",
    "group": "초월 [물딜]",
    "codes": [
      "E90H"
    ],
    "abilities": {
      "발동이동속도 감소": 75,
      "발동방어력 감소": 60,
      "바제스": true,
      "공중이동": "true",
      "범위 현재 체력 퍼센트 대미지": "true"
    },
    "description": "스플딜러 발동이감\n마나스킬 60깍\n얘 하나로 이감 해결됨\n단, 발동이감이라 변수 있음\n[알비다와 어울리지만 원딜이 더 좋음]"
  },
  {
    "sourceId": "190H",
    "sourceName": "(S)쵸파 💙✚ (깍50 공속30 공증100)",
    "group": "초월 [물딜]",
    "codes": [
      "190H"
    ],
    "abilities": {
      "방어력 감소": 50,
      "공격속도 증가": 30,
      "공격력 증가": 100,
      "바제스": true
    },
    "description": "공증100 공속 버퍼\n특강하면 공증50 없어지고 스플딜러\n\n스플 딜러랑 조합이 좋음\n보잡 능력 없어서 보잡 필수"
  },
  {
    "sourceId": "unit_1747756917990_920",
    "sourceName": "(S)쵸파 몬스터포인트 (깍50 공속30 공증50)",
    "group": "초월 [물딜]",
    "codes": [
      "190H"
    ],
    "abilities": {
      "방어력 감소": 50,
      "공격속도 증가": 30,
      "공격력 증가": 50
    },
    "description": "공증 공속 버퍼 / 스플딜러\n\n스플 딜러랑 조합이 좋음\n보잡 능력 없어서 보잡 필수"
  },
  {
    "sourceId": "F90H",
    "sourceName": "(A)조로 💙✚ (0.4스턴 이감30 깍30)",
    "group": "초월 [물딜]",
    "codes": [
      "F90H"
    ],
    "abilities": {
      "스턴": 0.4,
      "이동속도 감소": 30,
      "방어력 감소": 30,
      "바제스": true
    },
    "description": "스킬딜러 이감 약한스턴\n아이템 '슈스이'로 강화 가능\n[추천 조합 : 크로커 제한, 봉쿠레 히든, 로져]"
  },
  {
    "sourceId": "unit_1767356628978_5789",
    "sourceName": "(A)조로 염왕💙(0.4스턴 이감42 깍42)",
    "group": "초월 [물딜]",
    "codes": [
      "TB0H"
    ],
    "abilities": {
      "방어력 감소": 42,
      "이동속도 감소": 42,
      "스턴": 0.4,
      "바제스": "true"
    },
    "description": ""
  },
  {
    "sourceId": "unit_1779054071704_519",
    "sourceName": "(A)베가펑크 💙✚ (공증40 공속40 이감40)",
    "group": "초월 [물딜]",
    "codes": [
      "AA0H"
    ],
    "abilities": {
      "공격력 증가": 40,
      "공격속도 증가": 40,
      "이동속도 감소": 40
    },
    "description": "*특성포인트 6개 있어야 제값함. 소환딜러\n물딜/마딜 둘 다 가능\n특강 : 요크(스턴)\n물딜 : 릴리스(보잡, 방깎)\n마딜 : 아틀라스(방무뎀)\n14스토리 깨면 그린블러드 획득"
  },
  {
    "sourceId": "unit_1779015720197_7602",
    "sourceName": "(A)베가펑크 💙✚ (공증40 공속40 이감40)",
    "group": "초월 [물딜]",
    "codes": [
      "AA0H"
    ],
    "abilities": {
      "공격력 증가": 40,
      "공격속도 증가": 40,
      "이동속도 감소": 40
    },
    "description": "*특성포인트 6개 있어야 제값함. 소환딜러\n물딜/마딜 둘 다 가능\n특강 : 요크(스턴)\n물딜 : 릴리스(보잡, 방깎)\n마딜 : 아틀라스(방무뎀)\n14스토리 깨면 그린블러드 획득"
  },
  {
    "sourceId": "unit_1779054276300_5909",
    "sourceName": "(A)베가펑크 💙✚ (공증40 공속40 이감40)",
    "group": "초월 [물딜]",
    "codes": [
      "AA0H"
    ],
    "abilities": {
      "공격력 증가": 40,
      "공격속도 증가": 40,
      "이동속도 감소": 40
    },
    "description": "*특성포인트 6개 있어야 제값함. 소환딜러\n물딜/마딜 둘 다 가능\n특강 : 요크(스턴)\n물딜 : 릴리스(보잡, 방깎)\n마딜 : 아틀라스(방무뎀)\n14스토리 깨면 그린블러드 획득"
  },
  {
    "sourceId": "unit_1779054200606_9136",
    "sourceName": "(A)베가펑크 💙✚ (공증40 공속40 이감40)",
    "group": "초월 [물딜]",
    "codes": [
      "AA0H"
    ],
    "abilities": {
      "공격력 증가": 40,
      "공격속도 증가": 40,
      "이동속도 감소": 40
    },
    "description": "*특성포인트 6개 있어야 제값함. 소환딜러\n물딜/마딜 둘 다 가능\n특강 : 요크(스턴)\n물딜 : 릴리스(보잡, 방깎)\n마딜 : 아틀라스(방무뎀)\n14스토리 깨면 그린블러드 획득"
  },
  {
    "sourceId": "XB0H",
    "sourceName": "(F)보니 🤍✚ (발동깍40 광잡)",
    "group": "초월 [물딜]",
    "codes": [
      "XB0H"
    ],
    "abilities": {
      "발동방어력 감소": 40,
      "바제스": true
    },
    "description": "*뉴비 비추천\n'나이조작' 디버프\n변신했을 때 강함\n\n특강을 하면 마나를 더 많이 쓰고 딜이 쎄짐\n특강 안 하면 변신을 오래해서 특강 안 해도 괜찮음\n\n마딜로 쓰기 더 좋음\n마나스킬 변신하면 단일\n\n[추천조합 : 코알라+키쿠+ 방주맥심, 에넬 제한]\n"
  },
  {
    "sourceId": "unit_1779054466128_8565",
    "sourceName": "릴리스 💙 (광보잡, 깍40)",
    "group": "초월 [물딜]",
    "codes": [
      "BA0H"
    ],
    "abilities": {
      "방어력 감소": 40,
      "보스 잡기": "true",
      "광폭화": "true"
    },
    "description": "베가펑크 특포 소환"
  },
  {
    "sourceId": "unit_1779054378124_5918",
    "sourceName": "요크 💙 (1.3스턴)",
    "group": "초월 [물딜]",
    "codes": [
      "MA0H"
    ],
    "abilities": {
      "스턴": 1.3
    },
    "description": "베가펑크 특성강화"
  },
  {
    "sourceId": "unit_1779054533261_4748",
    "sourceName": "아틀라스 💙 (방무뎀)",
    "group": "초월 [물딜]",
    "codes": [
      "EA0H"
    ],
    "abilities": {
      "방어력 무시 대미지": "true"
    },
    "description": ""
  },
  {
    "sourceId": "A40h",
    "sourceName": "(B)흰수염 💖✚ (0.5스턴 깍45 발동이감60)",
    "group": "불멸 [물딜]",
    "codes": [
      "A40h"
    ],
    "abilities": {
      "스턴": 0.5,
      "방어력 감소": 45,
      "발동이동속도 감소": 60,
      "바제스": true
    },
    "description": "스플딜러 약한스턴\n발동이감(못믿음)\n\n[추천조합 : 쵸파 초월, 버기 영원]"
  },
  {
    "sourceId": "J40h",
    "sourceName": "(A)로져 💙✚ (이감50 깍60 공증60 광잡)",
    "group": "불멸 [물딜]",
    "codes": [
      "J40h"
    ],
    "abilities": {
      "이동속도 감소": 50,
      "방어력 감소": 60,
      "공격력 증가": 60,
      "바제스": true,
      "광폭화": true
    },
    "description": "이동속도 감소(이감): 50\n방어력 감소(방깎): 60\n공격력 증가(공증): 60\n바제스 가능\n특성강화 시 보스잡기 능력 추가"
  },
  {
    "sourceId": "M70h",
    "sourceName": "(A)카이도 💖(이감60 공증-75 깍30 중첩)",
    "group": "불멸 [물딜]",
    "codes": [
      "M70h",
      "DA0h"
    ],
    "abilities": {
      "이동속도 감소": 60,
      "중첩방어력 감소": 30,
      "바제스": "true",
      "공격력 증가": -75,
      "공중이동": "true"
    },
    "description": "스플딜러 아군공격력감소\n중첩방깎\n공속이 느려서 공속 챙기면 좋음\n[추천조합 : 쵸파 초월, 알비다 ]"
  },
  {
    "sourceId": "unit_1767886180546_6011",
    "sourceName": "(A)카이도(용폼) 🚁💖(이감60 공증-75 깍30 중첩)",
    "group": "불멸 [물딜]",
    "codes": [
      "DA0h"
    ],
    "abilities": {
      "이동속도 감소": 60,
      "중첩방어력 감소": 30,
      "공격력 증가": -75,
      "바제스": "ture"
    },
    "description": ""
  },
  {
    "sourceId": "C40h",
    "sourceName": "(C)거프 💙✚ (1.2스턴  깍-15)",
    "group": "불멸 [물딜]",
    "codes": [
      "C40h"
    ],
    "abilities": {
      "스턴": 1.2,
      "방어력 감소": -15,
      "보스 잡기": true,
      "바제스": "true"
    },
    "description": "마나스킬(범위전퍼 5)\n바제스 클리어 가능\n범위딜러\n버프 개수 비례 딜 증가"
  },
  {
    "sourceId": "unit_1761060002112_2027",
    "sourceName": "(C)불릿 💙 (깍46 이감20 암브)",
    "group": "불멸 [물딜]",
    "codes": [
      "180h"
    ],
    "abilities": {
      "아머브레이크": "ture",
      "이동속도 감소": 20,
      "방어력 감소": 40
    },
    "description": "흔함 유닛을 써서 강화\n스플딜러\n방어력 감소 강화 우선\n방어력 감소 : 레전더리\n공격력, 공속 : 유니크\n필수\n보스 못 잡으니 보잡 유닛 필요함"
  },
  {
    "sourceId": "940h",
    "sourceName": "(D)레일리 💙✚ (깍20 공속45 암브 흡수)",
    "group": "불멸 [물딜]",
    "codes": [
      "940h"
    ],
    "abilities": {
      "방어력 감소": 25,
      "공격속도 증가": 45,
      "아머브레이크": "true",
      "바제스": true,
      "유닛삭제": "true"
    },
    "description": "*레일리 희귀함 필요*\n스플딜러, 공속버프, 삭제\n\n유닛 흡수해서 생기는 노획물을 판매하면 랜덤 위습이 생김. 유닛을 더 만들 수 있음\n보잡, 이감, 스턴이 없고 깍도 낮아서 뉴비에게 비추천"
  },
  {
    "sourceId": "F40h",
    "sourceName": "(D)스코퍼가반 💙✚ (단일깍60 광보잡)",
    "group": "불멸 [물딜]",
    "codes": [
      "F40h"
    ],
    "abilities": {
      "단일방어력 감소": 60,
      "보스 잡기": true,
      "광폭화": true,
      "바제스": "true"
    },
    "description": "단일방어력감소: 60\n바제스 클리어 가능\n단일딜러\n특성강화하면 블링크 생김\n보스, 스토리, 폐문에 좋음\n[추천 조합 : 쵸파 초월, 이감과 스턴 없이 풀방깍]"
  },
  {
    "sourceId": "I70h",
    "sourceName": "(A)카타쿠리 💖✚ (깍30 체젠2.85)",
    "group": "제한됨 [물딜]",
    "codes": [
      "I70h"
    ],
    "abilities": {
      "방어력 감소": 30,
      "체력 재생": 2.85,
      "보스 잡기": true,
      "광폭화": true,
      "바제스": "true"
    },
    "description": "스킬딜러 체젠버프\n스토리좋음 약한보잡\n\n\n[추천조합 : 모비딕호, 샤크 세라핌, 로빈초월]\n"
  },
  {
    "sourceId": "Q80h",
    "sourceName": "(B)알비다 💙 (깍25,암브,넉백,공증)",
    "group": "제한됨 [물딜]",
    "codes": [
      "Q80h"
    ],
    "abilities": {
      "방어력 감소": 25,
      "아머브레이크": 1,
      "공격력 증가": 33,
      "바제스": true,
      "범위 잃은 체력 퍼센트 대미지": "true"
    },
    "description": "*조합에 특성포인트 4개 필요\n넉백 스킬보조딜러\n풀이감일 때 혼자 스턴 역할 가능(위치 선정 중요)\n[추천 조합 : 징베 초월, 킹 제한, 로져, 도플라밍고 초월 ]\n"
  },
  {
    "sourceId": "F50h",
    "sourceName": "(A)크로커다일 💖✚ (0.5스턴 이감40 깍25)",
    "group": "제한됨 [물딜]",
    "codes": [
      "F50h"
    ],
    "abilities": {
      "스턴": 0.5,
      "이동속도 감소": 40,
      "방어력 감소": 25,
      "바제스": true
    },
    "description": "약한스킬딜러\n이감, 스턴, 방깎을 챙겨줘서 물딜에서 두루두루 쓰기 좋음\n"
  },
  {
    "sourceId": "IA0h",
    "sourceName": "(D)킹 💙🚁✚ (발동깍35, 암브)",
    "group": "제한됨 [물딜]",
    "codes": [
      "IA0h"
    ],
    "abilities": {
      "발동방어력 감소": 34,
      "바제스": true,
      "아머브레이크": 0.1,
      "공중이동": "true"
    },
    "description": "스플딜러 발동방깎\n*암브 필수\n아머브레이크가 쌓여야 쎄기 때문에 암브 필수\n뉴비에게 비추천\n[추천조합 : 징베 초월, 베르고, 스모커 전설]"
  },
  {
    "sourceId": "I50h",
    "sourceName": "(D)레베카 💙✚ (깍38 발동이감50)",
    "group": "제한됨 [물딜]",
    "codes": [
      "I50h"
    ],
    "abilities": {
      "방어력 감소": 38,
      "발동이동속도 감소": 50,
      "바제스": true
    },
    "description": "방어력감소: 38\n발동이감: 50 (없는수준)\n바제스 가능\n[매우 약해서 뉴비에게 추천 안 함]"
  },
  {
    "sourceId": "unit_1745689336668_9114",
    "sourceName": "(D)마르코 불사조폼 💖🚁✚ (스플딜 이감60+체젠)",
    "group": "제한됨 [물딜]",
    "codes": [
      "Q90h"
    ],
    "abilities": {
      "이동속도 감소": 60,
      "체력 재생": 4,
      "바제스": "true",
      "공중이동": "true"
    },
    "description": "*아이템 '불사조의 깃털' 필요\n불사조폼[물딜]\n이감45(특강60)\n체젠오라 스플딜러\n딜은 쎈데 방깎이 없어서 조합 맞추기 힘들기 때문에 뉴비에게 추천 안 함\n\n[추천조합 : 로빈초월, 카타쿠리, 모비딕호]"
  },
  {
    "sourceId": "B50h",
    "sourceName": "(B)카벤딧슈 💙✚  (0.9스턴 깍35 스플)",
    "group": "영원 [물딜]",
    "codes": [
      "B50h"
    ],
    "abilities": {
      "스턴": 0.9,
      "방어력 감소": 35,
      "아머브레이크": 0.1,
      "바제스": "true",
      "보스 잡기": true
    },
    "description": "스플딜러 단일암브\n보잡능력 있음\n"
  },
  {
    "sourceId": "A50h",
    "sourceName": "(D)버기 (0.4스턴 이감25 깍30 공속65 공증75)",
    "group": "영원 [물딜]",
    "codes": [
      "A50h"
    ],
    "abilities": {
      "스턴": 0.5,
      "방어력 감소": 41,
      "공격속도 증가": 65,
      "공격력 증가": 78,
      "이동속도 감소": 25,
      "바제스": true
    },
    "description": "소환딜러 버퍼\n공증, 공속 버프\n스플딜러랑 잘 어울림\n\n매우 비싸서 만들기 힘들기 때문에 뉴비에게 추천 안 함"
  },
  {
    "sourceId": "KB0H",
    "sourceName": "(B)니카(루초) 🤍🚁 (1스턴 깍35 공속35)",
    "group": "영원 [물딜]",
    "codes": [
      "KB0H"
    ],
    "abilities": {
      "스턴": 1,
      "방어력 감소": 35,
      "공격속도 증가": 25,
      "바제스": true,
      "공중이동": "true"
    },
    "description": "스플딜러 보스잡기\n단일스턴 범위스턴\n매우 강력해서 방깍이 낮아도 가능함\n매우 비쌈\n적 이속이 빠를수록 쎄짐"
  },
  {
    "sourceId": "KB0H_",
    "sourceName": "(B)니카(뱀초) 🤍🚁 (1스턴 깍35 공속35)",
    "group": "영원 [물딜]",
    "codes": [
      "KB0H_"
    ],
    "abilities": {
      "스턴": 1,
      "방어력 감소": 35,
      "공격속도 증가": 25,
      "바제스": true,
      "공중이동": "true"
    },
    "description": "스플딜러 보스잡기\n단일스턴 범위스턴\n매우 강력해서 방깍이 낮아도 가능함\n매우 비쌈\n적 이속이 빠를수록 쎄짐"
  },
  {
    "sourceId": "V80H",
    "sourceName": "(S)나미 ✚ (발동이감45, 라인딜)",
    "group": "초월 [마딜]",
    "codes": [
      "V80H"
    ],
    "abilities": {
      "발동이동속도 감소": 45,
      "범위 전체 체력 퍼센트 대미지": 0,
      "바제스": "ture"
    },
    "description": "골드획득량 증가\n탐색 보상 증가\n마뎀깡딜러\n발동이감(믿음직)"
  },
  {
    "sourceId": "U80H",
    "sourceName": "(B)시라호시 💙🚁✚ (1.3스턴)",
    "group": "초월 [마딜]",
    "codes": [
      "U80H"
    ],
    "abilities": {
      "스턴": 1.3,
      "바제스": true,
      "공중이동": "true"
    },
    "description": "스턴 잃퍼뎀\n특강하려면 30라 안으로 빨리 뽑아야함(성장형)\n기록지침 항해목표를 깨면 경험치 얻음(미리 깨도 됨)\n"
  },
  {
    "sourceId": "590H",
    "sourceName": "(S)아카이누 🤍✚ (발동이감 광보잡)",
    "group": "초월 [마딜]",
    "codes": [
      "590H"
    ],
    "abilities": {
      "발동이동속도 감소": 12,
      "보스 잡기": true,
      "바제스": true,
      "광폭화": true
    },
    "description": "*뉴비 추천\n라인딜 광보잡"
  },
  {
    "sourceId": "N50H",
    "sourceName": "(S)타시기 💙✚ (암브, 물마가능)",
    "group": "초월 [마딜]",
    "codes": [
      "N50H"
    ],
    "abilities": {
      "아머브레이크": 1,
      "보스 잡기": "true",
      "바제스": "true"
    },
    "description": "물딜/마딜 가능\n라인딜 보잡\n[추천조합(마딜) : 코알라, 베어 세라핌]\n\n*A급 물딜\n스킬딜러 암브 보잡"
  },
  {
    "sourceId": "H90H",
    "sourceName": "(S)상디 💙🚁✚(마뎀단일, 발동이감50)",
    "group": "초월 [마딜]",
    "codes": [
      "H90H",
      "G90H"
    ],
    "abilities": {
      "단일": 1,
      "발동이동속도 감소": 50,
      "바제스": true,
      "공중이동": "true"
    },
    "description": "단일 라인딜 공중이동\n단일 안 쳐도 라인딜 좋은 편\n특강 해주는 걸 추천\n특강하면 제르마로 변함"
  },
  {
    "sourceId": "unit_1767886116631_3690",
    "sourceName": "(S)상디 제르마(공속15/단일/발동이감50)",
    "group": "초월 [마딜]",
    "codes": [
      "G90H"
    ],
    "abilities": {
      "단일": 1,
      "발동이동속도 감소": 55,
      "공격속도 증가": 15,
      "바제스": "ture"
    },
    "description": "매 공격시 단일 적\n현재체력 3.7% 추가 마법데미지\n공격시 5% 확률\n범위 500 80만 마법데미지 + 3.5초동안 이감50%\n마나 115\n600범위 333만 마법데미지"
  },
  {
    "sourceId": "2B0H",
    "sourceName": "(A)뱀초 💙🚁✚ (방무뎀, 광보잡)",
    "group": "초월 [마딜]",
    "codes": [
      "2B0H"
    ],
    "abilities": {
      "방어력 무시 대미지": 0,
      "바제스": true,
      "보스 잡기": "true",
      "광폭화": "true",
      "공중이동": "true"
    },
    "description": "적 이동속도가 높을 수록 스킬 대미지 증가(이감 있어도 강함)\n광보잡(특성강화)\n\n[추천조합 : 센고쿠 불멸]"
  },
  {
    "sourceId": "Y80H",
    "sourceName": "(B)프랑키 💙✚ (마젠5+써니호)",
    "group": "초월 [마딜]",
    "codes": [
      "Y80H"
    ],
    "abilities": {
      "마나 재생": 5,
      "바제스": true,
      "범위 전체 체력 퍼센트 대미지": "true"
    },
    "description": "써니호 마나 조절 필요(써니호 2대가 번갈아가면서 마나 스킬 사용하게 조절)\n마나스킬딜러 마나리젠\n\n[추천조합 : 코알라, 방주맥심, 에넬 제한, 반더데켄, 키쿠, 시류]"
  },
  {
    "sourceId": "Z80H",
    "sourceName": "(B)샹크스 💙✚ (2.1스턴)",
    "group": "초월 [마딜]",
    "codes": [
      "Z80H"
    ],
    "abilities": {
      "스턴": 2.1,
      "바제스": true
    },
    "description": "라인딜, 우수한 스턴\n특강(보잡)\n단일을 칠 거면 검은수염 전설 필요함\n[추천조합 : 에이스 영원, 빅맘, 에넬 제한]"
  },
  {
    "sourceId": "I90H",
    "sourceName": "(A)브룩 💙🚁✚ (끝딜, 이감20, 마방깍3)",
    "group": "초월 [마딜]",
    "codes": [
      "I90H"
    ],
    "abilities": {
      "이동속도 감소": 15,
      "마법 방어력 감소": 3,
      "끝딜": 1.5,
      "바제스": true,
      "공중이동": "true"
    },
    "description": "끝딜 마뎀증\n이감15+맵전체이감5\n특상 시 미니라분 소환\n미니라분 : 공속 이감12"
  },
  {
    "sourceId": "5B0H",
    "sourceName": "(S)키자루 🚩💙✚ (1스턴 블링크)",
    "group": "초월 [마딜]",
    "codes": [
      "5B0H"
    ],
    "abilities": {
      "스턴": 1,
      "바제스": true,
      "순간이동": "true"
    },
    "description": "*레일리 희귀함 필요\n라인딜 스턴 보잡\n특포를 사용해서 성장함\n[추천조합 : 항법-특성공학]"
  },
  {
    "sourceId": "OC0H",
    "sourceName": "(A)코비 💖🚁✚ (단일)",
    "group": "초월 [마딜]",
    "codes": [
      "OC0H"
    ],
    "abilities": {
      "공격속도 증가": 10,
      "방어력 무시 대미지": true,
      "단일": 1,
      "바제스": "true",
      "공중이동": "true",
      "순간이동": "true"
    },
    "description": "*성장형이라 일찍 뽑아야함\n라인딜 방무딜\n버프 개수 비례 딜증가\n[추천조합 : 네코, 드래곤 전설, 피셔, 토키, 모비딕]\n*마나 리젠 버프는 안 받음"
  },
  {
    "sourceId": "4B0H",
    "sourceName": "(B)키드 💖✚ (이감33)",
    "group": "초월 [마딜]",
    "codes": [
      "4B0H"
    ],
    "abilities": {
      "이동속도 감소": 33,
      "바제스": true,
      "스턴": 0.3
    },
    "description": "라인딜 이감 체력스킬\n체젠 있으면 1스턴됨\n\n[추천조합 : 모비딕호 ]"
  },
  {
    "sourceId": "090H",
    "sourceName": "(F)검은수염 💖✚ (발동이감65, 피증25)",
    "group": "초월 [마딜]",
    "codes": [
      "090H"
    ],
    "abilities": {
      "발동이동속도 감소": 65,
      "모든피해증가": 25,
      "단일마법 대미지 증가": 10,
      "바제스": true,
      "광폭화": true
    },
    "description": "*뉴비 절대 비추천\n한마리씩 끌어당겨서 단일 유닛이랑 같이 공격"
  },
  {
    "sourceId": "W80H",
    "sourceName": "(B)루치 ✚ (단일2, 광잡, 폭뎀증10)",
    "group": "초월 [마딜]",
    "codes": [
      "W80H"
    ],
    "abilities": {
      "단일": 2,
      "바제스": true,
      "광폭화": true,
      "순간이동": "true",
      "폭발형 대미지 증폭": 10
    },
    "description": "뉴비 단일 연습용으로 좋음\n단일 폭뎀증 순간이동\n*폭발형 데미지(폭뎀 : 대부분의 단일, 끝딜 유닛이 가지고 있음. \n물리 데미지, 마법 데미지, 고정 데미지처럼 데미지의 한 종류. 폭발형 데미지 증가에 영향을 받음)"
  },
  {
    "sourceId": "690H",
    "sourceName": "(B)로우 🤍✚ (발동이감40, 방무뎀, 범퍼, 광잡)",
    "group": "초월 [마딜]",
    "codes": [
      "690H"
    ],
    "abilities": {
      "발동이동속도 감소": 40,
      "방어력 무시 대미지": 0,
      "범위 현재 체력 퍼센트 대미지": 0,
      "바제스": true,
      "광폭화": true,
      "순간이동": "true"
    },
    "description": "방무뎀 > 고뎀(특강)\n현재 체력 비례 범위딜\n발동이감(보통)\n\n"
  },
  {
    "sourceId": "790H",
    "sourceName": "(C)아오키지 💙✚ (1.2스턴, 이감70, 폭뎀증20)",
    "group": "초월 [마딜]",
    "codes": [
      "790H",
      "1B0H"
    ],
    "abilities": {
      "스턴": 1.2,
      "이동속도 감소": 70,
      "폭발형 대미지 증폭": 10
    },
    "description": "스턴 이감 폭뎀증\n혼자 스턴 가능\n딜이 약해서 단끝과 조합이 좋음"
  },
  {
    "sourceId": "Q40h",
    "sourceName": "(S)빅맘 💖🚁✚ (이감70->40*특강시)",
    "group": "불멸 [마딜]",
    "codes": [
      "Q40h"
    ],
    "abilities": {
      "이동속도 감소": 40,
      "유닛삭제": "true",
      "공중이동": "true"
    },
    "description": "라인딜러\n이감70 > 40(특강) + 딜증가\n쉽고 쎔\n[추천 조합 : 토키 ]"
  },
  {
    "sourceId": "E40h",
    "sourceName": "(A)센고쿠 💖✚ (1.1스,공증,방무딜, 현퍼)",
    "group": "불멸 [마딜]",
    "codes": [
      "E40h"
    ],
    "abilities": {
      "스턴": 1.1,
      "공격력 증가": 33,
      "방어력 무시 대미지": 0,
      "범위 현재 체력 퍼센트 대미지": 0
    },
    "description": "현재 체력 비례 범위 퍼센트딜 스턴 방무 특강(추가 스턴)\n[추천 조합 : 상디 초월, 모비딕호 ]"
  },
  {
    "sourceId": "G40h",
    "sourceName": "(B)제트 💙✚ (이감35, 광보잡)",
    "group": "불멸 [마딜]",
    "codes": [
      "G40h"
    ],
    "abilities": {
      "이동속도 감소": 35,
      "보스 잡기": true,
      "바제스": true,
      "광폭화": true,
      "순간이동": "true"
    },
    "description": "라인딜 보잡 이감\n특강 시 공속 필요\n\n[추천조합 : 로우 전설+발라티에, 토키]"
  },
  {
    "sourceId": "D40h",
    "sourceName": "(C)드래곤 💙✚ (폭뎀증, 1.4스, 공속20, 폭뎀증20)",
    "group": "불멸 [마딜]",
    "codes": [
      "D40h"
    ],
    "abilities": {
      "스턴": 1.4,
      "공격속도 증가": 20,
      "폭발형 대미지 증폭": 20,
      "보스 잡기": "true",
      "유닛삭제": "true"
    },
    "description": "스턴 공속버프 끝딜 삭제 폭뎀증(폭발형데미지증가)\n약한라인딜\n단끝 유닛 조합해서 적  한마리씩 잡아야함\n난이도 때문에 C\n[추천조합 : 루치 초월"
  },
  {
    "sourceId": "B40h",
    "sourceName": "(B)시키 💙🚁✚ (1.8스, 암브)",
    "group": "불멸 [마딜]",
    "codes": [
      "B40h"
    ],
    "abilities": {
      "스턴": 1.8,
      "바제스": true,
      "공중이동": "true",
      "아머브레이크": "true"
    },
    "description": "공중이동 스턴\n마나스킬(마뎀증폭15)\n특강(발동이감+라인딜)\n마딜은 특강 안 해도 씀\n\n*물딜로 쓸 때는 특성강화 필수. '해적선'을 소모해 스킬 최대 3회 강화\n+발동이감 35%(믿음직)\n+범위 물딜\n물딜로 사용 시 S급"
  },
  {
    "sourceId": "unit_1767886057577_8465",
    "sourceName": "(A)센고쿠 (1.5스,공증33,범퍼,방무딜)",
    "group": "불멸 [마딜]",
    "codes": [
      "MB0h"
    ],
    "abilities": {
      "스턴": 1.5,
      "공격력 증가": 33,
      "방어력 무시 대미지": 0,
      "범위 현재 체력 퍼센트 대미지": 0
    },
    "description": "현재 체력 비례 범위 퍼센트딜 스턴 방무 특강(추가 스턴)\n[추천 조합 : 상디 초월, 모비딕호 ]"
  },
  {
    "sourceId": "E50h",
    "sourceName": "(S)에넬 💙✚ (마방깍, 마젠1.5)",
    "group": "제한됨 [마딜]",
    "codes": [
      "E50h"
    ],
    "abilities": {
      "마나 재생": 1.5,
      "발동이동속도 감소": 35,
      "바제스": true,
      "순간이동": "true",
      "마법 방어력 감소": 18
    },
    "description": "라인딜 마나리젠 발동이감\n광보잡\n\n[추천조합 : 프랑키 초월, 베가펑크 ]"
  },
  {
    "sourceId": "O80h",
    "sourceName": "(C)마르코 인간폼 💖🚁✚ (이감60 체젠 단일)",
    "group": "제한됨 [마딜]",
    "codes": [
      "O80h"
    ],
    "abilities": {
      "단일": 1.5,
      "이동속도 감소": 60,
      "체력 재생": 6,
      "바제스": "true",
      "공중이동": "true"
    },
    "description": "*아이템 '불사조의 깃털' 필요\n인간폼 : 마딜\n단일 이감45(특강60)\n\n[추천조합 : 키드 초월, 빅맘, 캐럿 변이]"
  },
  {
    "sourceId": "480h",
    "sourceName": "(C)시노부 💖✚ (이감30 끝딜 광보잡)",
    "group": "제한됨 [마딜]",
    "codes": [
      "480h"
    ],
    "abilities": {
      "끝딜": 1.5,
      "이동속도 감소": 30,
      "보스 잡기": true,
      "광폭화": true,
      "순간이동": "true"
    },
    "description": "끝딜 보잡 이감\n보잡은 괜찮은데 광폭이 약함. 라인딜 부족.\n[추천조합 : 나미전설, 캐럿 ]"
  },
  {
    "sourceId": "040h",
    "sourceName": "(F)아인 ✚ (광잡, 삭제)",
    "group": "제한됨 [마딜]",
    "codes": [
      "040h"
    ],
    "abilities": {
      "광폭화": true,
      "유닛삭제": "true",
      "순간이동": "true"
    },
    "description": "*뉴비 비추천\n고유능력(뒤로뒤로)\n블링크 광폭\n\n[추천 조합 : 아인(2~3마리 같이 쓰면 좋음) ] \n"
  },
  {
    "sourceId": "G50h",
    "sourceName": "(D)레드필드 💖🚁✚ (전퍼 끝딜 41라이전조합)",
    "group": "제한됨 [마딜]",
    "codes": [
      "G50h"
    ],
    "abilities": {
      "끝딜": 1.5,
      "범위 전체 체력 퍼센트 대미지": 0,
      "보스 잡기": true,
      "바제스": true,
      "순간이동": "true"
    },
    "description": "*41라운드 이전에만 조합 가능\n범위끝딜 끝딜 특강(잃퍼)"
  },
  {
    "sourceId": "unit_1786779620531_6844",
    "sourceName": "",
    "group": "제한됨 [마딜]",
    "codes": [],
    "abilities": null,
    "description": ""
  },
  {
    "sourceId": "850h",
    "sourceName": "(B)미호크 💙 (이감45, 방무)",
    "group": "영원 [마딜]",
    "codes": [
      "850h"
    ],
    "abilities": {
      "이동속도 감소": 45,
      "방어력 무시 대미지": 0,
      "바제스": true
    },
    "description": "*해적선 2척 필요\n방무딜 단일스턴\n보잡 성능 없어서 보잡 유닛 필요함\n\n[추천조합]\n1. 방무딜 공증 조합\n네코마, 피셔\n2. 마나리젠 조합\n프랑키 초월, 베가펑크"
  },
  {
    "sourceId": "950h",
    "sourceName": "(A)에이스 💙✚ (끝딜, 이감45)",
    "group": "영원 [마딜]",
    "codes": [
      "950h"
    ],
    "abilities": {
      "이동속도 감소": 45,
      "바제스": true
    },
    "description": "라인딜 이감\n\n[추천 조합 : 샹크스 초월, 에넬 제함, 시키 불멸 ]\n"
  },
  {
    "sourceId": "760h",
    "sourceName": "(S)우타 🤍✚ (0.5스턴 물마가능 공속15 이감45 공증30 끝딜 마방깍, 폭뎀증 )",
    "group": "영원 [마딜]",
    "codes": [
      "760h"
    ],
    "abilities": {
      "끝딜": 0.5,
      "단일": 0.5,
      "스턴": 0.4,
      "바제스": true,
      "마법 방어력 감소": 10,
      "폭발형 대미지 증폭": 10,
      "공격속도 증가": 15,
      "발동이동속도 감소": 45,
      "발동공격력 증가": 30
    },
    "description": "*아이템 '우타의 헤드셋'과 레일리 희귀함 필요\n \n체력이 10이하로 감소하면 수면\n수면이 되면 이감 없어짐"
  },
  {
    "sourceId": "750h",
    "sourceName": "(C)비비 💙✚ (끝딜, 강화)",
    "group": "영원 [마딜]",
    "codes": [
      "750h"
    ],
    "abilities": {
      "끝딜": 0,
      "바제스": true,
      "광폭화": true,
      "순간이동": "true"
    },
    "description": "*쓸 줄 알면 S급, 모르면 D급\n강화 끝딜\n7강까지 권장함\n[추천조합 : 샹크스, 상디, 가반]"
  },
  {
    "sourceId": "C50h",
    "sourceName": "(A)핸콕 💙✚ (0.9스턴 깍50 발동이감60, 물마가능)",
    "group": "영원 [마딜]",
    "codes": [
      "C50h"
    ],
    "abilities": {
      "스턴": 0.9,
      "방어력 감소": 55,
      "마법 방어력 감소": 5,
      "발동이동속도 감소": 60,
      "바제스": true,
      "범위 전체 체력 퍼센트 대미지": "true"
    },
    "description": "*레일리 희귀함 필요\n물딜/마딜 가능\n\n스턴(특강해야 혼자 스턴)\n라인딜 끝딜 발동이감 마방깍\n\n[추천조합(마딜) : 코알라+키쿠or베어 세라핌+방맥, 에넬 제한, 빅맘 ]\n\n\nA급 물딜러\n스킬딜러 스턴\n발동이감(못믿음)"
  },
  {
    "sourceId": "R80h",
    "sourceName": "(A)오뎅 💙✚ (마뎀증,공증50)",
    "group": "영원 [마딜]",
    "codes": [
      "R80h"
    ],
    "abilities": {
      "마법 대미지 증가": 1,
      "공격력 증가": 50,
      "바제스": true,
      "광폭화": "true"
    },
    "description": "라인딜 보잡 광폭(특강)\n\n[추천조합 : 코알라, 방주, 발라티에, 나미 전설]"
  },
  {
    "sourceId": "unit_1767356778906_9384",
    "sourceName": "(C)테조로 💖✚(공속25/끝딜)",
    "group": "영원 [마딜]",
    "codes": [
      "A60h"
    ],
    "abilities": {
      "공격속도 증가": 25,
      "끝딜": 1
    },
    "description": "골드량 비례 딜 증가\n폭뎀 스턴 공속\n특강 필수, 빨리 뽑아야 좋음\n[추천조합 : 항법-바운티헌터, 검은수염 전설, 캐럿 변이 ]\n\n만들기 어려워서 c등급"
  },
  {
    "sourceId": "JC0h",
    "sourceName": "(D)류마 💙✚ (단일+끝딜)",
    "group": "영원 [마딜]",
    "codes": [
      "JC0h"
    ],
    "abilities": {
      "단일": 1,
      "끝딜": 1,
      "범위 잃은 체력 퍼센트 대미지": "true"
    },
    "description": "*성장형 딜러라서 빨리 뽑아야 좋음\n폭뎀 끝딜 보잡\n토키까진 시너지 효율이 좋음\n아이템'슈스이'로 강화가능\n공증이 있어야 오니가르기 조건 충족함\n난이도가 있어서 뉴비에게 비추천\n[[오로성 -새턴일 때 매우 약해짐 ]]"
  }
]
```
