$ErrorActionPreference = 'Stop'
& dotnet run --project (Join-Path $PSScriptRoot 'Checks.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Offshore hold checks failed.' }
