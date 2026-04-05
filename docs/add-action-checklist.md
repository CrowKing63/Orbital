# Adding a New Action Type — Checklist

When adding a new `ActionType`, **all** files below must be modified. Missing any one causes broken functionality or invisible actions in the settings UI.

| # | File | What to do |
|---|------|------------|
| 1 | `ActionType.cs` | Add new value to enum + `TryFromString` switch case (`RequiresLlm` defaults to false for non-LLM actions) |
| 2 | `ClipboardHelper.cs` | Add key simulation methods needed for the action (including VK constants) |
| 3 | `Services/ActionExecutorService.cs` | Add new case to `ExecuteAsync()` switch (upper switch for non-LLM, lower switch for LLM) |
| 4 | `RadialMenuWindow.xaml.cs` | Decide `requiresWrite` group membership in `PopulateBarButtons()`. Add branch in `ActionButton_Click` if special behavior needed (e.g. SelectAll re-showing popup) |
| 5 | `SettingsManager.cs` | Add to default action list in `CreateDefaultSettings()` (for new installs) |
| 6 | `ActionPresets.cs` | Add to appropriate category section in `All` list (shown in Settings → Add Action → Library) |
| 6a | `ActionEditDialog.xaml` | Add `<ComboBoxItem Tag="YourType" Content="{DynamicResource Str_ActionYourType}" .../>` to `ResultActionBox` ComboBox — **missing this causes preset import to fall back to index 0 (Replace), pasting clipboard content as a bug** |
| 6b | `Strings/Strings.*.xaml` | Add `Str_ActionYourType` + `Str_ActionYourTypeTooltip` to **all 10 language files** (en, ko, de, fr, es, ja, zh, ru, it, pt) |
| 7 | `README.md` | Add new row to Action Types table |
| 8 | `Orbital.csproj` | Bump version patch (e.g. 0.2.5 → 0.2.6) |

## requiresWrite flag

`requiresWrite` in `RadialMenuWindow.PopulateBarButtons()` controls **whether to hide the button on read-only controls** (browser body etc.).

- `true`: actions that write or delete in the target document (Paste, Cut, SimulateKey, Replace)
- `false`: actions valid in read-only contexts (DirectCopy, Browser, Popup, **SelectAll**, etc.)

## ActionPresets.cs category structure

```
// ── Utility (no LLM) ──   Copy, Cut, Paste, Select All, Search
// ── Translation ───────   Translate to Korean/English/Japanese
// ── Writing ───────────   Polish, Formal, Casual, Shorter, Bullet Points
// ── Analysis (Popup) ──   Summarize, Explain, ELI5, Fix Code
```

Insert new LLM actions into the appropriate category.

## Actions requiring special behavior

Actions that can't be handled by `ActionExecutorService.ExecuteAsync()` alone (e.g. re-showing popup after action) should be handled with a separate branch in `RadialMenuWindow.ActionButton_Click()`. Branch **before** calling `ExecuteAsync` and `return` to prevent the call.

```csharp
if (action.ActionType == ActionType.YourNewType)
{
    await Task.Run(async () =>
    {
        // special behavior
    });
    return; // prevent ExecuteAsync call
}
```
