# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## App Name

The application is named **Orbital**. Always use "Orbital" — never "Orbit" — when referring to the app by name in UI strings, documentation, commit messages, and comments.

## Language Policy

**All project artifacts must be written in English.** This includes:
- UI strings (labels, buttons, tooltips, error messages, window titles)
- Code comments
- Commit messages
- Documentation (README, changelogs, release notes)
- Default action names and prompt templates in `SettingsManager.cs`

Conversation between the developer and Claude may be in Korean. Code artifacts are always English.

## Project Overview

**Orbital**은 Windows 시스템 트레이 앱으로, 마우스로 텍스트를 드래그 선택하면 커서 위에 수평 바형 메뉴(Bar Menu)를 띄워 LLM 기반 액션을 실행합니다. OpenAI 호환 API를 사용하며, 사용자 정의 프롬프트 액션을 지원합니다.

## Build & Run

```bash
# 빌드 (dotnet이 PATH에 없을 경우 전체 경로 사용)
"C:\Program Files\dotnet\dotnet.exe" build Orbital.csproj

# 실행 (빌드 후 exe 직접 실행)
./bin/Debug/net8.0-windows/Orbital.exe

# Release 빌드
"C:\Program Files\dotnet\dotnet.exe" build -c Release
```

- 테스트 프레임워크 없음 (수동 테스트만)
- WPF + WinForms 혼용 (`UseWPF`, `UseWindowsForms` 둘 다 활성화) — WinForms는 `NotifyIcon`(트레이)에만 사용
- **네임스페이스 충돌**: WPF+WinForms 공존으로 `Application`, `Clipboard`, `MessageBox`, `Button`, `IDataObject`, `Timer` 등이 모호해짐 → `GlobalUsings.cs`에서 전역 별칭으로 일괄 해소. **`Color`는 GlobalUsings에 없음** — `System.Windows.Media.Color`로 완전 경로 직접 사용할 것

## Architecture

### 핵심 데이터 흐름

```
[마우스 이벤트]
  SystemHookManager (Win32 WH_MOUSE_LL 훅)
    - LBUTTONDOWN: 클라이언트 영역 여부 확인 (제목 표시줄·시스템 셸 창 필터링)
    - LBUTTONUP: 드래그 거리 > 8px → OnMouseUp 이벤트
    - 300ms 이상 정지 유지 → OnLongPress 이벤트 (텍스트 없이 메뉴 표시)
    - 더블클릭 감지 → OnDoubleClickRelease 이벤트

[App.xaml.cs — TriggerSelectionMenu()]
  OnMouseUp(drag)       → Task.Run + 50ms 대기 → CheckEditability(dragStartPos) → ShowAtCursor
  OnDoubleClickRelease  → Task.Run + 150ms 대기 → CheckEditability(cursorPos)   → ShowAtCursor
  OnLongPress           → Task.Run → IsOverEditableControl() → ShowAtCursor(hasText=false)
  OnKeyboardSelection   → Task.Run → ShowAtCursor(isEditable=true, hasText=true)

[CheckEditability() — UIA 기반 컨트롤 타입 판별]
  ControlType.Edit / ComboBox → (canSelect=true, canWrite=true,  hasText=UIA선택여부)
  ControlType.Document (writable) → (true, true,  UIA선택여부)  ← Notepad, Sticky Notes 등
  ControlType.Document (read-only) → (true, false, true)         ← 브라우저 본문 (낙관적)
  미인식 리프 타입 → 부모 1단계 올라가서 위 규칙 재적용
  그 외(게임, 바탕화면 등) → (false, false, false) → 팝업 미표시

[RadialMenuWindow.ShowAtCursor()]
  → SettingsManager.Actions 기준 StackPanel에 수평 버튼 동적 생성
  → LLM 필요 액션(Replace/Copy/Popup)은 텍스트 없을 때 비활성화(Opacity 0.4)
  → Opacity=0으로 오프스크린 표시 → UpdateLayout()으로 크기 측정 → 커서 위 8px 위치 후 Opacity=1
      ↓ 버튼 클릭
[ActionExecutorService.ExecuteAsync()]
  → ResultAction 분기:
     "Replace"    → LLM 호출 → ClipboardHelper.ReplaceSelectedText() (Ctrl+V)
     "Copy"       → LLM 호출 → Clipboard.SetText()
     "Popup"      → LLM 호출 → ResultTooltipWindow 표시
     "DirectCopy" → Clipboard.SetText(selectedText) (LLM 미사용)
     "Cut"        → Clipboard.SetText() + Delete 키 시뮬레이션 (LLM 미사용)
     "Paste"      → Ctrl+V 시뮬레이션 (LLM 미사용, 롱프레스로 주로 사용)
     "Browser"    → Google 검색 URL로 브라우저 오픈 (LLM 미사용)
```

### 주요 컴포넌트

| 파일 | 역할 |
|------|------|
| `App.xaml.cs` | 앱 진입점. 트레이 아이콘, 마우스 훅, ActionExecutor 초기화 및 재생성 |
| `GlobalUsings.cs` | WPF/WinForms 타입 충돌 전역 별칭 (`Application`, `Clipboard`, `Button` 등) |
| `SystemHookManager.cs` | Win32 WH_MOUSE_LL 훅. HTCLIENT 필터 + 8px 드래그 임계값 + 500ms 롱프레스 타이머 |
| `ClipboardHelper.cs` | `SendInput`으로 Ctrl+C/V/Delete 시뮬레이션. 클립보드 백업/복원 포함 |
| `RadialMenuWindow.xaml.cs` | 투명 WPF 창(SizeToContent pill 바형). StackPanel 수평 배치. 텍스트 없을 때 LLM 버튼 비활성화 |
| `ResultTooltipWindow.xaml.cs` | AI 결과 팝업. 우측 하단 표시, 20초 자동 닫힘, 복사 버튼 |
| `SettingsManager.cs` | `ActionProfile`/`AppSettings` 모델. `%APPDATA%\Orbital\settings.json` 저장. DPAPI 암호화 |
| `Services/LlmApiService.cs` | `ILlmApiService` + `OpenAiApiService`. 전체 URL 직접 조립 방식으로 BaseAddress 경로 버그 방지 |
| `Services/ActionExecutorService.cs` | ResultAction별 분기 실행. 반드시 백그라운드 스레드에서 호출 |
| `SettingsWindow.xaml.cs` | 제공자 드롭다운(OpenAI/OpenRouter/사용자지정) → URL·모델 자동 완성. API 키·액션 CRUD |
| `ActionEditDialog.xaml.cs` | 개별 ActionProfile 추가/편집 다이얼로그 |

### 설정 모델

```csharp
ActionProfile {
    Name: string          // 버튼에 표시될 이름
    PromptFormat: string  // "{text}" 플레이스홀더 포함 프롬프트 (LLM 미사용 액션은 빈 문자열)
    ResultAction: string  // "Replace" | "Copy" | "Popup" | "Browser" | "DirectCopy" | "Cut" | "Paste"
}

AppSettings {
    EncryptedApiKey: string  // DPAPI 암호화된 API 키
    ApiBaseUrl: string       // 기본값 "https://api.openai.com/v1"
    ModelName: string        // 기본값 "gpt-4o-mini". OpenRouter 무료: "openrouter/free"
    Actions: List<ActionProfile>
}
```

### 중요 제약사항

- **스레딩**: `OnMouseUp` 핸들러는 `Task.Run` 백그라운드에서 실행. UI 접근은 항상 `Dispatcher.Invoke` 사용
- **클립보드**: `GetSelectedText()`(Ctrl+C)는 버튼 클릭 시에만 호출(lazy). 팝업 표시 전 프로액티브 호출 없음 — 아래 "팝업 트리거 가드레일" 참고
- **드래그 필터링**: LBUTTONDOWN 시 `GetAncestor(GA_ROOT)`로 루트 창 구하고 `IsPointInClientArea()`로 클라이언트 영역 검사. UWP/WinUI/WebView2 대응으로 `GetWindowRect` 폴백 포함
- **API 키 없을 때**: `_actionExecutor`가 `null`. `RadialMenuWindow`에서 null 체크 후 MessageBox 표시
- **설정 변경 반영**: 설정창 닫힌 후 `App.RebuildActionExecutor()` 호출로 새 키·URL·모델 즉시 적용
- **DPI 처리**: `ShowAtCursor()`에서 `PresentationSource`로 DPI 스케일 보정 후 화면 경계 클램핑
- **OpenAI 호환 API**: `ApiBaseUrl` 변경으로 OpenRouter, LM Studio, Ollama 등 사용 가능. `ModelName`도 해당 제공자 형식으로 변경 필요
- **OpenRouter 무료 라우팅**: 모델 `openrouter/free` 사용 시 무료 모델 중 자동 선택 (크레딧 미소모)

## Popup Trigger System — 설계 결정 & 가드레일

이 섹션은 팝업 트리거 시스템의 핵심 설계 결정을 기록한다.
각 결정은 실패한 시도 끝에 정착된 것이므로 **"개선"을 위해 변경하기 전에 반드시 이 문서를 확인할 것.**

### 1. 팝업 표시 여부: UIA 기반 (Ctrl+C 미사용)

팝업을 띄울지 판단하는 시점에 `ClipboardHelper.GetSelectedText()`(Ctrl+C 시뮬레이션)를 **호출하지 않는다.**
과거에 프로액티브 Ctrl+C 방식을 사용했으나 제거됨:
- 빠른 연속 클릭 시 사용자 클립보드 오염
- CPU 부하 (클릭마다 Ctrl+C 발생)

`ClipboardHelper.GetSelectedText()`는 사용자가 **버튼을 실제로 클릭할 때만** 호출된다(`RadialMenuWindow.xaml.cs`).
팝업 표시 여부와 `hasText` 플래그는 UIA `TextPattern`으로 판별한다.

### 2. hasText = true (낙관적) — read-only Document

`ControlType.Document`이면서 read-only인 컨트롤(브라우저 본문 등)은 `hasText = true`로 고정한다.
UIA 선택 상태를 확인해서 `hasText`를 결정하면 안 되는 이유:
- Chrome/Edge는 mouse-up 직후 UIA TextPattern 선택 상태를 즉시 업데이트하지 않음
- 50~150ms 대기 후에도 미반영될 수 있음
- 드래그(8px+)는 그 자체로 선택 의도가 명확하므로 낙관적으로 처리

실제 텍스트는 버튼 클릭 시 Ctrl+C로 획득. 선택된 텍스트가 없으면 액션이 조용히 중단됨.

### 3. editability check 위치: dragStartPos

`CheckEditability()`를 호출할 때 드래그 **끝** 위치가 아닌 **시작** 위치를 사용한다.
(`SystemHookManager.LastButtonDownPos` → `TriggerSelectionMenu`의 `editCheckX/Y` 파라미터)

이유: 드래그가 텍스트 필드 경계 밖에서 끝나는 경우(특히 아이트래커 사용자, 필드 가장자리)
끝 위치의 UIA 요소는 텍스트 필드가 아닐 수 있음.

### 4. 클라이언트 영역 판별: GetAncestor + GetWindowRect 폴백

`_mouseDownInClient` 판별 순서:
1. `GetAncestor(hwnd, GA_ROOT)`로 루트 창 구함
2. `GetClientRect` + `ClientToScreen`으로 클라이언트 영역 계산
3. 실패하거나 빈 rect이면 `GetWindowRect`로 폴백

이유: UWP(`Windows.UI.Core.CoreWindow`), WinUI 3, WebView2 등의 자식 HWND는
`ClientToScreen`이 실패하거나 잘못된 좌표를 반환할 수 있음.
폴백은 제목 표시줄 필터링이 덜 정밀하지만, 트리거 자체가 안 되는 것보다 낫다.

### 5. UIA 컨트롤 타입 판별: 부모 1단계 폴백

`AutomationElement.FromPoint()`가 미인식 타입(Custom, Text 등 리프 요소)을 반환하면
`ControlViewWalker.GetParent()`로 부모 1단계만 올라가서 재판별한다.
더 깊이 올라가면 게임/비텍스트 앱에서 오탐 발생 가능.

### 현재 팝업 표시 조건 요약

| 트리거 | 조건 |
|--------|------|
| 드래그(8px+) | 시작 위치가 Edit/ComboBox/Document → 항상 팝업 |
| 더블클릭 | Edit/ComboBox/Document → 항상 팝업 (150ms 대기) |
| 롱프레스(300ms) | Edit 또는 writable Document(IsKeyboardFocusable) 위에서만 |
| Ctrl+A / Shift+화살표 | 항상 팝업 |
| 게임·바탕화면·작업표시줄 | `IsSystemShellWindow` 또는 UIA 미인식 → 팝업 없음 |

### 알려진 미해결 이슈 (v0.2.4 기준)

- **Windows Sticky Notes**: 팝업 미작동. 위 수정 모두 적용했으나 효과 없음.
  가설: 2024년 Win32 재작성 버전으로 UIA 트리 구조 불명. 조사 도구 필요.
  → **Microsoft Accessibility Insights** (accessibilityinsights.io) 또는 Windows SDK `Inspect.exe`로
    Sticky Notes 텍스트 영역의 ControlType 직접 확인 후 접근할 것.
- **브라우저 본문 더블클릭**: 작동하나 150ms 지연 있음.

### 관련 파일

- `App.xaml.cs`: `CheckEditability()`, `IsOverEditableControl()`, `TriggerSelectionMenu()`
- `SystemHookManager.cs`: `IsPointInClientArea()`, `IsSystemShellWindow()`, `LastButtonDownPos`

## Adding a New Action Type — Checklist

새 `ActionType`을 추가할 때는 아래 파일을 **모두** 수정해야 한다. 하나라도 빠지면 기능이 누락되거나 설정 UI에서 보이지 않는다.

| # | 파일 | 할 일 |
|---|------|--------|
| 1 | `ActionType.cs` | enum에 새 값 추가 + `TryFromString` switch case 추가 (`RequiresLlm`은 LLM 미사용이면 기본값 false로 처리됨) |
| 2 | `ClipboardHelper.cs` | 새 액션에 필요한 키 시뮬레이션 메서드가 있으면 추가 (VK 상수 포함) |
| 3 | `Services/ActionExecutorService.cs` | `ExecuteAsync()` switch에 새 case 추가 (LLM 미사용이면 상단 switch, LLM 사용이면 하단 switch) |
| 4 | `RadialMenuWindow.xaml.cs` | `PopulateBarButtons()`의 `requiresWrite` 그룹에 포함 여부 결정. 특수 동작(예: SelectAll의 팝업 재표시)이 필요하면 `ActionButton_Click`에 분기 추가 |
| 5 | `SettingsManager.cs` | `CreateDefaultSettings()`의 기본 액션 목록에 추가 (신규 설치 사용자용) |
| 6 | `ActionPresets.cs` | `All` 리스트의 적절한 카테고리 섹션에 추가 (설정 → 액션 추가 → 라이브러리에 표시됨) |
| 7 | `README.md` | Action Types 표에 새 행 추가 |
| 8 | `Orbital.csproj` | 버전 번호 patch 올리기 (예: 0.2.5 → 0.2.6) |

### requiresWrite 판단 기준

`RadialMenuWindow.PopulateBarButtons()`의 `requiresWrite` 플래그는 **읽기 전용 컨트롤(브라우저 본문 등)에서 버튼을 숨길지** 결정한다.

- `true`로 설정: 대상 문서에 내용을 쓰거나 삭제하는 액션 (Paste, Cut, SimulateKey, Replace)
- `false`로 유지: 선택·복사·검색·팝업처럼 읽기 전용 컨텍스트에서도 유효한 액션 (DirectCopy, Browser, Popup, **SelectAll** 등)

### ActionPresets.cs 카테고리 구조

```
// ── Utility (no LLM) ──   Copy, Cut, Paste, Select All, Search
// ── Translation ───────   Translate to Korean/English/Japanese
// ── Writing ───────────   Polish, Formal, Casual, Shorter, Bullet Points
// ── Analysis (Popup) ──   Summarize, Explain, ELI5, Fix Code
```

새 LLM 액션은 성격에 맞는 카테고리에 삽입한다.

### 특수 동작이 필요한 액션

`ActionExecutorService.ExecuteAsync()`만으로 처리할 수 없는 액션(예: 액션 완료 후 팝업 재표시)은 `RadialMenuWindow.ActionButton_Click()`에 별도 분기로 처리한다. `ExecuteAsync`를 호출하기 **전에** 분기하고 `return`으로 빠져나와야 한다.

```csharp
if (action.ActionType == ActionType.YourNewType)
{
    await Task.Run(async () =>
    {
        // 특수 동작
    });
    return; // ExecuteAsync 호출 방지
}
```

## Release & Auto-Update

- **자동 업데이트**: Velopack (`Velopack` NuGet + `vpk` dotnet global tool) 사용
  - `csq` (Clowd.Squirrel CLI)는 NuGet에 없음 — 사용하지 말 것
- **GitHub Actions**: `.github/workflows/release.yml` — `v*` 태그 push 시 자동 릴리즈
- **`vpk pack` 출력물**: `RELEASES`, `releases.win.json`, `assets.win.json`, `*-full.nupkg`,
  `OrbitalSetup.exe`, `*-win-Portable.zip` (자동 생성 — 우리 포터블과 중복이므로 삭제)
- **GitHub Release 파일 역할**
  - 사용자용: `OrbitalSetup.exe` (설치), `Orbital-{ver}-Portable.zip` (포터블)
  - Velopack 내부용: `RELEASES`, `releases.win.json`, `assets.win.json`, `*-full.nupkg`
  - GitHub Releases는 파일 숨기기 불가 — 내부 파일도 노출되나 사용자가 건드릴 필요 없음
