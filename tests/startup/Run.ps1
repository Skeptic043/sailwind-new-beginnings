param([string]$PythonPath = 'python')
$ErrorActionPreference = 'Stop'
& $PythonPath (Join-Path $PSScriptRoot 'generate-yards.py')
if ($LASTEXITCODE -ne 0) { throw 'startup production extraction failed.' }
& dotnet run --project (Join-Path $PSScriptRoot 'Lifecycle.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'startup checks failed.' }
