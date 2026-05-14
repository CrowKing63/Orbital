# Project Guidelines for AI Agents

This document outlines the coding and documentation standards for the Orbital project.

## Language Standards

1. **Multilingual Support**: This project is designed to support multiple languages. 
2. **Code Comments**: All code comments must be written in **English** to ensure consistency and accessibility for global contributors.
3. **Documentation**: All documentation uploaded to GitHub (including Markdown files, release notes, and wikis) must be written in **English**.

## Localization & Encoding (CRITICAL)

1. **UTF-8 Integrity**: All `Strings.*.xaml` files must be preserved in **UTF-8** encoding. 
2. **No Corruption**: Never save files if you detect `?` or replacement characters in place of localized text (e.g., Korean, Japanese, Russian characters). If `view_file` shows corrupted characters, STOP and do not write to the file.
3. **Precise Edits**: When modifying localization files, use `replace_file_content` with the most minimal and precise `TargetContent` possible. Avoid replacing large blocks of text to minimize the risk of encoding shifts.
4. **Post-Edit Verification**: After every edit to a `Strings.*.xaml` file, you **MUST** run `view_file` on the modified lines to verify that the localized characters are still intact and readable.
5. **Terminal Rendering Caveat**: If PowerShell or terminal output looks corrupted, do not assume the file bytes are damaged. Verify the actual XML string values by reading the file as UTF-8 and printing the target values as Unicode escape sequences (for example `\uD31D\uC5C5...`) before deciding the file is corrupted.

## Encoding Recovery/Modification Rules (Prevention of Recurrence)

- **Lessons Learned from Recent Issue:**
  - **Failed Approach (Caused Corruption):** Using scripts (e.g., PowerShell `Get-Content -Raw` + `Set-Content`) to re-serialize the entire file. This failed to preserve the original encoding, leading to massive corruption of non-ASCII characters (Hangul) and comments.
  - **Correct Approach (Successful):** Reverting via Git and applying minimal changes using `apply_patch` (or precise `replace_file_content`). This avoids full file re-encoding and preserves character integrity.

- **Prohibitions:**
  - **DO NOT** rewrite entire C# / XAML / MD files containing non-ASCII characters using `Get-Content` / `Set-Content` patterns.
  - **DO NOT** use global regex replacements that trigger a full file rewrite unless encoding preservation is guaranteed.

- **Recommendations:**
  - **Prefer Minimal Edits**: Use `apply_patch` or `replace_file_content` targeting the smallest possible range of lines.
  - **Revert on Detection**: If encoding issues (e.g., `?` or replacement characters) are detected, immediately run `git checkout -- <file>` to revert and restart with a safer method.
  - **Verify Manually**: If script-based modification is unavoidable, manually verify sample lines containing localized text before and after the operation.
  - **Use Escape-Based Verification First**: For `Strings.*.xaml`, prefer parsing the XML as UTF-8 and comparing the edited values as Unicode escape sequences instead of trusting terminal rendering of raw non-ASCII text.

- **Safe Verification Workflow for `Strings.*.xaml`:**
  1. Read the file explicitly as UTF-8.
  2. Parse the XML and extract only the edited `x:Key` values.
  3. Print those values as Unicode escape sequences or code points, not raw terminal text.
  4. Apply the minimal patch.
  5. Re-run the same extraction and confirm the escape output matches the intended string.
  6. Only treat the file as corrupted if the escape/code-point output is wrong, not merely because the terminal rendered glyphs incorrectly.

- **Mandatory Validation Checklist:**
  - [ ] Check modified files to ensure non-ASCII characters (Hangul, etc.) and comments are still readable.
  - [ ] Verify that the project (e.g., `AltKey.csproj`) builds successfully.
  - [ ] If any corruption is found, stop immediately, revert, and use a more precise editing method.
