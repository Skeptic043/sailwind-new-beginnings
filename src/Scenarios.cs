using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace NewBeginnings
{
    // Where a scenario puts the boat: at the chosen location as usual, or out
    // at sea from that location's island, near land or far from it.
    internal enum ScenarioPlace { Port, NearLand, FarAtSea }

    // A scenario is a preset start. Its defaults seed the player's own copy of
    // the selection and starting options, which they customize and keep; the
    // rest applies once when the game starts.
    internal sealed class Scenario
    {
        internal string Id, Name, Description;
        internal ScenarioPlace Place;
        // Hull damage set once after the start settles; zero keeps the normal one-time repair.
        internal float HullDamage;
        // Default sail damage percentage when Sail Damage is installed and the player leaves it at Off/None.
        internal int DefaultSailDamage;
        // Drunkenness set once when play begins; zero leaves it alone.
        internal float Alcohol;
        internal bool RegionalCompass;
        // Random Port chooses only islands without a port.
        internal bool Portless;
        // Fill the free deck space with rum barrels (at-sea starts only).
        internal bool FillWithRum;
        // Allow Bone Isle even when the player has secret islands off in main settings.
        internal bool AllowBoneIsle;
        internal readonly Dictionary<string, int> Guaranteed = new Dictionary<string, int>(StringComparer.Ordinal);
        internal Action<SelectionSettings, StartingOptionsSettings> Defaults = (selection, options) => { };
        internal bool AtSea => Place != ScenarioPlace.Port;
    }

    internal static class Scenarios
    {
        internal static readonly Scenario[] All =
        {
            new Scenario
            {
                Id = "shipwrecked", Name = "Shipwrecked",
                Description = "Stranded on an island without a port. Your boat is badly damaged, but you have a bucket.",
                HullDamage = .8f, DefaultSailDamage = 50, Portless = true, Guaranteed = { { StartLoadout.BucketKey, 1 } },
                Defaults = (selection, options) =>
                {
                    selection.RandomPort = true;
                    selection.NoShipyardPorts = true;
                    selection.PortPools.UnionWith(new[] { PortPool.AlAnkh, PortPool.Emerald, PortPool.Aestrin,
                        PortPool.FireFishLagoon, PortPool.SmallIslands });
                    selection.RandomBoat = true;
                }
            },
            new Scenario
            {
                Id = "fishermans-luck", Name = "Fisherman's Luck",
                Description = "A small boat, a rod and hooks, a barrel of water and a compass. No food and no money.",
                RegionalCompass = true,
                Guaranteed = { { StartLoadout.WaterBarrelKey, 1 }, { StartLoadout.FishingRodKey, 1 }, { StartLoadout.FishingHooksKey, 1 } },
                Defaults = (selection, options) =>
                {
                    selection.RandomPort = true;
                    selection.RandomBoat = true;
                    selection.BoatSizes.Clear();
                    selection.BoatSizes.Add(BoatSize.Small);
                    options.StandardSupplies = false;
                    NoMoney(options);
                }
            },
            new Scenario
            {
                Id = "poor-drunk", Name = "Poor Drunk",
                Description = "Your last coin went on rum. An empty rum barrel, a bottle of water, a compass and a fuzzy head.",
                RegionalCompass = true, Alcohol = 50f,
                Guaranteed = { { AdditionalEquipment.EmptyRumBarrelKey, 1 }, { AdditionalEquipment.LargeWaterBottleKey, 1 } },
                Defaults = (selection, options) =>
                {
                    selection.RandomPort = true;
                    selection.RandomBoat = true;
                    options.StandardSupplies = false;
                    NoMoney(options);
                }
            },
            new Scenario
            {
                Id = "lost-at-sea", Name = "Adrift",
                Description = "Adrift at sea with a compass and a few supplies.",
                Place = ScenarioPlace.FarAtSea, RegionalCompass = true,
                Defaults = (selection, options) =>
                {
                    selection.RandomPort = true;
                    selection.RandomBoat = true;
                    options.StandardSupplies = false;
                    options.EquipmentQuantities[StartLoadout.WaterBarrelKey] = 1;
                    options.EquipmentQuantities[StartLoadout.SalmonKey] = 1;
                    options.EquipmentQuantities[StartLoadout.OrangesKey] = 1;
                }
            },
            new Scenario
            {
                Id = "drunk-and-lost", Name = "Drunk at Sea",
                Description = "At sea within sight of land, already drunk, with a hold full of rum and one barrel of water lost somewhere among it.",
                Place = ScenarioPlace.NearLand, Alcohol = 80f, FillWithRum = true,
                Guaranteed = { { StartLoadout.WaterBarrelKey, 1 } },
                Defaults = (selection, options) =>
                {
                    selection.RandomPort = true;
                    selection.RandomBoat = true;
                    options.StandardSupplies = false;
                }
            },
            new Scenario
            {
                Id = "boned", Name = "Boned",
                Description = "Stranded on Bone Isle with a small boat, one bottle of water, one uncooked eel and no money.",
                AllowBoneIsle = true,
                Guaranteed = { { AdditionalEquipment.LargeWaterBottleKey, 1 }, { StartLoadout.EelKey, 1 } },
                Defaults = (selection, options) =>
                {
                    selection.RandomPort = false;
                    selection.DestinationKey = KnownSites.BoneIsleKey;
                    selection.RandomBoat = true;
                    selection.BoatSizes.Clear();
                    selection.BoatSizes.Add(BoatSize.Small);
                    options.StandardSupplies = false;
                    NoMoney(options);
                }
            },
        };

        private static void NoMoney(StartingOptionsSettings options)
        {
            for (var i = 0; i < options.CurrencyOverrides.Length; i++) options.CurrencyOverrides[i] = 0;
        }
    }

    // One scenario's own saved choices, in a file beside the main configuration.
    internal sealed class ScenarioProfile
    {
        internal readonly Scenario Scenario;
        internal readonly ConfigFile File;
        internal readonly SelectionConfig Selection;
        internal readonly StartingOptionsConfig Options;

        internal ScenarioProfile(Scenario scenario, SelectionSettings main)
        {
            Scenario = scenario;
            var path = Path.Combine(Path.Combine(Paths.ConfigPath, Plugin.Id + ".scenarios"), scenario.Id + ".cfg");
            var created = !System.IO.File.Exists(path);
            File = new ConfigFile(path, true) { SaveOnConfigSet = false };
            Selection = new SelectionConfig(File, scenario: true);
            Options = new StartingOptionsConfig(File);
            if (created)
            {
                // A new profile starts from the scenario's defaults.
                scenario.Defaults(Selection.Settings, Options.Settings);
                Selection.Save();
                Options.Save();
            }
            UseSpoilers(main);
        }

        internal void Reload(SelectionSettings main)
        {
            if (System.IO.File.Exists(File.ConfigFilePath)) File.Reload();
            Selection.Read();
            Options.Read();
            UseSpoilers(main);
        }

        // Spoiler choices belong to the main file; a scenario follows them.
        private void UseSpoilers(SelectionSettings main)
        {
            var selection = Selection.Settings;
            selection.EnablePirateIsland = main.EnablePirateIsland;
            selection.EnableSaffronIsland = main.EnableSaffronIsland;
            selection.EnableBoneIsle = main.EnableBoneIsle || Scenario.AllowBoneIsle;
            selection.EnableChronos = main.EnableChronos;
            selection.PortlessOnly = Scenario.Portless;
        }
    }
}
