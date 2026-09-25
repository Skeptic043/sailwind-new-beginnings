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
        internal HashSet<string> AdditionalEquipment { get; } = new HashSet<string>(StringComparer.Ordinal);

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
            AdditionalEquipment.Clear();
            AdditionalEquipment.UnionWith(other.AdditionalEquipment);
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
            return reason == null;
        }
    }
}
