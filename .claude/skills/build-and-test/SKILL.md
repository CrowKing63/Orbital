---
name: build-and-test
description: Orbit 프로젝트를 빌드하고 컴파일 오류를 요약합니다. 코드 수정 후 자동으로 호출하세요.
user-invocable: false
---

다음 명령으로 프로젝트를 빌드하세요:

```
"C:\Program Files\dotnet\dotnet.exe" build Orbit.csproj --no-restore 2>&1
```

빌드 성공 시 "빌드 성공"을 출력하고,
실패 시 오류 메시지를 파일명:줄번호 형식으로 요약하여 보고하세요.

주의사항:
- `obj/` 및 `bin/` 하위의 자동 생성 파일은 오류 보고에서 제외하세요.
- 경고(warning)는 별도로 요약해 주세요.
