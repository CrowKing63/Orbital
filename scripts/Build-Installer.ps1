#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Orbital installer (.exe) using dotnet publish + Inno Setup 6.
.DESCRIPTION
    1. Reads the version from Orbital.csproj
    2. Publishes a self-contained build to publish\installer\
    3. Runs Inno Setup to produce dist\Orbital-<version>-Setup.exe
.EXAMPLE
    .\scripts\Build-Installer.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Split-Path $PSScriptRoot -Parent
Push-Location $Root

try {
    # --- Read version from csproj ---
    $csprojPath = Join-Path $Root 'Orbital.csproj'
    [xml]$csproj = Get-Content $csprojPath
    $Version = $csproj.Project.PropertyGroup.Version | Select-Object -First 1
    if (-not $Version) { $Version = '1.0.0' }
    Write-Host "Building installer for Orbital v$Version" -ForegroundColor Cyan

    # --- dotnet publish ---
    $publishDir = Join-Path $Root 'publish\installer'
    Write-Host "Publishing (self-contained)..." -ForegroundColor Gray
    & 'C:\Program Files\dotnet\dotnet.exe' publish Orbital.csproj `
        /p:PublishProfile=installer `
        --configuration Release `
        --output $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

    # --- Run Inno Setup ---
    $iscc = $null
    $candidates = @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $iscc = $c; break }
    }
    if (-not $iscc) {
        throw "Inno Setup 6 (ISCC.exe) not found. Install from https://jrsoftware.org/isdl.php"
    }

    $issPath = Join-Path $Root 'installer\Orbital.iss'
    New-Item -ItemType Directory -Force -Path (Join-Path $Root 'dist') | Out-Null
    Write-Host "Running Inno Setup..." -ForegroundColor Gray
    & $iscc $issPath /DMyAppVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed (exit $LASTEXITCODE)" }

    $output = Join-Path $Root "dist\Orbital-$Version-Setup.exe"
    Write-Host ""
    Write-Host "Installer built successfully:" -ForegroundColor Green
    Write-Host "  $output" -ForegroundColor Green
}
finally {
    Pop-Location
}
