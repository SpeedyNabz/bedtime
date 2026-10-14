<#
.SYNOPSIS
    Builds the release zip: a ready-to-run Bedtime plus install.ps1 and uninstall.ps1.

.DESCRIPTION
    The app is published with its own copy of .NET, so people installing from the zip don't need .NET or any
    developer tools. Microsoft's Windows App Runtime stays shared; install.ps1 offers to install it if missing.

    Output: artifacts\Bedtime-<version>-win-x64.zip (version from src\Bedtime\Bedtime.csproj).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\Bedtime\Bedtime.csproj'
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$name = "Bedtime-$version-win-x64"
$staging = Join-Path $root "artifacts\package\$name"
$zip = Join-Path $root "artifacts\$name.zip"

Remove-Item (Join-Path $root 'artifacts\package') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $zip -Force -ErrorAction SilentlyContinue

Write-Host "Building $name" -ForegroundColor White
dotnet publish $project -c Release -r win-x64 -p:Platform=x64 --self-contained true -o (Join-Path $staging 'app') --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'The build failed.' }

Copy-Item (Join-Path $root 'install.ps1'), (Join-Path $root 'uninstall.ps1'), (Join-Path $root 'LICENSE') $staging
Set-Content (Join-Path $staging 'README.txt') -Encoding utf8 -Value @"
Bedtime $version
A Windows 11 tray app that tells you when to go to bed.
https://github.com/SpeedyNabz/bedtime

INSTALL
  Right-click install.ps1 and choose "Run with PowerShell".
  Or, in PowerShell in this folder:  powershell -ExecutionPolicy Bypass -File .\install.ps1

  Bedtime installs for your account only (no administrator rights), starts when you sign in, and shows a
  moon next to the clock. It needs Microsoft's Windows App Runtime 2.5; the installer offers to download
  it if it's missing.

UNINSTALL
  Settings > Apps > Installed apps > Bedtime > Uninstall
"@

Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -CompressionLevel Optimal
$file = Get-Item $zip
Write-Host ("{0} ({1:N1} MB)" -f $file.FullName, ($file.Length / 1MB)) -ForegroundColor Green
Write-Host "SHA-256 $((Get-FileHash $zip -Algorithm SHA256).Hash)"
