# Task 04 — Document Popup Mechanism in README and Settings Window

**Complexity**: Low — markdown + XAML text edits
**Model**: Small model OK

## Goal

1. Add a "How It Works" section to `README.md` explaining the two popup triggers.
2. Add a short description + GitHub link at the bottom of `SettingsWindow.xaml`.

---

## Part A — README.md

Find the section where the app is described (near the top, after the badges/title).
Insert a new `## How It Works` section with the following content:

```markdown
## How It Works

Orbital watches your mouse globally and shows a floating action bar above your cursor in two situations:

| Trigger | How to activate |
|---------|----------------|
| **Text selection** | Click and drag to select text anywhere on screen. When you release the mouse button, Orbital reads the selection and shows the action bar. |
| **Long press** | Hold the left mouse button (≥ 300 ms) without dragging in a text field. The action bar appears with clipboard/paste actions available. |

Click any action button to run it. Click anywhere else to dismiss.
```

---

## Part B — SettingsWindow.xaml

### B-1 — Add a "How It Works" hint inside the settings window

In `SettingsWindow.xaml`, locate the bottom `<StackPanel>` (Grid Row 4, the button row).
Add a new `<StackPanel>` **above** the button row (insert a new row before it) containing
a small description text and the GitHub hyperlink.

Add a new `RowDefinition` to the Grid (before the last "hdan 버튼" row):

```xml
<!-- Info / link row -->
<RowDefinition Height="Auto"/>
```

Then add this panel in that new row (adjust `Grid.Row` index accordingly — it becomes row 4,
and the button row becomes row 5):

```xml
<!-- ── Info & GitHub link ── -->
<StackPanel Grid.Row="4" Orientation="Vertical" Margin="0,10,0,0">
    <TextBlock FontSize="10" Foreground="#3A3A6A" TextWrapping="Wrap"
               Margin="0,0,0,4">
        Popup triggers: drag to select text, or hold left button (≥ 300 ms) in a text field.
    </TextBlock>
    <TextBlock FontSize="10">
        <Hyperlink NavigateUri="https://github.com/CrowKing63/Orbital"
                   RequestNavigate="Hyperlink_RequestNavigate">
            GitHub — CrowKing63/Orbital
        </Hyperlink>
    </TextBlock>
</StackPanel>
```

### B-2 — Handle `RequestNavigate` in code-behind

In `SettingsWindow.xaml.cs`, add this event handler:

```csharp
private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
{
    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
        FileName = e.Uri.AbsoluteUri,
        UseShellExecute = true
    });
    e.Handled = true;
}
```

Also add the namespace at the top of the file if missing:
```csharp
using System.Windows.Navigation;
```

---

## Files to Edit

| File | Change |
|------|--------|
| `README.md` | Add `## How It Works` section |
| `SettingsWindow.xaml` | New row + info panel with GitHub hyperlink |
| `SettingsWindow.xaml.cs` | Add `Hyperlink_RequestNavigate` handler |

## Verification Checklist

- [ ] README renders correctly with the new section (view in GitHub or a Markdown previewer)
- [ ] Settings window shows the description text and hyperlink at the bottom
- [ ] Clicking the hyperlink opens `https://github.com/CrowKing63/Orbital` in the default browser
- [ ] `dotnet build` succeeds with no errors
