param (
    [Parameter(Position=0)]
    [ValidateSet("major", "minor", "patch")]
    [string]$VersionType = "patch",
    
    [Parameter(Position=1)]
    [string]$CustomVersion = $null,
    
    [string]$CommitMessage = "release: {version}"
)

# 1. 자동 탐색: .csproj 파일을 찾음
$projectFile = Get-ChildItem -Recurse -Include *.csproj | Select-Object -First 1
if ($null -eq $projectFile) {
    Write-Error "Could not find any .csproj file in the current directory or subdirectories."
    exit 1
}
$csprojPath = $projectFile.FullName
Write-Host "Target project: $($projectFile.Name)"

[xml]$csproj = Get-Content $csprojPath
$currentVersionStr = $csproj.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($currentVersionStr)) { $currentVersionStr = "0.0.0" }

Write-Host "Current version: $currentVersionStr"

# 2. 버전 계산 (문자열 파싱 방식이 더 직관적이고 안전함)
if ($CustomVersion) {
    $newVersionStr = $CustomVersion
} else {
    $parts = $currentVersionStr.Split('.')
    # 최소 3자리 보장
    while ($parts.Count -lt 3) { $parts += "0" }
    
    [int]$major = [int]$parts[0]
    [int]$minor = [int]$parts[1]
    [int]$patch = [int]$parts[2]

    if ($VersionType -eq "major") {
        $newVersionStr = "$($major + 1).0.0"
    } elseif ($VersionType -eq "minor") {
        $newVersionStr = "$major.$($minor + 1).0"
    } else {
        $newVersionStr = "$major.$minor.$($patch + 1)"
    }
}

$newAssemblyVersionStr = "$newVersionStr.0"
Write-Host "New version: $newVersionStr (AssemblyVersion: $newAssemblyVersionStr)"

# 3. 파일 업데이트
$csproj.Project.PropertyGroup.Version = $newVersionStr
$csproj.Project.PropertyGroup.AssemblyVersion = $newAssemblyVersionStr
$csproj.Project.PropertyGroup.FileVersion = $newAssemblyVersionStr
$csproj.Save($csprojPath)

Write-Host "Updated $csprojPath"

# 4. Git 작업 (현재 브랜치 자동 감지)
try {
    $currentBranch = git rev-parse --abbrev-ref HEAD
    $finalCommitMessage = $CommitMessage.Replace("{version}", "v$newVersionStr")

    Write-Host "Committing changes to branch '$currentBranch'..."
    git add .
    git commit -m $finalCommitMessage

    Write-Host "Tagging v$newVersionStr..."
    git tag "v$newVersionStr"

    Write-Host "Pushing to origin..."
    git push origin $currentBranch
    git push origin --tags

    Write-Host "Successfully released v$newVersionStr!"
} catch {
    Write-Error "Git operation failed. Please check if git is installed and configured."
}
