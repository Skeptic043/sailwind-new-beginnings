using System;
using System.Collections.Generic;

namespace NewBeginnings
{
    internal sealed class StartingOptionsSettings
    {
        internal float CurrencyMultiplier { get; set; } = 1f;
        internal int?[] CurrencyOverrides { get; } = new int?[4];
        internal int StartingReputation { get; set; }
        internal int[] FactionReputation { get; } = new int[3];
        internal bool StandardSupplies { get; set; } = true;
        internal Dictionary<string, int> EquipmentQuantities { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        internal long EquipmentTotal => System.Linq.Enumerable.Sum(EquipmentQuantities.Values, value => (long)value);
        // Random loadout replaces the chosen equipment and standard supplies
        // with a roll from the catalog, made once when the game starts.
        internal bool RandomLoadout { get; set; }
        internal int RandomBudget { get; set; } = DefaultRandomBudget;
        internal bool RandomModItems { get; set; }
        internal bool RandomMultiples { get; set; } = true;
        internal HashSet<string> RandomExclusions { get; } = new HashSet<string>(StringComparer.Ordinal);
        internal Dictionary<string, int> RandomGuaranteed { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        internal long RandomGuaranteedTotal => System.Linq.Enumerable.Sum(RandomGuaranteed.Values, value => (long)value);
        // Optional mod integrations (ignored when the corresponding mod is absent).
        // StartingSeason: 0 = Default, 1 = Winter, 2 = Spring, 3 = Summer, 4 = Autumn.
        internal int StartingSeason { get; set; }
        // StartingSailDamage: 0, 25, 50 or 75 percent (never 100%).
        internal int StartingSailDamage { get; set; }
        internal bool StartWithShepherd { get; set; }
        internal bool StartWithCat { get; set; }
        internal bool StartWithBooby { get; set; }
        internal const int DefaultRandomBudget = 1000, MaximumRandomBudget = 100000;
        internal static readonly int[] ValidSailDamageLevels = { 0, 25, 50, 75 };
        internal static readonly string[] SeasonNames = { "Default", "Winter", "Spring", "Summer", "Autumn" };

        internal void NormalizeEquipment()
        {
            NormalizeQuantities(EquipmentQuantities);
            NormalizeQuantities(RandomGuaranteed);
        }

        private static void NormalizeQuantities(Dictionary<string, int> quantities)
        {
            var normalized = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in quantities)
            {
                if (pair.Value == 0) continue;
                var keys = new HashSet<string>(StringComparer.Ordinal) { pair.Key };
                NewBeginnings.AdditionalEquipment.NormalizeSelection(keys);
                foreach (var key in keys)
                {
                    normalized.TryGetValue(key, out var prior);
                    var quantity = (long)prior + pair.Value;
                    if (quantity < 0 || quantity > int.MaxValue)
                        throw new InvalidOperationException("Combined equipment quantity is outside the whole-number range 0 to " + int.MaxValue + ": " + key);
                    normalized[key] = (int)quantity;
                }
            }
            quantities.Clear();
            foreach (var pair in normalized) quantities.Add(pair.Key, pair.Value);
        }

        internal StartingOptionsSettings Copy()
        {
            var copy = new StartingOptionsSettings();
            copy.ReplaceWith(this);
            return copy;
        }

        internal void ReplaceWith(StartingOptionsSettings other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (ReferenceEquals(this, other)) return;
            CurrencyMultiplier = other.CurrencyMultiplier;
            StartingReputation = other.StartingReputation;
            Array.Copy(other.CurrencyOverrides, CurrencyOverrides, CurrencyOverrides.Length);
            Array.Copy(other.FactionReputation, FactionReputation, FactionReputation.Length);
            StandardSupplies = other.StandardSupplies;
            EquipmentQuantities.Clear();
            foreach (var pair in other.EquipmentQuantities) EquipmentQuantities.Add(pair.Key, pair.Value);
            RandomLoadout = other.RandomLoadout;
            RandomBudget = other.RandomBudget;
            RandomModItems = other.RandomModItems;
            RandomMultiples = other.RandomMultiples;
            RandomExclusions.Clear();
            RandomExclusions.UnionWith(other.RandomExclusions);
            RandomGuaranteed.Clear();
            foreach (var pair in other.RandomGuaranteed) RandomGuaranteed.Add(pair.Key, pair.Value);
            StartingSeason = other.StartingSeason;
            StartingSailDamage = other.StartingSailDamage;
            StartWithShepherd = other.StartWithShepherd;
            StartWithCat = other.StartWithCat;
            StartWithBooby = other.StartWithBooby;
        }

        internal bool TryValidate(out string reason)
        {
            reason = null;
            if (float.IsNaN(CurrencyMultiplier) || float.IsInfinity(CurrencyMultiplier) ||
                CurrencyMultiplier < 0f || CurrencyMultiplier > 100f)
                reason = "Starting currency multiplier must be between 0 and 100.";
            else if (StartingReputation < 0 || StartingReputation > 10)
                reason = "Starting faction reputation must be between 0 and 10.";
            else if (RandomBudget < 0 || RandomBudget > MaximumRandomBudget)
                reason = "Random loadout budget must be between 0 and " + MaximumRandomBudget + ".";
            else if (StartingSeason < 0 || StartingSeason >= SeasonNames.Length)
                reason = "Starting season must be Default, Winter, Spring, Summer or Autumn.";
            else if (Array.IndexOf(ValidSailDamageLevels, StartingSailDamage) < 0)
                reason = "Starting sail damage must be 0, 25, 50 or 75.";
            foreach (var amount in CurrencyOverrides)
                if (amount.HasValue && amount.Value < 0)
                    reason = "Currency overrides must be whole numbers from 0 to 2147483647, or blank.";
            foreach (var level in FactionReputation)
                if (level < 0 || level > 10)
                    reason = "Faction reputation overrides must be between 0 and 10.";
            foreach (var pair in EquipmentQuantities)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value < 0)
                    reason = "Equipment quantities must be whole numbers from 0 to " + int.MaxValue + ". Zero removes the selection.";
            }
            foreach (var pair in RandomGuaranteed)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value < 0)
                    reason = "Guaranteed equipment quantities must be whole numbers from 0 to " + int.MaxValue + ". Zero removes the selection.";
            }
            if (reason == null)
                try { Copy().NormalizeEquipment(); }
                catch (InvalidOperationException exception) { reason = exception.Message; }
            return reason == null;
        }
    }
}
