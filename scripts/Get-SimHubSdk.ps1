# CI only: installs SimHub silently into a scratch folder so the plugin can compile against its DLLs.
# Nothing from SimHub is packaged; players already have SimHub installed.
param(
    [string]$Version = '9.13.2',
    [string]$Destination = (Join-Path $env:RUNNER_TEMP 'SimHub')
)
$ErrorActionPreference = 'Stop'
if (Test-Path (Join-Path $Destination 'SimHub.Plugins.dll')) { Write-Host "SimHub already in $Destination"; return }
$work = Join-Path ([IO.Path]::GetTempPath()) "simhub-$Version"
New-Item -ItemType Directory -Force $work | Out-Null
$zip = Join-Path $work 'SimHub.zip'
Invoke-WebRequest "https://github.com/SHWotever/SimHub/releases/download/$Version/SimHub.$Version.zip" -OutFile $zip
Expand-Archive $zip -DestinationPath $work -Force
$setup = Get-ChildItem $work -Recurse -Filter '*.exe' | Select-Object -First 1
if (-not $setup) { throw "No installer found in SimHub.$Version.zip" }
Write-Host "Running $($setup.Name) silently into $Destination"
$process = Start-Process $setup.FullName -PassThru -Wait -ArgumentList @(
    '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOICONS', "/DIR=`"$Destination`"")
if ($process.ExitCode -ne 0) { throw "SimHub setup exited with $($process.ExitCode)" }
if (-not (Test-Path (Join-Path $Destination 'SimHub.Plugins.dll'))) { throw "SimHub.Plugins.dll not found in $Destination after setup" }
# Shows where SimHub records its install folder, which the player installer looks up.
Get-ChildItem 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall', 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
    Get-ItemProperty | Where-Object { $_.DisplayName -like '*SimHub*' } |
    ForEach-Object { Write-Host "Uninstall key: $($_.PSChildName) InstallLocation=$($_.InstallLocation)" }
