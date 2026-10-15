# Testing New Beginnings

## Requirements

- Windows, PowerShell 7 and Windows PowerShell 5.1.
- .NET SDK 10 and .NET Framework 4.7.1 reference assemblies.
- Python 3 on `PATH`, or an explicit Python executable.
- Independently supplied Sailwind and BepInEx 5 assemblies, as described in [BUILD.md](BUILD.md).
- BepInEx's matching `Mono.Cecil.dll`, `MonoMod.Utils.dll` and `MonoMod.RuntimeDetour.dll` for the isolated patch-binding checks.

Game, loader and optional mod assemblies remain local build/test inputs. They are not included in this repository or its packages.

## Run

From the project directory:

```powershell
pwsh -File ./Test.ps1 `
  -GameManagedDir 'C:\Games\Sailwind\Sailwind_Data\Managed' `
  -ReferenceDir 'C:\Games\Sailwind\BepInEx\core' `
  -HarmonyDependencyDir 'C:\Games\Sailwind\BepInEx\core'
```

Use your installation's paths. `-PythonPath` selects a different Python executable. Supply `-ScrambledSeasPath` and `-DifficultyPath` to include their binding checks. Optional providers omitted from the command are reported as absent, rather than counted as passing integration checks.

The command builds the production DLL and runs each suite in a separate process. Tests that extract methods regenerate them from the current production source before compilation. Generation, compilation, assertion and binding errors make the command fail. Generated sources and build output are ignored by Git.

## Coverage

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

## Limits

The managed suites replace Unity objects, collision queries and selected native operations with test doubles. Cargo surface fixtures are authored geometry, not copies of game meshes.

Patch checks run in an isolated desktop process and do not invoke gameplay methods or modify a running game. Desktop CLR cannot prepare some Unity methods that reference engine internal calls. For the full target set, a small test-only Harmony backend generates replacement methods without installing detours. A negative control verifies that Harmony still rejects an invalid patch argument. Two supported targets are also detoured and unpatched in a separate process.

The Difficulty observer requires a live loader instance for its normal discovery path. When its DLL is supplied, the test checks the exact provider target and production patch methods directly. Provider checks do not reproduce BepInEx startup or coinstalled patch order.

These checks protect specific regressions. They do not establish physical dock clearance, buoyancy, cloth rendering, real item callbacks or native save-file behavior. Gameplay acceptance remains separate from automated results.
