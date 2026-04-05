---
name: release
description: Bump version in Orbital.csproj, commit, create git tag, and push to trigger the GitHub Actions release pipeline. Usage: /release <version> (e.g. /release 1.2.0 or /release 1.2.0-beta.1)
disable-model-invocation: true
---

# Release Orbital

The user wants to release a new version of Orbital. The target version is provided as an argument (e.g. `1.2.0` or `1.2.0-beta.1`).

If no version is provided, ask the user for it before proceeding.

## Steps

Perform each step below in order. After each step, confirm success before continuing.

1. **Read current version**: Read `Orbital.csproj` and find the current `<Version>` value. Show the user: "Current version: X.Y.Z → New version: A.B.C"

2. **Update Orbital.csproj**: Edit `Orbital.csproj` to update all three version fields:
   - `<Version>{version}</Version>`
   - `<AssemblyVersion>{version}.0</AssemblyVersion>`
   - `<FileVersion>{version}.0</FileVersion>`

3. **Build check**: Run `"C:\Program Files\dotnet\dotnet.exe" build Orbital.csproj --no-restore --configuration Release` to confirm the build succeeds before tagging.

4. **Stage and commit**: Stage `Orbital.csproj` and commit with message:
   `chore: bump version to {version}`

5. **Create git tag**: Create tag `v{version}` (annotated):
   `git tag -a v{version} -m "Release v{version}"`

6. **Push**: Push the commit and tag:
   `git push origin main && git push origin v{version}`

7. **Done**: Inform the user that the GitHub Actions release pipeline has been triggered. They can monitor it at the repository's Actions tab.

## Notes

- Pushing a `v*.*.*` tag triggers `.github/workflows/release.yml`, which builds the installer, creates the Velopack package, and publishes a GitHub Release automatically.
- Pre-release versions (containing `-`, e.g. `1.2.0-beta.1`) will be marked as pre-release on GitHub.
- Do NOT skip the build check in step 3 — a failed build will produce a broken release artifact.
