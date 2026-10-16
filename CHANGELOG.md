# Release notes

## Version 1.5.1

### Added

- Automatic starting-location discovery for mod ports.
- A `Mod ports` checkbox as one of the options for Random Port pool checkboxes when mod locations are detected.
- A `Use shipyard berth` option that starts the game with the selected boat at the port's shipyard berth. It applies to manual and random starts and is disabled by default.
- A `Spawn` tab in Starting options menu that contains boat/island exclusions, shipyard settings and a Secret islands toggle. Existing settings carry over.
- Fort Juniper is an added starting location when [Old Chronian](https://github.com/Aquilarts/OldChronian) is installed. Fort Juniper also qualifies for Shipyard ports only spawning.
- Bottleneck Isle is an added starting location when [Better Ports](https://github.com/winterspices/BetterPorts) is installed.
- Boat authors can register the player-facing name used in the boat selector and exclusion list using the new [boat author API](BOAT_AUTHORS.md).
- Port authors can supply optional dock, berth, shore, mooring and shipyard details for an automatically discovered native port using the new [port author API](PORT_AUTHORS.md).

<details>
<summary>Spoilers: additional starting locations</summary>

- Chronos is now included in Small Islands randomization if enabled, and can be enabled or disabled in the new `Spawn` tab.
- Pirate Island and Saffron Island are now optional Small Islands starts when enabled in the `Spawn` tab.
- Pirate Island uses one of its existing docks for Small boats when they can moor there. Larger boats that don't fit well at the dock wait offshore until the player boards them (make sure to drop the anchor when you board to prevent the ship floating away).
- At Saffron, the player and supplies start on dry land near the water while the boat waits offshore until the player boards (make sure to drop the anchor when you board to prevent the ship floating away).

</details>

### Changed

- Shipyard ports only uses detected shipyards instead of a fixed list of vanilla ports.
- Exclusions and shipyard settings can be edited from the new Spawn tab without enabling random selection first. Port controls, Spawn labels and menu messages have also been rearranged for readability.
- Starting supplies favor the space nearest the player and suitable crates can begin in short stacks up to 3 items high.
- Starting mooring lines receive up to one metre of extra slack on every boat (down from 3).
- Siren Song can now be used with any boat.

## Version 1.2.3

### Added

- The [Santa Maria](https://github.com/winterspices/AgeOfExploration) is now recognized as a medium boat.

### Changed

- Boats from mods that New Beginnings doesn't already know are now sorted into Small, Medium or Large by hull size when possible, so they join the matching Random Boat pool and the exclusion list instead of being manual selection only.
- [FFL Paraw](https://github.com/alesparise/FFLParaw-Sailwind-Mod) is now labeled Paraw in the boat selector and exclusion list.

### Fixed

- The log no longer shows a false "catalog has no usable start" warning at every launch.
- Changes made in [Configuration Manager](https://github.com/BepInEx/BepInEx.ConfigurationManager) now apply while the game is running instead of being overwritten by the new-game menu.

## Version 1.2.2

### Added

- A Shipyard ports only option for Random Port. It limits starts to Gold Rock City, Dragon Cliffs, Fort Aestrin and Kicia Bay within the checked regions.

### Changed

- The Baghlah is now labeled Baghlah (Bigbuq) in the boat selector and exclusion list.

### Fixed

- Normal regional starter maps now pack into supply crates instead of being left on the ground.

## Version 1.2.1

### Changed

- Boats starting at their original port now keep their existing berth, moorings and anchor when available instead of being moved to the port's recovery location. This change includes mod boats that use the game's native mooring setup.

### Fixed

- Fixed fishing rods selected as starting equipment failing to cast.

## Version 1.2.0

### Added

- A Standard supplies toggle in the equipment menu.
- Box of wall hooks, Box of salmon, Box of oranges, Barrel of water, Barrel of rum and Single cheese in the equipment menu. Each box of wall hooks contains 12 hooks for hanging items on walls.

### Changed

- Replaced equipment checkboxes with quantity controls. 
- Replaced the single lantern candle with Box of candles.
- Adjusted the equipment menu spacing to keep labels and controls separate while retaining ten items per page.

### Fixed

- Restored missing [Kemy Navigation Tools](https://github.com/Kemylar/KemyNavigationTools) items in the equipment menu.
- Addressed a crate-allocation error that could prevent selected equipment from being placed.

### Known issues

- Large starting loadouts can cause the game to hitch and take longer to place. Small ports may not have enough clear ground for all your crates and barrels, so supplies may fail to appear. Try fewer items or a larger port if this happens.

## Version 1.1.0

### New

Starting Options:

- A starting-money multiplier from 0x to 100x, with optional custom amounts for each of the four currencies.
- A starting-region reputation slider and separate overrides for all three regions.
- A paged equipment checklist that adds one of each selected item alongside your normal supplies.
- Automatic discovery of eligible small mod equipment items.

### Changed

- Eligible starting supplies and selected equipment are packed into crates beside the player.

## Version 1.0.1

### Fixed

- Fixed an error that prevented starting a new game on Sailwind’s stable branch.
- Fixed incompatibility with [Save Slots Plus](https://github.com/bryon82/SailwindSaveSlotsPlus).
- Improved handling of incompatible boat entries so other valid starts remain available.

### Changed

- Moved Continue and Back slightly lower when Save Slots Plus is installed to leave more room for its save-name field.
- Added startup error handling to keep unexpected compatibility failures from leaving the new-game menu blocked.

## Version 1.0.0

New Beginnings adds port and boat selection to Sailwind's new-game menu. Choose both, randomize either one, or roll the dice on both.

### Added

- Independent Random Port and Random Boat options, with regional port pools and boat size filters.
- Separate island and boat exclusion lists that let you prevent starting with specific boats or at certain ports.
- Starter supplies placed near the player, with the selected region and [difficulty](https://github.com/NANDbrew/SailwindDifficulty) reflected in the supplies you receive.
- Extra mooring slack and clearance for larger ships at spawn to avoid issues, followed by one hull repair after the boat settles.
- A stock medium lateen starter sail for an unfitted [HMS Leopard](https://github.com/winterspices/HMSLeopard).

### Known issues

- HMS Leopard's attached lateen starter sail can briefly flash fully deployed when unfurling before returning to its actual state.
