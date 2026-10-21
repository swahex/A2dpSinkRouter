[CmdletBinding()]
param([switch]$SelfContained, [switch]$CheckAudioCore, [ValidatePattern('^[A-Za-z0-9._-]+$')][string]$OutputName = 'win-x64')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$project = Join-Path $projectRoot 'src\A2dpSinkRouter\A2dpSinkRouter.csproj'
$outputFolder = if ($SelfContained) { $OutputName + '-self-contained' } else { $OutputName }
$output = Join-Path (Join-Path $projectRoot 'artifacts') $outputFolder
# Refuse to mix a minimal release with documentation left by older build scripts.
foreach ($legacy in @('docs', 'IMPLEMENTATION.zh-CN.md', 'ICON-DESIGN.md', 'UI-CHANGES.zh-CN.md')) {
    if (Test-Path -LiteralPath (Join-Path $output $legacy)) { throw "Output contains old development documentation. Choose a fresh -OutputName: $output" }
}
& $dotnet publish $project -c Release -r win-x64 --self-contained $SelfContained.IsPresent.ToString().ToLowerInvariant() -p:CopyOutputSymbolsToPublishDirectory=false -o $output
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
Get-ChildItem -LiteralPath $output -Filter '*.pdb' -File | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
$text = [IO.File]::ReadAllText((Join-Path $projectRoot 'tools\packaging\README.md'))
# The packaging template links to the repository root; the release README lives there.
$text = $text.Replace('](../../', '](')
if ($SelfContained) {
    $text = $text.Replace('This framework-dependent package requires **.NET 8 Desktop Runtime x64**.', 'This self-contained package includes the .NET 8 Desktop Runtime; no separate runtime installation is required.')
}
[IO.File]::WriteAllText((Join-Path $output 'README.md'), $text, [Text.UTF8Encoding]::new($false))
# Remove only obsolete language-specific READMEs when reusing an output directory.
foreach ($legacyReadme in @('README.zh-CN.md', 'README.en.md')) {
    $legacyPath = Join-Path $output $legacyReadme
    if (Test-Path -LiteralPath $legacyPath) { Remove-Item -LiteralPath $legacyPath }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination (Join-Path $output 'CHANGELOG.md')
foreach ($name in @('LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    $notice = Join-Path $projectRoot $name
    if (Test-Path -LiteralPath $notice) { Copy-Item -LiteralPath $notice -Destination (Join-Path $output $name) }
}
if ($CheckAudioCore) {
    & $dotnet run --project (Join-Path $projectRoot 'tests\AudioCoreChecks\AudioCoreChecks.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Audio core checks failed.' }
}
Write-Output "Built: $output\A2dpSinkRouter.exe"
