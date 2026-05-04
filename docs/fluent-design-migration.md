# Windows 11 Fluent Design Migration Analysis

**Issue Reference**: GitHub Issue - "Design is out of place"

**Date**: 2026-05-04

**Status**: Accepted (Pending Implementation)

---

## Issue Summary

User requested modernizing the Orbital app UI to match Windows 11 Fluent Design language, similar to Clipboard Manager, Start Menu, and Settings app.

---

## Current State Analysis

### Project Overview
- **Type**: WPF (Windows Presentation Foundation) desktop application
- **Target**: .NET 8.0 Windows
- **Purpose**: Floating action bar/pill menu for AI-powered text actions

### Technology Stack
| Component | Technology |
|-----------|------------|
| Primary UI | WPF with XAML |
| System Tray | Windows Forms (NotifyIcon) |
| Theme System | XAML ResourceDictionary (Dark.xaml, Light.xaml) |
| Localization | XAML ResourceDictionary (10 languages) |

### Current Design System

#### Color Palette (Dark Theme)
| Token | Color | Purpose |
|-------|-------|---------|
| `BgDeep` | `#0A0A18` | Deep background |
| `BgPanel` | `#11112A` | Panel backgrounds |
| `BgElement` | `#191932` | Input elements |
| `AccentVi` | `#7080FF` | Primary accent (violet) |
| `AccentCy` | `#00D4FF` | Secondary accent (cyan) |

#### Design Characteristics
- Corner Radius: 6-14px (consistent)
- Shadows: DropShadowEffect for depth
- Typography: Segoe UI Variable
- Icons: Segoe MDL2 Assets + Emoji

---

## Windows 11 Fluent Design Key Elements

1. **Mica Material**: Semi-transparent window background showing desktop
2. **Acrylic**: Glass-like effect for popups/surfaces
3. **System Accent Colors**: Follow user's Windows theme
4. **Modern Controls**: Win11-style buttons, combo boxes, etc.
5. **Proper Elevation**: Visual hierarchy through layering
6. **Subtle Animations**: Smooth transitions

---

## Implementation Feasibility Analysis

### High Feasibility (Easy)
- ✅ Update color palette to Windows 11 neutral colors
- ✅ Use system accent colors (instead of custom colors)
- ✅ Update control templates (buttons, inputs, etc.)
- ✅ Improve spacing and typography hierarchy

### Medium Feasibility (Requires Effort)
- ⚠️ **Mica Effect**: Requires Windows 11 DWM API (P/Invoke)
  - `DwmSetWindowAttribute` + `DWMWA_SYSTEMBACKDROP_TYPE`
  - Requires Windows 11 Build 22000+
  - Need fallback for older versions
- ⚠️ **Acrylic Effect**: Apply glass effect to popups

### Low Feasibility (Challenging)
- ❌ **Perfect Fluent Design Match**: WPF is older technology compared to WinUI 3
- ❌ **System-level Mica**: Requires significant interop code

---

## Proposed Strategy

### Option 1: ModernWpf Library (Recommended)
```
NuGet: ModernWpf or WPF-UI
```
- Provides Fluent Design controls and Mica support
- Easy and quick implementation
- Drawback: Additional dependency

### Option 2: Hybrid Approach (Most Realistic)
1. **Phase 1**: Update colors/controls to Windows 11 style
2. **Phase 2**: Add Mica effect via DWM API (Windows 11 only)
3. **Phase 3**: Enhance system theme integration

### Option 3: Fully Custom Implementation
- Manual update of all control templates
- High effort, maintenance burden

---

## Recommended Action Plan

### Short Term (Issue Response)
- Update color palette to more neutral Windows 11 style
- Add system accent color support
- Improve control styles

### Medium Term
- Add Mica effect (DWM API)
- Evaluate ModernWpf library adoption

### Response to Issue
- Acknowledge acceptance of the request
- Explain implementation challenges
- Set realistic timeline expectations
- Welcome PRs from community

---

## Technical Notes

### DWM API for Mica (Reference)
```csharp
// For Mica effect on Windows 11
[DllImport("dwmapi.dll", PreserveSig = true)]
public static extern int DwmSetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE attribute, ref int pvAttribute, int cbAttribute);

// DWMWA_SYSTEMBACKDROP_TYPE = 38
// DWMSBT_MAINWINDOW = 2 (Mica)
```

### Files to Modify
- `Themes/Dark.xaml` - Update color tokens
- `Themes/Light.xaml` - Update color tokens
- `App.xaml` - Update global styles
- `RadialMenuWindow.xaml` - Pill menu styling
- `SettingsWindow.xaml` - Settings UI styling
- `App.xaml.cs` - Add system theme detection

---

## Decision

**Accepted** - Will implement gradually with hybrid approach.

**Priority**: Medium (aesthetic improvement, not critical functionality)

**Estimated Effort**: 2-3 weeks for full implementation
