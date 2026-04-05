# Orbital Session Work Orders

This document turns the audit report into session-sized work packets.
Each packet is designed to be:

- self-contained enough for a fresh agent session
- small enough to finish in one focused pass
- strict about scope so one packet does not sprawl into three

## How To Use

In a future session, mention one packet ID and ask for execution.

Recommended prompt pattern:

```text
Run ORB-S01 from docs/session_work_orders.md. Stay within that card, implement it end-to-end, verify the result, and summarize what changed.
```

Rules for any agent working a packet:

- Read only the referenced packet plus the files it names.
- Do not expand into other packets unless blocked by a hard dependency.
- If the repo has already changed, adapt the packet to current `HEAD` instead of forcing the old shape.
- Finish with code changes, verification, and a short status note.

## Recommended Order

Core stabilization first:

1. `ORB-S01`
2. `ORB-S02`
3. `ORB-S03`
4. `ORB-S04`
5. `ORB-S05`
6. `ORB-S06`
7. `ORB-S07`
8. `ORB-S08`
9. `ORB-S09`
10. `ORB-S10`

Expansion after stabilization:

11. `ORB-E01`
12. `ORB-E02`

---

## ORB-S01 - Decouple Local Actions From API Key Gating

Priority: P1
Expected size: one session

Goal:

Make non-LLM actions usable even when no API key is configured.

Why this exists:

- The app currently disables all actions when `_actionExecutor` is `null`.
- Local actions such as `Paste`, `Browser`, `DirectCopy`, and `Cut` should not require remote credentials.

Likely files:

- [App.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/App.xaml.cs)
- [RadialMenuWindow.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/RadialMenuWindow.xaml.cs)
- [ActionExecutorService.cs](C:/Users/CrowKing63/Developments/Orbital/Services/ActionExecutorService.cs)

Required work:

- Separate local action execution from LLM-dependent execution.
- Ensure the radial menu can execute local actions even when no API key is present.
- Keep user-facing messaging specific: only LLM actions should complain about missing API configuration.
- Preserve current behavior for LLM actions when a key is configured.

Out of scope:

- redesigning action metadata
- changing settings UI
- clipboard pipeline refactors

Verification:

- build with `dotnet build Orbit.csproj`
- confirm code path shows local actions no longer depend on `_actionExecutor != null`
- confirm LLM actions still fail gracefully without a key

Done when:

- a first-run user without an API key can still use local-only actions
- only LLM actions are gated on API configuration

Shortcut:

```text
Run ORB-S01 from docs/session_work_orders.md.
```

---

## ORB-S02 - Add Selection Gating For Unsafe Actions

Priority: P1
Expected size: one session

Goal:

Prevent unsafe actions from running when no text is selected.

Why this exists:

- Long-press opens the menu with empty text.
- Some actions currently remain enabled and can clear clipboard contents or send stray `Delete` keystrokes.

Likely files:

- [App.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/App.xaml.cs)
- [RadialMenuWindow.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/RadialMenuWindow.xaml.cs)
- [SettingsManager.cs](C:/Users/CrowKing63/Developments/Orbital/SettingsManager.cs)
- [ActionExecutorService.cs](C:/Users/CrowKing63/Developments/Orbital/Services/ActionExecutorService.cs)
- [ActionEditDialog.xaml](C:/Users/CrowKing63/Developments/Orbital/ActionEditDialog.xaml)
- [ActionEditDialog.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/ActionEditDialog.xaml.cs)

Required work:

- Introduce a simple action capability check for whether selection text is required.
- Apply safe defaults to built-in actions.
- Disable unsafe actions in the menu when selection text is empty.
- Add a defensive runtime guard so disabled-in-UI is not the only protection.
- Keep `Paste` available without selected text.

Out of scope:

- full action system redesign
- import/export of action definitions
- provider changes

Verification:

- build with `dotnet build Orbit.csproj`
- confirm empty-selection paths cannot run `Cut`, `DirectCopy`, `Browser`, or LLM text transforms
- confirm `Paste` still works from long-press mode

Done when:

- no empty-selection flow can clear clipboard or delete arbitrary text through built-in actions

Shortcut:

```text
Run ORB-S02 from docs/session_work_orders.md.
```

---

## ORB-S03 - Make Menu And Tooltip Placement Monitor-Aware

Priority: P1
Expected size: one session

Goal:

Keep the radial menu and result tooltip on the correct monitor with correct bounds.

Why this exists:

- Current placement logic uses `SystemParameters.WorkArea`, which maps poorly to multi-monitor behavior.
- The core UX of Orbital depends on cursor-proximate display.

Likely files:

- [RadialMenuWindow.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/RadialMenuWindow.xaml.cs)
- [ResultTooltipWindow.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/ResultTooltipWindow.xaml.cs)
- [App.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/App.xaml.cs)

Required work:

- Resolve the target monitor from the cursor position or active interaction point.
- Clamp menu position to that monitor's work area.
- Place result tooltips on the same monitor as the originating interaction when practical.
- Preserve current DPI handling or improve it if needed for correctness.

Out of scope:

- redesigning the tooltip UI
- changing hook behavior
- clipboard work

Verification:

- build with `dotnet build Orbit.csproj`
- confirm code no longer relies on a single global work area for all popups
- document any assumptions if tooltip origin tracking needs a lightweight parameter change

Done when:

- menu and tooltip positioning are monitor-aware in code and bounded to the relevant display

Shortcut:

```text
Run ORB-S03 from docs/session_work_orders.md.
```

---

## ORB-S04 - Recover Gracefully From Corrupted Settings

Priority: P1
Expected size: one session

Goal:

Prevent a bad `%APPDATA%\Orbit\settings.json` from breaking startup.

Why this exists:

- Settings are loaded during startup with no deserialization recovery path.
- One malformed file can currently block the app before it is usable.

Likely files:

- [SettingsManager.cs](C:/Users/CrowKing63/Developments/Orbital/SettingsManager.cs)
- [App.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/App.xaml.cs)

Required work:

- Add guarded file read and JSON parse recovery.
- Back up corrupted settings before regenerating defaults.
- Keep startup alive even after recovery.
- Surface a minimal explanation to the user if recovery occurs.

Out of scope:

- settings schema migrations
- redesigning settings storage
- adding full telemetry

Verification:

- build with `dotnet build Orbit.csproj`
- reason through or test a malformed JSON file path
- confirm the fallback path recreates valid settings

Done when:

- corrupted settings trigger recovery instead of startup failure

Shortcut:

```text
Run ORB-S04 from docs/session_work_orders.md.
```

---

## ORB-S05 - Serialize Clipboard Operations And Reduce Timing Races

Priority: P2
Expected size: one session

Goal:

Make clipboard capture and replace behavior less race-prone and less destructive.

Why this exists:

- Current selection capture is timing-based.
- Multiple background tasks can overlap.
- Replace behavior overwrites clipboard state without restoration.

Likely files:

- [ClipboardHelper.cs](C:/Users/CrowKing63/Developments/Orbital/ClipboardHelper.cs)
- [App.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/App.xaml.cs)
- [ActionExecutorService.cs](C:/Users/CrowKing63/Developments/Orbital/Services/ActionExecutorService.cs)

Required work:

- Prevent overlapping clipboard workflows from racing each other.
- Reduce reliance on fire-and-forget background timing where practical.
- Restore clipboard state after replace-oriented operations if that can be done safely.
- Keep the implementation small and understandable; do not attempt a platform-wide rewrite.

Out of scope:

- replacing clipboard capture with a full UI Automation subsystem
- action metadata refactor
- settings changes

Verification:

- build with `dotnet build Orbit.csproj`
- inspect that selection capture cannot start multiple overlapping destructive clipboard flows
- confirm replace flow does not permanently destroy prior clipboard state if restoration is implemented

Done when:

- clipboard interactions are serialized or otherwise guarded against overlap
- the code clearly reduces user clipboard damage

Shortcut:

```text
Run ORB-S05 from docs/session_work_orders.md.
```

---

## ORB-S06 - Make Settings Persistence Behavior Predictable

Priority: P2
Expected size: one session

Goal:

Stop users from losing action edits by closing the settings window.

Why this exists:

- Action edits mutate in-memory state before save.
- Closing the window can discard those changes silently.

Likely files:

- [SettingsWindow.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/SettingsWindow.xaml.cs)
- [SettingsWindow.xaml](C:/Users/CrowKing63/Developments/Orbital/SettingsWindow.xaml)
- [ActionEditDialog.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/ActionEditDialog.xaml.cs)

Required work:

- Choose one clear model: immediate save or dirty-state confirmation.
- Implement that model consistently for action add, edit, and delete.
- Keep API settings and action settings behavior understandable to the user.
- Avoid introducing a large MVVM rewrite just for this fix.

Out of scope:

- redesigning the whole settings UI
- provider feature expansion
- export/import

Verification:

- build with `dotnet build Orbit.csproj`
- confirm action changes are either persisted immediately or explicitly confirmed on close
- confirm there is no silent-loss path through normal window close behavior

Done when:

- settings persistence semantics are consistent and predictable

Shortcut:

```text
Run ORB-S06 from docs/session_work_orders.md.
```

---

## ORB-S07 - Add Hook Startup Diagnostics

Priority: P3
Expected size: one session

Goal:

Make hook installation failure visible instead of silently failing.

Why this exists:

- The global mouse hook is critical to the app.
- If hook installation fails, the current user experience is close to "nothing happens".

Likely files:

- [SystemHookManager.cs](C:/Users/CrowKing63/Developments/Orbital/SystemHookManager.cs)
- [App.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/App.xaml.cs)

Required work:

- Validate hook installation success.
- Surface failure with a clear message path that a user can actually see.
- Keep failure handling lightweight and appropriate for a tray app.
- Include enough detail for future debugging without spamming users.

Out of scope:

- full logging framework
- crash reporting integration
- broader hook redesign

Verification:

- build with `dotnet build Orbit.csproj`
- confirm hook setup has an explicit success/failure path in code
- confirm startup behavior is still sane if hook installation fails

Done when:

- hook installation can no longer fail silently

Shortcut:

```text
Run ORB-S07 from docs/session_work_orders.md.
```

---

## ORB-S08 - Harden HTTP Client Lifetime And Error Parsing

Priority: P3
Expected size: one session

Goal:

Reduce networking fragility in the LLM service layer.

Why this exists:

- `HttpClient` lifetime is not managed well.
- Error handling is thin.
- Response parsing assumes one exact provider shape.

Likely files:

- [LlmApiService.cs](C:/Users/CrowKing63/Developments/Orbital/Services/LlmApiService.cs)
- [App.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/App.xaml.cs)

Required work:

- Improve `HttpClient` lifetime management.
- Preserve useful error detail when the provider fails.
- Make response parsing a bit more defensive without turning this into a full provider abstraction layer.
- Keep compatibility with the current OpenAI-compatible flow.

Out of scope:

- adding new providers
- major dependency injection framework adoption
- streaming responses

Verification:

- build with `dotnet build Orbit.csproj`
- confirm service lifetime is clearer and safer in code
- confirm API failure paths return more actionable messages

Done when:

- the network layer is safer to reconfigure and easier to debug

Shortcut:

```text
Run ORB-S08 from docs/session_work_orders.md.
```

---

## ORB-S09 - Add Unit Tests For Settings And Action Gating

Priority: P2
Expected size: one session

Goal:

Create the first focused automated tests around the highest-risk logic that can be tested without UI automation.

Why this exists:

- The repository currently has no test project.
- Settings recovery and action gating are both high-value and testable.

Likely files:

- [Orbit.csproj](C:/Users/CrowKing63/Developments/Orbital/Orbit.csproj)
- [SettingsManager.cs](C:/Users/CrowKing63/Developments/Orbital/SettingsManager.cs)
- [ActionExecutorService.cs](C:/Users/CrowKing63/Developments/Orbital/Services/ActionExecutorService.cs)
- any new test project files you add

Required work:

- Add a test project using a lightweight .NET test stack.
- Cover settings recovery behavior if `ORB-S04` has landed; otherwise cover settings defaults and key behavior.
- Cover action gating logic if `ORB-S02` has landed; otherwise add tests to the pure helper logic you introduce now.
- Keep tests narrow and fast.

Out of scope:

- UI automation
- end-to-end clipboard tests
- integration tests against real providers

Verification:

- run the project tests locally
- keep `dotnet build Orbit.csproj` green
- include exact commands used in the final summary

Done when:

- the repo has at least one runnable automated test project covering real business logic

Shortcut:

```text
Run ORB-S09 from docs/session_work_orders.md.
```

---

## ORB-S10 - Extend CI To Run Tests

Priority: P2
Expected size: one session

Goal:

Move CI beyond "build only" so regressions are caught earlier.

Why this exists:

- Current GitHub Actions only restore, build, and upload artifacts.
- Once tests exist, CI needs to execute them.

Likely files:

- [.github/workflows/build.yml](C:/Users/CrowKing63/Developments/Orbital/.github/workflows/build.yml)
- any new test project files already present in the repo

Required work:

- Update CI to restore, build, and run tests.
- Keep artifact publication working.
- Avoid unnecessary workflow complexity.
- If no tests exist yet, make the workflow ready for them or note the dependency and stop cleanly.

Out of scope:

- release signing
- installer generation
- deployment workflow redesign

Verification:

- validate workflow syntax as far as practical from the local environment
- run local test commands if the test project exists
- summarize exact CI changes

Done when:

- CI has an explicit test execution step or a clearly justified minimal adaptation if tests are not yet merged

Shortcut:

```text
Run ORB-S10 from docs/session_work_orders.md.
```

---

## ORB-E01 - Replace String-Based ResultAction With A Safer Type

Priority: improvement
Expected size: one session

Goal:

Reduce stringly-typed action handling by introducing a safer typed representation.

Why this exists:

- `ResultAction` is currently a free-form string shared across settings, UI, and execution.
- Typos and branching drift will get worse as actions grow.

Likely files:

- [SettingsManager.cs](C:/Users/CrowKing63/Developments/Orbital/SettingsManager.cs)
- [ActionExecutorService.cs](C:/Users/CrowKing63/Developments/Orbital/Services/ActionExecutorService.cs)
- [ActionEditDialog.xaml](C:/Users/CrowKing63/Developments/Orbital/ActionEditDialog.xaml)
- [ActionEditDialog.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/ActionEditDialog.xaml.cs)
- [RadialMenuWindow.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/RadialMenuWindow.xaml.cs)

Required work:

- Introduce an enum or equivalent typed representation for built-in action kinds.
- Preserve compatibility with saved settings as needed.
- Update UI selection and execution branching to use the safer type.
- Keep the refactor focused; do not expand into a plugin system.

Out of scope:

- action import/export
- provider routing redesign
- large settings migrations

Verification:

- build with `dotnet build Orbit.csproj`
- confirm the typed representation is used end-to-end in code paths you touched

Done when:

- action dispatch no longer depends primarily on raw magic strings

Shortcut:

```text
Run ORB-E01 from docs/session_work_orders.md.
```

---

## ORB-E02 - Add Action Pack Import/Export

Priority: expansion
Expected size: one session

Goal:

Allow users to back up and share action definitions independently of the rest of settings.

Why this exists:

- This is a high-value expansion path for team workflows.
- It also gives operators a safer way to distribute curated action sets.

Likely files:

- [SettingsManager.cs](C:/Users/CrowKing63/Developments/Orbital/SettingsManager.cs)
- [SettingsWindow.xaml](C:/Users/CrowKing63/Developments/Orbital/SettingsWindow.xaml)
- [SettingsWindow.xaml.cs](C:/Users/CrowKing63/Developments/Orbital/SettingsWindow.xaml.cs)

Required work:

- Add export for action definitions only.
- Add import with validation and a safe merge or replace behavior.
- Keep API keys out of the exported format.
- Make the UX simple and explicit.

Out of scope:

- cloud sync
- team registry service
- packaging full app profiles

Verification:

- build with `dotnet build Orbit.csproj`
- confirm exported data excludes API secrets
- confirm imported action packs validate before mutating settings

Done when:

- action definitions can be exported and imported without touching secrets

Shortcut:

```text
Run ORB-E02 from docs/session_work_orders.md.
```

