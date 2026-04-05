# Task 07 — Add a Light Theme (Claude Code Light Style)

**Complexity**: HIGH — WPF ResourceDictionary system, multi-file XAML refactor
**Model**: ⚠️ Advanced model required. Small models should NOT attempt this alone.

## Goal

Add a second visual theme alongside the current dark "space" theme.

| Theme | Style |
|-------|-------|
| **Dark** (current) | Deep space / nebula — dark navy bg, cyan/purple accents |
| **Light** | Claude Code Light — warm off-white bg, dark text, Claude orange accent |

The active theme is selected in Settings and persisted in `AppSettings`.

### Claude Code Light Palette

| Role | Color |
|------|-------|
| Background (popup pill) | `#F5F4EF` |
| Background (settings) | `#FAFAF7` |
| Panel background | `#EFEDE8` |
| Border | `#D4D0C8` |
| Primary text | `#1A1917` |
| Secondary text | `#6B6860` |
| Accent (Claude orange) | `#DA7756` |
| Button hover | `#EDE9E3` |
| Button pressed | `#E0DBD3` |
| Separator | `#D4D0C8` |

---

## Architecture Overview

The approach uses **WPF merged ResourceDictionaries** with dynamic theming:

1. Create two theme resource files: `Themes/Dark.xaml` and `Themes/Light.xaml`
2. Each defines the same set of named brushes/colors used across XAML files
3. `App.xaml` loads `Dark.xaml` by default
4. On theme change, swap the dictionary at runtime via code

---

## Step 1 — Create `Themes/` folder and theme files

### `Themes/Dark.xaml`

Extract the current hardcoded dark colors into named resources:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Popup pill -->
    <Color x:Key="PillBgColor">#CC0D0D22</Color>
    <Color x:Key="PillBorderColor">#507080FF</Color>
    <Color x:Key="PillGlowColor">#7080FF</Color>
    <Color x:Key="SeparatorColor">#28708000</Color>

    <!-- Buttons -->
    <Color x:Key="BtnFgColor">#C8C8FF</Color>
    <Color x:Key="BtnHoverBgColor">#2800D4FF</Color>
    <Color x:Key="BtnPressedBgColor">#4000D4FF</Color>
    <Color x:Key="BtnHoverFgColor">#00D4FF</Color>
    <Color x:Key="BtnDisabledFgColor">#30506080</Color>

    <!-- Settings window -->
    <SolidColorBrush x:Key="WindowBgBrush">#0A0A18</SolidColorBrush>
    <SolidColorBrush x:Key="PanelBgBrush">#11112A</SolidColorBrush>
    <SolidColorBrush x:Key="PanelBorderBrush">#28285A</SolidColorBrush>
    <SolidColorBrush x:Key="LabelFgBrush">#5A5A90</SolidColorBrush>
    <SolidColorBrush x:Key="TitleFgBrush">#8090FF</SolidColorBrush>
    <SolidColorBrush x:Key="SubtitleFgBrush">#9898C8</SolidColorBrush>
    <SolidColorBrush x:Key="AccentBrush">#7080FF</SolidColorBrush>
</ResourceDictionary>
```

### `Themes/Light.xaml`

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Popup pill -->
    <Color x:Key="PillBgColor">#F0F5F4EF</Color>
    <Color x:Key="PillBorderColor">#80D4D0C8</Color>
    <Color x:Key="PillGlowColor">#DA7756</Color>
    <Color x:Key="SeparatorColor">#60D4D0C8</Color>

    <!-- Buttons -->
    <Color x:Key="BtnFgColor">#1A1917</Color>
    <Color x:Key="BtnHoverBgColor">#40EDE9E3</Color>
    <Color x:Key="BtnPressedBgColor">#60E0DBD3</Color>
    <Color x:Key="BtnHoverFgColor">#DA7756</Color>
    <Color x:Key="BtnDisabledFgColor">#80B0B0A8</Color>

    <!-- Settings window -->
    <SolidColorBrush x:Key="WindowBgBrush">#FAFAF7</SolidColorBrush>
    <SolidColorBrush x:Key="PanelBgBrush">#EFEDE8</SolidColorBrush>
    <SolidColorBrush x:Key="PanelBorderBrush">#D4D0C8</SolidColorBrush>
    <SolidColorBrush x:Key="LabelFgBrush">#6B6860</SolidColorBrush>
    <SolidColorBrush x:Key="TitleFgBrush">#1A1917</SolidColorBrush>
    <SolidColorBrush x:Key="SubtitleFgBrush">#6B6860</SolidColorBrush>
    <SolidColorBrush x:Key="AccentBrush">#DA7756</SolidColorBrush>
</ResourceDictionary>
```

---

## Step 2 — Update `App.xaml` to merge a theme dictionary

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary x:Name="ThemeDict" Source="Themes/Dark.xaml"/>
            <ResourceDictionary Source="Styles/Base.xaml"/>  <!-- existing styles remain here -->
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

---

## Step 3 — Replace hardcoded colors in XAML with `DynamicResource`

This is the most tedious part. In `RadialMenuWindow.xaml` and `SettingsWindow.xaml`
and `ActionEditDialog.xaml`, replace every hardcoded color with the named keys.

Examples:
- `Background="#CC0D0D22"` → `Background="{DynamicResource PillBgBrush}"` (create a Brush resource from the Color)
- `Foreground="#C8C8FF"` → `Foreground="{DynamicResource BtnFgBrush}"`

> **Note**: In XAML, `DynamicResource` is required (not `StaticResource`) when the dictionary
> will be swapped at runtime.

---

## Step 4 — Add `Theme` property to `AppSettings`

File: `SettingsManager.cs`

```csharp
public class AppSettings
{
    // ... existing fields ...
    public string Theme { get; set; } = "Dark";  // "Dark" | "Light"
}
```

---

## Step 5 — Theme switching logic

File: `App.xaml.cs`

Add a public static method:

```csharp
public static void ApplyTheme(string themeName)
{
    var dict = Current.Resources.MergedDictionaries
        .FirstOrDefault(d => d.Source != null &&
            (d.Source.OriginalString.Contains("Dark.xaml") ||
             d.Source.OriginalString.Contains("Light.xaml")));

    if (dict != null)
        Current.Resources.MergedDictionaries.Remove(dict);

    string path = themeName == "Light" ? "Themes/Light.xaml" : "Themes/Dark.xaml";
    Current.Resources.MergedDictionaries.Insert(0, new ResourceDictionary
    {
        Source = new Uri(path, UriKind.Relative)
    });

    SettingsManager.CurrentSettings.Theme = themeName;
    SettingsManager.SaveSettings();
}
```

Call on startup in `OnStartup`:
```csharp
App.ApplyTheme(SettingsManager.CurrentSettings.Theme);
```

---

## Step 6 — Add Theme selector in `SettingsWindow.xaml`

Add a new row in the API settings grid (or a separate row below it):

```xml
<StackPanel Grid.Row="..." Orientation="Horizontal" Margin="0,0,0,10">
    <TextBlock Text="Theme" FontSize="11" Foreground="{DynamicResource LabelFgBrush}"
               VerticalAlignment="Center" Margin="0,0,12,0"/>
    <ComboBox x:Name="ThemeBox" Width="120" SelectionChanged="ThemeBox_SelectionChanged">
        <ComboBoxItem Content="Dark"  Tag="Dark"/>
        <ComboBoxItem Content="Light" Tag="Light"/>
    </ComboBox>
</StackPanel>
```

In `SettingsWindow.xaml.cs`:

```csharp
private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (_suppressEvents) return;
    if (ThemeBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        App.ApplyTheme(tag);
}
```

Also set the initial selection in `LoadSettings()`:
```csharp
foreach (ComboBoxItem item in ThemeBox.Items)
{
    if (item.Tag?.ToString() == (SettingsManager.CurrentSettings.Theme ?? "Dark"))
    {
        ThemeBox.SelectedItem = item;
        break;
    }
}
```

---

## Files to Edit / Create

| File | Change |
|------|--------|
| `Themes/Dark.xaml` | **Create** — extract current dark colors as named resources |
| `Themes/Light.xaml` | **Create** — Claude Code Light palette |
| `App.xaml` | Merge theme dictionary, add `ApplyTheme` call |
| `App.xaml.cs` | Add `ApplyTheme(string)` static method |
| `RadialMenuWindow.xaml` | Replace hardcoded colors with `DynamicResource` |
| `SettingsWindow.xaml` | Replace hardcoded colors + add Theme ComboBox |
| `ActionEditDialog.xaml` | Replace hardcoded colors with `DynamicResource` |
| `SettingsManager.cs` | Add `Theme` property to `AppSettings` |
| `SettingsWindow.xaml.cs` | Load/switch theme |

## Verification Checklist

- [ ] App starts in Dark theme by default
- [ ] Switching to Light in Settings immediately changes popup + settings window appearance
- [ ] Theme choice is persisted — survives app restart
- [ ] All UI elements are visible and readable in both themes
- [ ] No hardcoded colors remain in XAML (grep for `#[0-9A-Fa-f]` in xaml files to verify)
- [ ] `dotnet build` succeeds
