# Task 01 — Clean Up Release File Structure

**Complexity**: Low — YAML + pubxml edits only
**Model**: Small model OK

## Goal

The portable release zip currently dumps every DLL alongside `Orbital.exe`.
Switch both portable and installer builds to **single-file publish** so all
managed DLLs are bundled into the exe, resulting in clean release artifacts.

Expected portable zip layout after fix:
```
Orbital-x.y.z/
  Orbital.exe        ← single self-contained exe (~120 MB)
  Cleanup.cmd
```

## Files to Edit

| File | Change |
|------|--------|
| `Properties/PublishProfiles/portable.pubxml` | Add `<PublishSingleFile>true</PublishSingleFile>` |
| `Properties/PublishProfiles/installer.pubxml` | Same addition |
| `.github/workflows/release.yml` | Simplify the "Package portable zip" step |

## Steps

### Step 1 — Update `portable.pubxml`

Open `Properties/PublishProfiles/portable.pubxml` and add the line:

```xml
<PublishSingleFile>true</PublishSingleFile>
```

inside the existing `<PropertyGroup>`. Final file should look like:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project>
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <Platform>x64</Platform>
    <PublishDir>publish\portable\</PublishDir>
    <PublishProtocol>FileSystem</PublishProtocol>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <PublishReadyToRun>false</PublishReadyToRun>
  </PropertyGroup>
</Project>
```

### Step 2 — Update `installer.pubxml`

Open `Properties/PublishProfiles/installer.pubxml` and add the same line:

```xml
<PublishSingleFile>true</PublishSingleFile>
```

### Step 3 — Update `release.yml` packaging step

Find the step named **"Package portable zip"** in `.github/workflows/release.yml`
(around line 66). Replace the `Copy-Item publish/portable/*` line so it copies
only `Orbital.exe` and `Cleanup.cmd`:

```yaml
- name: Package portable zip
  shell: pwsh
  run: |
    $ver = '${{ steps.version.outputs.version }}'
    $folder = "Orbital-$ver"
    $staging = "publish/staging/$folder"
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    Copy-Item publish/portable/Orbital.exe $staging
    Copy-Item portable/Cleanup.cmd $staging
    Compress-Archive -Path "publish/staging/$folder" -DestinationPath "dist/Orbital-$ver-Portable.zip"
```

## Verification Checklist

- [ ] `dotnet publish Orbital.csproj /p:PublishProfile=portable -c Release` produces only `Orbital.exe` (+ `Orbital.pdb` optionally) in `publish/portable/`
- [ ] `dotnet publish Orbital.csproj /p:PublishProfile=installer -c Release` produces only `Orbital.exe` in `publish/installer/`
- [ ] The resulting zip contains exactly `Orbital.exe` and `Cleanup.cmd`
- [ ] `Orbital.exe` runs correctly after extraction

## Notes

- Single-file publish embeds all managed DLLs. The exe will be larger (~120 MB) but portable.
- Native Windows DLLs (e.g. `msvcp140.dll`) are NOT embedded; they are expected to be present on the OS. For Windows 10/11 this is always true.
- If a future task requires keeping DLLs separate, use `probing paths` in `.runtimeconfig.json` instead.
