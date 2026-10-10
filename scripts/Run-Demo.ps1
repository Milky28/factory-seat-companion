$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    # Fixture mode: synthetic LMU observations and the checked-in example briefing; nothing is written.
    dotnet run --project src/FactorySeatCompanion.Cli -- --briefing fixtures/briefing.json --fixtures fixtures/lmu --config config/companion.example.json --once
    if ($LASTEXITCODE -ne 0) { throw "Demo failed: exit $LASTEXITCODE." }
} finally { Pop-Location }
