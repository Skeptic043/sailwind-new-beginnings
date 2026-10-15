param([string]$PythonPath = 'python')
$ErrorActionPreference = 'Stop'
& $PythonPath (Join-Path $PSScriptRoot 'generate.py')
if ($LASTEXITCODE -ne 0) { throw 'berths production extraction failed.' }
& dotnet run --project (Join-Path $PSScriptRoot 'PirateCandidateChecks.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'berths checks failed.' }
