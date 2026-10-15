#requires -Version 7.0
param(
    [string]$PythonPath = 'python',
    [string]$GameManagedDir = 'C:\Steam Games\steamapps\common\Sailwind\Sailwind_Data\Managed',
    [string]$ReferenceDir = (Join-Path $PSScriptRoot '.local/references'),
    [string]$HarmonyDependencyDir = '',
    [string]$ScrambledSeasPath = '',
    [string]$DifficultyPath = ''
)

$ErrorActionPreference = 'Stop'
$pwsh = (Get-Process -Id $PID).Path
$failed = [Collections.Generic.List[string]]::new()
foreach ($suite in @('equipment', 'cargo', 'startup', 'holds', 'selection', 'berths', 'moorings')) {
    Write-Host "`n=== $suite ==="
    try {
        $arguments = @('-NoProfile', '-File', (Join-Path $PSScriptRoot "tests/$suite/Run.ps1"))
        if ($suite -in @('equipment', 'cargo', 'startup', 'selection', 'berths')) {
            $arguments += @('-PythonPath', $PythonPath)
        }
        if ($suite -eq 'selection') { $arguments += @('-ReferenceDir', $ReferenceDir) }
        & $pwsh @arguments
        if ($LASTEXITCODE -ne 0) { throw "Child process exited with code $LASTEXITCODE." }
        Write-Host "PASS: $suite"
    } catch {
        $failed.Add($suite)
        Write-Host "FAIL: $suite - $($_.Exception.Message)"
    }
}

Write-Host "`n=== production build ==="
try {
    & $pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Build.ps1') `
        -GameManagedDir $GameManagedDir -ReferenceDir $ReferenceDir
    if ($LASTEXITCODE -ne 0) { throw "Child process exited with code $LASTEXITCODE." }
    Write-Host 'PASS: production build'
} catch {
    $failed.Add('production build')
    Write-Host "FAIL: production build - $($_.Exception.Message)"
}

Write-Host "`n=== native patch binding ==="
if ($failed.Contains('production build')) {
    $failed.Add('native patch binding')
    Write-Host 'FAIL: native patch binding requires a successful current production build.'
} else {
    try {
        $desktop = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
        & $desktop -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'tests/binding/Run.ps1') `
            -GameManagedDir $GameManagedDir -ReferenceDir $ReferenceDir `
            -HarmonyDependencyDir $HarmonyDependencyDir `
            -ScrambledSeasPath $ScrambledSeasPath -DifficultyPath $DifficultyPath
        if ($LASTEXITCODE -ne 0) { throw "Child process exited with code $LASTEXITCODE." }
        Write-Host 'PASS: native patch binding'
    } catch {
        $failed.Add('native patch binding')
        Write-Host "FAIL: native patch binding - $($_.Exception.Message)"
    }
}

if ($failed.Count -gt 0) {
    Write-Host "`nFailed: $($failed -join ', ')"
    exit 1
}
Write-Host "`nAll maintained suites, production build and native patch binding passed."
exit 0
