using System;
using System.Collections.Generic;

namespace NewBeginnings
{
    /// <summary>Friendly display names supplied by boat authors for live purchasable boats.</summary>
    public static class BoatNames
    {
        private sealed class Registration
        {
            internal PurchasableBoat Boat;
            internal string DisplayName;
        }

        private static readonly List<Registration> Registrations = new List<Registration>();

        /// <summary>Registers or replaces the friendly display name for this live boat.</summary>
        public static bool Register(PurchasableBoat boat, string displayName)
        {
            if (boat == null || string.IsNullOrWhiteSpace(displayName)) return false;
            Prune();
            var name = displayName.Trim();
            var existing = Registrations.Find(item => ReferenceEquals(item.Boat, boat));
            if (existing != null)
                existing.DisplayName = name;
            else
                Registrations.Add(new Registration { Boat = boat, DisplayName = name });
            return true;
        }

        internal static string DisplayName(PurchasableBoat boat, string fallback)
        {
            Prune();
            var registration = boat == null ? null :
                Registrations.Find(item => ReferenceEquals(item.Boat, boat));
            return registration != null ? registration.DisplayName : fallback;
        }

        private static void Prune() => Registrations.RemoveAll(item => item.Boat == null);
    }
}
