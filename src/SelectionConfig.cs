using BepInEx.Configuration;

namespace NewBeginnings
{
    internal sealed class SelectionConfig
    {
        private readonly ConfigFile file;
        private readonly ConfigEntry<int> lastPort;
        private readonly ConfigEntry<string> lastDestination;
        private readonly ConfigEntry<bool> randomPort;
        private readonly ConfigEntry<bool> shipyardPortsOnly;
        private readonly ConfigEntry<bool> includeModPorts;
        private readonly ConfigEntry<bool> enablePirateIsland;
        private readonly ConfigEntry<bool> enableSaffronIsland;
        private readonly ConfigEntry<bool> alAnkh;
        private readonly ConfigEntry<bool> emerald;
        private readonly ConfigEntry<bool> aestrin;
        private readonly ConfigEntry<bool> fireFishLagoon;
        private readonly ConfigEntry<bool> smallIslands;
        private readonly ConfigEntry<bool> chronos;
        private readonly ConfigEntry<int> lastBoat;
        private readonly ConfigEntry<bool> randomBoat;
        private readonly ConfigEntry<bool> smallBoats;
        private readonly ConfigEntry<bool> mediumBoats;
        private readonly ConfigEntry<bool> largeBoats;
        private readonly ConfigEntry<string> excludedRandomPorts;
        private readonly ConfigEntry<string> excludedRandomBoats;
        private readonly ConfigEntry<string> boatSizeOverrides;
        private readonly ConfigEntry<string> portPoolOverrides;
        private readonly ConfigWarnings warnings = new ConfigWarnings();

        internal SelectionSettings Settings { get; } = new SelectionSettings();
        internal IndividualExclusions IndividualExclusions { get; } =
            new NewBeginnings.IndividualExclusions();
        internal SelectionOverrides Overrides { get; private set; }

        internal SelectionConfig(ConfigFile file)
        {
            this.file = file;
            lastPort = file.Bind("Selection", "LastSelectedPort", 2,
                "Last manually selected port index. Default: Neverdin.");
            lastDestination = file.Bind("Selection", "LastSelectedDestination", "",
                "Stable identity of the last manually selected starting location. Empty uses LastSelectedPort. Missing locations retain their selection until you choose another.");
            randomPort = file.Bind("Selection", "RandomPort", false,
                "Choose a random starting location from the checked geographic pools and enabled mod ports.");
            shipyardPortsOnly = file.Bind("Selection", "ShipyardPortsOnly", false,
                "When Random Port is enabled, limit eligible locations to detected shipyards. Ignore individual island exclusions without changing them. Spoiler location options still apply.");
            includeModPorts = file.Bind("Random Port Pools", "IncludeModPorts", false,
                "Include detected mod starting locations in random starts regardless of their geographic pool. Shipyard, spoiler and placement requirements still apply.");
            enablePirateIsland = file.Bind("Spoilers", "EnablePirateIsland", false,
                Hidden("Enable Pirate Island as a manual starting location and in the Small Islands random pool."));
            enableSaffronIsland = file.Bind("Spoilers", "EnableSaffronIsland", false,
                Hidden("Enable Saffron Island as a manual starting location and in the Small Islands random pool."));
            var spoilerMenu = new SpoilerSettingsMenu(Settings, enablePirateIsland, enableSaffronIsland);
            file.Bind("Advanced", "SpoilerOptions", "",
                new ConfigDescription("Reveal optional hidden starting locations.", null,
                    new ConfigurationManagerAttributes { CustomDrawer = spoilerMenu.Draw, Category = "Advanced",
                        DispName = "Spoiler options", HideSettingName = true, HideDefaultButton = true }));
            alAnkh = file.Bind("Random Port Pools", "AlAnkh", true, "Include Al'Ankh ports.");
            emerald = file.Bind("Random Port Pools", "EmeraldArchipelago", true,
                "Include Emerald Archipelago ports.");
            aestrin = file.Bind("Random Port Pools", "Aestrin", true, "Include Aestrin ports.");
            fireFishLagoon = file.Bind("Random Port Pools", "FireFishLagoon", false,
                "Include Fire Fish Lagoon ports.");
            smallIslands = file.Bind("Random Port Pools", "SmallIslands", false,
                "Include geographically separate small islands.");
            chronos = file.Bind("Random Port Pools", "Chronos", false,
                "Include ordinary Chronos in random starts. Fort Juniper follows IncludeModPorts separately.");
            lastBoat = file.Bind("Selection", "LastSelectedBoat", 10,
                "Last manually selected boat scene index. Default: small dhow.");
            randomBoat = file.Bind("Selection", "RandomBoat", false,
                "Choose a random boat from the checked size pools.");
            smallBoats = file.Bind("Random Boat Sizes", "Small", true, "Include small boats.");
            mediumBoats = file.Bind("Random Boat Sizes", "Medium", true, "Include medium boats.");
            largeBoats = file.Bind("Random Boat Sizes", "Large", true, "Include large boats.");
            excludedRandomPorts = file.Bind("Individual Random Exclusions", "Ports", "",
                "Exact runtime port names or stable site identities excluded from random starts, encoded and separated by semicolons. Edit with the new-game menu.");
            excludedRandomBoats = file.Bind("Individual Random Exclusions", "Boats", "",
                "Exact runtime names of boats excluded from random starts, encoded and separated by semicolons. Edit with the new-game menu.");
            boatSizeOverrides = file.Bind("Compatibility", "BoatSizeOverrides", "",
                "Semicolon-separated sceneIndex|exactGameObjectName|Small/Medium/Large records.");
            portPoolOverrides = file.Bind("Compatibility", "PortPoolOverrides", "",
                "Semicolon-separated portIndex|exactGameObjectName|AlAnkh/Emerald/Aestrin/FireFishLagoon/SmallIslands/Chronos records.");

            Read();
            Save();
        }

        private static ConfigDescription Hidden(string description) => new ConfigDescription(description, null,
            new ConfigurationManagerAttributes { Browsable = false });

        // Startup and live reloads (Configuration Manager or a reread file) share
        // this read, so validation, fallbacks and warnings are identical. The
        // existing objects are updated in place for every holder.
        internal void Read()
        {
            warnings.Begin();
            Settings.PortIndex = lastPort.Value;
            Settings.DestinationKey = lastDestination.Value ?? "";
            Settings.RandomPort = randomPort.Value;
            Settings.ShipyardPortsOnly = shipyardPortsOnly.Value;
            Settings.IncludeModPorts = includeModPorts.Value;
            Settings.EnablePirateIsland = enablePirateIsland.Value;
            Settings.EnableSaffronIsland = enableSaffronIsland.Value;
            Settings.BoatSceneIndex = lastBoat.Value;
            Settings.RandomBoat = randomBoat.Value;
            IndividualExclusions.ReplaceWith(NewBeginnings.IndividualExclusions.Load(
                excludedRandomPorts.Value, excludedRandomBoats.Value));
            Settings.PortPools.Clear();
            if (alAnkh.Value) Settings.PortPools.Add(PortPool.AlAnkh);
            if (emerald.Value) Settings.PortPools.Add(PortPool.Emerald);
            if (aestrin.Value) Settings.PortPools.Add(PortPool.Aestrin);
            if (fireFishLagoon.Value) Settings.PortPools.Add(PortPool.FireFishLagoon);
            if (smallIslands.Value) Settings.PortPools.Add(PortPool.SmallIslands);
            if (chronos.Value) Settings.PortPools.Add(PortPool.Chronos);
            Settings.BoatSizes.Clear();
            if (smallBoats.Value) Settings.BoatSizes.Add(BoatSize.Small);
            if (mediumBoats.Value) Settings.BoatSizes.Add(BoatSize.Medium);
            if (largeBoats.Value) Settings.BoatSizes.Add(BoatSize.Large);
            if (Settings.PortPools.Count == 0 && !Settings.IncludeModPorts)
            {
                Settings.PortPools.Add(PortPool.AlAnkh);
                warnings.Warn("Every random port pool was disabled; Al'Ankh was restored.");
            }
            if (Settings.BoatSizes.Count == 0)
            {
                Settings.BoatSizes.Add(BoatSize.Small);
                warnings.Warn("Every random boat size was disabled; Small was restored.");
            }
            Overrides = SelectionOverrides.Parse(boatSizeOverrides.Value,
                portPoolOverrides.Value);
            foreach (var error in Overrides.Errors)
                warnings.Warn(error);
            warnings.End();
        }

        internal void Save()
        {
            lastPort.Value = Settings.PortIndex;
            lastDestination.Value = Settings.DestinationKey ?? "";
            randomPort.Value = Settings.RandomPort;
            shipyardPortsOnly.Value = Settings.ShipyardPortsOnly;
            includeModPorts.Value = Settings.IncludeModPorts;
            enablePirateIsland.Value = Settings.EnablePirateIsland;
            enableSaffronIsland.Value = Settings.EnableSaffronIsland;
            alAnkh.Value = Settings.PortPools.Contains(PortPool.AlAnkh);
            emerald.Value = Settings.PortPools.Contains(PortPool.Emerald);
            aestrin.Value = Settings.PortPools.Contains(PortPool.Aestrin);
            fireFishLagoon.Value = Settings.PortPools.Contains(PortPool.FireFishLagoon);
            smallIslands.Value = Settings.PortPools.Contains(PortPool.SmallIslands);
            chronos.Value = Settings.PortPools.Contains(PortPool.Chronos);
            lastBoat.Value = Settings.BoatSceneIndex;
            randomBoat.Value = Settings.RandomBoat;
            smallBoats.Value = Settings.BoatSizes.Contains(BoatSize.Small);
            mediumBoats.Value = Settings.BoatSizes.Contains(BoatSize.Medium);
            largeBoats.Value = Settings.BoatSizes.Contains(BoatSize.Large);
            excludedRandomPorts.Value = IndividualExclusions.SavePorts();
            excludedRandomBoats.Value = IndividualExclusions.SaveBoats();
            file.Save();
        }
    }
}
