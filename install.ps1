<#
.SYNOPSIS
    Builds Bedtime and installs it for the current user. No administrator rights needed.

.DESCRIPTION
    - Publishes a Release build to %LOCALAPPDATA%\Programs\Bedtime
    - Adds a Start menu shortcut, starts Bedtime at sign-in, and lists it in Settings > Apps > Installed apps
    - Starts Bedtime and puts its moon icon on the taskbar (rather than under the ^ overflow)

    Run it again to update; your settings are kept. Remove with Settings > Apps, or uninstall.ps1.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Bedtime')
)

$ErrorActionPreference = 'Stop'
$exePath = Join-Path $InstallDir 'Bedtime.exe'

function Step([string]$text) { Write-Host "  $text" -ForegroundColor Cyan }

# Windows doesn't show notifications from elevated apps, and an elevated install would start Bedtime elevated.
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Please run install.ps1 from a normal (not "Run as administrator") PowerShell window.'
}

# Bedtime uses the shared Windows App Runtime for notifications.
$runtime = Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime.2' |
    Where-Object { $_.Architecture -eq 'X64' -and [version]$_.Version -ge [version]'2.5.1.0' }
if (-not $runtime) {
    throw 'Bedtime needs the Windows App Runtime 2.5 (x64). Install it from https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads, then run install.ps1 again.'
}

Write-Host 'Installing Bedtime' -ForegroundColor White

Step 'Building'
$publishDir = Join-Path $PSScriptRoot 'artifacts\publish'
dotnet publish (Join-Path $PSScriptRoot 'src\Bedtime\Bedtime.csproj') -c Release -r win-x64 -p:Platform=x64 -o $publishDir --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'The build failed.' }

$running = Get-Process -Name 'Bedtime' -ErrorAction SilentlyContinue
if ($running) {
    Step 'Closing the running copy'
    # Ask it to quit so it removes its tray icon cleanly. Windows only routes --quit between copies of
    # the same exe path, so use the running copy's own exe.
    foreach ($process in $running) {
        if ($process.Path) { Start-Process -FilePath $process.Path -ArgumentList '--quit' -Wait }
    }
    $running | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
    Get-Process -Name 'Bedtime' -ErrorAction SilentlyContinue | Stop-Process -Force
}

Step "Copying files to $InstallDir"
New-Item -ItemType Directory -Force $InstallDir | Out-Null
robocopy $publishDir $InstallDir /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying files failed (robocopy exit code $LASTEXITCODE)." }
Copy-Item (Join-Path $PSScriptRoot 'uninstall.ps1') $InstallDir -Force

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
Start-Process -FilePath $exePath

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
