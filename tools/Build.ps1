[CmdletBinding()]
param([switch]$SelfContained, [switch]$CheckAudioCore, [ValidatePattern('^[A-Za-z0-9._-]+$')][string]$OutputName = 'win-x64', [string]$DotnetPath)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnet = if ($DotnetPath) { $DotnetPath } elseif (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$project = Join-Path $projectRoot 'src\A2dpSinkRouter\A2dpSinkRouter.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
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
$runtimeRequirement = if ($SelfContained) { 'This self-contained package includes the .NET 10 Desktop Runtime; no separate runtime installation is required.' } else { 'This framework-dependent package requires **.NET 10 Desktop Runtime x64**.' }
$text = $text.Replace('{{VERSION}}', $version).Replace('{{RUNTIME_REQUIREMENT}}', $runtimeRequirement)
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
if ($SelfContained) {
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $output 'A2dpSinkRouter.runtimeconfig.json') -Raw | ConvertFrom-Json
    foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
        $packageName = "$($framework.name.ToLowerInvariant()).runtime.win-x64"
        $packageRoot = Join-Path $env:NUGET_PACKAGES "$packageName/$($framework.version)"
        $notices = @(Get-ChildItem -LiteralPath $packageRoot -File | Where-Object { $_.Name -match '^(LICENSE(?:\..*)?|THIRD[-_]PARTY.*)$' })
        if (!$notices.Count) { throw "Runtime license files missing: $packageRoot" }
        $noticeOutput = Join-Path $output "licenses/$packageName"
        New-Item -ItemType Directory -Path $noticeOutput -Force | Out-Null
        $notices | Copy-Item -Destination $noticeOutput
    }
}
if ($CheckAudioCore) {
    & $dotnet run --project (Join-Path $projectRoot 'tests\AudioCoreChecks\AudioCoreChecks.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Audio core checks failed.' }
    & $dotnet run --project (Join-Path $projectRoot 'tests\ConnectionChecks\ConnectionChecks.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Connection lifecycle checks failed.' }
    & $dotnet run --project (Join-Path $projectRoot 'tests\EndpointChecks\EndpointChecks.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Endpoint property checks failed.' }
}
Write-Output "Built: $output\A2dpSinkRouter.exe"
