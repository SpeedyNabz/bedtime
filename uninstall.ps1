<#
.SYNOPSIS
    Removes Bedtime for the current user. Settings > Apps > Installed apps runs this too.

.PARAMETER KeepSettings
    Keep %LOCALAPPDATA%\Bedtime (your wake-up times and the log).
#>
[CmdletBinding()]
param(
    [switch]$KeepSettings
)

$ErrorActionPreference = 'Continue'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\Bedtime'
$exePath = Join-Path $installDir 'Bedtime.exe'

# When started from the install folder (as Settings does), rerun from a temporary copy so the folder can go.
if ($PSCommandPath -and $PSCommandPath.StartsWith($installDir, [StringComparison]::OrdinalIgnoreCase)) {
    $temp = Join-Path $env:TEMP 'Bedtime-uninstall.ps1'
    Copy-Item $PSCommandPath $temp -Force
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$temp`"")
    if ($KeepSettings) { $arguments += '-KeepSettings' }
    Start-Process powershell.exe -ArgumentList $arguments
    return
}

Write-Host 'Removing Bedtime' -ForegroundColor White

if (Test-Path $exePath) {
    $running = Get-Process -Name 'Bedtime' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exePath }
    if ($running) {
        Start-Process -FilePath $exePath -ArgumentList '--quit' -Wait
        $running | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
        $running | Where-Object { -not $_.HasExited } | Stop-Process -Force
    }
    # Removes the notification registration (name, icon and click handler) from Windows.
    Start-Process -FilePath $exePath -ArgumentList '--unregister' -Wait
}

Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Bedtime' -ErrorAction SilentlyContinue
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run' -Name 'Bedtime' -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'Bedtime.lnk') -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Bedtime' -Recurse -ErrorAction SilentlyContinue
Get-ChildItem 'HKCU:\Control Panel\NotifyIconSettings' -ErrorAction SilentlyContinue |
    Where-Object { (Get-ItemProperty $_.PSPath).ExecutablePath -like '*\Programs\Bedtime\Bedtime.exe' } |
    Remove-Item -Recurse -ErrorAction SilentlyContinue

Remove-Item $installDir -Recurse -Force -ErrorAction SilentlyContinue
if (-not $KeepSettings) {
    Remove-Item (Join-Path $env:LOCALAPPDATA 'Bedtime') -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Bedtime has been removed.' -ForegroundColor Green
Start-Sleep -Seconds 2
