# Checking a new start

Build and package checks do not run Sailwind. Check changed startup behavior in-game using a separate new game.

## Menu and placement

- On a fresh launch, confirm the parchment appears behind the new-game controls before opening any submenu. Recheck after returning from exclusions and Starting Options, then after leaving and reopening the new-game menu.
- Confirm manual port and boat choices, then try each Random switch independently.
- Check that the relevant exclusion list appears, its pages wrap, and the single Done button saves changes and returns to selection. An excluded entry should remain available manually.
- Check the player's final position, mooring lines and complete starter supplies after placement finishes. Confirm that player controls return and no startup correction continues after the boat has settled.
- For difficulty-related changes, check the actual supplies for the selected difficulty. A Hard start's cup should remain upright with its water.
- For HMS Leopard rig changes, check the initial furled sail and normal reefing controls after startup.

Choose combinations relevant to the change. Larger ships can ground at cramped ports, so distinguish a port's physical clearance from a failed placement.

## Starting options

- Open each page with and without Scrambled Seas and Save Slots Plus. Check label size, pointer targets, slider clicks and dragging, and the native name field after returning. The single centered Done button must retain edits when reopening the menu. With Sailwind Difficulty installed, check that its panel does not cover the Starting Options panel or Done button.
- Check a default start, then 0x, a fractional multiplier and 100x. Only the selected starting faction's currency should multiply. Check blank, 0 and positive custom amounts for all four currencies, including an override of the starting faction's currency. Repeat a relevant case with Sailwind Difficulty and Scrambled Seas.
- Check default reputation, the main starting-region level, and distinct values for all three regions. Positive regional values must win over the main value. Zero must leave that region at its normal value unless the main slider applies there. Verify both the displayed level and its trading/mission effects.
- Browse every equipment page and check one item already present in the normal kit: it should add exactly one extra. Try no selection, an individual map, the filled hook/firewood boxes, and every available entry. The boxes should contain 20 hooks and 12 firewood respectively. Old map, Brining jar, single hook and single firewood should not appear. Normal small supplies should share the crates with selected extras, including on a start with no extras. Count and retrieve everything from every crate, including the final partially filled crate, and retrieve the loose large equipment. Check the original food/drink amounts and that the checklist adds no food, drinks, furniture or trade goods. Repeat default packing with Difficulty and Scrambled Seas, confirming their intended removals stay removed.
- With equipment mods installed, browse past the final vanilla page to Mod items. The page counter should show the combined total from the first page and continue through the mod section. Check distinct variants and their source names, select both vanilla and mod tools, and verify the added items work normally. Large or custom-scale mod equipment, large speakers, Turbo Wolfer, every weathervane and all Kemy furniture should be absent. The native filled supply boxes remain selectable. After removing a selected mod, Clear unavailable should remove its missing selections while preserving available selections. Repeat after changing mod load order to check that selections still identify the same items.
- Check automatic discovery with another mod that registers small tools, including tools made only from native components and tools using their own item classes. Verify their visuals, crate insertion/retrieval and normal controls. A template with no usable mesh/material should not appear. With Climate installed, check its barometer, thermometer and hygrometer separately from Windicators' barometer. The inspected broken Windicators template should be unavailable, so a previously selected copy can be removed with Clear unavailable.
- With the refactored Radio installed, confirm the checklist names are Radio and Small Speaker. Select both alongside a vanilla tool and another mod tool, then check crate retrieval and normal device controls. Large speakers and Turbo Wolfer must stay excluded.
- Open and pick up an added crate during initial setup, then check that placement does not move it or its contents afterward. Once setup has finished, normal purchases, spending, reputation changes and equipment movement must remain under the game's control.

User trials reported successful boat and equipment spawns, all 55 entries in the original checklist appearing in crates, a two-crate selection, custom currency amounts, a 30x Aestrin currency multiplier and regional reputation overrides. A subsequent trial confirmed that Climate and Realistic Skies items appear and work correctly. These reports do not cover every case above or every discovered mod item. Managed and assembly-inspection checks do not establish menu rendering, item behavior or combined-mod compatibility. No game-save behavior is added by this feature.

## Reporting a problem

Include the New Beginnings version, boat, port, difficulty and relevant installed mods. Describe what happened after placement finished, including any missing supplies or controls that remained unavailable.

Preserve `BepInEx/LogOutput.log` from the active profile and Sailwind's `Player.log` before restarting. Include a screenshot when the problem concerns position or sail appearance.
