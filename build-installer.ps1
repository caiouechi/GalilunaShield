<#
.SYNOPSIS
  Builds the Galiluna Shield Windows installer (MSI).

.DESCRIPTION
  1. Runs the test suite.
  2. Publishes the desktop app and the command-line engine as self-contained 64-bit builds
     (the target machine does NOT need .NET installed).
  3. Compiles the WiX installer from installer\Package.wxs.

  Output: installer\out\GalilunaShield-<version>-x64.msi

.PARAMETER Version
  Product version (e.g. 1.0.0). Defaults to the <Version> in GalilunaShield.App.csproj.

.PARAMETER SkipTests
  Skip running the unit tests.

.EXAMPLE
  .\build-installer.ps1
  .\build-installer.ps1 -Version 1.2.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Set-Location $root

if (-not $Version) {
    $csproj = Get-Content "$root\GalilunaShield.App\GalilunaShield.App.csproj" -Raw
    $Version = if ($csproj -match "<Version>([^<]+)</Version>") { $Matches[1] } else { "1.0.0" }
}
Write-Host "==> Galiluna Shield $Version" -ForegroundColor Cyan

$publish = "$root\publish\app"
$out = "$root\installer\out"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
New-Item -ItemType Directory -Force $publish, $out | Out-Null

if (-not $SkipTests) {
    Write-Host "==> Running tests" -ForegroundColor Cyan
    dotnet test "$root\GalilunaShield.Tests" -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

Write-Host "==> Publishing desktop app (self-contained, win-x64)" -ForegroundColor Cyan
dotnet publish "$root\GalilunaShield.App" -c Release -r win-x64 --self-contained true -o $publish --nologo -v q `
    -p:Version=$Version -p:PublishReadyToRun=true -p:DebugType=none -p:GenerateDocumentationFile=false
if ($LASTEXITCODE -ne 0) { throw "Publishing the desktop app failed." }

Write-Host "==> Publishing command-line engine into the same folder" -ForegroundColor Cyan
dotnet publish "$root\GalilunaShield" -c Release -r win-x64 --self-contained true -o $publish --nologo -v q `
    -p:Version=$Version -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "Publishing the CLI failed." }

Write-Host "==> Publishing parent web dashboard into \web" -ForegroundColor Cyan
dotnet publish "$root\GalilunaShield.Web" -c Release -r win-x64 --self-contained true -o "$publish\web" --nologo -v q `
    -p:Version=$Version -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "Publishing the web dashboard failed." }

# Third-party license texts, bundled as the EULA promises
Copy-Item "$root\installer\THIRD-PARTY-LICENSES.txt" $publish -Force
Copy-Item "$root\docs\PARENTS-GUIDE.md" $publish -Force -ErrorAction SilentlyContinue

$sizeMb = [math]::Round((Get-ChildItem $publish -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "    published $((Get-ChildItem $publish -Recurse -File).Count) files, $sizeMb MB"

Write-Host "==> Building MSI" -ForegroundColor Cyan
$msi = "$out\GalilunaShield-$Version-x64.msi"
dotnet tool restore | Out-Null
dotnet wix build "$root\installer\Package.wxs" `
    -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext `
    -d "PublishDir=$publish" -d "Version=$Version" `
    -arch x64 -bindpath "$root\installer" -o $msi
if ($LASTEXITCODE -ne 0) { throw "WiX build failed." }

$msiMb = [math]::Round((Get-Item $msi).Length / 1MB, 1)
Write-Host "==> Done: $msi ($msiMb MB)" -ForegroundColor Green
