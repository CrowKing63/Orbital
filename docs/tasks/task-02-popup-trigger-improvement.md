# Task 02 — Smarter Popup Trigger Conditions

**Complexity**: HIGH — requires Win32 UI Automation API
**Model**: ⚠️ Advanced model required. Do NOT assign to small models.

## Goal

Currently the popup fires even when the user drags or long-presses over
non-text areas (desktop, toolbars, images, etc.).

Desired behavior:
- **Drag selection**: Only show popup when `GetSelectedText()` actually returns text.
  *(Already partially guarded — investigate why it still fires spuriously.)*
- **Long-press**: Only fire when the cursor is over a **text-editable control**
  (Edit box, rich text editor, browser text field, etc.).

## Root Cause Analysis (do this first)

### Bug A — Spurious drag popup

`App.xaml.cs` line 195 already guards: `if (!string.IsNullOrWhiteSpace(selectedText))`.
If popups still appear without text, the likely cause is that `ClipboardHelper.GetSelectedText()`
is returning stale clipboard content (not from the drag selection).

Check `ClipboardHelper.cs`:
- Confirm the old clipboard backup is restored correctly when `Ctrl+C` yields no new text.
- Confirm `GetSelectedText()` returns `string.Empty` when clipboard didn't change after `Ctrl+C`.

### Bug B — Long-press fires everywhere

`SystemHookManager_OnLongPress` in `App.xaml.cs` line 207 calls
`_radialMenu.ShowAtCursor(e.X, e.Y, string.Empty)` with no context check.

## Implementation Plan

### Part 1 — Fix ClipboardHelper (if Bug A is confirmed)

File: `ClipboardHelper.cs`

- Before sending `Ctrl+C`, snapshot the clipboard.
- After the 100 ms wait, compare new clipboard content to the snapshot.
- If they are identical (no new selection was copied), return `string.Empty`.

### Part 2 — Text-field detection for long-press

File: `SystemHookManager.cs` (add helper) + `App.xaml.cs` (use it)

Use **UI Automation** (`System.Windows.Automation`) to check the element under the cursor:

```csharp
// Add NuGet: none needed — UIAutomationClient is included in .NET 8 Windows
using System.Windows.Automation;

public static bool IsOverEditableControl(int screenX, int screenY)
{
    try
    {
        var element = AutomationElement.FromPoint(new System.Windows.Point(screenX, screenY));
        if (element == null) return false;

        var controlType = element.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;

        // Accept: Edit, Document, and custom types that support ValuePattern or TextPattern
        if (controlType == ControlType.Edit || controlType == ControlType.Document)
            return true;

        // Check for ValuePattern (generic editable controls, e.g. browser inputs)
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out _))
        {
            var readOnly = element.GetCurrentPropertyValue(ValuePatternIdentifiers.IsReadOnlyProperty);
            if (readOnly is bool b && !b) return true;
        }

        // Check for TextPattern (rich text editors)
        if (element.TryGetCurrentPattern(TextPattern.Pattern, out _))
            return true;

        return false;
    }
    catch
    {
        return false; // fail open: don't block popup on error
    }
}
```

In `App.xaml.cs`, modify `SystemHookManager_OnLongPress`:

```csharp
private void SystemHookManager_OnLongPress(object? sender, SystemHookManager.MousePoint e)
{
    if (!IsOverEditableControl(e.X, e.Y)) return;  // ← add this guard

    Dispatcher.Invoke(() =>
    {
        _radialMenu.ShowAtCursor(e.X, e.Y, string.Empty);
    });
}
```

Place `IsOverEditableControl` as a private static method in `App.xaml.cs`.

## Caveats & Edge Cases

- **Browser text fields**: Chromium/Edge expose `ControlType.Edit` via UIA — works.
- **Electron apps**: UIA support varies. `TryGetCurrentPattern` fallback handles most cases.
- **Performance**: `AutomationElement.FromPoint` is synchronous but fast (<5 ms). Safe to call in the hook callback thread.
- **Accessibility permissions**: UIA requires no special permissions on Windows 10/11.
- **fail-open policy**: On any exception, return `false` (suppress popup) to avoid crashing the hook.

## Files to Edit

| File | Change |
|------|--------|
| `ClipboardHelper.cs` | Fix stale-clipboard false-positive (Part 1) |
| `App.xaml.cs` | Add `IsOverEditableControl()` + guard in `OnLongPress` handler |

## Verification Checklist

- [ ] Long-press on desktop → no popup
- [ ] Long-press on browser address bar → popup appears
- [ ] Long-press on Notepad text area → popup appears
- [ ] Long-press on a button label → no popup
- [ ] Drag over image with no text → no popup
- [ ] Drag to select text in Notepad → popup appears
