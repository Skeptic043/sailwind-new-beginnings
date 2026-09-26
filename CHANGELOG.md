# Release notes

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
