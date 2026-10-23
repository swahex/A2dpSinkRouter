[CmdletBinding()]
param([string]$ExpectedTag, [string]$DotnetPath)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'src/A2dpSinkRouter/A2dpSinkRouter.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Expected a stable three-part project version, got: $version" }
if ($ExpectedTag -and $ExpectedTag -cne "v$version") { throw "Tag '$ExpectedTag' does not match project version 'v$version'." }

$buildOptions = @{}
if ($DotnetPath) { $buildOptions.DotnetPath = $DotnetPath }
& (Join-Path $PSScriptRoot 'Build.ps1') @buildOptions -CheckAudioCore
& (Join-Path $PSScriptRoot 'Build.ps1') @buildOptions -SelfContained

$releaseRoot = Join-Path $projectRoot 'artifacts/release'
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
foreach ($package in @(
    @{ Folder = 'win-x64'; Suffix = 'framework-dependent' },
    @{ Folder = 'win-x64-self-contained'; Suffix = 'self-contained' }
)) {
    $name = "A2dpSinkRouter-$version-win-x64-$($package.Suffix).zip"
    $archive = Join-Path $releaseRoot $name
    Compress-Archive -Path (Join-Path $projectRoot "artifacts/$($package.Folder)/*") -DestinationPath $archive -Force
}
Write-Output "Release packages: $releaseRoot"
