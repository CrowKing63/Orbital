# Orbit 관리자 감사 보고서

작성일: 2026-03-08
대상: 앱 관리자 그룹 -> 구현 에이전트
범위: 현재 워크스페이스 소스 점검 + `dotnet build Orbit.csproj`

## 요약

Orbit는 이미 방향성이 분명한 MVP입니다. Windows 트레이 상주, 전역 마우스 훅, 선택 텍스트 추출, 커서 근처 액션 메뉴, LLM 변환 파이프라인이 한 흐름으로 연결되어 있습니다.

다만 지금 단계는 기능 확장보다 안정화가 우선입니다. 현재 코드 기준으로는 "가끔 안 되는 앱"처럼 보일 수 있는 행동 레벨의 리스크가 몇 개 선명하게 보입니다. 특히 아래 다섯 가지는 다음 구현 사이클에서 우선적으로 막아야 합니다.

- API 키가 없으면 비LLM 액션까지 막히는 구조
- 텍스트 선택이 없는데도 롱프레스 메뉴에서 파괴적 액션이 활성화되는 구조
- 멀티 모니터에서 메뉴/툴팁 위치 계산이 틀어지는 구조
- 설정 파일 손상 시 앱 기동 자체가 실패할 수 있는 구조
- 클립보드 추출/치환이 타이밍 의존적이고 사용자 클립보드를 덮어쓰는 구조

결론적으로, 이 프로젝트는 "재설계"보다 "상호작용 안정화"가 먼저입니다. 핵심 경험이 커서 주변에서 즉시 반응해야 하는 앱이기 때문에, 작은 불안정성도 체감상 치명적으로 보입니다.

## 현재 제품 형태

- 기술 스택: 단일 `net8.0-windows` WPF 앱이며 WinForms `NotifyIcon`을 같이 사용합니다. 별도 테스트 프로젝트는 아직 없습니다. [Orbit.csproj](C:/Users/CrowKing63/Developments/Orbital/Orbit.csproj#L1), [.github/workflows/build.yml](C:/Users/CrowKing63/Developments/Orbital/.github/workflows/build.yml#L1)
- 진입 흐름: 앱 시작 시 설정을 읽고, 트레이 아이콘을 띄우고, 전역 마우스 훅을 설치한 뒤, 드래그 선택 또는 롱프레스에 반응해 메뉴를 띄웁니다. [App.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/App.xaml.cs#L16)
- 액션 모델: `ActionProfile` 하나에 이름, 프롬프트, 결과 방식이 함께 들어 있고, 로컬 액션과 LLM 액션이 같은 실행 경로를 공유합니다. [SettingsManager.cs](C:/Users/CrowKing63/Developments/Orbital/SettingsManager.cs#L10), [ActionExecutorService.cs](C:/Users/CrowKing63/Developments/Orbital/Services/ActionExecutorService.cs#L21)
- 저장 구조: 설정은 `%APPDATA%\Orbit\settings.json`에 저장되고, API 키는 DPAPI로 보호됩니다. [SettingsManager.cs](C:/Users/CrowKing63/Developments/Orbital/SettingsManager.cs#L25)

## 우선순위 이슈

### P1. API 키가 없으면 비LLM 액션도 쓸 수 없음

근거:

- [App.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/App.xaml.cs#L33)에서 API 키가 없으면 `_actionExecutor`를 `null`로 만듭니다.
- [RadialMenuWindow.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/RadialMenuWindow.xaml.cs#L109)에서 `_actionExecutor == null`이면 모든 버튼 클릭을 차단합니다.
- [ActionExecutorService.cs](C:/Users/CrowKing63/Developments/Orbital/Services/ActionExecutorService.cs#L24)에는 `Browser`, `DirectCopy`, `Cut`, `Paste` 같은 비LLM 액션이 이미 따로 구현되어 있습니다.

영향:

- 첫 실행 경험이 깨집니다.
- 복사/붙여넣기 같은 로컬 유틸리티 가치가 API 설정 뒤로 밀립니다.
- 사용자는 "앱이 안 된다"고 오해하기 쉽습니다.

지시:

- 액션 실행기를 `로컬 액션`과 `LLM 액션`으로 분리하십시오.
- API 키가 없어도 로컬 액션은 항상 실행 가능해야 합니다.
- 실제로 모델 호출이 필요한 액션만 자격 조건을 걸어야 합니다.

### P1. 선택 텍스트가 없는데도 롱프레스 메뉴에서 파괴적 액션이 활성화됨

근거:

- [App.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/App.xaml.cs#L138)에서 롱프레스 메뉴는 `string.Empty`로 열립니다.
- [RadialMenuWindow.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/RadialMenuWindow.xaml.cs#L53)에서 텍스트가 없어도 비LLM 액션은 모두 활성화됩니다.
- [ActionExecutorService.cs](C:/Users/CrowKing63/Developments/Orbital/Services/ActionExecutorService.cs#L34)에서 `DirectCopy`, `Cut`은 `selectedText`를 그대로 클립보드에 넣습니다.
- [ClipboardHelper.cs](C:/Users/CrowKing63/Developments/Orbital/ClipboardHelper.cs#L126)에서 `Delete` 키는 실제 선택 여부와 무관하게 전송됩니다.

영향:

- 선택 텍스트 없이 `DirectCopy`를 누르면 클립보드가 빈 값으로 덮일 수 있습니다.
- 선택 텍스트 없이 `Cut`을 누르면 "잘라내기"가 아니라 다음 문자 삭제처럼 동작할 수 있습니다.
- 현재 메뉴 활성화 규칙은 "비LLM이면 안전하다"는 가정 위에 있는데, 이 가정이 틀렸습니다.

지시:

- 액션 메타데이터에 `RequiresSelection` 같은 플래그를 추가하십시오.
- 기본값으로 `Cut`, `DirectCopy`, `Browser`, `Replace`, `Copy`, `Popup`은 선택 텍스트를 요구하도록 두십시오.
- 현재 구조에서 선택 없이 안전한 기본 액션은 사실상 `Paste`뿐입니다.

### P1. 멀티 모니터에서 메뉴/툴팁 위치가 어긋날 가능성이 높음

근거:

- [RadialMenuWindow.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/RadialMenuWindow.xaml.cs#L93)에서 메뉴 위치 보정이 `SystemParameters.WorkArea` 기준입니다.
- [ResultTooltipWindow.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/ResultTooltipWindow.xaml.cs#L27)에서도 툴팁 위치가 동일하게 `SystemParameters.WorkArea` 기준입니다.

영향:

- 메뉴와 툴팁이 사실상 주 모니터 기준으로 계산됩니다.
- 보조 모니터에서 선택했는데 팝업이 다른 화면에 뜨거나 경계 보정이 잘못될 수 있습니다.
- 이 앱은 "커서 근처 반응"이 핵심이므로 위치 오류는 경험 자체를 해칩니다.

지시:

- 커서 좌표 또는 대상 윈도우 핸들 기준으로 실제 모니터를 판별하십시오.
- 전역 WorkArea가 아니라 모니터별 WorkArea와 DPI를 사용하십시오.
- 결과 툴팁도 액션이 시작된 같은 모니터에 유지하십시오.

### P1. 설정 파일이 손상되면 앱이 기동 실패할 수 있음

근거:

- [SettingsManager.cs](C:/Users/CrowKing63/Developments/Orbital/SettingsManager.cs#L34)에서 JSON 읽기와 역직렬화에 대한 복구 경로가 없습니다.
- [App.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/App.xaml.cs#L21)에서 이 로직이 트레이 UI가 올라오기 전에 실행됩니다.

영향:

- `%APPDATA%\Orbit\settings.json` 하나만 깨져도 앱이 시작되지 않을 수 있습니다.
- 사용자 입장에서는 복구 경로가 없고, 수동 파일 삭제가 사실상 유일한 해결책이 됩니다.
- 운영 지원 비용이 불필요하게 올라갑니다.

지시:

- 파일 읽기와 역직렬화를 예외 처리로 감싸십시오.
- 손상 파일은 백업 후 기본 설정으로 재생성하십시오.
- 가능하면 트레이 알림 또는 안내 다이얼로그로 복구 사실을 사용자에게 알려주십시오.

### P2. 클립보드 파이프라인이 타이밍 의존적이고 사용자 상태를 파괴함

근거:

- [ClipboardHelper.cs](C:/Users/CrowKing63/Developments/Orbital/ClipboardHelper.cs#L78)에서 `Ctrl+C` 전에 클립보드를 비웁니다.
- [ClipboardHelper.cs](C:/Users/CrowKing63/Developments/Orbital/ClipboardHelper.cs#L87)에서 결과 수집이 고정 `Thread.Sleep(100)`에 의존합니다.
- [ClipboardHelper.cs](C:/Users/CrowKing63/Developments/Orbital/ClipboardHelper.cs#L110)에서 치환 시 기존 클립보드를 복구하지 않습니다.
- [App.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/App.xaml.cs#L123)에서 선택 텍스트 추출이 fire-and-forget 백그라운드 작업으로 여러 번 겹칠 수 있습니다.

영향:

- 사용자의 기존 클립보드 내용이 유실될 수 있습니다.
- 앱/환경별 타이밍 차이로 빈 문자열이나 이전 선택값이 들어올 수 있습니다.
- 빠른 반복 선택 시 작업 간 경합이 생길 수 있습니다.

지시:

- 클립보드 작업을 직렬화하고, 취소/디바운스 가능한 파이프라인으로 바꾸십시오.
- `Replace`는 의도적으로 복사 결과를 남기는 액션이 아닌 이상, 기존 클립보드를 복구하는 쪽이 맞습니다.
- 가능하면 클립보드는 주 수단이 아니라 fallback 수단으로 내리고, 더 결정적인 선택 텍스트 획득 경로를 검토하십시오.

### P2. 설정 창에서 변경 사항 유실 가능성이 큼

근거:

- [SettingsWindow.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/SettingsWindow.xaml.cs#L179) 이후의 추가/수정/삭제는 메모리만 바꿉니다.
- [SettingsWindow.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/SettingsWindow.xaml.cs#L165)에서 실제 저장은 명시적 저장 시점에만 일어납니다.
- 닫기 버튼이나 창 종료 시점에 dirty state 확인이 없습니다.

영향:

- 사용자가 타이틀바의 닫기 버튼으로 창을 닫으면 액션 수정 내용이 사라질 수 있습니다.
- API 설정과 액션 설정의 저장 방식이 달라서 사용자가 혼란을 겪습니다.

지시:

- 액션 추가/수정/삭제를 즉시 저장하거나, 최소한 저장/폐기 확인을 붙이십시오.
- 설정 종류별로 저장 시맨틱을 통일하십시오.

### P3. 훅/네트워크 인프라가 확장 전에 더 단단해져야 함

근거:

- [SystemHookManager.cs](C:/Users/CrowKing63/Developments/Orbital/SystemHookManager.cs#L63)에서 훅 설치 성공 여부를 검증하지 않습니다.
- [LlmApiService.cs](C:/Users/CrowKing63/Developments/Orbital/Services/LlmApiService.cs#L26)에서 `HttpClient`를 인스턴스마다 만들고 해제하지 않습니다.
- [LlmApiService.cs](C:/Users/CrowKing63/Developments/Orbital/Services/LlmApiService.cs#L68)에서 응답 JSON 형태를 하나로 고정 가정합니다.

영향:

- 훅 설치 실패가 "아무 일도 안 일어나는 앱"으로 보일 수 있습니다.
- 백엔드가 늘어날수록 호환성 문제와 자원 관리 문제가 커집니다.
- 설정 변경이 반복될수록 네트워크 리소스 관리가 지저분해집니다.

지시:

- 훅 설치 실패를 사용자/로그에 드러내십시오.
- `HttpClient` 수명 관리를 중앙화하십시오.
- 응답 파싱은 보수적으로 하고, 원본 에러 문맥을 최대한 보존하십시오.

## 구조 개선 제안

- 액션 메타데이터를 명시적으로 분리하십시오. `RequiresSelection`, `RequiresLlm`, `MutatesClipboard`, `SupportsEmptyInput` 정도는 최소 단위로 필요합니다.
- 클립보드, 훅, LLM 공급자 코드를 인터페이스 뒤로 빼서 테스트 가능하게 만드십시오.
- `선택 감지 -> 메뉴 표시 -> 액션 실행`을 하나의 상태 흐름으로 다루십시오. 지금은 `App`, `ClipboardHelper`, `RadialMenuWindow`에 타이밍 책임이 흩어져 있습니다.
- 문자열 기반 `ResultAction`은 enum + 메타데이터 구조로 교체하는 편이 안전합니다.
- 공급자 설정과 액션 정의를 분리해 두어야 추후 멀티 제공자 라우팅이 쉬워집니다.

## 운영/품질 공백

- 저장소 안에 자동화 테스트가 없습니다.
- CI는 restore/build/artifact upload까지만 수행하며, 테스트 실행, 패키징, 서명, 스모크 체크는 없습니다. [.github/workflows/build.yml](C:/Users/CrowKing63/Developments/Orbital/.github/workflows/build.yml#L21)
- 저장소 내부에 설치기 또는 publish 자동화가 없습니다.
- 구조화 로그, 크래시 리포트, 현장 진단 모드가 없습니다.
- 선택 텍스트를 외부 LLM 제공자에게 보낼 때의 프라이버시 가드레일이 없습니다.

## 확장 가능 요소

- 앱별 규칙: 브라우저, IDE, 메신저, 문서 편집기마다 다른 액션 세트 적용
- 로컬 모델 우선 모드: Ollama, LM Studio를 "커스텀 URL"이 아니라 1급 기능으로 지원
- 액션 팩 import/export: 팀 공용 프리셋, 역할별 프롬프트 번들
- 변수 확장: `{text}`, `{clipboard}`, `{app}`, `{url}`, `{selection_language}`
- 후처리 플러그인: 마크다운 정리, 번역 톤 변환, 코드 포맷터, 요약 템플릿
- 프라이버시 모드: 민감 앱 차단, 메일/PII 마스킹 후 전송, 확인 다이얼로그
- 키보드 진입/접근성 패스: 드래그에 의존하기 어려운 사용자 대응

## 권장 실행 순서

1. 안정화 단계

- 로컬 액션과 API 키 의존성 분리
- 액션별 선택 텍스트 요구 조건 도입
- 모니터 인식 기반 위치 계산 수정
- 설정 파일 복구 경로 추가

2. 신뢰성 단계

- 클립보드 작업 직렬화 및 타이밍 의존 감소
- 설정창 dirty state 처리
- 훅 진단 및 구조화 로그 추가
- 설정, 공급자 파싱, 액션 게이팅에 대한 단위 테스트 추가

3. 확장 단계

- 액션 메타데이터와 공급자 추상화 정식화
- import/export 및 앱별 라우팅 추가
- 프라이버시 제어 추가 후 외부 배포 확대

## 검증 메모

이번 패스에서 직접 확인한 것:

- 저장소 구조
- 주요 소스 경로와 실행 흐름
- `dotnet build Orbit.csproj` 성공

이번 패스에서 직접 실행 재현하지는 않은 것:

- 실GUI 상호작용
- 서드파티 앱별 클립보드 타이밍 차이
- 멀티 모니터 런타임 재현
- 실제 LLM 공급자 호출

즉, 위 이슈들은 대부분 "소스 경로상 고신뢰도 문제"로 보되, 구현 단계에서 GUI 재현 테스트를 꼭 붙이는 것이 좋습니다.
