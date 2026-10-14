<#
.SYNOPSIS
    Installs Bedtime for the current user. No administrator rights needed.

.DESCRIPTION
    - From the release zip, installs the prebuilt app in the "app" folder next to this script.
      From a clone of the repository, builds it first (needs the .NET 10 SDK).
    - Installs to %LOCALAPPDATA%\Programs\Bedtime, adds a Start menu shortcut, starts Bedtime when you sign in,
      and lists it in Settings > Apps > Installed apps.
    - Starts Bedtime and puts its moon icon on the taskbar (rather than under the ^ overflow).

    Run it again to update; your settings are kept. Remove with Settings > Apps, or uninstall.ps1.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Bedtime')
)

$ErrorActionPreference = 'Stop'
$exePath = Join-Path $InstallDir 'Bedtime.exe'
$runtimeInstallerUrl = 'https://aka.ms/windowsappsdk/2.5/2.5.1/windowsappruntimeinstall-x64.exe'

# "Run with PowerShell" closes the window as soon as the script ends; keep errors readable in that case.
$fromExplorer = (Get-CimInstance Win32_Process -Filter "ProcessId=$PID").CommandLine -match 'Set-ExecutionPolicy -Scope Process Bypass'
trap {
    Write-Host "`n$_" -ForegroundColor Red
    if ($fromExplorer) { Read-Host 'Press Enter to close' | Out-Null }
    exit 1
}

function Step([string]$text) { Write-Host "  $text" -ForegroundColor Cyan }

function Test-WindowsAppRuntime {
    [bool](Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime.2' |
        Where-Object { $_.Architecture -eq 'X64' -and [version]$_.Version -ge [version]'2.5.1.0' })
}

# Windows doesn't show notifications from elevated apps, and an elevated install would start Bedtime elevated.
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Please run install.ps1 from a normal (not "Run as administrator") PowerShell window.'
}

Write-Host 'Installing Bedtime' -ForegroundColor White

# Bedtime uses Microsoft's shared Windows App Runtime (for its notifications). Offer to install it if missing.
if (-not (Test-WindowsAppRuntime)) {
    Write-Host '  Bedtime needs the Windows App Runtime 2.5 from Microsoft, which is not installed.' -ForegroundColor Yellow
    $answer = Read-Host '  Download and install it now? [Y/n]'
    if ($answer -match '^\s*n') {
        throw "Install the Windows App Runtime 2.5 (x64) from $runtimeInstallerUrl, then run install.ps1 again."
    }

    Step 'Downloading the Windows App Runtime'
    $installer = Join-Path $env:TEMP 'windowsappruntimeinstall-x64.exe'
    Invoke-WebRequest -Uri $runtimeInstallerUrl -OutFile $installer -UseBasicParsing
    $signature = Get-AuthenticodeSignature $installer
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
        throw "The downloaded runtime installer isn't signed by Microsoft; not running it."
    }

    Step 'Installing the Windows App Runtime'
    Start-Process -FilePath $installer -ArgumentList '--quiet' -Wait
    Remove-Item $installer -ErrorAction SilentlyContinue
    if (-not (Test-WindowsAppRuntime)) {
        throw "The Windows App Runtime still isn't installed. Install it from $runtimeInstallerUrl, then run install.ps1 again."
    }
}

$prebuilt = Join-Path $PSScriptRoot 'app'
if (Test-Path (Join-Path $prebuilt 'Bedtime.exe')) {
    $sourceDir = $prebuilt
}
else {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'Building from source needs the .NET 10 SDK (https://dotnet.microsoft.com/download). Or download the ready-made zip from https://github.com/SpeedyNabz/bedtime/releases/latest.'
    }
    Step 'Building'
    $sourceDir = Join-Path $PSScriptRoot 'artifacts\publish'
    dotnet publish (Join-Path $PSScriptRoot 'src\Bedtime\Bedtime.csproj') -c Release -r win-x64 -p:Platform=x64 -o $sourceDir --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'The build failed.' }
}

$running = Get-Process -Name 'Bedtime' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exePath }
if ($running) {
    Step 'Closing the running copy'
    # Ask it to quit so it removes its tray icon cleanly (handled by the running copy itself).
    Start-Process -FilePath $exePath -ArgumentList '--quit' -Wait
    $running | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
    $running | Where-Object { -not $_.HasExited } | Stop-Process -Force
}

Step "Copying files to $InstallDir"
New-Item -ItemType Directory -Force $InstallDir | Out-Null
robocopy $sourceDir $InstallDir /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying files failed (robocopy exit code $LASTEXITCODE)." }
Copy-Item (Join-Path $PSScriptRoot 'uninstall.ps1') $InstallDir -Force
# Files from a downloaded zip carry a "from the internet" mark; you chose to install them, so clear it.
Get-ChildItem $InstallDir -Recurse -File | Unblock-File

Step 'Adding the Start menu shortcut'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'Bedtime.lnk'))
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $InstallDir
$shortcut.IconLocation = "$exePath,0"
$shortcut.Description = 'Reminds you to go to bed in time for your wake-up'
$shortcut.Save()

Step 'Starting Bedtime when you sign in'
Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Bedtime' -Value "`"$exePath`" --background"
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run' -Name 'Bedtime' -ErrorAction SilentlyContinue

Step 'Adding Bedtime to Settings > Apps'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Bedtime'
New-Item -Path $uninstallKey -Force | Out-Null
$sizeKb = [int]((Get-ChildItem $InstallDir -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
$values = @{
    DisplayName     = 'Bedtime'
    DisplayIcon     = "$exePath,0"
    DisplayVersion  = (Get-Item $exePath).VersionInfo.ProductVersion -replace '\+.*$', ''
    InstallLocation = $InstallDir
    InstallDate     = (Get-Date -Format 'yyyyMMdd')
    UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$InstallDir\uninstall.ps1`""
}
foreach ($name in $values.Keys) { Set-ItemProperty $uninstallKey -Name $name -Value $values[$name] }
foreach ($name in 'NoModify', 'NoRepair') { New-ItemProperty $uninstallKey -Name $name -Value 1 -PropertyType DWord -Force | Out-Null }
New-ItemProperty $uninstallKey -Name 'EstimatedSize' -Value $sizeKb -PropertyType DWord -Force | Out-Null

Step 'Starting Bedtime'
# In the background: on a first install Bedtime opens its panel by itself; an update shouldn't pop it up.
Start-Process -FilePath $exePath -ArgumentList '--background'

# New tray icons start in the ^ overflow; promote ours once Windows has recorded it (best effort).
for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Milliseconds 500
    $entry = Get-ChildItem 'HKCU:\Control Panel\NotifyIconSettings' -ErrorAction SilentlyContinue |
        Where-Object { $path = (Get-ItemProperty $_.PSPath).ExecutablePath; $path -eq $exePath -or $path -like '*\Programs\Bedtime\Bedtime.exe' } |
        Select-Object -First 1
    if ($entry) {
        Set-ItemProperty $entry.PSPath -Name 'IsPromoted' -Value 1 -Type DWord
        break
    }
}

Write-Host 'Done. Click the moon next to the clock to set your wake-up times.' -ForegroundColor Green
exit 0 # robocopy leaves a non-zero (success) code behind
