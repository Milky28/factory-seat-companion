# Builds the SimHub plugin and bundles the companion CLI next to it in artifacts/simhub.
# Requires a SimHub install (for its SDK DLLs), the .NET 8 SDK and the .NET Framework 4.8 Developer Pack.
# -Install copies the bundle into SimHub; close SimHub first.
# -SelfContained publishes the CLI as one exe that needs no .NET install (used for player packages).
param(
    [string]$SimHubPath = $(if ($env:SIMHUB_INSTALL_PATH) { $env:SIMHUB_INSTALL_PATH } else { 'C:\Program Files (x86)\SimHub\' }),
    [switch]$Install,
    [switch]$SelfContained
)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    if (-not (Test-Path (Join-Path $SimHubPath 'SimHub.Plugins.dll'))) { throw "SimHub.Plugins.dll not found in $SimHubPath. Pass -SimHubPath." }
    $out = 'artifacts/simhub'
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    dotnet build src/FactorySeatCompanion.SimHub -c Release -o $out "/p:SimHubPath=$($SimHubPath.TrimEnd('\'))"
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
    $publish = @('publish', 'src/FactorySeatCompanion.Cli', '-c', 'Release', '-o', "$out/FactorySeatCompanion")
    if ($SelfContained) {
        # The runtime pack comes from nuget.org; the repo's NuGet.Config deliberately lists no sources.
        $publish += @('-r', 'win-x64', '--self-contained', '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=none', '--source', 'https://api.nuget.org/v3/index.json')
    }
    dotnet @publish
    if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed.' }
    Copy-Item dashboard/FactorySeatCompanion.djson $out
    if ($Install) {
        Copy-Item "$out/FactorySeatCompanion.SimHub.dll" $SimHubPath -Force
        Copy-Item "$out/FactorySeatCompanion" $SimHubPath -Recurse -Force
        Write-Host "Installed into $SimHubPath. Import $out/FactorySeatCompanion.djson in Dash Studio."
    }
} finally { Pop-Location }
