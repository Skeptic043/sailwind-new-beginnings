using System.Linq;
using UnityEngine;

namespace NewBeginnings
{
    // The native release marker is the floating, player-controlled berth.
    // AdmitShip also opens editing, cameras and orders, so startup never calls it.
    internal static class ShipyardStart
    {
        internal static bool Available(PortChoice destination)
        {
            var yard = destination?.Yard;
            return StartDestinations.Loaded(yard) && yard.enabled &&
                StartDestinations.Loaded(yard.shipReleasePosition);
        }

        internal static bool TryPlan(PortChoice destination, out StartBerthPlan plan)
        {
            plan = null;
            if (!Available(destination)) return false;
            var yard = destination.Yard;
            var marker = yard.shipReleasePosition;
            var frame = destination.DockRoot;
            // Native and provider yards can keep their documents in another
            // hierarchy. Their owner reference identifies the shore counter.
            var documents = Resources.FindObjectsOfTypeAll<ShipyardDocuments>()
                .FirstOrDefault(d => StartDestinations.Loaded(d) && d.shipyard == yard);
            var seed = documents != null ? documents.transform.position : yard.transform.position;
            var verified = Plugin.Instance.TryFindStartShore(destination, seed, marker.position,
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
