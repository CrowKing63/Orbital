# Task 09 — Two-Case Trigger Classifier

**Status**: CONFIRMED — implementation in progress
**Complexity**: MEDIUM-HIGH — UIA classification, per-trigger wiring, no new dependencies
**Model**: ⚠️ Advanced model recommended

**Resolved decisions**: D1 = clipboard verification allowed for Case B (existing guardrail
overridden — this is the app's core behavior); D2 = focus-first; D3 = target-context
classification, confirmed by the user (gesture set is unchanged).

## 1. Goal

Replace the current three-flag trigger verdict (`canSelect`, `canWrite`, `hasText`) with one
explicit classification into exactly two supported cases:

| Case | Name | Meaning | Popup behavior |
|------|------|---------|----------------|
| A | Text input field | The gesture landed in a control that accepts keyboard text input | Show even with no selection; write actions (Paste/Cut/Replace) available |
| B | Selection outside an input field | The target does not accept text input, but a text selection gesture occurred (search/copy/translate actions are meaningful) | Show selection-dependent actions only; write actions hidden |
| — | Unsupported | Neither of the above | No popup |

**Scope note**: "two cases" means the classification of the *target context*, not a reduction
in the set of gestures. Drag, double-click, keyboard selection, long-press, and custom hotkey
remain separate trigger paths; they all feed the same classifier.

## 2. Why this is worth doing

`App.xaml.cs:709` (`CheckEditability`) currently returns three loosely-coupled booleans and the
callers reinterpret them per trigger path. This produced several ad-hoc mechanisms:

- Browser fast path returns `(true, false, true)` for any known browser window — `hasText` is
  optimistic, i.e. it means "a selection gesture happened", not "text is selected".
- Keyboard path (`App.xaml.cs:664`) hard-codes `isEditable = true`, so Shift+Arrow in a
  read-only document is misclassified as Case A.
- `CheckFocusedElement` fallback exists only for double-click (`useFocusFallback`), even though
  focus is the most reliable signal in Chromium-based apps where `FromPoint` resolves to a
  top-level `ControlType.Window`.
- `hasText` is refined with `HasRealFocusedSelection()` on one path only.

None of these are wrong individually; together they make the verdict hard to predict without
reading all call sites (see `docs/popup-trigger-design.md` and
`orbital-dev/references/ui-trigger-debugging.md`). One named classifier with one decision order
makes the behavior reviewable.

## 3. Guardrails

The following older decisions are **superseded by user decision for this task** — the
two-case classification is the app's core behavior and takes priority:

- ~~No proactive Ctrl+C to decide popup visibility~~ — now allowed for Case B, narrowly
  scoped (§4.5).
- ~~Optimistic `hasText` for read-only Document~~ — Case B now verifies the selection and
  suppresses the popup when verification proves there is no text.

These guardrails **remain in force**:

1. **Use the drag-start point** for the editability check, not the mouse-up point (§3).
2. **One-level parent fallback only** in the UIA tree; walking deeper causes false positives (§5).
3. All existing throttle / dismiss-suppression / cancellation gates in `TriggerSelectionMenu`
   (`App.xaml.cs:622-636`) stay unchanged.
4. The clipboard read must snapshot and restore the user's clipboard — reuse
   `ClipboardHelper`'s existing backup/restore path, never write a separate one.

## 4. Proposed classifier

### 4.1 Inputs

```
TriggerContext
  Gesture         Drag | DoubleClick | KeyboardSelection | Hotkey | LongPress
  CheckPoint      (x, y)          // drag-start for Drag, otherwise the gesture point
  ButtonDownClass string          // SystemHookManager.LastButtonDownHwndClass
  SelectionIntent bool            // gesture itself implies a selection attempt
```

### 4.2 Output

```
TriggerDecision
  Case       A | B | Unsupported
  IsEditable bool   // A: true; B: false
  HasText    bool   // false only when a real selection is known to be empty
```

Replace the `(bool canSelect, bool canWrite, bool hasText)` return of `CheckEditability` with
`TriggerDecision`. Keep `hasText` as a *display hint* for button visibility, never as a popup
suppression gate.

### 4.3 Decision order

Evaluate in this order; stop at the first match.

```
1. Resolve the focused element (AutomationElement.FocusedElement).
   Accept only if the gesture point lies inside its BoundingRectangle, or if the
   focused element has no usable rectangle and the gesture is keyboard-driven.
   Classify by ControlType:
      Edit / ComboBox                       -> Case A
      Document with writable ValuePattern,
        or keyboard-focusable Document      -> Case A
      read-only Document / Text / Pane
        with a non-collapsed TextPattern
        selection                           -> Case B
      read-only Document / Text / Pane with
        no usable selection                 -> Case B, verified in §4.5
      any other type                        -> continue

2. Resolve the element at CheckPoint (AutomationElement.FromPoint, CacheRequest).
   Reject excluded root/control window classes first.
   Classify by ControlType:
      Edit / ComboBox                       -> Case A
      Document writable                     -> Case A
      Document read-only                    -> Case B if SelectionIntent, else Unsupported
      Pane exposing TextPattern             -> Case A if writable, else Case B
      Text whose one-level parent is Group  -> Case B if SelectionIntent
      Edit/Document one-level parent        -> classify the parent the same way
      anything else                         -> continue

3. If ButtonDownClass is a known browser class (s_browserClasses)
      -> Case B with selection verification (§4.5). Must come after 1 and 2:
         an empty input field in a browser is still Case A.

4. Clipboard-fallback editor class/process match -> Case A.

5. Unsupported.
```

Notes:

- Step 1 replaces the current double-click-only focus fallback with a general one. This is the
  main behavioral fix: Chrome/Notepad keyboard and double-click selections both go through it.
- The bounding-rectangle check in step 1 is required: without it, a click on a title bar or
  button leaves the previously focused text control active and causes a false Case A.
- Step 2 keeps `FromPoint` as a secondary signal because for some legacy Win32 controls focus
  is not set before mouse-up.
- `HasRealFocusedSelection()` refines `HasText` for Case B on all paths where a TextPattern is
  available. A collapsed selection or whitespace-only text sets `HasText=false`; `null`
  (no TextPattern) keeps the optimistic value.

### 4.4 Per-trigger wiring

| Trigger | Context passed | Notes |
|---------|----------------|-------|
| Drag (`OnMouseUp`) | `Gesture=Drag`, `CheckPoint=LastButtonDownPos`, `ButtonDownClass`, `SelectionIntent=true` | Already does this (`App.xaml.cs:537`) |
| Double-click | `Gesture=DoubleClick`, `CheckPoint=gesture point`, `ButtonDownClass`, `SelectionIntent=true`, keep 80 ms settle + `ignoreDismissSuppression` | Already does this (`App.xaml.cs:551`) |
| Keyboard selection / hotkey | `Gesture=KeyboardSelection`, `SelectionIntent=true`, focused element first | Removes the unconditional `isEditable=true`; a read-only document now classifies as B |
| Long-press | `Gesture=LongPress`, `IsOverEditableControl` guard stays, `HasText=false` | Classifies as A by definition; unchanged behavior |

### 4.5 Case B selection verification (D1)

Case B is only real when text is actually selected. Verification order:

1. `HasRealFocusedSelection()` — UIA TextPattern. `true` / `false` is authoritative.
2. If UIA returns `null` (no TextPattern — typical for Chromium), call
   `ClipboardHelper.GetSelectedText()`. Non-empty trimmed result → selection exists;
   empty → no selection → **return Unsupported, no popup**.
3. If the clipboard call throws, fall back to optimistic `true` so the popup is not
   lost on a transient failure.

Scope limits: only Case B. Case A (input field) uses UIA on the element as today and never
verifies via clipboard; it must show even with no selection (Paste/Select All). One attempt
per trigger, reusing `ClipboardHelper`'s snapshot/restore path. Called only from the existing
background trigger task, never from a UI/hook callback thread.

## 5. Explicitly unsupported surfaces

These are out of scope and must be documented as such, not treated as bugs:

- Custom-rendered text with no UIA tree and no Win32 edit class (most games, some design tools).
- Apps whose selection is only visible on screen (canvas-rendered text) with no TextPattern.
- Cross-process focus races where the target app never exposes a focused text element.

For these, the classifier returns Unsupported and no popup appears. Detection cannot be made
reliable by adding more fallback levels; doing so reintroduces false positives.

## 6. Verification plan

Ground truth must be measured, not inferred from code reading.

- **Verdict tier**: `scripts/uia-probe.ps1` samples the `(case, isEditable, hasText)` verdict at
  the cursor while hovering/dragging. Use it for the control-type parent chain and fast-path hits.
- **Selection tier**: a .NET 8 C# console probe using `System.Windows.Automation` (same stack as
  Orbital) prints `FocusedElement.ControlType` and `TextPattern.GetSelection()` state. PowerShell
  5.1's UIA client cannot read TextPattern — a "no TP" result from it proves nothing.
- Confirm zero build errors before running any probe; a stale binary silently runs old code.

### Test matrix

| Surface | Gesture | Expected case | Popup |
|---------|---------|---------------|-------|
| Notepad / Win11 Notepad text area | drag, double-click, long-press | A | yes, write actions visible |
| Browser input / address bar | drag, double-click | B via fast path | yes, write actions hidden |
| Browser article body | drag, double-click | B | yes |
| Read-only PDF viewer text | drag with keyboard selection | B | yes, write actions hidden |
| Desktop / taskbar / image | drag, double-click | Unsupported | none |
| Empty Notepad line | double-click | A, `HasText=false` | yes, Copy/Search/LLM hidden |
| Title bar of a focused editor | drag ending in editor | Unsupported or A per rect check | no false A |
| Shift+Arrow in read-only document | keyboard | B (was A) | yes, write actions hidden |

Regression gates: 80 ms throttle, 150 ms dismiss suppression, double-click bypass, and
`_selectionCts` cancellation all still behave as documented in
`orbital-dev/references/ui-trigger-debugging.md`.

## 7. Files to change

| File | Change |
|------|--------|
| `App.xaml.cs` | Add `TriggerDecision` + `ClassifyTrigger()`; rewire the five handlers; retire `CheckEditability` return tuple or make it an internal helper |
| `docs/popup-trigger-design.md` | Record the two-case model and the new focus-first decision order |
| `docs/tasks/README.md` | Mark this task's status after implementation |
| Tests (`Orbit.Tests/`) | Unit-test the pure classification helper with injected UIA results where practical |

No new NuGet packages; `System.Windows.Automation` is available in `net8.0-windows`.

## 8. Decisions (resolved)

- **D1 — Clipboard verification for Case B**: **allowed**. The earlier no-proactive-Ctrl+C
  guidance is explicitly overridden for this task because reliable Case B detection is the
  app's core behavior. Scope is limited to Case B (§4.5).
- **D2 — Decision order**: **focus-first → point → browser class → fallback classes**. The
  browser class check must come after focus/point; otherwise an empty browser input field is
  wrongly rejected as Case B with no selection.
- **D3 — Meaning of "two cases"**: target-context classification; the gesture set is unchanged.

## 9. Verification results (measured on this machine)

A reflection probe invoked the real private `App.ClassifyTrigger` against live UIA state:

| Scenario | Result |
|----------|--------|
| Notepad, clicked, no selection | `InputField`, editable=true, hasText=false |
| Notepad, "hello world" selected | `InputField`, editable=true, hasText=true |
| Chrome page, text selected, browser class passed | `OutsideSelection`, editable=false, hasText=true |
| Chrome page, selection collapsed | `Unsupported` (no popup) |
| Chrome empty `<input>` / address bar, browser class passed | `InputField`, editable=true, hasText=false (regression fix) |
| Gesture point on title bar, text control focused | `Unsupported` (false-positive guard holds) |
| Desktop / non-text surface | `Unsupported` |

`dotnet build`: 0 warnings, 0 errors. `dotnet test Orbit.Tests` (Release): 51/51 passed.

Browser fast path is no longer zero-UIA: it calls `HasRealFocusedSelection()` and, when that is
inconclusive, `ClipboardHelper.GetSelectedText()`. This is the accepted cost of D1 and can add
up to ~200 ms before the popup appears.

## 10. Acceptance criteria

- All five trigger paths produce a `TriggerDecision` from one classifier.
- Read-only keyboard selection classifies as B, not A.
- Case B with no verified selection produces no popup.
- Unsupported surfaces produce no popup; no new false positives on title bars or buttons.
- `dotnet build` and `dotnet test Orbit.Tests/Orbital.Tests.csproj --configuration Release` pass.
- User manually verifies in a real app before any code commit (repo standing rule).
