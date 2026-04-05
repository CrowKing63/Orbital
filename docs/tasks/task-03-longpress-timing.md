# Task 03 — Reduce Long-Press Threshold to 300 ms

**Complexity**: Trivial — one constant change
**Model**: Small model OK

## Goal

Reduce the long-press activation time from 500 ms to 300 ms so the popup
feels more responsive.

## File to Edit

`SystemHookManager.cs`

## Step

Find line 25:

```csharp
private const int LongPressMs = 500;
```

Change to:

```csharp
private const int LongPressMs = 300;
```

That is the only change needed.

## Verification Checklist

- [x] Build succeeds (`dotnet build`)
- [ ] Long-press in an editable field triggers popup in ~300 ms (visually faster than before)
- [ ] A quick click (< 300 ms) does NOT trigger the popup
