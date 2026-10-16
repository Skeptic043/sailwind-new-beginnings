using System.Linq;
using UnityEngine;

namespace NewBeginnings
{
    // The native release marker is the floating, player-controlled berth.
    // AdmitShip also opens editing, cameras and orders, so startup never calls it.
    internal static class ShipyardStart
    {
        // These native capabilities exist before their additive scenery loads.
        // Mod/API locations continue to advertise their actual discovered yard.
        internal static bool HasNativeYard(PortChoice destination) => KnownSites.NativeYardName(destination) != null;

        // Both yards belong to Fort Aestrin. Prefer the loaded drydock, while
        // retaining the normal yard when the optional drydock is absent.
        internal static Shipyard PreferredYard(PortChoice destination, Shipyard[] yards)
        {
            if (!HasNativeYard(destination)) return destination?.Yard;
            var local = yards.Where(yard => Usable(yard) &&
                StartDestinations.BelongsToIsland(yard, destination.Island)).ToArray();
            var drydock = KnownSites.IsFortAestrin(destination) ? local.FirstOrDefault(KnownSites.IsFortAestrinDrydock) : null;
            return drydock ?? local.FirstOrDefault(yard => yard.name == KnownSites.NativeYardName(destination)) ?? destination.Yard;
        }

        private static Shipyard ResolveYard(PortChoice destination)
        {
            if (destination?.HasAuthoredYardScope == true) return destination.Yard;
            return HasNativeYard(destination)
                ? PreferredYard(destination, Resources.FindObjectsOfTypeAll<Shipyard>()) : destination?.Yard;
        }

        private static bool Usable(Shipyard yard) => StartDestinations.Loaded(yard) && yard.enabled &&
            StartDestinations.Loaded(yard.shipReleasePosition);

        // Menu sampling must not depend on the island scenery already being
        // loaded. Resolve the actual yard when preparing the selected start.
        internal static bool Available(PortChoice destination) => destination?.HasAuthoredYardScope == true
            ? Usable(destination.Yard)
            : HasNativeYard(destination) || Usable(destination?.Yard);

        internal static bool TryPlan(PortChoice destination, out StartBerthPlan plan,
            Rigidbody body = null, BoatMooringRopes ropes = null)
        {
            plan = null;
            var yard = ResolveYard(destination);
            if (!Usable(yard)) return false;
            var useNativeYardPolicy = !destination.HasAuthoredYardScope && HasNativeYard(destination);
            if (useNativeYardPolicy && (destination.Index == KnownSites.GoldRockPort ||
                destination.Index == KnownSites.DragonCliffsPort || destination.Index == KnownSites.KiciaBayPort))
            {
                if (StartBerthPlanner.TryNativeShipyard(destination, yard, body, ropes, out plan, out var berthReason))
                {
                    StartBerthPlanner.PrepareShipyardMoorings(plan, body, ropes);
                    return true;
                }
                Plugin.Instance.Warn("The shipyard's dock could not be measured (" + berthReason +
                    "). The start uses its release marker and shore reference.");
            }
            var marker = yard.shipReleasePosition;
            var frame = destination.DockRoot;
            // Native and provider yards can keep their documents in another
            // hierarchy. Their owner reference identifies the shore counter.
            var documents = Resources.FindObjectsOfTypeAll<ShipyardDocuments>()
                .FirstOrDefault(d => StartDestinations.Loaded(d) && d.shipyard == yard);
            var seed = destination.Index == KnownSites.KiciaBayPort && useNativeYardPolicy && StartDestinations.Loaded(destination.Recovery)
                ? destination.Recovery.transform.position
                : documents != null ? documents.transform.position : yard.transform.position;
            if (KnownSites.IsFortJuniper(destination) &&
                StartBerthPlanner.TryFortShipyardShore(destination, marker.position, out var dockShore)) seed = dockShore;
            var verified = ShoreProbe.TryFindStartShore(destination, seed, seed,
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
                LocalShorePosition = frame.InverseTransformPoint(shore) };
            StartBerthPlanner.PrepareShipyardMoorings(plan, body, ropes);
            return true;
        }
    }
}
