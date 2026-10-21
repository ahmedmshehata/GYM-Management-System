<#
.SYNOPSIS
  Builds the release package: tests, self-contained publish, zip + checksum. Optionally white-labelled.

.DESCRIPTION
  Standard package (default in .\publish):
    publish\GymPro\GymPro.exe             the gym app (no .NET install needed on the target PC)
    publish\GymPro\GymPro.Branding.exe    branding tool (logo, title, colour, default theme)
    publish\GymPro\appsettings.json       settings, kept next to the exe so they stay editable
    publish\GymPro-<version>-<rid>.zip    the folder above, ready to copy to gym PCs (+ .sha256)

  White-label package (-Brand <brand pack folder, brand.json or .brandpack>):
    publish\<exeName>\<exeName>.exe       the app with the brand's exe name, file properties and icon
    publish\<exeName>\appsettings.json    "Branding" section filled from the brand pack
    publish\<exeName>\Branding\           logo.png + app.ico
    publish\<exeName>-<version>-<rid>.zip (+ .sha256)
  The end-user branding tool (it shows the GymPro name) is left out. -IncludeConfigurator adds Brand Studio.

.EXAMPLE
  pwsh tools/publish.ps1
  pwsh tools/publish.ps1 -Version 1.1.0
  pwsh tools/publish.ps1 -Brand brands/sample
  pwsh tools/publish.ps1 -Brand D:\brands\iron-club.brandpack -IncludeConfigurator
  pwsh tools/publish.ps1 -FrameworkDependent      # ~10x smaller, but PCs need the .NET 10 Desktop Runtime
  pwsh tools/publish.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    # Defaults to <Version> in Directory.Build.props.
    [string]$Version,
    [string]$Runtime = 'win-x64',
    [string]$OutDir,
    # Brand pack: a folder with brand.json, the brand.json file itself, or a .brandpack zip (made with Brand Studio).
    [string]$Brand,
    # With -Brand: also ship Brand Studio (normally only resellers need it).
    [switch]$IncludeConfigurator,
    [switch]$FrameworkDependent,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutDir) { $OutDir = Join-Path $root 'publish' }
$OutDir = [IO.Path]::GetFullPath($OutDir)

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
function Xml([string]$s) { [System.Security.SecurityElement]::Escape($s) }

# ------------------------------------------------------------------ brand pack
$brandInfo = $null
$tempDir = $null
if ($Brand) {
    $Brand = [IO.Path]::GetFullPath($Brand)
    $packDir = $Brand
    if (Test-Path $Brand -PathType Leaf) {
        if ([IO.Path]::GetFileName($Brand) -ieq 'brand.json') {
            $packDir = Split-Path $Brand -Parent
        } else {
            # .brandpack zip: extract the three known files only (never arbitrary paths from the archive).
            $tempDir = Join-Path ([IO.Path]::GetTempPath()) ("brandpack-" + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory $tempDir | Out-Null
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $zipIn = [IO.Compression.ZipFile]::OpenRead($Brand)
            try {
                foreach ($name in 'brand.json', 'logo.png', 'app.ico') {
                    $entry = $zipIn.Entries | Where-Object { $_.FullName -ieq $name } | Select-Object -First 1
                    if ($entry) { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $tempDir $name)) }
                }
            } finally { $zipIn.Dispose() }
            $packDir = $tempDir
        }
    }
    $brandJson = Join-Path $packDir 'brand.json'
    if (-not (Test-Path $brandJson)) { throw "No brand.json in $Brand." }
    $b = Get-Content $brandJson -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable

    # Essentials checked here; Brand Studio runs the full validation before it saves a pack.
    foreach ($k in 'productName', 'shortName', 'exeName') {
        if ([string]::IsNullOrWhiteSpace($b[$k])) { throw "brand.json: '$k' is required." }
    }
    if ($b.exeName -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,38}[A-Za-z0-9_-])?$' -or $b.exeName -match '\.exe$') {
        throw "brand.json: exeName '$($b.exeName)' must be a plain file name without spaces or .exe."
    }
    $logo = if ($b.logoPath) { Join-Path $packDir $b.logoPath } else { $null }
    $icon = if ($b.iconPath) { Join-Path $packDir $b.iconPath } else { $null }
    # Without them the build would show the GymPro logo/icon, which defeats white-labelling.
    if (-not $logo -or -not (Test-Path $logo)) { throw 'The brand pack needs a logo (logoPath). Create one in Brand Studio.' }
    if (-not $icon -or -not (Test-Path $icon)) { throw 'The brand pack needs an icon (iconPath, .ico). Brand Studio generates it from the logo.' }
    foreach ($f in $logo, $icon) {
        if (-not ([IO.Path]::GetFullPath($f)).StartsWith($packDir, [StringComparison]::OrdinalIgnoreCase)) { throw "'$f' points outside the brand pack." }
    }
    $brandInfo = @{ Data = $b; Logo = $logo; Icon = $icon; Exe = $b.exeName }
}

$exeName = if ($brandInfo) { $brandInfo.Exe } else { 'GymPro' }
$display = if ($brandInfo) { $brandInfo.Data.productName } else { 'GymPro' }

$sw = [Diagnostics.Stopwatch]::StartNew()
$appDir = Join-Path $OutDir $exeName
$kind = if ($FrameworkDependent) { 'framework-dependent' } else { 'self-contained' }
Write-Host "$display $Version  |  $Runtime  |  $kind  ->  $appDir" -ForegroundColor Green

# A running copy would lock the files we're about to replace.
if (Get-Process -Name $exeName, GymPro.Branding, GymPro.BrandStudio -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($appDir, [StringComparison]::OrdinalIgnoreCase) }) {
    throw "$exeName is running from $appDir. Close it first."
}

try {
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

    if ($brandInfo) {
        # Exe name, file properties and icon are compiled into the program, so they come from a generated .props
        # that GymPro.App.csproj imports. Only the app project imports it; libraries keep their own names.
        $d = $brandInfo.Data
        $propsFile = Join-Path ([IO.Path]::GetTempPath()) ("gympro-brand-" + [guid]::NewGuid().ToString('N') + ".props")
        $company = if ($d.companyName) { $d.companyName } else { $d.productName }
        $copy = if ($d.copyright) { $d.copyright } else { "Copyright (c) $((Get-Date).Year) $company" }
        $desc = if ($d.description) { $d.description } else { $d.productName }
        @"
<Project>
  <PropertyGroup>
    <AssemblyName>$(Xml $d.exeName)</AssemblyName>
    <Product>$(Xml $d.productName)</Product>
    <AssemblyTitle>$(Xml $d.productName)</AssemblyTitle>
    <Company>$(Xml $company)</Company>
    <Authors>$(Xml $company)</Authors>
    <Copyright>$(Xml $copy)</Copyright>
    <Description>$(Xml $desc)</Description>
    <ApplicationIcon>$(Xml ([IO.Path]::GetFullPath($brandInfo.Icon)))</ApplicationIcon>
  </PropertyGroup>
</Project>
"@ | Set-Content -Path $propsFile -Encoding utf8

        # Separate obj/bin per brand: a different AssemblyName in the shared obj folder could leave stale WPF
        # resource paths behind and break the next normal build.
        $artifacts = Join-Path $root (Join-Path "artifacts" "brand-$exeName")
        Step "Publishing GymPro.App as $exeName.exe"
        try { Invoke-Dotnet publish (Join-Path $root 'src\GymPro.App') @common "-p:GymProBrandProps=$propsFile" --artifacts-path $artifacts }
        finally { Remove-Item $propsFile -ErrorAction SilentlyContinue }

        if ($IncludeConfigurator) {
            Step 'Publishing Brand Studio'
            Invoke-Dotnet publish (Join-Path $root 'src\GymPro.BrandStudio') @common
        }

        Step 'Applying brand to appsettings.json'
        $brandDir = Join-Path $appDir 'Branding'
        New-Item -ItemType Directory -Force $brandDir | Out-Null
        Copy-Item $brandInfo.Logo (Join-Path $brandDir 'logo.png') -Force
        Copy-Item $brandInfo.Icon (Join-Path $brandDir 'app.ico') -Force

        # brand.json property names match BrandingOptions, so the profile is the "Branding" section
        # (covered by BrandJsonAndOptionsTests.Brand_json_binds_directly_into_the_Branding_section).
        $section = [ordered]@{}
        foreach ($k in $d.Keys | Where-Object { $_ -notin 'schemaVersion', 'logoPath', 'iconPath' }) { $section[$k] = $d[$k] }
        $section['appTitle'] = $d.productName   # older app versions read AppTitle
        $section['logoPath'] = 'Branding/logo.png'
        $section['logoBase64'] = $null
        $section['iconPath'] = 'Branding/app.ico'
        $settingsPath = Join-Path $appDir 'appsettings.json'
        $settings = Get-Content $settingsPath -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
        $settings['Branding'] = $section
        $settings | ConvertTo-Json -Depth 10 | Set-Content -Path $settingsPath -Encoding utf8
    } else {
        foreach ($project in 'GymPro.App', 'GymPro.Branding') {
            Step "Publishing $project"
            Invoke-Dotnet publish (Join-Path $root "src\$project") @common
        }
    }

    # Anything unexpected means a setting leaked files out of the single-file bundle.
    $expected = if ($brandInfo) {
        @("$exeName.exe", 'appsettings.json') + $(if ($IncludeConfigurator) { 'GymPro.BrandStudio.exe' })
    } else {
        @('GymPro.exe', 'GymPro.Branding.exe', 'appsettings.json')
    }
    $extra = Get-ChildItem $appDir -File | Where-Object { $_.Name -notin $expected }
    if ($extra) { Write-Warning "Unexpected files in the package: $($extra.Name -join ', ')" }
    foreach ($f in $expected) { if (-not (Test-Path (Join-Path $appDir $f))) { throw "Missing $f in $appDir." } }

    Step 'Creating zip'
    $suffix = if ($FrameworkDependent) { "$Runtime-fdd" } else { $Runtime }
    $zip = Join-Path $OutDir "$exeName-$Version-$suffix.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $appDir '*') -DestinationPath $zip -CompressionLevel Optimal
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii
} finally {
    if ($tempDir) { Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue }
}

Step 'Done'
Get-ChildItem $appDir -Recurse -File |
    Select-Object @{ n = 'File'; e = { $_.FullName.Substring($OutDir.TrimEnd('\').Length + 1) } },
                  @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } |
    Format-Table -AutoSize | Out-String | Write-Host
$vi = (Get-Item (Join-Path $appDir "$exeName.exe")).VersionInfo
Write-Host ("{0} {1} (exe: {2}, company: {3})  ·  {4}  ·  SHA256 {5}  ·  {6:N0}s" -f $vi.ProductName, $Version,
    $vi.ProductVersion, $vi.CompanyName, (Split-Path $zip -Leaf), $hash, $sw.Elapsed.TotalSeconds) -ForegroundColor Green
if ($FrameworkDependent) {
    Write-Host 'Note: target PCs need the .NET 10 Desktop Runtime (x64): https://dotnet.microsoft.com/download/dotnet/10.0' -ForegroundColor Yellow
}
