# Popup Trigger System — Design Decisions & Guardrails

Each decision was reached after failed attempts — **check this document before changing anything "for improvement".**

## 1. Popup visibility: UIA-based (no proactive Ctrl+C)

Do NOT call `ClipboardHelper.GetSelectedText()` (Ctrl+C simulation) when deciding whether to show the popup.
Proactive Ctrl+C was removed because:
- Contaminates clipboard on rapid clicks
- CPU overhead (Ctrl+C on every click)

`ClipboardHelper.GetSelectedText()` is only called when the user **actually clicks a button** (`RadialMenuWindow.xaml.cs`).
`hasText` flag is determined via UIA `TextPattern`.

## 2. hasText = true (optimistic) — read-only Document

`ControlType.Document` + read-only (browser body etc.) → always set `hasText = true`.
Do NOT use UIA selection state to determine `hasText` because:
- Chrome/Edge don't update UIA TextPattern selection state immediately after mouse-up
- Still may be unreflected after 50–150ms delay
- Drag (8px+) is itself a clear signal of selection intent → handle optimistically

Actual text is retrieved via Ctrl+C at button click time. If no text selected, action silently aborts.

## 3. editability check position: dragStartPos

Call `CheckEditability()` with the drag **start** position, not the end position.
(`SystemHookManager.LastButtonDownPos` → `editCheckX/Y` param of `TriggerSelectionMenu`)

Reason: drags that end outside a text field boundary (eye-tracker users, field edges) —
the UIA element at the end position may not be a text field.

## 4. Client area detection: GetAncestor + GetWindowRect fallback

`_mouseDownInClient` detection order:
1. `GetAncestor(hwnd, GA_ROOT)` to get root window
2. `GetClientRect` + `ClientToScreen` to compute client area
3. If failed or empty rect → fall back to `GetWindowRect`

Reason: UWP (`Windows.UI.Core.CoreWindow`), WinUI 3, WebView2 child HWNDs may
return failure or wrong coords from `ClientToScreen`.
Fallback is less precise at title bar filtering, but better than not triggering at all.

## 5. UIA control type detection: one-level parent fallback

If `AutomationElement.FromPoint()` returns an unrecognized type (Custom, Text, leaf elements),
walk up one level with `ControlViewWalker.GetParent()` and re-classify.
Going deeper risks false positives in games / non-text apps.

## Popup trigger conditions summary

| Trigger | Condition |
|---------|-----------|
| Drag (8px+) | Start pos is Edit/ComboBox/Document → always show |
| Double-click | Edit/ComboBox/Document → always show (150ms delay) |
| Long-press (300ms) | Only over Edit or writable Document (IsKeyboardFocusable) |
| Ctrl+A / Shift+Arrow | Always show |
| Game / desktop / taskbar | `IsSystemShellWindow` or UIA unrecognized → no popup |

## Known unresolved issues (as of v0.2.4)

- **Windows Sticky Notes**: popup not working. All above fixes applied, no effect.
  Hypothesis: 2024 Win32 rewrite — UIA tree structure unknown. Use
  **Microsoft Accessibility Insights** (accessibilityinsights.io) or Windows SDK `Inspect.exe`
  to inspect ControlType of Sticky Notes text area directly.
- **Browser body double-click**: works but has 150ms delay.

## Related files

- `App.xaml.cs`: `CheckEditability()`, `IsOverEditableControl()`, `TriggerSelectionMenu()`
- `SystemHookManager.cs`: `IsPointInClientArea()`, `IsSystemShellWindow()`, `LastButtonDownPos`
