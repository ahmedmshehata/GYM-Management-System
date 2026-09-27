<#
.SYNOPSIS
  Builds the GymPro release package: tests, self-contained publish of the app and branding tool, zip + checksum.

.DESCRIPTION
  Output (default in .\publish):
    publish\GymPro\GymPro.exe             the gym app (no .NET install needed on the target PC)
    publish\GymPro\GymPro.Branding.exe    branding tool (logo, title, colour, default theme)
    publish\GymPro\appsettings.json       settings, kept next to the exe so they stay editable
    publish\GymPro-<version>-<rid>.zip    the folder above, ready to copy to gym PCs
    publish\GymPro-<version>-<rid>.zip.sha256

.EXAMPLE
  pwsh tools/publish.ps1
  pwsh tools/publish.ps1 -Version 1.1.0
  pwsh tools/publish.ps1 -FrameworkDependent      # ~10x smaller, but PCs need the .NET 10 Desktop Runtime
  pwsh tools/publish.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    # Defaults to <Version> in Directory.Build.props.
    [string]$Version,
    [string]$Runtime = 'win-x64',
    [string]$OutDir,
    [switch]$FrameworkDependent,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutDir) { $OutDir = Join-Path $root 'publish' }

if (-not $Version) {
    $props = [xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)
    $Version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
    if (-not $Version) { throw 'No -Version given and none found in Directory.Build.props.' }
}
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "Version '$Version' is not like 1.2.3 or 1.2.3-beta." }

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args[0]) failed (exit $LASTEXITCODE)." }
}

$sw = [Diagnostics.Stopwatch]::StartNew()
$appDir = Join-Path $OutDir 'GymPro'
$kind = if ($FrameworkDependent) { 'framework-dependent' } else { 'self-contained' }
Write-Host "GymPro $Version  |  $Runtime  |  $kind  ->  $OutDir" -ForegroundColor Green

# A running GymPro.exe would lock the files we're about to replace.
if (Get-Process -Name GymPro, GymPro.Branding -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($appDir, [StringComparison]::OrdinalIgnoreCase) }) {
    throw "GymPro is running from $appDir. Close it first."
}

if (-not $SkipTests) {
    Step 'Running tests (Release)'
    Invoke-Dotnet test (Join-Path $root 'tests\GymPro.Tests') -c Release --nologo -v quiet
}

Step 'Cleaning output'
if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }
New-Item -ItemType Directory -Force $appDir | Out-Null

$common = @(
    '-c', 'Release', '-r', $Runtime, '-o', $appDir, '--nologo',
    "-p:Version=$Version", '-p:DebugType=none'
)
if ($FrameworkDependent) {
    $common += @('--self-contained', 'false', '-p:PublishSingleFile=true')
} else {
    $common += @('--self-contained', 'true', '-p:PublishSingleFile=true',
                 '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true')
}

foreach ($project in 'GymPro.App', 'GymPro.Branding') {
    Step "Publishing $project"
    Invoke-Dotnet publish (Join-Path $root "src\$project") @common
}

# Anything besides the two exes and appsettings.json means a setting leaked files out of the bundle.
$expected = 'GymPro.exe', 'GymPro.Branding.exe', 'appsettings.json'
$extra = Get-ChildItem $appDir -File | Where-Object { $_.Name -notin $expected }
if ($extra) { Write-Warning "Unexpected files in the package: $($extra.Name -join ', ')" }
foreach ($f in $expected) { if (-not (Test-Path (Join-Path $appDir $f))) { throw "Missing $f in $appDir." } }

Step 'Creating zip'
$suffix = if ($FrameworkDependent) { "$Runtime-fdd" } else { $Runtime }
$zip = Join-Path $OutDir "GymPro-$Version-$suffix.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $appDir '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii

Step 'Done'
Get-ChildItem $OutDir -Recurse -File |
    Select-Object @{ n = 'File'; e = { $_.FullName.Substring($OutDir.TrimEnd('\').Length + 1) } },
                  @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } |
    Format-Table -AutoSize | Out-String | Write-Host
Write-Host ("Version {0} (exe: {1})  ·  SHA256 {2}  ·  {3:N0}s" -f $Version,
    (Get-Item (Join-Path $appDir 'GymPro.exe')).VersionInfo.ProductVersion, $hash, $sw.Elapsed.TotalSeconds) -ForegroundColor Green
if ($FrameworkDependent) {
    Write-Host 'Note: target PCs need the .NET 10 Desktop Runtime (x64): https://dotnet.microsoft.com/download/dotnet/10.0' -ForegroundColor Yellow
}
