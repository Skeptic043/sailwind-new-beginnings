# Release notes

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

### Compatibility

Tested with:

- [Scrambled Seas: NANDbrew Edition](https://github.com/NANDbrew/scrambled-seas)
- [Sailwind Difficulty](https://github.com/NANDbrew/SailwindDifficulty)
- [HMS Leopard](https://github.com/winterspices/HMSLeopard)
- Happy Bay Boat
- [Clipper](https://github.com/TheOriginOfAllEvil/Shattered-Seas-Expansion/releases/tag/b1.2.8)
- [Sh'ba](https://github.com/TheOriginOfAllEvil/Shattered-Seas-Expansion/releases/tag/sb0.2.1)
- [Paraw](https://github.com/alesparise/FFLParaw-Sailwind-Mod)
- [Old Chronian](https://github.com/Aquilarts/OldChronian)
- [Dinghies](https://github.com/alesparise/Dinghies-Sailwind-Mod)

<details>
<summary>Spoiler: a manually selected location</summary>

Chronos is available through manual selection only and is never chosen by Random Port.

</details>

Large ships can ground or collide at cramped ports, especially HMS Leopard. Use the exclusion lists or disable the Large boat pool to avoid those starts.

### Known issues

- HMS Leopard's attached lateen starter sail can briefly flash fully deployed when unfurling before returning to its actual state.
