param(
    [string]$PythonPath = 'python',
    [string]$ReferenceDir = (Join-Path $PSScriptRoot '../../.local/references')
)
$ErrorActionPreference = 'Stop'
$referencePath = (Resolve-Path -LiteralPath $ReferenceDir).Path
if (!(Test-Path -LiteralPath (Join-Path $referencePath 'BepInEx.dll'))) {
    throw 'Selection checks require BepInEx.dll in ReferenceDir.'
}
& $PythonPath (Join-Path $PSScriptRoot 'generate.py')
if ($LASTEXITCODE -ne 0) { throw 'Selection production extraction failed.' }
& $PythonPath (Join-Path $PSScriptRoot 'generate-refresh.py')
if ($LASTEXITCODE -ne 0) { throw 'Menu refresh extraction failed.' }
& dotnet build (Join-Path $PSScriptRoot 'Reload.csproj') -c Release -v q -nologo "-p:ReferenceDir=$referencePath"
if ($LASTEXITCODE -ne 0) { throw 'Selection checks failed to build.' }
& (Join-Path $PSScriptRoot 'bin/Release/net471/Reload.exe')
if ($LASTEXITCODE -ne 0) { throw 'Selection checks failed.' }
& $PythonPath (Join-Path $PSScriptRoot 'generate-port-overlay.py')
if ($LASTEXITCODE -ne 0) { throw 'Port overlay production extraction failed.' }
& dotnet build (Join-Path $PSScriptRoot 'PortOverlay.csproj') -c Release -v q -nologo
if ($LASTEXITCODE -ne 0) { throw 'Port overlay checks failed to build.' }
& (Join-Path $PSScriptRoot 'bin/Release/net10.0/PortOverlay.exe')
if ($LASTEXITCODE -ne 0) { throw 'Port overlay checks failed.' }
