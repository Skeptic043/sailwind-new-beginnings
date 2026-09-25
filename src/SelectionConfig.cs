using BepInEx.Configuration;

namespace NewBeginnings
{
    internal sealed class SelectionConfig
    {
        private readonly ConfigFile file;
        private readonly ConfigEntry<int> lastPort;
        private readonly ConfigEntry<bool> randomPort;
        private readonly ConfigEntry<bool> alAnkh;
        private readonly ConfigEntry<bool> emerald;
        private readonly ConfigEntry<bool> aestrin;
        private readonly ConfigEntry<bool> fireFishLagoon;
        private readonly ConfigEntry<bool> smallIslands;
        private readonly ConfigEntry<int> lastBoat;
        private readonly ConfigEntry<bool> randomBoat;
        private readonly ConfigEntry<bool> smallBoats;
        private readonly ConfigEntry<bool> mediumBoats;
        private readonly ConfigEntry<bool> largeBoats;
        private readonly ConfigEntry<string> excludedRandomPorts;
        private readonly ConfigEntry<string> excludedRandomBoats;
        private readonly ConfigEntry<string> boatSizeOverrides;
        private readonly ConfigEntry<string> portPoolOverrides;

        internal SelectionSettings Settings { get; }
        internal IndividualExclusions IndividualExclusions { get; }
        internal SelectionOverrides Overrides { get; private set; }

        internal SelectionConfig(ConfigFile file)
        {
            this.file = file;
            lastPort = file.Bind("Selection", "LastSelectedPort", 2,
                "Last manually selected port index. Default: Neverdin.");
            randomPort = file.Bind("Selection", "RandomPort", false,
                "Choose a random port from the checked geographic pools.");
            alAnkh = file.Bind("Random Port Pools", "AlAnkh", true, "Include Al'Ankh ports.");
            emerald = file.Bind("Random Port Pools", "EmeraldArchipelago", true,
                "Include Emerald Archipelago ports.");
            aestrin = file.Bind("Random Port Pools", "Aestrin", true, "Include Aestrin ports.");
            fireFishLagoon = file.Bind("Random Port Pools", "FireFishLagoon", false,
                "Include Fire Fish Lagoon ports.");
            smallIslands = file.Bind("Random Port Pools", "SmallIslands", false,
                "Include geographically separate small islands.");
            lastBoat = file.Bind("Selection", "LastSelectedBoat", 10,
                "Last manually selected boat scene index. Default: small dhow.");
            randomBoat = file.Bind("Selection", "RandomBoat", false,
                "Choose a random boat from the checked size pools.");
            smallBoats = file.Bind("Random Boat Sizes", "Small", true, "Include small boats.");
            mediumBoats = file.Bind("Random Boat Sizes", "Medium", true, "Include medium boats.");
            largeBoats = file.Bind("Random Boat Sizes", "Large", true, "Include large boats.");
            excludedRandomPorts = file.Bind("Individual Random Exclusions", "Ports", "",
                "Exact runtime names of ports excluded from random starts, encoded and separated by semicolons. Edit with the new-game menu.");
            excludedRandomBoats = file.Bind("Individual Random Exclusions", "Boats", "",
                "Exact runtime names of boats excluded from random starts, encoded and separated by semicolons. Edit with the new-game menu.");
            boatSizeOverrides = file.Bind("Compatibility", "BoatSizeOverrides", "",
                "Semicolon-separated sceneIndex|exactGameObjectName|Small/Medium/Large records.");
            portPoolOverrides = file.Bind("Compatibility", "PortPoolOverrides", "",
                "Semicolon-separated portIndex|exactGameObjectName|AlAnkh/Emerald/Aestrin/FireFishLagoon/SmallIslands records.");

            Settings = new SelectionSettings
            {
                PortIndex = lastPort.Value,
                RandomPort = randomPort.Value,
                BoatSceneIndex = lastBoat.Value,
                RandomBoat = randomBoat.Value
            };
            IndividualExclusions = NewBeginnings.IndividualExclusions.Load(
                excludedRandomPorts.Value, excludedRandomBoats.Value);
            Settings.PortPools.Clear();
            if (alAnkh.Value) Settings.PortPools.Add(PortPool.AlAnkh);
            if (emerald.Value) Settings.PortPools.Add(PortPool.Emerald);
            if (aestrin.Value) Settings.PortPools.Add(PortPool.Aestrin);
            if (fireFishLagoon.Value) Settings.PortPools.Add(PortPool.FireFishLagoon);
            if (smallIslands.Value) Settings.PortPools.Add(PortPool.SmallIslands);
            Settings.BoatSizes.Clear();
            if (smallBoats.Value) Settings.BoatSizes.Add(BoatSize.Small);
            if (mediumBoats.Value) Settings.BoatSizes.Add(BoatSize.Medium);
            if (largeBoats.Value) Settings.BoatSizes.Add(BoatSize.Large);
            if (Settings.PortPools.Count == 0)
            {
                Settings.PortPools.Add(PortPool.AlAnkh);
                Plugin.Instance.Warn("Every random port pool was disabled; Al'Ankh was restored.");
            }
            if (Settings.BoatSizes.Count == 0)
            {
                Settings.BoatSizes.Add(BoatSize.Small);
                Plugin.Instance.Warn("Every random boat size was disabled; Small was restored.");
            }
            RefreshOverrides();
            Save();
        }

        internal void RefreshOverrides()
        {
            Overrides = SelectionOverrides.Parse(boatSizeOverrides.Value,
                portPoolOverrides.Value);
            foreach (var error in Overrides.Errors)
                Plugin.Instance.Warn(error);
        }

        internal void Save()
        {
            lastPort.Value = Settings.PortIndex;
            randomPort.Value = Settings.RandomPort;
            alAnkh.Value = Settings.PortPools.Contains(PortPool.AlAnkh);
            emerald.Value = Settings.PortPools.Contains(PortPool.Emerald);
            aestrin.Value = Settings.PortPools.Contains(PortPool.Aestrin);
            fireFishLagoon.Value = Settings.PortPools.Contains(PortPool.FireFishLagoon);
            smallIslands.Value = Settings.PortPools.Contains(PortPool.SmallIslands);
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
