[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$PublishOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $false

$projectRoot = $PSScriptRoot
$repoRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..'))
$projectFile = Join-Path $projectRoot 'CcfDeadlineIsland.csproj'
[xml]$projectXml = Get-Content -LiteralPath $projectFile -Raw
$appVersion = [string]$projectXml.Project.PropertyGroup.Version
if ($appVersion -notmatch '^\d+\.\d+\.\d+$') { throw "Expected a three-part application version, got '$appVersion'." }
$parsedVersion = [version]$appVersion
$versionInfo = '{0}.{1}.{2}.0' -f $parsedVersion.Major, $parsedVersion.Minor, $parsedVersion.Build
$artifactsRoot = Join-Path $repoRoot 'artifacts'
$publishRoot = Join-Path $artifactsRoot "windows-island-$appVersion"
$installerPath = Join-Path $artifactsRoot "CCF-Deadline-Island_${appVersion}_x64-setup.exe"

function Find-DotnetSdk {
    $candidates = @((Join-Path $repoRoot '.tools\dotnet\dotnet.exe'))
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $command) { $candidates += $command.Source }
    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        $sdks = & $candidate --list-sdks
        if ($LASTEXITCODE -eq 0 -and @($sdks | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) { return $candidate }
    }
    throw 'A .NET SDK is required. Install .NET 8 SDK or use the local .tools/dotnet SDK.'
}

function Find-Makensis {
    $candidates = @()
    if ($env:MAKENSIS) { $candidates += $env:MAKENSIS }
    $command = Get-Command makensis -ErrorAction SilentlyContinue
    if ($null -ne $command) { $candidates += $command.Source }
    if ($env:LOCALAPPDATA) { $candidates += Join-Path $env:LOCALAPPDATA 'tauri\NSIS\makensis.exe' }
    foreach ($programFilesRoot in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if ($programFilesRoot) { $candidates += Join-Path $programFilesRoot 'NSIS\makensis.exe' }
    }
    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return [IO.Path]::GetFullPath($candidate) }
    }
    throw 'NSIS is required. Set MAKENSIS to makensis.exe, add it to PATH, or install NSIS.'
}

function Invoke-CheckedNative([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        Write-Host "$Executable failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
}

function Copy-PackageNotices([string]$PackagePath, [string]$Label, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $PackagePath -PathType Container)) {
        throw "Restored dependency directory was not found: $PackagePath"
    }
    $notices = @(Get-ChildItem -LiteralPath $PackagePath -File | Where-Object {
        $_.Name -match '^(licen[cs]e(?:\.txt)?|third[-_ ]?party[-_ ]?notices(?:\.txt)?)$'
    })
    if ($notices.Count -eq 0) { throw "No license text was found for $Label." }
    foreach ($notice in $notices) {
        $targetName = "$Label-$($notice.Name)"
        if (-not $notice.Extension) { $targetName += '.txt' }
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $Destination $targetName) -Force
    }
}

$dotnet = Find-DotnetSdk
$makensis = if ($PublishOnly) { $null } else { Find-Makensis }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $repoRoot '.tools\nuget' }
$packageRoot = [IO.Path]::GetFullPath($env:NUGET_PACKAGES)

New-Item -ItemType Directory -Path $artifactsRoot, $publishRoot -Force | Out-Null
Invoke-CheckedNative $dotnet @(
    'publish', $projectFile, '-c', $Configuration, '-r', 'win-x64', '--self-contained', 'true',
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $publishRoot
)

$licenseRoot = Join-Path $publishRoot 'licenses'
New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
$assetsPath = Join-Path $projectRoot 'obj\project.assets.json'
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$runtimeNames = @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64')
$runtimePackages = @{}
foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
    $downloads = $framework.Value.PSObject.Properties['downloadDependencies']
    if ($null -eq $downloads) { continue }
    foreach ($dependency in @($downloads.Value)) {
        if ($dependency.name -notin $runtimeNames) { continue }
        $runtimeVersion = ([string]$dependency.version).Trim('[]()'.ToCharArray()).Split(',')[0].Trim()
        if ($runtimeVersion -notmatch '^\d+\.\d+\.\d+$') { throw "Unexpected runtime version: $($dependency.version)" }
        $runtimePackages[[string]$dependency.name] = $runtimeVersion
    }
}
foreach ($runtimeName in $runtimeNames) {
    if (-not $runtimePackages.ContainsKey($runtimeName)) { throw "The publish restore did not record $runtimeName." }
    $runtimeVersion = $runtimePackages[$runtimeName]
    $runtimePath = Join-Path $packageRoot ($runtimeName.ToLowerInvariant() + '\' + $runtimeVersion)
    Copy-PackageNotices $runtimePath "$runtimeName-$runtimeVersion" $licenseRoot
}
foreach ($packageName in @('SkiaSharp', 'SkiaSharp.NativeAssets.Win32')) {
    Copy-PackageNotices (Join-Path $packageRoot ($packageName.ToLowerInvariant() + '\2.88.8')) "$packageName-2.88.8" $licenseRoot
}
foreach ($required in @('LICENSE', 'NOTICE.md', 'licenses\YamlDotNet-MIT.txt', 'licenses\Inter-OFL.txt',
    'licenses\Cascadia-OFL.txt', 'Upstream\DynamicWin\LICENSE', 'Upstream\DynamicWin\VENDOR-MANIFEST.json',
    'CCF Deadline Island.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishRoot $required) -PathType Leaf)) { throw "Missing distribution file: $required" }
}

Write-Host "Published self-contained Windows x64 app: $publishRoot"
if ($PublishOnly) { return }

Invoke-CheckedNative $makensis @(
    "/DPROJECT_DIR=$projectRoot", "/DPUBLISH_DIR=$publishRoot", "/DOUTPUT_FILE=$installerPath",
    "/DAPP_VERSION=$appVersion", "/DAPP_VERSION_FULL=$versionInfo", (Join-Path $projectRoot 'installer.nsi')
)
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) { throw 'NSIS succeeded without producing the expected installer.' }
Get-FileHash -LiteralPath $installerPath -Algorithm SHA256 | Format-List
Write-Host "Installer: $installerPath"
