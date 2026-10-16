using System;
using UnityEngine;

namespace NewBeginnings
{
    // Secret destinations, each enabled by its own spoiler setting.
    internal enum HiddenSite { None, Pirate, Saffron }

    // The specific game locations and boats this mod treats specially. Each
    // is matched by its scene index and complete object name, so a mod object
    // reusing only one of them is not mistaken for it. Placement for each
    // site stays with its owner; only identity matching lives here.
    internal static class KnownSites
    {
        // Native ports whose authored shipyard New Beginnings can start at.
        internal const int GoldRockPort = 0, DragonCliffsPort = 9, FortAestrinPort = 15, KiciaBayPort = 22;

        internal const int LeopardIndex = 207;
        internal const string LeopardName = "BOAT LEOPARD (207)(Clone)";

        // Site keys are persisted in settings and exclusions.
        internal const string PirateHideoutKey = "vanilla:pirate-hideout";
        internal const string FortJuniperKey = "site:oldchronian|fort-juniper";

        internal static bool IsTestPort(Port port) => port != null && port.portIndex == 7 &&
            string.Equals(port.gameObject.name, "port 7 (test port)", StringComparison.OrdinalIgnoreCase);

        internal static bool IsLeopard(int sceneIndex, string name) =>
            sceneIndex == LeopardIndex && name == LeopardName;

        internal static bool IsSaffronPort(Port port) => port != null && port.portIndex == 33 &&
            port.name == "port A 33 (flower)";

        internal static bool IsPirateHideout(IslandHorizon island) => island.islandIndex == 32 &&
            island.name == "island 32 A (hideout)";

        internal static bool IsChronos(PortChoice site) => site.Index == 21 && site.SiteIdentity == null &&
            string.Equals(site.IdentityName, "port 21 M chronos", StringComparison.Ordinal);

        internal static bool IsSirenSong(PortChoice site) => site.SiteIdentity == null && site.Index == 18 &&
            site.Port.name == "port M 18 Siren Song" && site.Island != null && site.Island.islandIndex == 21 &&
            site.Island.name == "island 21 M (Siren Song)";

        internal static bool IsOasis(Port port) => port != null && port.portIndex == 6 &&
            string.Equals(port.gameObject.name, "port A/M 6 (Oasis)", StringComparison.Ordinal);

        internal static bool IsCrabBeach(Port port) => port != null && port.portIndex == 11 &&
            string.Equals(port.gameObject.name, "port E 11 crab beach", StringComparison.OrdinalIgnoreCase);

        internal static bool IsFortJuniper(PortChoice site) => site.SiteIdentity == FortJuniperKey;

        // Old Chronian's Fort Juniper yard, by its root dock; null for any other yard.
        internal static Transform FortJuniperRoot(Shipyard yard)
        {
            if (yard.name != "FORT shipyard") return null;
            for (var value = yard.transform; value != null; value = value.parent)
                if (value.name == "JUNIPER(Clone)") return value;
            return null;
        }

        // The native port's own shipyard object, or null for any other location.
        internal static string NativeYardName(PortChoice destination)
        {
            if (destination == null || destination.SiteIdentity != null) return null;
            switch (destination.Index)
            {
                case GoldRockPort: return destination.IdentityName == "port A0 (Gold Rock)" ? "shipyard Al'Ankh" : null;
                case DragonCliffsPort: return destination.IdentityName == "port E 9 (Dragon cliffs)" ? "shipyard E DragonCliffs" : null;
                case FortAestrinPort: return destination.IdentityName == "port M 15 Fort" ? "shipyard Aestrin" : null;
                case KiciaBayPort: return destination.IdentityName == "port L 22 Lagoon Bay" ? "shipyard Lagoon" : null;
                default: return null;
            }
        }

        internal static bool IsFortAestrin(PortChoice destination) =>
            destination != null && destination.Index == FortAestrinPort && NativeYardName(destination) != null;

        internal static bool IsFortAestrinDrydock(Shipyard yard) => yard.name == "shipyard Aestrin long" &&
            yard.transform.parent != null && yard.transform.parent.name == "drydock(Clone)";
    }
}
