# Orbital — Task Backlog

Each task file contains step-by-step instructions sized for an AI agent.
**Complexity ratings** indicate which model tier to use.

| # | Task | File | Complexity | Model |
|---|------|------|-----------|-------|
| 01 | Release zip — single-file build (no loose DLLs) | `task-01-release-file-structure.md` | Low | Small OK |
| 02 | Smarter popup triggers (drag + long-press) | `task-02-popup-trigger-improvement.md` | **HIGH** | ⚠️ Advanced |
| 03 | Long-press threshold: 500 ms → 300 ms | `task-03-longpress-timing.md` | Trivial | Small OK |
| 04 | Document popup mechanism in README + Settings | `task-04-popup-docs-and-github-link.md` | Low | Small OK |
| 05 | Hide disabled buttons (instead of grey-out) | `task-05-hide-disabled-buttons.md` | Low | Small OK |
| 06 | Per-button display mode: Text+Icon / Text / Icon | `task-06-button-display-mode.md` | Medium | Small OK (review) |
| 07 | Light theme (Claude Code Light palette) | `task-07-design-theme.md` | **HIGH** | ⚠️ Advanced |
| 08 | Auto-start with Windows (opt-out, checkbox) | `task-08-autostart.md` | Low-Medium | Small OK |

---

## Recommended Execution Order

Run independent trivial tasks first to get quick wins, then tackle complex ones.

### Phase 1 — Quick wins (assign to small models in parallel)
- [ ] **Task 03** — Long-press 300 ms (1 line change)
- [ ] **Task 05** — Hide disabled buttons (1 method rewrite)
- [ ] **Task 01** — Single-file publish (2 pubxml edits + yml edit)
- [ ] **Task 08** — Auto-start checkbox

### Phase 2 — Medium tasks (small model, but review output)
- [ ] **Task 04** — README + Settings GitHub link
- [ ] **Task 06** — Per-button display mode

### Phase 3 — Advanced (assign to advanced model only)
- [ ] **Task 02** — Popup trigger improvement (UIAutomation + ClipboardHelper fix)
- [ ] **Task 07** — Light theme (WPF ResourceDictionary refactor)

### Phase 4 — Integration & QA (advanced model)
- [ ] Build entire project: `dotnet build`
- [ ] Run through all verification checklists in each task file
- [ ] Test edge cases: no API key, no text selected, long-press on desktop/editor/browser

---

## Key Files Reference

| File | Role |
|------|------|
| `SystemHookManager.cs` | Mouse hook — drag threshold, long-press timer |
| `App.xaml.cs` | Event handlers for mouse events → popup |
| `ClipboardHelper.cs` | Ctrl+C simulation, clipboard backup/restore |
| `RadialMenuWindow.xaml(.cs)` | Popup bar UI + button rendering |
| `SettingsManager.cs` | `ActionProfile`, `AppSettings`, JSON persistence |
| `SettingsWindow.xaml(.cs)` | Settings dialog |
| `ActionEditDialog.xaml(.cs)` | Per-action edit dialog |
| `Properties/PublishProfiles/*.pubxml` | Build output configuration |
| `.github/workflows/release.yml` | CI release pipeline |
