$ErrorActionPreference = 'Stop'
& dotnet run --project (Join-Path $PSScriptRoot 'TestHarness.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Mooring checks failed.' }
