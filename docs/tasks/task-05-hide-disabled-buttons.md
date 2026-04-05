# Task 05 — Hide Disabled Buttons When No Text Is Selected

**Complexity**: Low — single method change
**Model**: Small model OK

## Goal

When no text is selected (e.g., long-press trigger), buttons that require
selected text are currently shown at 40% opacity. Instead, hide them entirely.
Only buttons that can actually run (e.g., Paste) should be visible.

## File to Edit

`RadialMenuWindow.xaml.cs`

## Current Code (around line 28)

```csharp
private void PopulateBarButtons(bool hasText)
{
    ButtonPanel.Children.Clear();

    var actions = SettingsManager.CurrentSettings?.Actions;
    if (actions == null || actions.Count == 0) return;

    for (int i = 0; i < actions.Count; i++)
    {
        // separator (skip for first button)
        if (i > 0)
        {
            ButtonPanel.Children.Add(new Border { ... });
        }

        var action = actions[i];
        bool enabled = hasText || !action.IsSelectionRequired;

        // ... build content ...

        var btn = new Button
        {
            ...
            IsEnabled = enabled,
            Opacity = enabled ? 1.0 : 0.4
        };

        btn.Click += ActionButton_Click;
        ButtonPanel.Children.Add(btn);
    }
}
```

## Changes Required

1. **Skip hidden buttons entirely** — do not add them or their separator.
2. **Fix separator logic** — the separator should only appear before a visible button, and never before the first visible button.

## New Code

Replace the entire `PopulateBarButtons` method with:

```csharp
private void PopulateBarButtons(bool hasText)
{
    ButtonPanel.Children.Clear();

    var actions = SettingsManager.CurrentSettings?.Actions;
    if (actions == null || actions.Count == 0) return;

    bool firstVisible = true;

    foreach (var action in actions)
    {
        bool enabled = hasText || !action.IsSelectionRequired;

        // When no text is selected, completely hide buttons that require selection
        if (!enabled)
            continue;

        // Separator before every button except the first visible one
        if (!firstVisible)
        {
            ButtonPanel.Children.Add(new Border
            {
                Width = 1,
                Height = 18,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x28, 0x70, 0x80, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            });
        }

        // Create content with icon if available
        object content;
        if (!string.IsNullOrEmpty(action.Icon))
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
        else
        {
            content = action.Name;
        }

        var btn = new Button
        {
            Content = content,
            Tag = action,
            Style = (Style)FindResource("BarButtonStyle"),
            IsEnabled = true,
            Opacity = 1.0
        };

        btn.Click += ActionButton_Click;
        ButtonPanel.Children.Add(btn);
        firstVisible = false;
    }
}
```

## Verification Checklist

- [ ] With text selected: all actions are visible
- [ ] Without text (long-press): only Paste (and other non-LLM, non-selection-required actions) are visible
- [ ] No extra separator appears before the first visible button
- [ ] No separator appears after the last visible button
- [ ] `dotnet build` succeeds
