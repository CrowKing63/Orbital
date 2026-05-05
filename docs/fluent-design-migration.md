# Windows 11 Fluent Design Migration Plan

**Date**: 2026-05-05  
**Status**: Proposed  
**Priority**: Medium

---

## Goal

Reduce the visual gap between Orbital and modern Windows 11 apps without turning the work into a long-running UI rewrite.

This document scopes the migration into short, independent phases that can be:

- built and tested separately,
- committed separately,
- released separately if needed,
- interrupted by unrelated work without causing merge or rollout confusion.

---

## Current Constraints

### Architecture

- Orbital is a WPF desktop app targeting `.NET 8`.
- The tray menu uses WinForms `NotifyIcon`.
- Theme switching is already centralized through XAML resource dictionaries.
- Many core controls already use custom global templates.

### Important UI Constraints

- `SettingsWindow` and `ActionEditDialog` are normal windows and are realistic candidates for deeper Windows 11 styling.
- `RadialMenuWindow` and `ResultTooltipWindow` are transparent overlay windows using `WindowStyle="None"` and `AllowsTransparency="True"`.
- Because of that overlay structure, true Windows 11 backdrop effects such as Mica/Acrylic are not a safe first step for the popup surfaces.

### Scope Implication

- We should **not** frame this work as a full Fluent parity effort.
- We **should** frame it as a staged modernization of colors, spacing, control feel, and selective window treatment.

---

## What Is Realistic

### High Confidence

- Move the palette closer to Windows 11 neutral tones.
- Reduce the current heavy violet/cyan visual bias.
- Improve spacing, corner radius consistency, and control density.
- Refine global button, input, combo box, and list styles.
- Add better system theme and system accent integration.

### Medium Confidence

- Apply more Windows 11-like window treatment to `SettingsWindow`.
- Apply the same treatment to `ActionEditDialog`.
- Add optional DWM-based backdrop experiments for standard windows only.

### Low Confidence / Not Phase 1

- True Mica/Acrylic treatment for transparent overlay windows.
- Full adoption of a third-party Fluent control library.
- Perfect visual parity with WinUI 3 apps.

---

## Non-Goals

- No full WinUI rewrite.
- No broad dependency migration as part of the first rollout.
- No redesign of the popup interaction model.
- No attempt to make every surface use native Windows 11 materials.

---

## Delivery Strategy

Each phase below is designed to be safe for:

- `build`
- `test`
- `commit`
- optional `release`

after that phase alone.

Each phase should avoid partial foundations that require the next phase to make the app shippable.

---

## Phase 1: Token Cleanup

### Objective

Establish a safer design foundation without changing window behavior.

### Scope

- Refine theme color tokens in `Themes/Dark.xaml` and `Themes/Light.xaml`.
- Introduce Windows 11-inspired neutral surfaces and calmer accent usage.
- Normalize semantic token intent:
  - surface background,
  - panel background,
  - input background,
  - hover,
  - border,
  - accent,
  - critical/destructive.
- Keep existing resource keys where possible to minimize XAML churn.

### Files

- `Themes/Dark.xaml`
- `Themes/Light.xaml`

### Why This Phase Is Safe

- No control structure changes.
- No layout changes.
- Easy to review visually.
- Low merge risk with unrelated feature work.

### Exit Criteria

- App builds.
- Dark and Light themes both load.
- No missing resource keys.
- Popup, tooltip, settings, and dialogs remain visually coherent.

### Commit Guidance

Recommended as a standalone commit.

---

## Phase 2: Global Control Styling

### Objective

Modernize the feel of common controls while keeping the app behavior unchanged.

### Scope

- Adjust global styles in `App.xaml` for:
  - `Button`
  - `TextBox`
  - `PasswordBox`
  - `ComboBox`
  - `ListView`
  - `GridViewColumnHeader`
- Reduce heavy glow usage.
- Tune corner radius, padding, border contrast, and hover states.
- Prefer subtle elevation over bright accent-driven focus effects.

### Files

- `App.xaml`

### Why This Phase Is Safe

- Centralized change set.
- No code-behind changes required.
- Behavior should remain stable if resource names are preserved.

### Risks

- Overly aggressive template changes could regress sizing or readability.
- ComboBox and ListView templates need basic manual validation.

### Exit Criteria

- App builds.
- Settings window remains usable in both themes.
- Dialog fields remain readable and keyboard-focus states are clear.

### Commit Guidance

Ship as its own commit after a quick manual UI pass.

---

## Phase 3: Settings Window Modernization

### Objective

Improve the main management surface first, where Windows 11 styling has the highest payoff.

### Scope

- Refine layout, grouping, section hierarchy, and spacing in `SettingsWindow.xaml`.
- Make the header and panels feel more native to Windows 11.
- Reduce decorative glow if it fights readability.
- Keep existing settings flow and control bindings intact.

### Files

- `SettingsWindow.xaml`
- optional small code-behind adjustments in `SettingsWindow.xaml.cs`

### Why This Phase Is Safe

- Isolated to one window.
- High user-visible value.
- Does not affect popup activation logic or core action execution.

### Risks

- Layout regressions at smaller window widths.
- List/action area may need sizing adjustments.

### Exit Criteria

- App builds.
- Settings window works at its supported minimum size.
- Theme/language switching still updates correctly.
- Action list, import/export, and hotkey editing remain usable.

### Commit Guidance

Safe as a separate shippable increment.

---

## Phase 4: Action Dialog Alignment

### Objective

Bring secondary management UI into the same visual system as the settings window.

### Scope

- Update `ActionEditDialog.xaml` styling and spacing.
- Align field labels, button emphasis, and section grouping with Phase 3.
- Avoid changing validation or save behavior.

### Files

- `ActionEditDialog.xaml`
- optional small code-behind adjustments in `ActionEditDialog.xaml.cs`

### Why This Phase Is Safe

- Narrow surface area.
- No popup/selection behavior impact.
- Easy to validate manually.

### Exit Criteria

- App builds.
- Add/Edit action flows still work.
- Preset loading, save, and cancel behavior remain unchanged.

### Commit Guidance

Stand-alone commit recommended.

---

## Phase 5: System Accent Integration

### Objective

Make the app feel more native without introducing heavy platform coupling early.

### Scope

- Evaluate using Windows accent color for selected semantic tokens.
- Limit accent adoption to focus, selected states, and primary actions.
- Keep fallback colors for unsupported or disabled scenarios.

### Files

- `App.xaml.cs`
- `Themes/Dark.xaml`
- `Themes/Light.xaml`
- possibly `App.xaml`

### Why This Phase Is Safe

- Can be added after the visual baseline is already stable.
- If implementation is noisy, it can be deferred without blocking earlier work.

### Risks

- Accent-derived colors may reduce contrast in some user themes.
- Requires careful fallback behavior.

### Exit Criteria

- App builds.
- Accent behavior degrades safely when unavailable.
- Contrast remains acceptable in both themes.

### Commit Guidance

Do not combine with large styling rewrites. Keep this phase isolated.

---

## Phase 6: Standard Window Backdrop Prototype

### Objective

Prototype Windows 11 backdrop treatment only where it is technically reasonable.

### Scope

- Experiment with DWM backdrop APIs on:
  - `SettingsWindow`
  - `ActionEditDialog`
- Add Windows version guards and graceful fallback.
- Do not apply this to transparent overlay popup windows in the same phase.

### Files

- `SettingsWindow.xaml.cs`
- `ActionEditDialog.xaml.cs`
- possibly shared helper code

### Why This Phase Is Separate

- This is the first phase that depends on OS-specific behavior.
- It should not be mixed with basic styling work.
- It may be rejected or rolled back independently.

### Risks

- Inconsistent behavior across Windows versions.
- Visual mismatch if backdrop and token palette fight each other.

### Exit Criteria

- App builds.
- Supported systems show the intended effect.
- Unsupported systems fall back cleanly.
- No crashes or broken window initialization.

### Commit Guidance

Prototype and release separately. Do not block earlier phases on this.

---

## Explicit Deferrals

These items should stay out of the main migration track unless a later spike proves clear value:

- adopting `ModernWpf`,
- adopting `WPF-UI`,
- reworking popup windows away from transparent overlays,
- trying to force Mica/Acrylic onto `RadialMenuWindow`,
- broad re-templating driven by a third-party design system.

Those are architecture decisions, not polishing tasks.

---

## Suggested Implementation Order

1. Phase 1
2. Phase 2
3. Phase 3
4. Phase 4
5. Phase 5
6. Phase 6

This order front-loads low-risk visual wins and pushes platform-specific experiments to the end.

---

## Review Rule For Each Phase

Before merging or releasing a phase:

1. Build the app.
2. Open the settings window in both themes.
3. Verify popup surfaces still render correctly.
4. Verify no control lost focus visibility or contrast.
5. Keep the phase commit small enough that rollback is trivial.

---

## Decision

**Proceed with a phased hybrid approach.**

Success should be defined as:

- Orbital feels more aligned with Windows 11,
- the settings and dialog surfaces improve first,
- popup behavior stays stable,
- and no single phase becomes a long-running branch that blocks other work.
