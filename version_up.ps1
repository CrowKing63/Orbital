# version_up.ps1
# Script to increment version in .csproj, commit, and push tags.

$csproj = "Orbital.csproj"
if (-not (Test-Path $csproj)) {
    Write-Error "Could not find $csproj"
    exit
}

# Read the file content
$content = Get-Content $csproj -Raw

# Match <Version>x.y.z</Version>
if ($content -match '<Version>((\d+)\.(\d+)\.(\d+))</Version>') {
    $currentVersion = $matches[1]
    $major = [int]$matches[2]
    $minor = [int]$matches[3]
    $patch = [int]$matches[4]

    Write-Host "Current Version: $currentVersion"

    # Increment logic with carry-over (x.y.9 -> x.y+1.0)
    $newPatch = $patch + 1
    $newMinor = $minor
    $newMajor = $major

    if ($newPatch -gt 9) {
        $newPatch = 0
        $newMinor += 1
    }
    if ($newMinor -gt 9) {
        $newMinor = 0
        $newMajor += 1
    }

    $newVersion = "$newMajor.$newMinor.$newPatch"

    Write-Host "New Version: $newVersion"

    # Update file content
    $newContent = $content -replace "<Version>(\d+\.\d+\.\d+)</Version>", "<Version>$newVersion</Version>"
    $newContent = $newContent -replace "<AssemblyVersion>(\d+\.\d+\.\d+)\.\d+</AssemblyVersion>", "<AssemblyVersion>$newVersion.0</AssemblyVersion>"
    $newContent = $newContent -replace "<FileVersion>(\d+\.\d+\.\d+)\.\d+</FileVersion>", "<FileVersion>$newVersion.0</FileVersion>"

    $newContent | Set-Content $csproj -NoNewline -Encoding UTF8

    Write-Host "Updated $csproj to version $newVersion"

    # Git Operations
    Write-Host "Starting Git operations..."
    git add .
    git commit -m "chore: bump version to v$newVersion"
    
    # Tagging
    $tagName = "v$newVersion"
    git tag $tagName

    # Pushing
    $currentBranch = git rev-parse --abbrev-ref HEAD
    Write-Host "Pushing to origin $currentBranch and tags..."
    git push origin $currentBranch
    git push origin $tagName

    Write-Host "Successfully released $tagName"
}
else {
    Write-Error "Could not find <Version> tag in $csproj"
}
