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
