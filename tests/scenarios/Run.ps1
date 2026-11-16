param([string]$PythonPath = 'python')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & $PythonPath (Join-Path $PSScriptRoot 'generate.py')
    if ($LASTEXITCODE -ne 0) { throw 'Scenario source generation failed.' }
    & dotnet build ScenarioChecks.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Scenario harness compilation failed.' }
    & dotnet (Join-Path $PSScriptRoot 'bin/Release/net10.0/ScenarioChecks.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Scenario assertions failed.' }
}
finally {
    Pop-Location
}
