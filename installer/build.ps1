<#
.SYNOPSIS
Builds the PDF Manager installers: one MSI per architecture, in installer\out.

.DESCRIPTION
For each architecture, publishes the app self-contained (it carries its own .NET runtime, so
users don't need one installed), then builds an MSI from that publish with WiX 5. WiX comes
from NuGet like the app's other packages; nothing needs installing beyond the .NET SDK.

The MSI installs to Program Files\PDF Manager, adds a Start menu shortcut, and shows in
Installed apps. A newer MSI upgrades an older one in place. The version comes from
<Version> in PDFManager.csproj; raise it before building a release, or Windows sees the
new MSI as the version already installed.

.PARAMETER Arch
x64, arm64, or both (the default).

.EXAMPLE
.\installer\build.ps1

.EXAMPLE
.\installer\build.ps1 -Arch arm64
#>
param(
    [ValidateSet("x64", "arm64")]
    [string[]]$Arch = @("x64", "arm64")
)
$ErrorActionPreference = "Stop"

$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$out = Join-Path $PSScriptRoot "out"
New-Item -ItemType Directory -Force $out | Out-Null

foreach ($a in $Arch) {
    $publish = Join-Path $PSScriptRoot "obj\publish\win-$a\"
    # A file left from an earlier publish would end up in the MSI
    if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }

    "Publishing the app for $a..."
    dotnet publish (Join-Path $repo "PDFManager.csproj") -c Release -r "win-$a" --self-contained -o $publish -nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Publishing for $a failed." }

    "Building the $a installer..."
    $msiDir = Join-Path $PSScriptRoot "bin\$a\"
    dotnet build (Join-Path $PSScriptRoot "PDFManager.Installer.wixproj") -c Release -p:Platform=$a "-p:AppPublishDir=$publish" -o $msiDir -nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Building the $a installer failed." }

    $version = (Get-Item (Join-Path $publish "PDFManager.exe")).VersionInfo.ProductVersion -replace "\+.*$", ""
    $msi = Join-Path $out "PDFManager-$version-$a.msi"
    Copy-Item (Join-Path $msiDir "PDFManager.msi") $msi -Force
    "{0} ({1:N1} MB)" -f $msi, ((Get-Item $msi).Length / 1MB)
}
