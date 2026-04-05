# Task 06 — Per-Button Display Mode (Text + Icon / Text Only / Icon Only)

**Complexity**: Medium — data model + dialog UI + rendering logic
**Model**: Small model can attempt; review carefully for XAML/binding issues

## Goal

Allow each action button to be configured individually to show:
- `TextAndIcon` (default — current behavior)
- `TextOnly` — show label, hide icon
- `IconOnly` — show icon glyph, hide label

The setting is stored per `ActionProfile` and editable in `ActionEditDialog`.

---

## Step 1 — Add `DisplayMode` to `ActionProfile`

File: `SettingsManager.cs`

Add an enum **before** the `ActionProfile` class:

```csharp
public enum ButtonDisplayMode
{
    TextAndIcon,
    TextOnly,
    IconOnly
}
```

Add the property to `ActionProfile`:

```csharp
public ButtonDisplayMode DisplayMode { get; set; } = ButtonDisplayMode.TextAndIcon;
```

Full updated `ActionProfile` class:

```csharp
public class ActionProfile
{
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string PromptFormat { get; set; } = string.Empty;
    public string ResultAction { get; set; } = ActionType.Popup.ToSerializedString();
    public bool? RequiresSelection { get; set; }
    public ButtonDisplayMode DisplayMode { get; set; } = ButtonDisplayMode.TextAndIcon;

    [JsonIgnore]
    public ActionType ActionType
    {
        get => ActionTypeExtensions.FromString(ResultAction ?? "Popup");
        set => ResultAction = value.ToSerializedString();
    }

    [JsonIgnore]
    public bool IsSelectionRequired => RequiresSelection ?? (ActionType != Orbital.ActionType.Paste);
}
```

---

## Step 2 — Update button content rendering in `RadialMenuWindow.xaml.cs`

File: `RadialMenuWindow.xaml.cs`

In `PopulateBarButtons`, replace the content-building block (the `if (!string.IsNullOrEmpty(action.Icon))` section) with logic that respects `DisplayMode`:

```csharp
object content;
bool showIcon = !string.IsNullOrEmpty(action.Icon)
    && action.DisplayMode != ButtonDisplayMode.TextOnly;
bool showText = action.DisplayMode != ButtonDisplayMode.IconOnly;

if (showIcon && showText)
{
    var stack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
    stack.Children.Add(new TextBlock
    {
        Text = action.Icon,
        FontFamily = _iconFont,
        FontSize = 14,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0)
    });
    stack.Children.Add(new TextBlock
    {
        Text = action.Name,
        VerticalAlignment = VerticalAlignment.Center
    });
    content = stack;
}
else if (showIcon)
{
    content = new TextBlock
    {
        Text = action.Icon,
        FontFamily = _iconFont,
        FontSize = 16,
        VerticalAlignment = VerticalAlignment.Center
    };
}
else
{
    content = action.Name;
}
```

---

## Step 3 — Add `DisplayMode` dropdown to `ActionEditDialog.xaml`

File: `ActionEditDialog.xaml`

The dialog currently has 5 rows (Name/Icon, Prompt, Output Mode, Options, Buttons).
Insert a new row **after** the "Output Mode" row (Grid.Row="2").

Add a new `RowDefinition`:
```xml
<RowDefinition Height="Auto"/>
```

Then shift existing rows 3 and 4 to 4 and 5.

Insert this new panel at Grid.Row="3":

```xml
<!-- Display mode -->
<StackPanel Grid.Row="3" Margin="0,0,0,14">
    <TextBlock Text="Button Display" FontSize="11" FontWeight="SemiBold"
               Foreground="#5A5A90" Margin="0,0,0,6"/>
    <ComboBox x:Name="DisplayModeBox" SelectedIndex="0">
        <ComboBoxItem Content="Text + Icon" Tag="TextAndIcon"/>
        <ComboBoxItem Content="Text Only"   Tag="TextOnly"/>
        <ComboBoxItem Content="Icon Only"   Tag="IconOnly"/>
    </ComboBox>
</StackPanel>
```

Update the Options and Button rows: `Grid.Row="4"` and `Grid.Row="5"` respectively.

---

## Step 4 — Wire DisplayMode in `ActionEditDialog.xaml.cs`

File: `ActionEditDialog.xaml.cs`

### In `LoadProfile` (or constructor where fields are populated):

```csharp
// Set DisplayModeBox selection
foreach (ComboBoxItem item in DisplayModeBox.Items)
{
    if (item.Tag?.ToString() == profile.DisplayMode.ToString())
    {
        DisplayModeBox.SelectedItem = item;
        break;
    }
}
```

### In `Ok_Click` (where `Result` is built):

```csharp
var selectedMode = (DisplayModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "TextAndIcon";
Result.DisplayMode = Enum.TryParse<ButtonDisplayMode>(selectedMode, out var mode)
    ? mode
    : ButtonDisplayMode.TextAndIcon;
```

---

## Files to Edit

| File | Change |
|------|--------|
| `SettingsManager.cs` | Add `ButtonDisplayMode` enum + `DisplayMode` property to `ActionProfile` |
| `RadialMenuWindow.xaml.cs` | Update content-building block to respect `DisplayMode` |
| `ActionEditDialog.xaml` | Add new row + ComboBox for Display mode |
| `ActionEditDialog.xaml.cs` | Load and save `DisplayMode` |

## Verification Checklist

- [ ] New action defaults to `TextAndIcon` — shows both icon and name
- [ ] Setting `TextOnly` hides the icon glyph, shows only the label
- [ ] Setting `IconOnly` shows only the icon glyph, no label
- [ ] `DisplayMode` is persisted in `settings.json` and survives restart
- [ ] Existing actions without `DisplayMode` in JSON load as `TextAndIcon` (default)
- [ ] `dotnet build` succeeds
