# Release & Auto-Update

- **Installer**: Inno Setup (`installer/Orbital.iss`, compiled in CI via
  `Minionguyjpro/Inno-Setup-Action`). Per-user install to `%LocalAppData%\Orbital`,
  no elevation. Uninstall removes the HKCU Run key and offers AppData cleanup.
- **Auto-update**: Implemented in-app (`UpdateChecker` + `App.CheckForUpdatesAsync`)
  - Checks `https://api.github.com/repos/CrowKing63/Orbital/releases/latest` on startup
  - Stable releases only; compares tag version against the assembly version
  - Downloads `Orbital-<ver>-Setup.exe` to temp, then tray "Restart (vX)" launches it
    with `/VERYSILENT /SUPPRESSMSGBOXES`; the app writes a restart flag file, exits,
    and the installer relaunches the app after upgrading in place (`DoWaitForProcess`
    in `InitializeWizard` prevents file-lock races)
- **GitHub Actions**: `.github/workflows/release.yml` — auto-releases on `v*` tag push
- **GitHub Release assets** (user-facing only):
  - `Orbital-{ver}-Setup.exe` (installer), `Orbital-{ver}-Portable.zip` (portable)
