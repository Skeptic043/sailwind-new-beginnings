# Building New Beginnings

## Requirements

- Windows with PowerShell 7 and Windows PowerShell 5.1.
- .NET SDK 10 (the checks target .NET 10) and .NET Framework 4.7.1 reference assemblies, available through the developer pack or compatible SDK tooling.
- A local Sailwind installation and BepInExPack 5.4.2305.
- Python 3 on `PATH`, or an explicit Python executable.
- BepInEx's matching `Mono.Cecil.dll`, `MonoMod.Utils.dll` and `MonoMod.RuntimeDetour.dll` for the isolated patch-binding checks.

Game, loader and optional mod assemblies are build and test references only. They are not included in this repository or its packages.

## Build

From the project directory, run:

```powershell
pwsh -File ./Build.ps1 -GameManagedDir 'C:\Games\Sailwind\Sailwind_Data\Managed' -ReferenceDir 'C:\Games\Sailwind\BepInEx\core'
```

Use the paths for your own installation or mod profile. Alternatively, place `BepInEx.dll` and `0Harmony.dll` in `.local/references/` and omit `-ReferenceDir`.

The Release output is `bin/Release/net471/NewBeginnings.dll`.

## Test

From the project directory:

```powershell
pwsh -File ./Test.ps1 `
  -GameManagedDir 'C:\Games\Sailwind\Sailwind_Data\Managed' `
  -ReferenceDir 'C:\Games\Sailwind\BepInEx\core' `
  -HarmonyDependencyDir 'C:\Games\Sailwind\BepInEx\core'
```

Use your installation's paths. `-PythonPath` selects a different Python executable. Supply `-ScrambledSeasPath` and `-DifficultyPath` to include their binding checks. Optional providers omitted from the command are reported as absent, rather than counted as passing integration checks.

The command builds the production DLL and runs each suite in a separate process. Tests that extract methods regenerate them from the current production source before compilation. Generation, compilation, assertion and binding errors make the command fail. Generated sources and build output are ignored by Git.

### Coverage

| Suite | Behavior protected |
| --- | --- |
| Equipment | Selected items, crate contents and capacity, purchase callbacks, partial failures and reset |
| Cargo | Authored item geometry, layout, complete-roster fallback and distance-state handoff |
| Startup | Ownership, cancellation, native grant continuation and moored/held routing |
| Holds | Offshore hold persistence, save isolation and release when the player takes control |
| Selection | Configuration reload, pools, exclusions, menu state and destination lifetime |
| Berths | Fixed and surveyed placement, hull measurements, rope pairing and fallback behavior |
| Moorings | Settled endpoint lengths, native reset timing, player adjustments and startup cancellation |
| Binding | Current Harmony target resolution and wrapper generation, with separate isolated detour checks |

Each suite can also be run through its `tests/<suite>/Run.ps1` entry point. Their source files show which production files are compiled directly and which methods are extracted into controlled boundaries.

### Limits

The managed suites replace Unity objects, collision queries and selected native operations with test doubles. Cargo surface fixtures are authored geometry, not copies of game meshes.

Patch checks run in an isolated desktop process and do not invoke gameplay methods or modify a running game. Desktop CLR cannot prepare some Unity methods that reference engine internal calls. For the full target set, a small test-only Harmony backend generates replacement methods without installing detours. A negative control verifies that Harmony still rejects an invalid patch argument. Two supported targets are also detoured and unpatched in a separate process.

The Difficulty observer requires a live loader instance for its normal discovery path. When its DLL is supplied, the test checks the exact provider target and production patch methods directly. Provider checks do not reproduce BepInEx startup or coinstalled patch order.

These checks protect specific regressions. They do not establish physical dock clearance, buoyancy, cloth rendering, real item callbacks or native save-file behavior. Gameplay acceptance remains separate from automated results.

## Package

Run `Package.ps1` under PowerShell 7 with the same reference arguments. It checks the release metadata, icon and required documentation before producing versioned plugin and source ZIPs under `artifacts/`.

To package an already built Release DLL without rebuilding it:

```powershell
pwsh -File ./Package.ps1 -SkipBuild
```

The plugin package contains the DLL, manifest, icon, README, release notes and license. The source package contains an explicit list of source files and build documentation. Local development files, logs and build references are excluded. Package verification checks plugin identity and version, PNG dimensions, exact entry lists and extracted file hashes.

These scripts create local artifacts. They do not install the mod, push source code or publish a release.
