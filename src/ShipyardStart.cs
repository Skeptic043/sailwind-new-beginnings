using System.Linq;
using UnityEngine;

namespace NewBeginnings
{
    // The native release marker is the floating, player-controlled berth.
    // AdmitShip also opens editing, cameras and orders, so startup never calls it.
    internal static class ShipyardStart
    {
        private static string NativeYardName(PortChoice destination)
        {
            if (destination == null || destination.SiteIdentity != null) return null;
            switch (destination.Index)
            {
                case 0: return destination.IdentityName == "port A0 (Gold Rock)" ? "shipyard Al'Ankh" : null;
                case 9: return destination.IdentityName == "port E 9 (Dragon cliffs)" ? "shipyard E DragonCliffs" : null;
                case 15: return destination.IdentityName == "port M 15 Fort" ? "shipyard Aestrin" : null;
                case 22: return destination.IdentityName == "port L 22 Lagoon Bay" ? "shipyard Lagoon" : null;
                default: return null;
            }
        }

        // These native capabilities exist before their additive scenery loads.
        // Mod/API locations continue to advertise their actual discovered yard.
        internal static bool HasNativeYard(PortChoice destination) => NativeYardName(destination) != null;

        private static bool IsFortAestrin(PortChoice destination) => destination != null &&
            destination.SiteIdentity == null && destination.Index == 15 && destination.IdentityName == "port M 15 Fort";

        // Both yards belong to Fort Aestrin. Prefer the loaded drydock, while
        // retaining the normal yard when the optional drydock is absent.
        internal static Shipyard PreferredYard(PortChoice destination, Shipyard[] yards)
        {
            if (!HasNativeYard(destination)) return destination?.Yard;
            var local = yards.Where(yard => Usable(yard) &&
                StartDestinations.BelongsToIsland(yard, destination.Island)).ToArray();
            var drydock = IsFortAestrin(destination) ? local.FirstOrDefault(yard => yard.name == "shipyard Aestrin long" &&
                yard.transform.parent != null && yard.transform.parent.name == "drydock(Clone)") : null;
            return drydock ?? local.FirstOrDefault(yard => yard.name == NativeYardName(destination)) ?? destination.Yard;
        }

        private static Shipyard ResolveYard(PortChoice destination)
        {
            return HasNativeYard(destination)
                ? PreferredYard(destination, Resources.FindObjectsOfTypeAll<Shipyard>()) : destination?.Yard;
        }

        private static bool Usable(Shipyard yard) => StartDestinations.Loaded(yard) && yard.enabled &&
            StartDestinations.Loaded(yard.shipReleasePosition);

        // Menu sampling must not depend on the island scenery already being
        // loaded. Resolve the actual yard when preparing the selected start.
        internal static bool Available(PortChoice destination) => HasNativeYard(destination) || Usable(destination?.Yard);

        internal static bool TryPlan(PortChoice destination, out StartBerthPlan plan,
            Rigidbody body = null, BoatMooringRopes ropes = null)
        {
            plan = null;
            var yard = ResolveYard(destination);
            if (!Usable(yard)) return false;
            if (HasNativeYard(destination) && (destination.Index == 0 || destination.Index == 9 || destination.Index == 22))
            {
                if (StartBerthPlanner.TryNativeShipyard(destination, yard, body, ropes, out plan, out var berthReason)) return true;
                Plugin.Instance.Warn("The shipyard's dock could not be measured (" + berthReason +
                    "). The start uses its release marker and shore reference.");
            }
            var marker = yard.shipReleasePosition;
            var frame = destination.DockRoot;
            // Native and provider yards can keep their documents in another
            // hierarchy. Their owner reference identifies the shore counter.
            var documents = Resources.FindObjectsOfTypeAll<ShipyardDocuments>()
                .FirstOrDefault(d => StartDestinations.Loaded(d) && d.shipyard == yard);
            var seed = documents != null ? documents.transform.position : yard.transform.position;
            var verified = Plugin.Instance.TryFindStartShore(destination, seed, seed,
                out var shore, out _, out var reason);
            if (!verified)
            {
                shore = seed;
                Plugin.Instance.Warn("Shipyard shore placement was not verified (" + reason +
                    "). The player and supplies use the shipyard's shore reference.");
            }
            plan = new StartBerthPlan { Destination = destination, Frame = frame,
                Mode = StartBerthMode.ShipyardHeld,
                LocalPosition = frame.InverseTransformPoint(marker.position),
                LocalRotation = Quaternion.Inverse(frame.rotation) * marker.rotation,
                LocalShorePosition = frame.InverseTransformPoint(shore),
                LandingVerified = verified, ClearanceVerified = false, DepartureVerified = false };
            return true;
        }
    }
}
