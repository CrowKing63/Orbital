#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Orbital portable distribution (.zip) using dotnet publish.
.DESCRIPTION
    1. Reads the version from Orbital.csproj
    2. Publishes a self-contained multi-file build to publish\portable\
    3. Bundles all publish output + Cleanup.cmd into dist\Orbital-<version>-Portable.zip
.EXAMPLE
    .\scripts\Build-Portable.ps1
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
    Write-Host "Building portable package for Orbital v$Version" -ForegroundColor Cyan

    # --- dotnet publish ---
    $publishDir = Join-Path $Root 'publish\portable'
    Write-Host "Publishing (self-contained single-file)..." -ForegroundColor Gray
    & 'C:\Program Files\dotnet\dotnet.exe' publish Orbital.csproj `
        /p:PublishProfile=portable `
        --configuration Release `
        --output $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

    # --- Build zip ---
    $folderName  = "Orbital-$Version"
    $stagingDir  = Join-Path $Root "publish\staging\$folderName"
    New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null

    # Copy all published files
    Copy-Item "$publishDir\*" $stagingDir -Recurse

    # Copy Cleanup.cmd
    $cleanupSrc = Join-Path $Root 'portable\Cleanup.cmd'
    if (Test-Path $cleanupSrc) {
        Copy-Item $cleanupSrc $stagingDir
    }

    # Create zip
    New-Item -ItemType Directory -Force -Path (Join-Path $Root 'dist') | Out-Null
    $zipPath = Join-Path $Root "dist\Orbital-$Version-Portable.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    Compress-Archive -Path (Join-Path $Root "publish\staging\$folderName") -DestinationPath $zipPath

    Write-Host ""
    Write-Host "Portable package built successfully:" -ForegroundColor Green
    Write-Host "  $zipPath" -ForegroundColor Green

    # Cleanup staging
    Remove-Item (Join-Path $Root 'publish\staging') -Recurse -Force -ErrorAction SilentlyContinue
}
finally {
    Pop-Location
}
