# Release notes

## Version 1.3.6

### Changed

- Shipyard berth starts now connect the starter boat to nearby mooring cleats when its lines can reach. The existing hold remains available when a shipyard berth cannot be moored.
- Fort Juniper now places the player and supplies on the same shipyard dock as the starter boat and closer to it.
- Starter supply columns hold no more than three items, and remaining ground supplies stay within a slightly tighter area around the player.
- On'na starts can now use its separate mooring posts. When the native ropes reach the closest pair, the starter boat is moored instead of held nearby.

## Version 1.3.5

### Changed

- Kicia Bay shipyard starts now place the player and supplies at Kicia's ordinary landing on the neighboring dock. The boat remains at the shipyard berth.

### Fixed

- Starter supplies now use nearby supported positions when dock seams or fixed props prevent the usual compact layout, instead of withholding the complete group.

## Version 1.3.4

### Changed

- Native regional supplies now establish the dockside layout before added equipment. Fitting crates can stack on the starting table or the larger regional food crate, and remaining ground positions favor the space nearest the player.

### Fixed

- Fort Juniper shipyard starts now use Old Chronian's authored release berth and yard-side ground. This replaces the guessed Dock1 placement that could intersect the dock's underwater mesh and launch large boats.
- Native cheese and goat-cheese crates keep a level starting pose while the layout is planned, preventing their larger stack bases from being stranded after smaller added crates.

## Version 1.3.3

### Fixed

- Suitable starter crates now begin in short stacks and settle under the game's physics. Large loadouts no longer require a separate patch of dock for every box.
- A layout that cannot fit now stops after a bounded set of complete placement attempts instead of repeatedly searching the same dock for up to 30 seconds.
- Large equipment selections now prepare faster instead of repeating the same lookup for every packed item.
- Shipyard Ports Only now recognizes Gold Rock City, Dragon Cliffs, Fort Aestrin and Kicia Bay before their island scenery loads.
- Gold Rock City, Dragon Cliffs and Kicia Bay now place the player and supplies on measured shipyard dock surfaces independently of boat size. Wide hulls receive enough room beside the dock while retaining the shipyard's release heading.
- Fort Juniper uses the actual Old Chronian shipyard pier when Use shipyard berth is enabled. Its ordinary start still uses the wider lower-left dock.
- Fort Aestrin prefers Better Ports' drydock shipyard when it is loaded, with the normal shipyard retained as the fallback.

## Version 1.3.2

### Changed

- Island and boat exclusions can now be edited at any time. Shipyard ports only can also be changed without turning on Random Port first.
- Exclusion pages sit higher so their Back and Done buttons remain on the parchment.

### Fixed

- Fort Juniper starts now place every boat alongside the open side of the lower-left dock instead of using the cramped inner harbour.
- Starting supplies now use their authored collider geometry while inactive, preventing placement checks from treating crates and barrels as tiny points or checking stale positions.

## Version 1.3.1

### Changed

- Region and boat-size checkboxes remain on the main screen. Starting options now has a Spawn tab for exclusions and shipyard settings, replacing the separate Spawn options menu.
- Added a Secret islands toggle in the Spawn tab. It switches the hidden starting locations on or off together for manual and random selection, independently of geographic pools. Existing settings carry over.

## Version 1.3.0

### Added

- Spawn options groups random port pools, boat sizes, shipyard settings and exclusions in a separate menu. Existing choices carry over.
- Use shipyard berth optionally starts your boat at the selected shipyard's release berth, with you and your supplies ashore nearby. It applies to manual and random starts and is disabled by default.

### Changed

- The main menu keeps port and boat selection, random toggles, Spawn options and Starting options. Exclusion menus have separate Back and Done buttons to discard or save changes.

<details>
<summary>Spoiler: Saffron starting position</summary>

- Moved the landing closer to the water and the boat closer to shore, keeping the boat facing north.

</details>

## Version 1.2.10

### Changed

- Al'Ankh, Emerald and Aestrin use wider buttons on the top row. FFL moves to the row below with Small Islands and Mod ports.
- Mod-added starting locations appear together after the other ports in the selector and island exclusions.

### Fixed

- Reduced repeated catalog work when opening and refreshing the starting menu.

## Version 1.2.9

### Fixed

- Mooring and other optional setup failures keep your selected boat, location and starting options instead of substituting a regional start.
- A selected starter boat is still granted if its purchase prompt disappears during the new-game transition.
- Offshore boats waiting for you to board resume their hold after saving and loading. This applies to holds saved with this version, including after Scrambled Seas moves the islands.

### Changed

- Removed a repeated supply-space check during startup. Supplies still use the existing placement checks.

## Version 1.2.8

### Changed

- The start menu no longer hides boats it expects might not fit a location. If a selected start cannot be placed, the menu shows the reason when you press Continue.

### Fixed

- Any boat can now start at Siren Song. The Cog for sale moves to the free cleats at the end of the dock so your boat gets the normal berth. This also happens when you load a save with your boat in that berth.

## Version 1.2.7

### Fixed

- Starting at an added or hidden location no longer freezes for several seconds while the berth, landing and supplies are checked.

## Version 1.2.6

### Changed

- A selected start that runs into a placement problem now carries on and writes a warning to the log instead of returning to the main menu. The boat and player use the closest fit the checks found.
- With Scrambled Seas, a start problem no longer requires restarting the game before another new game.

### Fixed

- Harbours with short piers, such as Fort Juniper, now find a berth for large boats.

## Version 1.2.4

### Added

- Automatic starting-location discovery for modded ports, including Better Ports' Bottleneck Isle.
- A Mod ports checkbox appears with the Random Port pool checkboxes when added locations are detected. It starts disabled and adds those locations regardless of the checked regions. Leave every other pool unchecked for mod ports only, or select an added location manually without enabling the checkbox.
- A separate option for including Chronos's original berth in Random Port. It starts disabled and appears in the Configuration Manager spoiler reveal.
- An optional [port-author API](PORT_AUTHORS.md) for specifying a preferred dock, berth or landing when automatic discovery needs more information.

<details>
<summary>Spoilers: additional starting locations</summary>

- Fort Juniper is a separate starting location when Old Chronian is installed and qualifies for Shipyard ports only.
- Pirate Island and Saffron Island are optional Small Islands starts. Both start disabled behind the Configuration Manager spoiler reveal and can be enabled separately for manual or random selection. Mod ports does not affect either island.
- Pirate Island allows Small boats at its existing docks. Medium, Large and unclassified boats start farther offshore and are held in place until the player boards them.
- At Saffron, the player and supplies start on dry land. The boat waits in open water off the east coast and is held in place until the player boards it.

</details>

### Changed

- Shipyard ports only uses detected shipyards instead of a fixed list of vanilla ports.
- Added locations without a normal starting berth check room for the boat, dry landing and supplies before granting the boat. If a suitable start cannot be found, the menu shows a reason.
- A selected mod location remains selected when its provider is missing and is shown as unavailable until it returns or another location is chosen.
- Pirate Island's offshore starts use a fixed open-water position southeast of the island, outside its enclosed inner lagoon, with the bow facing east. If the clearance checks cannot confirm that position, the start still proceeds and writes a warning to the log.
- Start menu messages, such as an unavailable port and boat combination, now appear above the New Beginnings title.

### Fixed

- Docks that sit close to the waterline, such as Bottleneck Isle's, are no longer rejected while the precise ocean-height sample is still initializing.
- Large harbours with many moorings, such as Fort Juniper, now consider berths along each pier instead of only mooring pairs that span water between separate piers.
- Custom shore starts now reserve space for the final packed starter-supply footprint instead of counting packable items as both loose cargo and crate contents.
- Addressed Pirate Island falling back to a regional start when Scrambled Seas handled the new-game transition before its scenery could load.
- Addressed Pirate Island rejecting every dock while the precise ocean-height sample was still initializing.
- Pirate Island's automatic dock starts now pass their selected moorings into the dry-ground check, so nearby shore can be validated before the precise ocean-height sample is ready.
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
