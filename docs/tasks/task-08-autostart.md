# Task 08 — Windows Startup Auto-Run Setting

**Complexity**: Low-Medium — registry + settings + UI checkbox
**Model**: Small model OK

## Goal

Add an opt-out option to launch Orbital automatically when Windows starts.
Default is **off** (opt-out). The user controls it via a checkbox in Settings.

Implementation uses the standard Windows user-level registry key:
`HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`

No admin privileges required.

---

## Step 1 — Add `RunAtStartup` to `AppSettings`

File: `SettingsManager.cs`

In the `AppSettings` class, add:

```csharp
public bool RunAtStartup { get; set; } = false;
```

---

## Step 2 — Add registry helpers to `SettingsManager`

File: `SettingsManager.cs`

Add at the top of the file:
```csharp
using Microsoft.Win32;
```

Add these two methods to `SettingsManager`:

```csharp
private const string RunRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
private const string RunRegistryValueName = "Orbital";

public static void ApplyStartupRegistry(bool enable)
{
    try
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true);
        if (key == null) return;

        if (enable)
        {
            string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                             ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            key.SetValue(RunRegistryValueName, $"\"{exePath}\"");
        }
        else
        {
            if (key.GetValue(RunRegistryValueName) != null)
                key.DeleteValue(RunRegistryValueName, throwOnMissingValue: false);
        }
    }
    catch { /* non-fatal: startup registry is best-effort */ }
}

public static bool IsStartupRegistryEnabled()
{
    try
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: false);
        return key?.GetValue(RunRegistryValueName) != null;
    }
    catch { return false; }
}
```

---

## Step 3 — Sync registry on app startup

File: `App.xaml.cs`

In `OnStartup`, after `SettingsManager.LoadSettings()`, add:

```csharp
// Sync startup registry with settings
if (SettingsManager.CurrentSettings.RunAtStartup != SettingsManager.IsStartupRegistryEnabled())
{
    SettingsManager.ApplyStartupRegistry(SettingsManager.CurrentSettings.RunAtStartup);
}
```

This handles the case where the exe path changed (reinstall/move).

---

## Step 4 — Add checkbox to `SettingsWindow.xaml`

File: `SettingsWindow.xaml`

Inside the API settings `<Border>` (the `<StackPanel>` that holds the grid), or in a new row below it, add:

```xml
<!-- ── Startup ── -->
<Border Grid.Row="..." Background="#11112A" CornerRadius="10"
        BorderBrush="#28285A" BorderThickness="1" Margin="0,0,0,16" Padding="16,12">
    <StackPanel>
        <TextBlock Text="Startup" FontSize="11" FontWeight="SemiBold"
                   Foreground="#5A5A90" Margin="0,0,0,10"/>
        <CheckBox x:Name="RunAtStartupCheck"
                  Content="Launch Orbital when Windows starts"
                  Foreground="#A0A0C0" FontSize="12"
                  Checked="RunAtStartupCheck_Changed"
                  Unchecked="RunAtStartupCheck_Changed"/>
    </StackPanel>
</Border>
```

Add a new `RowDefinition Height="Auto"` and assign the correct `Grid.Row` index.
Shift subsequent rows down by 1.

---

## Step 5 — Wire checkbox in `SettingsWindow.xaml.cs`

File: `SettingsWindow.xaml.cs`

### In `LoadSettings()`, add:

```csharp
RunAtStartupCheck.IsChecked = SettingsManager.CurrentSettings.RunAtStartup;
```

### Add new event handler:

```csharp
private void RunAtStartupCheck_Changed(object sender, RoutedEventArgs e)
{
    if (_suppressEvents) return;
    bool enable = RunAtStartupCheck.IsChecked == true;
    SettingsManager.CurrentSettings.RunAtStartup = enable;
    SettingsManager.ApplyStartupRegistry(enable);
    SettingsManager.SaveSettings();
}
```

---

## Files to Edit

| File | Change |
|------|--------|
| `SettingsManager.cs` | Add `RunAtStartup` property + registry helpers |
| `App.xaml.cs` | Sync registry on startup |
| `SettingsWindow.xaml` | Add Startup section with checkbox |
| `SettingsWindow.xaml.cs` | Load + handle checkbox state change |

## Verification Checklist

- [ ] Default: `RunAtStartup = false`, no registry entry is created
- [ ] Checking the box: `HKCU\...\Run\Orbital` entry is created with the exe path
- [ ] Unchecking the box: registry entry is removed
- [ ] Restarting the app with the box checked: Orbital appears in Windows startup (Task Manager > Startup tab)
- [ ] Moving the exe and re-launching: registry entry is updated to new path
- [ ] `dotnet build` succeeds
