# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Orbit**는 Windows 시스템 트레이 앱으로, 마우스로 텍스트를 드래그 선택하면 커서 위치에 방사형 메뉴(Radial Menu)를 띄워 LLM 기반 액션을 실행합니다. OpenAI 호환 API를 사용하며, 사용자 정의 프롬프트 액션을 지원합니다.

## Build & Run

```bash
# 빌드
dotnet build

# 실행 (빌드 후 exe 직접 실행)
./bin/Debug/net8.0-windows/Orbit.exe

# Release 빌드
dotnet build -c Release
```

- 테스트 프레임워크 없음 (수동 테스트만)
- WPF + WinForms 혼용 (`UseWPF`, `UseWindowsForms` 둘 다 활성화) — WinForms는 `NotifyIcon`(트레이)에만 사용

## Architecture

### 핵심 데이터 흐름

```
[마우스 드래그 감지]
  SystemHookManager (Win32 WH_MOUSE_LL 훅)
      ↓ OnMouseUp 이벤트
[App.xaml.cs]
      ↓ Task.Run + 50ms 대기
[ClipboardHelper.GetSelectedText()]
  → Ctrl+C 시뮬레이션 → 클립보드에서 텍스트 추출 → 원본 클립보드 복원
      ↓
[RadialMenuWindow.ShowAtCursor()]
  → 방사형 버튼 동적 생성 (Settings.Actions 기준)
      ↓ 버튼 클릭
[ActionExecutorService.ExecuteAsync()]
  → ResultAction 분기:
     "Replace" → LLM 호출 → ClipboardHelper.ReplaceSelectedText() (Ctrl+V 시뮬레이션)
     "Copy"    → LLM 호출 → Clipboard.SetText()
     "Popup"   → LLM 호출 → ResultTooltipWindow 표시
     "Browser" → Google 검색 URL로 브라우저 오픈 (LLM 미사용)
```

### 주요 컴포넌트

| 파일 | 역할 |
|------|------|
| `App.xaml.cs` | 앱 진입점. 트레이 아이콘, 마우스 훅, 컴포넌트 연결 관리 |
| `SystemHookManager.cs` | Win32 저수준 마우스 훅. 드래그 임계값(8px) 초과 시 OnMouseUp 이벤트 발생 |
| `ClipboardHelper.cs` | `SendInput` API로 Ctrl+C/V 시뮬레이션. 기존 클립보드 내용 백업/복원 |
| `RadialMenuWindow.xaml.cs` | 투명 WPF 창, 액션 수에 따라 원형으로 버튼 배치. 포커스 잃으면 자동 숨김 |
| `SettingsManager.cs` | `ActionProfile`/`AppSettings` 모델 정의. `%APPDATA%\Orbit\settings.json`에 저장. API 키는 Windows DPAPI로 암호화 |
| `Services/LlmApiService.cs` | `ILlmApiService` 인터페이스 + `OpenAiApiService` 구현 (기본 모델: `gpt-4o-mini`) |
| `Services/ActionExecutorService.cs` | 액션 타입별 실행 로직. 반드시 백그라운드 스레드에서 호출, UI는 Dispatcher 통해 처리 |
| `SettingsWindow.xaml.cs` | API 키·Base URL·액션 목록 관리 UI |
| `ActionEditDialog.xaml.cs` | 개별 ActionProfile 추가/편집 다이얼로그 |

### 설정 모델

```csharp
ActionProfile {
    Name: string          // 버튼에 표시될 이름
    PromptFormat: string  // "{text}" 플레이스홀더 포함 프롬프트
    ResultAction: string  // "Replace" | "Copy" | "Popup" | "Browser"
}
```

### 중요 제약사항

- **스레딩**: `SystemHookManager_OnMouseUp`은 `Task.Run`으로 백그라운드에서 실행됨. UI 접근은 항상 `Dispatcher.Invoke` 사용
- **클립보드 경쟁**: `GetSelectedText()`는 선택 완료 대기(50ms) 후 Ctrl+C 시뮬레이션 + 100ms 대기. 타이밍 민감
- **API 키 없을 때**: `_actionExecutor`가 `null`이 됨. `RadialMenuWindow`에서 null 체크 후 MessageBox 표시
- **DPI 처리**: `ShowAtCursor()`에서 `PresentationSource`로 DPI 스케일 보정
- **OpenAI 호환 API**: `ApiBaseUrl`을 변경하면 다른 OpenAI 호환 엔드포인트 사용 가능 (예: LM Studio, Ollama)
