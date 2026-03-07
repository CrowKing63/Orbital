# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Orbit**는 Windows 시스템 트레이 앱으로, 마우스로 텍스트를 드래그 선택하면 커서 위치에 방사형 메뉴(Radial Menu)를 띄워 LLM 기반 액션을 실행합니다. OpenAI 호환 API를 사용하며, 사용자 정의 프롬프트 액션을 지원합니다.

## Build & Run

```bash
# 빌드 (dotnet이 PATH에 없을 경우 전체 경로 사용)
"C:\Program Files\dotnet\dotnet.exe" build Orbit.csproj

# 실행 (빌드 후 exe 직접 실행)
./bin/Debug/net8.0-windows/Orbit.exe

# Release 빌드
"C:\Program Files\dotnet\dotnet.exe" build -c Release
```

- 테스트 프레임워크 없음 (수동 테스트만)
- WPF + WinForms 혼용 (`UseWPF`, `UseWindowsForms` 둘 다 활성화) — WinForms는 `NotifyIcon`(트레이)에만 사용
- **네임스페이스 충돌**: WPF+WinForms 공존으로 `Application`, `Clipboard`, `MessageBox`, `Button`, `IDataObject`, `Timer` 등이 모호해짐 → `GlobalUsings.cs`에서 전역 별칭으로 일괄 해소

## Architecture

### 핵심 데이터 흐름

```
[마우스 이벤트]
  SystemHookManager (Win32 WH_MOUSE_LL 훅)
    - LBUTTONDOWN: HTCLIENT 여부 확인 (제목 표시줄·스크롤바 드래그 필터링)
    - LBUTTONUP: 드래그 거리 > 8px → OnMouseUp 이벤트
    - 500ms 이상 정지 유지 → OnLongPress 이벤트 (텍스트 없이 메뉴 표시)

[App.xaml.cs]
  OnMouseUp  → Task.Run + 50ms 대기 → ClipboardHelper.GetSelectedText()
  OnLongPress → Dispatcher.Invoke → RadialMenuWindow.ShowAtCursor(text="")

[ClipboardHelper.GetSelectedText()]
  → 기존 클립보드 백업 → Ctrl+C 시뮬레이션 → 텍스트 추출 → 클립보드 복원

[RadialMenuWindow.ShowAtCursor()]
  → SettingsManager.Actions 기준 방사형 버튼 동적 생성
  → LLM 필요 액션(Replace/Copy/Popup)은 텍스트 없을 때 비활성화(Opacity 0.35)
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
| `RadialMenuWindow.xaml.cs` | 투명 WPF 창. 액션 수 기반 원형 배치. 텍스트 없을 때 LLM 버튼 비활성화 |
| `ResultTooltipWindow.xaml.cs` | AI 결과 팝업. 우측 하단 표시, 20초 자동 닫힘, 복사 버튼 |
| `SettingsManager.cs` | `ActionProfile`/`AppSettings` 모델. `%APPDATA%\Orbit\settings.json` 저장. DPAPI 암호화 |
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
- **클립보드 경쟁**: `GetSelectedText()`는 50ms 대기 후 Ctrl+C + 100ms 대기. 타이밍 민감. 기존 클립보드 백업 후 복원
- **드래그 필터링**: LBUTTONDOWN 위치에서 `WM_NCHITTEST`로 `HTCLIENT(1)` 여부 확인. 제목 표시줄·스크롤바 드래그 무시
- **API 키 없을 때**: `_actionExecutor`가 `null`. `RadialMenuWindow`에서 null 체크 후 MessageBox 표시
- **설정 변경 반영**: 설정창 닫힌 후 `App.RebuildActionExecutor()` 호출로 새 키·URL·모델 즉시 적용
- **DPI 처리**: `ShowAtCursor()`에서 `PresentationSource`로 DPI 스케일 보정 후 화면 경계 클램핑
- **OpenAI 호환 API**: `ApiBaseUrl` 변경으로 OpenRouter, LM Studio, Ollama 등 사용 가능. `ModelName`도 해당 제공자 형식으로 변경 필요
- **OpenRouter 무료 라우팅**: 모델 `openrouter/free` 사용 시 무료 모델 중 자동 선택 (크레딧 미소모)
