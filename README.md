# New Beginnings

New Beginnings adds port and boat selection or randomization to Sailwind's new-game menu. Choose both, randomize either one, or roll the dice on both. You can also adjust your starting money, reputation and equipment before setting sail.

## Install

### Mod managers

Install New Beginnings through r2modman or Thunderstore Mod Manager, then launch Sailwind through your manager. Dependencies are installed automatically.

### Manual installation

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) in your Sailwind game folder, following its installation instructions.
2. Download and extract the New Beginnings ZIP. Copy its `BepInEx/plugins/NewBeginnings` folder into `BepInEx/plugins` in your Sailwind folder.
3. Launch Sailwind normally.

## How it works

Choose a port and boat, or randomize one or both, then press Continue to start a new game. Random Port and Random Boat show their pool options on the main screen. Island and boat exclusions and the shipyard settings are in the `Spawn` tab under `Starting options`.

Ports added by other mods are discovered automatically. When added ports are detected, `Mod ports` includes them in Random Port, or you can select one manually.

The starting options menu allows you to adjust your money, reputation, and equipment. The money menu allows you to multiply your starting cash, with overrides to set a specific amount of each currency as well. For reputation, you can select your starting region reputation level, or set specific levels for each region. Items that fit are packed into crates near your spawn instead of being placed directly on the ground.

<details>
<summary>Spoilers: additional starting locations</summary>

Chronos, Pirate Island and Saffron Island can be enabled in the `Spawn` tab. They remain available for manual selection and join Small Islands randomization when `Small Islands` is enabled.

</details>

## Starting supplies and mooring

With standard supplies enabled, your starting port determines the regional supplies, which are packed into crates on the ground nearby. Larger supplies are placed beside them. If [Sailwind Difficulty](https://github.com/NANDbrew/SailwindDifficulty) is installed, its selected difficulty determines those supplies.

Starting mooring lines receive up to 1 metre of extra slack on every boat, and large boats start 1 metre farther out from the dock to give them more space. [HMS Leopard](https://github.com/winterspices/HMSLeopard) gets 3 metres of extra distance to allow for its size. Every starting boat receives one hull repair about eight seconds after it is ready, including boats kept at their original berth, to clear any startup damage.

## Configuration

The new-game menu saves choices, exclusions and starting options in `BepInEx/config/com.skeptic043.sailwind.newbeginnings.cfg`. After launching once, settings can also be changed in that file or in game using the BepInEx 5 version of [BepInEx Configuration Manager](https://github.com/BepInEx/BepInEx.ConfigurationManager).

Most setup finishes shortly after starting, including mooring setup and the one-time hull repair. Offshore starting boats remain held until you board them, and unfinished holds are saved and restored. Board an offshore starter before uninstalling New Beginnings.

## Compatibility and limitations

Large starting loadouts can take longer to place. Small ports may not have enough clear ground for all your crates and barrels, so supplies may fail to appear. Reduce your selection or choose a larger port if this happens.

For the starting options menu, automatic mod equipment detection looks for small tools that can go into crates. It can find items from untested mods, but cannot guarantee automatic inclusion of every mod item. Please open an issue if you run into any problems with mod items. Equipment in the following mods is tested working and compatible:

- [SailwindClimate](https://github.com/bryon82/SailwindClimate)
- [Windicators](https://github.com/NANDbrew/Windicators)
- [Propeller](https://github.com/DogEggz01/Propeller)
- [Realistic-Skies](https://github.com/KingCam77/Realistic-Skies)
- [KemyNavigationTools](https://github.com/Kemylar/KemyNavigationTools)
- [Sailwind-Radio](https://github.com/Skeptic043/Sailwind-Radio)

Port and boat selection has been tested with [Scrambled Seas: NANDbrew Edition](https://github.com/NANDbrew/scrambled-seas), [Sailwind Difficulty](https://github.com/NANDbrew/SailwindDifficulty), and [SaveSlotsPlus](https://github.com/bryon82/SailwindSaveSlotsPlus). The following mods that add boats have also been tested with port and boat selection:

- [HMS Leopard](https://github.com/winterspices/HMSLeopard)
- Happy Bay Boat
- [Clipper](https://github.com/TheOriginOfAllEvil/Shattered-Seas-Expansion/releases/tag/b1.2.8)
- [Sh'ba](https://github.com/TheOriginOfAllEvil/Shattered-Seas-Expansion/releases/tag/sb0.2.1)
- [Paraw](https://github.com/alesparise/FFLParaw-Sailwind-Mod)
- [Old Chronian](https://github.com/Aquilarts/OldChronian)
- [Dinghies](https://github.com/alesparise/Dinghies-Sailwind-Mod)
- [Age of Exploration](https://github.com/winterspices/AgeOfExploration)

Boats from other mods that aren't on this list are sorted into Small, Medium or Large by hull size when possible, so they can come up in Random Boat. Any that can't be measured stay available for manual selection.

Each original vanilla port has been tested with at least one large boat, but this does not mean every hull fits every port, particularly HMS Leopard. Large ships can take a moment to settle, and some ports don't have enough room for them. Use Island Exclusions to leave cramped ports out of random starts, exclude particular boats, or disable the Large size pool.

Ports with limited room for large ship spawns include (but are not limited to):

- Sage Hills
- Mirage Mountain
- Aestra Abbey
- Old Ankh Town

HMS Leopard is especially large and can struggle at ports that work for other large ships. She has grounded at Neverdin on spawn in my testing and may be difficult to free there. She also ships without default sails. Because of this, on a new start with an unfitted Leopard, New Beginnings adds one stock medium lateen to its central main mast.

## Known issues

- HMS Leopard's starter sail can briefly flash fully deployed when unfurling, then return to its actual reefing position.

## AI Use

AI was used to write all of the code in this project. The original concept, design direction, testing, debugging, and release decisions are my own. If you prefer not to use mods developed with AI assistance, I understand and respect that choice.

## Issues and links

[Report an issue](https://github.com/Skeptic043/sailwind-new-beginnings/issues) with your mod version and `BepInEx/LogOutput.log`.

[Source code](https://github.com/Skeptic043/sailwind-new-beginnings) · [Release notes](CHANGELOG.md) · [MIT License](LICENSE) · [Support on Ko-fi](https://ko-fi.com/skeptic043) · skeptic043
