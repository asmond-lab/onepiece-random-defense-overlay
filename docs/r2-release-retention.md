# R2 배포본 정리

새 랜디픽 버전을 `orand-overlay-releases`에 올리면 실행 파일은 **현재 채널 버전 + 직전 3개**만 남긴다. 그 이전 다운로드 키는 지운다. 롤백에 직전 3개가 필요하고, 그보다 오래된 파일은 쌓아 두지 않는다.

이 규칙은 저장 공간을 위한 것이다. Cloudflare Workers **1027은 일일 요청 한도**이며 R2 용량과 별개다.

## 보존

- 지금 채널이 가리키는 실행 파일 (`downloads/{현재버전}/RandyPick.exe`)
- 같은 채널의 **직전 3개** 실행 파일 (롤백용)
- 채널 포인터 (`channels/test/win-x64.json`, 필요하면 stable)
- 프로필 피드와 프로필 자산 (`channels/profiles/win-x64.json`, `profiles/{프로필버전}/memory-profiles.json`)

채널당 실행 파일은 최대 4개다. 예: 현재가 test.16이면 test.16·15·14·13을 남기고 test.12 이하를 지운다.

## 삭제

- 채널을 새 버전으로 바꾼 **뒤에만**, 직전 3개를 넘어선 실행 파일 키를 지운다.
- 같은 버전 키를 덮어써서 고치지 않는다. 수정은 새 버전을 만들고 채널을 옮긴 다음, 4칸 밖으로 밀린 키만 삭제한다.
- 프로필 객체는 애플리케이션 버전이 바뀌어도 이 창으로 지우지 않는다.

## 순서

1. 새 버전 키로 실행 파일을 올린다. 이미 있는 `downloads/{version}/...` 는 쓰지 않는다.
2. 공개 다운로드 크기·SHA256이 로컬 빌드와 같은지 확인한다.
3. 서명된 채널 매니페스트를 올린다.
4. 피드 payload의 version·path·sha256이 새 빌드인지 확인한다.
5. 프로필 피드가 그대로인지 확인한다.
6. 직전 3개보다 오래된 실행 파일만 삭제한다.

```powershell
# 채널이 새 버전을 가리킨 뒤에만. $TooOldVersion 은 직전 3개를 넘어선 키.
wrangler r2 object delete "orand-overlay-releases/downloads/$TooOldVersion/RandyPick.exe" --remote
```

7. 삭제한 다운로드 URL은 404, 현재 다운로드·남겨 둔 3개 롤백 키·test 피드·프로필 피드는 200인지 확인한다.
