param(
    [string]$PythonPath = 'python',
    [switch]$SelfTestFailure
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & $PythonPath (Join-Path $PSScriptRoot 'Generate.py')
    if ($LASTEXITCODE -ne 0) { throw 'Equipment source generation failed.' }
    & dotnet build EquipmentChecks.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Equipment harness compilation failed.' }
    $arguments = @()
    if ($SelfTestFailure) { $arguments += '--self-test-failure' }
    & dotnet (Join-Path $PSScriptRoot 'bin/Release/net10.0/EquipmentChecks.dll') @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Equipment assertions failed.' }
}
finally {
    Pop-Location
}
