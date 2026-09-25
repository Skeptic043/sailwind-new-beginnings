using System;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;

namespace NewBeginnings
{
    internal sealed class StartingOptionsConfig
    {
        private readonly ConfigFile file;
        private readonly ConfigEntry<float> multiplier;
        private readonly ConfigEntry<int> startingReputation;
        private readonly ConfigEntry<string>[] currencies = new ConfigEntry<string>[4];
        private readonly ConfigEntry<int>[] factions = new ConfigEntry<int>[3];
        private readonly ConfigEntry<string> equipment;

        internal StartingOptionsSettings Settings { get; } = new StartingOptionsSettings();

        internal StartingOptionsConfig(ConfigFile file)
        {
            this.file = file;
            multiplier = file.Bind("Starting Options", "CurrencyMultiplier", 1f,
                "Multiply the resolved starting faction's currency after other startup adjustments. Range 0 to 100.");
            startingReputation = file.Bind("Starting Options", "StartingFactionReputation", 0,
                "Starting faction reputation level, 1 to 10. Zero leaves the normal reputation unchanged.");
            var keys = new[] { "AlAnkh", "Emerald", "Aestrin", "Gold" };
            for (var i = 0; i < currencies.Length; i++)
            {
                currencies[i] = file.Bind("Starting Currency Overrides", keys[i], "",
                    "Final starting balance for this currency. Blank uses the multiplier for the starting faction's currency and leaves other currencies unchanged; zero is valid.");
                if (string.IsNullOrWhiteSpace(currencies[i].Value)) continue;
                if (int.TryParse(currencies[i].Value, NumberStyles.None, CultureInfo.InvariantCulture,
                    out var amount) && amount >= 0) Settings.CurrencyOverrides[i] = amount;
                else Plugin.Instance?.Warn("Ignored invalid starting currency override: " + keys[i] + ".");
            }
            for (var i = 0; i < factions.Length; i++)
            {
                factions[i] = file.Bind("Starting Reputation Overrides", keys[i], 0,
                    "Faction reputation level, 1 to 10. Zero uses the starting faction slider here if applicable, otherwise leaves reputation unchanged.");
                Settings.FactionReputation[i] = ValidLevel(factions[i].Value, keys[i]);
            }
            equipment = file.Bind("Starting Options", "AdditionalEquipment", "",
                "Extra equipment IDs, encoded and separated by semicolons. Each selected entry adds one item. Edit with the new-game menu.");
            foreach (var id in equipment.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try { Settings.AdditionalEquipment.Add(Uri.UnescapeDataString(id)); }
                catch (UriFormatException) { Plugin.Instance?.Warn("Ignored malformed additional equipment ID."); }
            }
            AdditionalEquipment.NormalizeSelection(Settings.AdditionalEquipment);
            Settings.CurrencyMultiplier = multiplier.Value;
            if (float.IsNaN(multiplier.Value) || float.IsInfinity(multiplier.Value) ||
                multiplier.Value < 0f || multiplier.Value > 100f)
            {
                Settings.CurrencyMultiplier = 1f;
                Plugin.Instance?.Warn("Invalid starting currency multiplier; using 1x.");
            }
            Settings.StartingReputation = ValidLevel(startingReputation.Value, "starting faction");
        }

        private static int ValidLevel(int value, string name)
        {
            if (value >= 0 && value <= 10) return value;
            Plugin.Instance?.Warn("Invalid reputation level for " + name + "; using default.");
            return 0;
        }

        internal void Save()
        {
            if (!Settings.TryValidate(out var reason)) throw new InvalidOperationException(reason);
            AdditionalEquipment.NormalizeSelection(Settings.AdditionalEquipment);
            multiplier.Value = Settings.CurrencyMultiplier;
            startingReputation.Value = Settings.StartingReputation;
            for (var i = 0; i < currencies.Length; i++)
                currencies[i].Value = Settings.CurrencyOverrides[i]?.ToString(CultureInfo.InvariantCulture) ?? "";
            for (var i = 0; i < factions.Length; i++) factions[i].Value = Settings.FactionReputation[i];
            equipment.Value = string.Join(";", Settings.AdditionalEquipment.OrderBy(id => id, StringComparer.Ordinal)
                .Select(Uri.EscapeDataString).ToArray());
            file.Save();
        }
    }
}
