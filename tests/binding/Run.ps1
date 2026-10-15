#requires -PSEdition Desktop
param(
    [string]$GameManagedDir = 'C:\Steam Games\steamapps\common\Sailwind\Sailwind_Data\Managed',
    [string]$ReferenceDir = (Join-Path $PSScriptRoot '../../.local/references'),
    [string]$HarmonyDependencyDir = '',
    [string]$ScrambledSeasPath = '',
    [string]$DifficultyPath = '',
    [switch]$DetourSmoke
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
if (-not $HarmonyDependencyDir) { $HarmonyDependencyDir = $ReferenceDir }
$providerCopyDir = Join-Path $root ('.local/binding-inputs/' + [Guid]::NewGuid().ToString('N'))
function Load-Provider([string]$path) {
    # Local copies preserve LoadFrom dependency resolution without changing an
    # installed DLL's download marker. CLR can lock these copies until exit.
    [void][IO.Directory]::CreateDirectory($providerCopyDir)
    $copy = Join-Path $providerCopyDir ([IO.Path]::GetFileName($path))
    [IO.File]::WriteAllBytes($copy, [IO.File]::ReadAllBytes($path))
    return [Reflection.Assembly]::LoadFrom($copy)
}
$harmony = $null
try {
    $required = @(
        (Join-Path $GameManagedDir 'Assembly-CSharp.dll'),
        (Join-Path $GameManagedDir 'UnityEngine.CoreModule.dll'),
        (Join-Path $ReferenceDir 'BepInEx.dll'),
        (Join-Path $ReferenceDir '0Harmony.dll'),
        (Join-Path $HarmonyDependencyDir 'Mono.Cecil.dll'),
        (Join-Path $HarmonyDependencyDir 'MonoMod.Utils.dll'),
        (Join-Path $HarmonyDependencyDir 'MonoMod.RuntimeDetour.dll'),
        (Join-Path $root 'bin/Release/net471/NewBeginnings.dll')
    )
    foreach ($path in $required) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required binding input is missing: $path" }
    }
    foreach ($path in @($ScrambledSeasPath, $DifficultyPath)) {
        if ($path -and -not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Explicit provider is missing: $path" }
    }
    foreach ($path in [IO.Directory]::GetFiles($GameManagedDir, 'UnityEngine*.dll')) {
        [void][Reflection.Assembly]::LoadFrom($path)
    }
    [void][Reflection.Assembly]::LoadFrom((Join-Path $GameManagedDir 'Assembly-CSharp.dll'))
    foreach ($name in @('BepInEx.dll', '0Harmony.dll')) {
        [void][Reflection.Assembly]::LoadFrom((Join-Path $ReferenceDir $name))
    }
    foreach ($pattern in @('Mono.Cecil*.dll', 'MonoMod*.dll')) {
        foreach ($path in [IO.Directory]::GetFiles($HarmonyDependencyDir, $pattern)) {
            [void][Reflection.Assembly]::LoadFrom($path)
        }
    }
    $assembly = [Reflection.Assembly]::LoadFrom((Join-Path $root 'bin/Release/net471/NewBeginnings.dll'))
    if (-not $DetourSmoke) {
        Add-Type -Path (Join-Path $PSScriptRoot 'GenerationOnlyPatcher.cs') -ReferencedAssemblies @(
            [HarmonyLib.Harmony].Assembly.Location,
            [MonoMod.Utils.DynamicMethodDefinition].Assembly.Location,
            (Join-Path $HarmonyDependencyDir 'Mono.Cecil.dll'))
        [GenerationOnlyPatcher]::Register()
    }
    $harmony = [HarmonyLib.Harmony]::new('newbeginnings.tests.binding')
    $static = [Reflection.BindingFlags]'Static,Public,NonPublic'
    $patchClasses = @($assembly.GetTypes() | Where-Object {
        @($_.GetCustomAttributesData() | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' }).Count -gt 0
    } | Sort-Object FullName)
    if ($patchClasses.Count -eq 0) { throw 'No Harmony patch classes were discovered.' }
    $bound = 0
    foreach ($type in $patchClasses) {
        if ($DetourSmoke -and $type.FullName -notin @(
            'NewBeginnings.FixedStart+BeginPatch',
            'NewBeginnings.StartingValues+ApplyPatch')) { continue }
        if ($type.FullName -eq 'NewBeginnings.StarterCargo+DifficultyStarterAdjustmentPatch') {
            Write-Output 'INAPPLICABLE: Difficulty Prepare requires a live Chainloader instance; checked separately through explicit provider binding.'
            continue
        }
        $patches = $harmony.CreateClassProcessor($type).Patch()
        if ($null -eq $patches -or $patches.Count -eq 0) {
            throw "Patch produced no binding: $($type.FullName)"
        }
        if ($type.FullName -eq 'NewBeginnings.FixedStart+NativeStartTweenPatch') {
            $ready = $assembly.GetType('NewBeginnings.FixedStart', $true).GetField('nativeTweenSeamReady', $static).GetValue($null)
            if (-not $ready) { throw 'Native player-start transpiler did not enable its seam.' }
        }
        $bound++
        $kind = if ($DetourSmoke) { 'DETOUR' } else { 'IL GENERATION' }
        Write-Output "${kind} PASS: $($type.FullName) ($($patches.Count) target(s))"
    }

    if (-not $DetourSmoke) {
        [GenerationOnlyPatcher]::VerifyInvalidPrefixRejected($harmony)
        Write-Output 'CONTROL PASS: Harmony rejected an invalid prefix argument through the generation-only backend.'
    }

    if (-not $DetourSmoke -and $DifficultyPath) {
        $provider = Load-Provider $DifficultyPath
        $target = $provider.GetType('SailwindDifficulty.Patches+StarterSetPatches', $true).GetMethod(
            'Prefix', $static, $null, [Type[]]@([StarterSet], [PortRegion]), $null)
        if ($null -eq $target -or $target.ReturnType -ne [void]) { throw 'Difficulty starter adjustment signature does not match production discovery.' }
        $observer = $assembly.GetType('NewBeginnings.StarterCargo+DifficultyStarterAdjustmentPatch', $true)
        $prefix = $observer.GetMethod('Prefix', $static)
        $finalizer = $observer.GetMethod('Finalizer', $static)
        [void]$harmony.Patch($target, [HarmonyLib.HarmonyMethod]::new($prefix), $null, $null, [HarmonyLib.HarmonyMethod]::new($finalizer))
        $info = [HarmonyLib.Harmony]::GetPatchInfo($target)
        if (-not @($info.Prefixes | Where-Object { $_.owner -eq $harmony.Id -and $_.PatchMethod -eq $prefix }).Count -or
            -not @($info.Finalizers | Where-Object { $_.owner -eq $harmony.Id -and $_.PatchMethod -eq $finalizer }).Count) {
            throw 'Difficulty observer prefix/finalizer binding was not installed.'
        }
        Write-Output 'IL GENERATION PASS: explicit Difficulty target and production observer. Chainloader discovery/bootstrap was not exercised.'
    } elseif (-not $DetourSmoke) { Write-Output 'ABSENT: optional Difficulty provider (supply -DifficultyPath to check its binding).' }

    if (-not $DetourSmoke -and $ScrambledSeasPath) {
        $provider = Load-Provider $ScrambledSeasPath
        $target = $provider.GetType('ScrambledSeas.Patches+StartNewGamePatch', $true).GetMethod(
            'MovePlayerToStartPos', $static, $null,
            [Type[]]@([StartMenu], [UnityEngine.Transform], [UnityEngine.Transform], [UnityEngine.GameObject]), $null)
        if ($null -eq $target -or $target.ReturnType -ne [Collections.IEnumerator]) { throw 'Scrambled Seas factory signature does not match production discovery.' }
        $prefix = $assembly.GetType('NewBeginnings.ScrambledSeasIntegration', $true).GetMethod('BeforePlayerStart', $static)
        [void]$harmony.Patch($target, [HarmonyLib.HarmonyMethod]::new($prefix))
        $info = [HarmonyLib.Harmony]::GetPatchInfo($target)
        if (-not @($info.Prefixes | Where-Object { $_.owner -eq $harmony.Id -and $_.PatchMethod -eq $prefix }).Count) {
            throw 'Scrambled Seas factory prefix binding was not installed.'
        }
        Write-Output 'IL GENERATION PASS: explicit Scrambled Seas factory and production prefix. Chainloader discovery/bootstrap was not exercised.'
    } elseif (-not $DetourSmoke) { Write-Output 'ABSENT: optional Scrambled Seas provider (supply -ScrambledSeasPath to check its binding).' }
    if ($DetourSmoke) {
        if ($bound -ne 2) { throw "Expected two isolated detour smoke checks; completed $bound." }
        Write-Output 'Two isolated CLR detour smoke checks passed.'
    } else {
        Write-Output "Generated replacements for $bound/$($patchClasses.Count) discovered patch classes; optional bootstrap observer reported separately."
    }
    Write-Output 'No gameplay target, plugin Awake or provider bootstrap was invoked. Generation does not prove Unity detours or gameplay.'
} catch {
    Write-Error "Native binding failed: $($_.Exception.ToString())" -ErrorAction Continue
    exit 1
} finally {
    if ($null -ne $harmony) {
        try { $harmony.UnpatchSelf() }
        catch { Write-Error "Failed to remove isolated patches: $($_.Exception.Message)" -ErrorAction Continue; exit 1 }
    }
}
if (-not $DetourSmoke) {
    & (Join-Path $PSHOME 'powershell.exe') -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath `
        -GameManagedDir $GameManagedDir -ReferenceDir $ReferenceDir -HarmonyDependencyDir $HarmonyDependencyDir -DetourSmoke
    if ($LASTEXITCODE -ne 0) { exit 1 }
}
exit 0
