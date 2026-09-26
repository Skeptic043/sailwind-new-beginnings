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

        internal void NormalizeEquipment()
        {
            var normalized = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in EquipmentQuantities)
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
            EquipmentQuantities.Clear();
            foreach (var pair in normalized) EquipmentQuantities.Add(pair.Key, pair.Value);
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
        }

        internal bool TryValidate(out string reason)
        {
            reason = null;
            if (float.IsNaN(CurrencyMultiplier) || float.IsInfinity(CurrencyMultiplier) ||
                CurrencyMultiplier < 0f || CurrencyMultiplier > 100f)
                reason = "Starting currency multiplier must be between 0 and 100.";
            else if (StartingReputation < 0 || StartingReputation > 10)
                reason = "Starting faction reputation must be between 0 and 10.";
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
            if (reason == null)
                try { Copy().NormalizeEquipment(); }
                catch (InvalidOperationException exception) { reason = exception.Message; }
            return reason == null;
        }
    }
}
