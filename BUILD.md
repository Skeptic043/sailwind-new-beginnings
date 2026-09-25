# Building New Beginnings

## Requirements

- Windows with PowerShell 7 and a .NET SDK capable of building the `net471` project.
- .NET Framework 4.7.1 reference assemblies, available through the developer pack or compatible SDK tooling.
- A local Sailwind installation and BepInExPack 5.4.2305.

Game and loader assemblies are build references only. They are not included in this repository or its packages.

## Build

From the project directory, run:

```powershell
pwsh -File ./Build.ps1 -GameManagedDir 'C:\Games\Sailwind\Sailwind_Data\Managed' -ReferenceDir 'C:\Games\Sailwind\BepInEx\core'
```

Use the paths for your own installation or mod profile. Alternatively, place `BepInEx.dll` and `0Harmony.dll` in `.local/references/` and omit `-ReferenceDir`.

The Release output is `bin/Release/net471/NewBeginnings.dll`. Build output alone does not establish in-game compatibility. See [TESTING.md](TESTING.md) for startup checks and failure reports.

## Local packages

Run `Package.ps1` under PowerShell 7 with the same reference arguments. It checks the release metadata, icon and required documentation before producing versioned plugin and source ZIPs under `artifacts/`.

To package an already built Release DLL without rebuilding it:

```powershell
pwsh -File ./Package.ps1 -SkipBuild
```

The plugin package contains the DLL, manifest, icon, README, release notes and license. The source package contains an explicit list of source files and build documentation. Local evidence, logs, build references and agent records are excluded. Package verification checks plugin identity and version, PNG dimensions, exact entry lists and extracted file hashes.

These scripts create local artifacts. They do not install the mod, push source code or publish a release.
