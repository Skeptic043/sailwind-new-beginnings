param([string]$PythonPath = 'python')
$ErrorActionPreference = 'Stop'
& $PythonPath (Join-Path $PSScriptRoot 'generate.py')
if ($LASTEXITCODE -ne 0) { throw 'cargo production extraction failed.' }
& dotnet run --project (Join-Path $PSScriptRoot 'Checks.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'cargo checks failed.' }
