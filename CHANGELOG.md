# Release notes

## Version 1.2.4

### Added

- Automatic starting-location discovery for modded ports, including Better Ports' Bottleneck Isle.
- A Mod ports checkbox appears with the Random Port pool checkboxes when added locations are detected. It starts disabled and adds those locations regardless of the checked regions. Leave every other pool unchecked for mod ports only, or select an added location manually without enabling the checkbox.
- A separate Chronos pool option for including its original berth in Random Port. It starts disabled.
- An optional [port-author API](PORT_AUTHORS.md) for specifying a preferred dock, berth or landing when automatic discovery needs more information.

<details>
<summary>Spoilers: additional starting locations</summary>

- Fort Juniper is a separate starting location when Old Chronian is installed and qualifies for Shipyard ports only.
- Pirate Island and Saffron Island are optional Small Islands starts. Both start disabled behind the Configuration Manager spoiler reveal and can be enabled separately for manual or random selection. Mod ports does not affect either island.
- Pirate Island allows Small boats at its existing docks. Medium, Large and unclassified boats start farther offshore and are held in place until the player boards them.
- At Saffron, the player and supplies start on dry land with the boat anchored nearby.

</details>

### Changed

- Shipyard ports only uses detected shipyards instead of a fixed list of vanilla ports.
- Added locations without a normal starting berth check room for the boat, dry landing and supplies before granting the boat. If a suitable start cannot be found, the menu shows a reason.
- A selected mod location remains selected when its provider is missing and is shown as unavailable until it returns or another location is chosen.
- Pirate Island's offshore starts use the best available surveyed position. If no surveyed position passes the clearance checks, the start still proceeds at a bounded outer position and writes a warning to the log.

### Fixed

- Custom shore starts now reserve space for the final packed starter-supply footprint instead of counting packable items as both loose cargo and crate contents.
- Addressed Pirate Island falling back to a regional start when Scrambled Seas handled the new-game transition before its scenery could load.
- Addressed Pirate Island rejecting every dock while the precise ocean-height sample was still initializing. Dock checks now use the live maximum water bound until the precise sample is ready.
- Pirate Island's automatic dock starts now pass their selected moorings into the dry-ground check, allowing that same water-bound fallback to validate nearby shore.
- Pirate Island placement now shares a strict search budget across docking and anchoring, checks distinct dock directions first and avoids repeating failed shore searches.
- Pirate Island checks the widest supported dock offsets first so its bounded survey reaches open water before testing positions closer to shore.
- Failed Pirate anchorage checks now report rejection counts and include the blocking collider when a collision check rejects the last candidate.
- Native-anchor starts now use the boat's physical joint attachments, including the layout used by the Santa Maria and vanilla Mediterranean boats.
- After Scrambled Seas has moved the world, a failed custom start now requires a game restart. This prevents repeated New Game clicks from moving the same world again.

## Version 1.2.3

### Added

- The [Santa Maria](https://github.com/winterspices/AgeOfExploration) is now recognized as a medium boat.

### Changed

- Boats from mods that New Beginnings doesn't already know are now sorted into Small, Medium or Large by hull size when possible, so they join the matching Random Boat pool and the exclusion list instead of being manual selection only.
- FFL Paraw is now labeled Paraw in the boat selector and exclusion list.

### Fixed

- The log no longer shows a false "catalog has no usable start" warning at every launch.
- Changes made in Configuration Manager now apply while the game is running instead of being overwritten by the new-game menu.

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

- Restored missing Kemy navigation tools in the equipment menu.
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
- Fixed incompatibility with Save Slots Plus.
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
