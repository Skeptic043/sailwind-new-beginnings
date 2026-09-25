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

Choose a port and boat, or randomize one or both, then press Continue to start a new game. Turn on Random Port or Random Boat to change its options and open the exclusion menu. In the exclusion menu, uncheck entries to leave them out of random starts, then click Done to save changes.

The starting options menu allows you to adjust your money, reputation, and equipment. The money menu allows you to multiply your starting cash, with overrides to set a specific amount of each currency as well. For reputation, you can select your starting region reputation level, or set specific levels for each region. The equipment menu allows you to select what equipment you want to spawn with. Items that fit are packed into crates near your spawn, while fishing hooks and firewood come in their own filled boxes.

<details>
<summary>Spoiler: a manually selected location</summary>

Chronos is never randomly picked and must be manually chosen.

</details>

## Starting supplies and mooring

Your starting port determines the regional supplies, which are packed into crates on the ground nearby when they fit. Larger supplies are placed beside them. If [Sailwind Difficulty](https://github.com/NANDbrew/SailwindDifficulty) is installed, its selected difficulty determines the supplies you receive.

Boats receive up to 3 metres of extra mooring line, and large boats start 1 metre farther out from the dock to give them more space. [HMS Leopard](https://github.com/winterspices/HMSLeopard) gets 3 metres of extra distance and up to 5 metres of extra line to allow for its size. About eight seconds after the starting boat is ready, it receives one hull repair to clear damage from its initial settling.

## Configuration

The new-game menu saves choices, exclusions and starting options in `BepInEx/config/com.skeptic043.sailwind.newbeginnings.cfg`.

Once startup finishes, including mooring setup and the one-time hull repair, New Beginnings no longer moves the player, boat or supplies or changes their state, and can be safely uninstalled.

## Compatibility and limitations

For the starting options menu, automatic mod equipment detection looks for small tools that can go into crates. It can find items from untested mods, but cannot guarantee automatic inclusion of every mod item. Please open an issue if you run into any problems with mod items. Equipment in the following mods is tested working and compatible:

- [SailwindClimate](https://github.com/bryon82/SailwindClimate)
- [Windicators](https://github.com/NANDbrew/Windicators)
- [Propeller](https://github.com/DogEggz01/Propeller)
- [Realistic-Skies](https://github.com/KingCam77/Realistic-Skies)
- [KemyNavigationTools](https://github.com/Kemylar/KemyNavigationTools)

Port and boat selection has been tested with [Scrambled Seas: NANDbrew Edition](https://github.com/NANDbrew/scrambled-seas), [Sailwind Difficulty](https://github.com/NANDbrew/SailwindDifficulty), and [SaveSlotsPlus](https://github.com/bryon82/SailwindSaveSlotsPlus). The following mods that add boats have also been tested with port and boat selection:

- [HMS Leopard](https://github.com/winterspices/HMSLeopard)
- Happy Bay Boat
- [Clipper](https://github.com/TheOriginOfAllEvil/Shattered-Seas-Expansion/releases/tag/b1.2.8)
- [Sh'ba](https://github.com/TheOriginOfAllEvil/Shattered-Seas-Expansion/releases/tag/sb0.2.1)
- [Paraw](https://github.com/alesparise/FFLParaw-Sailwind-Mod)
- [Old Chronian](https://github.com/Aquilarts/OldChronian)
- [Dinghies](https://github.com/alesparise/Dinghies-Sailwind-Mod)

Every port has been tested with at least one large boat, but this does not mean every hull fits every port, particularly HMS Leopard. Large ships can take a moment to settle, and some ports don't have enough room for them. Use Island Exclusions to leave cramped ports out of random starts, exclude particular boats, or disable the Large size pool.

Ports with limited room for large ship spawns include (but are not limited to):

- Sage Hills
- Mirage Mountain
- Aestra Abbey

HMS Leopard is especially large and can struggle at ports that work for other large ships. She has grounded at Neverdin on spawn in my testing and may be difficult to free there. She also ships without default sails. Because of this, on a new start with an unfitted Leopard, New Beginnings adds one stock medium lateen to its central main mast.

## Known issues

- HMS Leopard's starter sail can briefly flash fully deployed when unfurling, then return to its actual reefing position.

## AI Use

AI was used to write code for this project. The original concept, design direction, testing, debugging, and release decisions are my own. If you prefer not to use mods developed with AI assistance, I understand and respect that choice.

## Issues and links

[Report an issue](https://github.com/Skeptic043/sailwind-new-beginnings/issues) with your mod version and `BepInEx/LogOutput.log`.

[Source code](https://github.com/Skeptic043/sailwind-new-beginnings) · [Release notes](CHANGELOG.md) · [MIT License](LICENSE) · [Support on Ko-fi](https://ko-fi.com/skeptic043) · skeptic043
