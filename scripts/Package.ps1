# Builds the player download (zip) and installer (exe) into artifacts/.
# Needs what Build-Plugin.ps1 needs, plus Inno Setup 6 for the installer (skip it with -NoInstaller).
param(
    [string]$SimHubPath = $(if ($env:SIMHUB_INSTALL_PATH) { $env:SIMHUB_INSTALL_PATH } else { 'C:\Program Files (x86)\SimHub\' }),
    [string]$Version = '0.1.0-dev',
    [switch]$NoInstaller
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Build-Plugin.ps1') -SimHubPath $SimHubPath -SelfContained
Push-Location $root
try {
    # Same layout as the SimHub folder, so the zip can be extracted straight into it.
    $files = 'artifacts/package/files'
    if (Test-Path 'artifacts/package') { Remove-Item 'artifacts/package' -Recurse -Force }
    New-Item -ItemType Directory -Force "$files/DashTemplates/FactorySeatCompanion" | Out-Null
    Copy-Item 'artifacts/simhub/FactorySeatCompanion.SimHub.dll' $files
    Copy-Item 'artifacts/simhub/FactorySeatCompanion' $files -Recurse
    Copy-Item 'config/companion.player.json' "$files/FactorySeatCompanion/companion.json"
    Copy-Item 'dashboard/FactorySeatCompanion.djson' "$files/DashTemplates/FactorySeatCompanion"

    $zip = "artifacts/FactorySeatCompanion-$Version.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive "$files/*" $zip
    Write-Host "Download: $zip"

    if (-not $NoInstaller) {
        $iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
        if (-not $iscc) { $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
        if (-not (Test-Path $iscc)) { throw 'Inno Setup 6 (ISCC.exe) not found. Install it or pass -NoInstaller.' }
        $numeric = ([regex]::Match($Version, '^\d+(\.\d+){0,3}').Value + '.0.0.0').Split('.')[0..3] -join '.'
        & $iscc "/DAppVersion=$Version" "/DNumericVersion=$numeric" "/DSourceDir=$(Resolve-Path $files)" "/DOutputDir=$(Resolve-Path 'artifacts')" 'installer/FactorySeatCompanion.iss'
        if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
        Write-Host "Installer: artifacts/FactorySeatCompanion-Setup-$Version.exe"
    }
} finally { Pop-Location }
