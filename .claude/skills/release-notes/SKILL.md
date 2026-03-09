---
name: release-notes
description: 최근 커밋을 기반으로 Orbital 릴리즈 노트를 한국어로 작성합니다.
disable-model-invocation: true
---

1. 먼저 최신 태그를 확인합니다:
   ```
   git tag --sort=-creatordate | head -5
   ```

2. 이전 태그부터 HEAD까지의 커밋 목록을 가져옵니다:
   ```
   git log [이전태그]..HEAD --oneline
   ```
   태그가 없으면 `git log --oneline -20`을 사용합니다.

3. 커밋 내용을 분석해 GitHub Release용 한국어 릴리즈 노트를 작성하세요.

출력 형식:
```
## 새 기능
- ...

## 버그 수정
- ...

## 개선사항
- ...

## 알려진 이슈
- ...
```

규칙:
- 내부 리팩토링, obj/bin 파일 변경은 제외
- 사용자에게 영향 있는 변경만 포함
- 기술 용어는 한국어로 풀어서 설명
