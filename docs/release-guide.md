# Release & Auto-Update

- **Auto-update**: Uses Velopack (`Velopack` NuGet + `vpk` dotnet global tool)
  - Do NOT use `csq` (Clowd.Squirrel CLI) — not available on NuGet
- **GitHub Actions**: `.github/workflows/release.yml` — auto-releases on `v*` tag push
- **`vpk pack` outputs**: `RELEASES`, `releases.win.json`, `assets.win.json`, `*-full.nupkg`,
  `OrbitalSetup.exe`, `*-win-Portable.zip` (auto-generated — delete as it duplicates our portable)
- **GitHub Release file roles**
  - For users: `OrbitalSetup.exe` (installer), `Orbital-{ver}-Portable.zip` (portable)
  - Velopack internal: `RELEASES`, `releases.win.json`, `assets.win.json`, `*-full.nupkg`
  - GitHub Releases can't hide files — internal files are exposed but users don't need to touch them
