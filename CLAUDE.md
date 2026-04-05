# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## App Name

The application is named **Orbital**. Always use "Orbital" — never "Orbit" — when referring to the app by name in UI strings, documentation, commit messages, and comments.

## Language Policy

**All project artifacts must be written in English.** This includes:
- UI strings (labels, buttons, tooltips, error messages, window titles)
- Code comments, commit messages, documentation

Conversation between the developer and Claude may be in Korean. Code artifacts are always English.

## Project Overview

**Orbital**은 Windows 시스템 트레이 앱으로, 마우스로 텍스트를 드래그 선택하면 커서 위에 수평 바형 메뉴(Bar Menu)를 띄워 LLM 기반 액션을 실행합니다. OpenAI 호환 API를 사용하며, 사용자 정의 프롬프트 액션을 지원합니다.

## Build & Run

```bash
"C:\Program Files\dotnet\dotnet.exe" build Orbital.csproj
./bin/Debug/net8.0-windows/Orbital.exe
"C:\Program Files\dotnet\dotnet.exe" build -c Release
```

- 테스트 프레임워크 없음 (수동 테스트만)
- WPF + WinForms 혼용 (`UseWPF`, `UseWindowsForms`) — WinForms는 `NotifyIcon`(트레이)에만 사용
- **네임스페이스 충돌**: `Application`, `Clipboard`, `MessageBox`, `Button`, `IDataObject`, `Timer` → `GlobalUsings.cs`에서 전역 별칭. **`Color`는 GlobalUsings에 없음** — `System.Windows.Media.Color`로 완전 경로 사용

## Architecture

### 핵심 데이터 흐름

```
[마우스 이벤트] SystemHookManager (Win32 WH_MOUSE_LL)
  LBUTTONUP (drag >8px) → Task.Run + 50ms → CheckEditability(dragStartPos) → ShowAtCursor
  DoubleClick           → Task.Run + 150ms → CheckEditability(cursorPos)   → ShowAtCursor
  LongPress (300ms)     → Task.Run → IsOverEditableControl() → ShowAtCursor(hasText=false)
  KeyboardSelection     → Task.Run → ShowAtCursor(isEditable=true, hasText=true)

[CheckEditability() — UIA]
  Edit / ComboBox               → (canSelect=true, canWrite=true,  hasText=UIA선택여부)
  Document (writable)           → (true, true,  UIA선택여부)
  Document (read-only)          → (true, false, true)  ← 브라우저 본문 (낙관적)
  미인식 리프 → 부모 1단계 재적용
  그 외 → (false, false, false) → 팝업 미표시

[ActionExecutorService.ExecuteAsync()]
  Replace    → LLM → ClipboardHelper.ReplaceSelectedText() (Ctrl+V)
  Copy       → LLM → Clipboard.SetText()
  Popup      → LLM → ResultTooltipWindow
  DirectCopy → Clipboard.SetText(selectedText)  (no LLM)
  Cut        → Clipboard.SetText() + Delete key (no LLM)
  Paste      → Ctrl+V simulation               (no LLM)
  Browser    → Google search URL               (no LLM)
```

### 주요 컴포넌트

| 파일 | 역할 |
|------|------|
| `App.xaml.cs` | 앱 진입점. 트레이 아이콘, 마우스 훅, ActionExecutor 초기화/재생성 |
| `GlobalUsings.cs` | WPF/WinForms 타입 충돌 전역 별칭 |
| `SystemHookManager.cs` | Win32 WH_MOUSE_LL 훅. HTCLIENT 필터 + 8px 드래그 임계값 |
| `ClipboardHelper.cs` | `SendInput`으로 Ctrl+C/V/Delete 시뮬레이션. 클립보드 백업/복원 |
| `RadialMenuWindow.xaml.cs` | 투명 WPF 창(pill 바형). 텍스트 없을 때 LLM 버튼 비활성화 |
| `ResultTooltipWindow.xaml.cs` | AI 결과 팝업. 우측 하단, 20초 자동 닫힘, 복사 버튼 |
| `SettingsManager.cs` | `ActionProfile`/`AppSettings`. `%APPDATA%\Orbital\settings.json`. DPAPI 암호화 |
| `Services/LlmApiService.cs` | `ILlmApiService` + `OpenAiApiService`. 전체 URL 직접 조립 |
| `Services/ActionExecutorService.cs` | ResultAction별 분기. 반드시 백그라운드 스레드에서 호출 |
| `SettingsWindow.xaml.cs` | 제공자 드롭다운 → URL·모델 자동 완성. API 키·액션 CRUD |
| `ActionEditDialog.xaml.cs` | 개별 ActionProfile 추가/편집 다이얼로그 |

### 설정 모델

```csharp
ActionProfile { Name, PromptFormat,  // "{text}" placeholder
                ResultAction }       // "Replace"|"Copy"|"Popup"|"Browser"|"DirectCopy"|"Cut"|"Paste"
AppSettings   { EncryptedApiKey, ApiBaseUrl, ModelName, Actions }
```

### 중요 제약사항

- **스레딩**: UI 접근은 항상 `Dispatcher.Invoke`
- **클립보드**: `GetSelectedText()`는 버튼 클릭 시에만 호출 (팝업 표시 전 프로액티브 호출 금지) → [popup-trigger-design.md](docs/popup-trigger-design.md)
- **드래그 필터링**: `GetAncestor(GA_ROOT)` + `IsPointInClientArea()` + `GetWindowRect` 폴백
- **API 키 없을 때**: `_actionExecutor`가 `null` → `RadialMenuWindow`에서 MessageBox
- **설정 변경 반영**: 설정창 닫힌 후 `App.RebuildActionExecutor()` 호출
- **DPI 처리**: `PresentationSource`로 DPI 스케일 보정 후 화면 경계 클램핑
- **OpenAI 호환 API**: `ApiBaseUrl` 변경으로 OpenRouter, LM Studio, Ollama 등 사용 가능

## Popup Trigger System

> 설계 결정 및 가드레일 전체: [docs/popup-trigger-design.md](docs/popup-trigger-design.md)

**핵심 규칙**: 팝업 표시 여부 판단 시 Ctrl+C 호출 금지. UIA TextPattern으로만 판별.
read-only Document(브라우저)는 `hasText = true`로 낙관적 처리.
`CheckEditability()`는 드래그 **시작** 위치로 호출 (`LastButtonDownPos`).

## Adding a New Action Type

> 전체 체크리스트: [docs/add-action-checklist.md](docs/add-action-checklist.md)

수정 필요 파일 요약: `ActionType.cs` → `ClipboardHelper.cs` → `ActionExecutorService.cs` → `RadialMenuWindow.xaml.cs` → `SettingsManager.cs` → `ActionPresets.cs` → `ActionEditDialog.xaml` → `Strings/*.xaml` (10개 언어) → `README.md` → `Orbital.csproj` (버전 bump)

**주의**: `ActionEditDialog.xaml`의 `ResultActionBox`에 ComboBoxItem 누락 시 프리셋 로드가 index 0(Replace)으로 폴백되는 버그 발생.

## Release & Auto-Update

> 상세: [docs/release-guide.md](docs/release-guide.md)

Velopack (`vpk` dotnet global tool) 사용. `v*` 태그 push → GitHub Actions 자동 릴리즈.
