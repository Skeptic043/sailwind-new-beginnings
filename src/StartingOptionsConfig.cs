using System;
using System.Collections.Generic;
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
        private readonly ConfigEntry<bool> standardSupplies;
        private readonly ConfigWarnings warnings = new ConfigWarnings();
        private static readonly string[] Keys = { "AlAnkh", "Emerald", "Aestrin", "Gold" };

        internal StartingOptionsSettings Settings { get; } = new StartingOptionsSettings();

        internal StartingOptionsConfig(ConfigFile file)
        {
            this.file = file;
            multiplier = file.Bind("Starting Options", "CurrencyMultiplier", 1f,
                "Multiply the resolved starting faction's currency after other startup adjustments. Range 0 to 100.");
            startingReputation = file.Bind("Starting Options", "StartingFactionReputation", 0,
                "Starting faction reputation level, 1 to 10. Zero leaves the normal reputation unchanged.");
            for (var i = 0; i < currencies.Length; i++)
                currencies[i] = file.Bind("Starting Currency Overrides", Keys[i], "",
                    "Final starting balance for this currency. Blank uses the multiplier for the starting faction's currency and leaves other currencies unchanged; zero is valid.");
            for (var i = 0; i < factions.Length; i++)
                factions[i] = file.Bind("Starting Reputation Overrides", Keys[i], 0,
                    "Faction reputation level, 1 to 10. Zero uses the starting faction slider here if applicable, otherwise leaves reputation unchanged.");
            equipment = file.Bind("Starting Options", "AdditionalEquipment", "",
                "Encoded equipment IDs with =quantity, separated by semicolons. Legacy IDs without a quantity mean one. Whole-number range 0 to 2147483647; zero removes a selection. Edit with the new-game menu.");
            standardSupplies = file.Bind("Starting Options", "StandardSupplies", true,
                "Include the normal regional starter supplies. Disable to start with only the equipment quantities chosen here.");
            Read();
        }

        // Startup and live reloads (Configuration Manager or a reread file) share
        // this read, so validation, fallbacks and warnings are identical.
        internal void Read()
        {
            warnings.Begin();
            for (var i = 0; i < currencies.Length; i++)
            {
                Settings.CurrencyOverrides[i] = null;
                if (string.IsNullOrWhiteSpace(currencies[i].Value)) continue;
                if (int.TryParse(currencies[i].Value, NumberStyles.None, CultureInfo.InvariantCulture,
                    out var amount) && amount >= 0) Settings.CurrencyOverrides[i] = amount;
                else warnings.Warn("Ignored invalid starting currency override: " + Keys[i] + ".");
            }
            for (var i = 0; i < factions.Length; i++)
                Settings.FactionReputation[i] = ValidLevel(factions[i].Value, Keys[i]);
            Settings.StandardSupplies = standardSupplies.Value;
            ReadEquipment(equipment.Value, Settings, warnings.Warn);
            Settings.CurrencyMultiplier = multiplier.Value;
            if (float.IsNaN(multiplier.Value) || float.IsInfinity(multiplier.Value) ||
                multiplier.Value < 0f || multiplier.Value > 100f)
            {
                Settings.CurrencyMultiplier = 1f;
                warnings.Warn("Invalid starting currency multiplier; using 1x.");
            }
            Settings.StartingReputation = ValidLevel(startingReputation.Value, "starting faction");
            warnings.End();
        }

        private int ValidLevel(int value, string name)
        {
            if (value >= 0 && value <= 10) return value;
            warnings.Warn("Invalid reputation level for " + name + "; using default.");
            return 0;
        }

        internal static void ReadEquipment(string value, StartingOptionsSettings settings,
            Action<string> warn = null)
        {
            warn = warn ?? (message => Plugin.Instance?.Warn(message));
            settings.EquipmentQuantities.Clear();
            foreach (var token in (value ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = token.LastIndexOf('=');
                var encoded = separator < 0 ? token : token.Substring(0, separator);
                var count = 1;
                if (separator >= 0 && (!int.TryParse(token.Substring(separator + 1), NumberStyles.None,
                    CultureInfo.InvariantCulture, out count) || count < 0))
                {
                    warn("Ignored invalid equipment quantity.");
                    continue;
                }
                if (count == 0) continue;
                try
                {
                    // Uri.UnescapeDataString preserves broken percent escapes; reject them explicitly.
                    for (var index = 0; index < encoded.Length; index++)
                        if (encoded[index] == '%' && (index + 2 >= encoded.Length ||
                            !Uri.IsHexDigit(encoded[index + 1]) || !Uri.IsHexDigit(encoded[index + 2])))
                            throw new UriFormatException();
                        else if (encoded[index] == '%') index += 2;
                    var id = Uri.UnescapeDataString(encoded);
                    if (id.Length == 0) continue;
                    // Duplicate legacy checkbox IDs still mean one selected item.
                    settings.EquipmentQuantities[id] = Math.Max(count,
                        settings.EquipmentQuantities.TryGetValue(id, out var prior) ? prior : 0);
                }
                catch (UriFormatException) { warn("Ignored malformed additional equipment ID."); }
            }
            try { settings.NormalizeEquipment(); }
            catch (InvalidOperationException exception)
            {
                // Keep the original entries visible for correction, rather than clipping quantities.
                warn(exception.Message + ". Correct these equipment entries before starting.");
            }
        }

        internal static string WriteEquipment(StartingOptionsSettings settings) =>
            string.Join(";", settings.EquipmentQuantities.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + pair.Value.ToString(CultureInfo.InvariantCulture)).ToArray());

        internal void Save()
        {
            if (!Settings.TryValidate(out var reason)) throw new InvalidOperationException(reason);
            Settings.NormalizeEquipment();
            if (!Settings.TryValidate(out reason)) throw new InvalidOperationException(reason);
            multiplier.Value = Settings.CurrencyMultiplier;
            startingReputation.Value = Settings.StartingReputation;
            for (var i = 0; i < currencies.Length; i++)
                currencies[i].Value = Settings.CurrencyOverrides[i]?.ToString(CultureInfo.InvariantCulture) ?? "";
            for (var i = 0; i < factions.Length; i++) factions[i].Value = Settings.FactionReputation[i];
            equipment.Value = WriteEquipment(Settings);
            standardSupplies.Value = Settings.StandardSupplies;
            file.Save();
        }
    }

    // Startup reports every configuration problem. A later reload repeats only
    // problems absent from the previous read, so one bad value is not logged
    // again for each unrelated setting change.
    internal sealed class ConfigWarnings
    {
        private HashSet<string> previous = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> current = new HashSet<string>(StringComparer.Ordinal);

        internal void Begin() => current = new HashSet<string>(StringComparer.Ordinal);

        internal void Warn(string message)
        {
            current.Add(message);
            if (!previous.Contains(message)) Plugin.Instance?.Warn(message);
        }

        internal void End() => previous = current;
    }
}
