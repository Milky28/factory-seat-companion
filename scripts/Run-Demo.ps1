$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    # Refresh a COPY in the companion's own directory. Never writes the supplied source briefing.
    $demoDir = Join-Path (Get-Location) 'demo'
    New-Item -ItemType Directory -Path $demoDir -Force | Out-Null
    $briefing = Get-Content -LiteralPath 'fixtures/briefing.provisional.json' -Raw | ConvertFrom-Json
    $briefing.generatedAt = [DateTimeOffset]::UtcNow.ToString('O')
    $briefing | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $demoDir 'briefing.json') -Encoding utf8
    dotnet run --project src/FactorySeatCompanion.Cli -- --briefing demo/briefing.json --fixtures fixtures/lmu --config config/companion.example.json --once
    if ($LASTEXITCODE -ne 0) { throw "Demo failed: exit $LASTEXITCODE." }
} finally { Pop-Location }
