using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NewBeginnings
{
    // Siren Song's recovery cleats are also the native Cog's authored berth.
    // Use the next straight dock section without moving either boat's world
    // markers. The farther two cleats turn inland and are not a straight berth.
    internal static class SirenSongBerth
    {
        private static readonly string[] CleatNames =
        {
            "dock_mooring M", "dock_mooring M (1)", "dock_mooring M (2)",
            "dock_mooring M (3)", "dock_mooring M (5)", "dock_mooring M (4)"
        };

        internal sealed class Plan
        {
            internal Vector3 Position;
            internal Vector3 ShorePosition;
            internal GPButtonDockMooring Front, Back;
        }

        internal static bool IsTarget(Port port) => port != null && port.portIndex == 18 &&
            string.Equals(port.gameObject.name, "port M 18 Siren Song", StringComparison.Ordinal);

        internal static bool TryGetMoorings(Port port, RecoveryPort recovery, Rigidbody body,
            out GPButtonDockMooring front, out GPButtonDockMooring back, out string reason)
        {
            front = back = null;
            reason = null;
            if (!IsTarget(port)) { reason = "Not the inspected Siren Song port."; return false; }
            if (!TryReadCleats(port, recovery, out var cleats, out reason)) return false;
            front = cleats[3]; back = cleats[2];
            foreach (var dock in new[] { front, back })
            {
                if (dock.spring == null || dock.spring.connectedBody != null && dock.spring.connectedBody != body)
                {
                    reason = "Siren Song's alternate dock spring is unavailable or occupied.";
                    return false;
                }
                foreach (var ropes in SceneBoats())
                {
                    if (ropes.GetComponent<Rigidbody>() == body) continue;
                    // Native MoorClosestRope can place visual lines at authored
                    // cleats without connecting the spring. Reserve those too.
                    if (ropes.mooringFront == dock.transform || ropes.mooringBack == dock.transform)
                    {
                        reason = "Siren Song's alternate cleat belongs to another boat's authored berth.";
                        return false;
                    }
                }
            }
            return true;
        }

        internal static bool TryCreate(ResolvedStart selected, out Plan plan)
        {
            plan = null;
            if (!IsTarget(selected.Port)) return false;
            if (!TryGetMoorings(selected.Port, selected.Recovery, selected.Body,
                out var front, out var back, out var reason) ||
                !TryReadCleats(selected.Port, selected.Recovery, out var cleats, out reason))
                throw new InvalidOperationException(reason);
            var marker = selected.Recovery.boatPos;
            var own = ReadHull(selected.Body, marker.position, marker.rotation);
            var obstacles = new List<BerthClearance.Footprint>();
            foreach (var ropes in SceneBoats())
            {
                var body = ropes.GetComponent<Rigidbody>();
                if (body == null || body == selected.Body) continue;
                var root = body.transform.position;
                var dx = root.x - marker.position.x;
                var dz = root.z - marker.position.z;
                // This includes inactive/hidden scene boats, and ignores their
                // temporary depth below the ocean. Prefab assets are excluded.
                if (dx * dx + dz * dz > 150f * 150f) continue;
                obstacles.Add(ReadHull(body, root, body.transform.rotation));
            }
            var points = cleats.Select(c => new Vector2(c.transform.position.x, c.transform.position.z)).ToArray();
            if (!TryPosition(own, obstacles.ToArray(), points,
                new Vector2(marker.position.x, marker.position.z),
                out var delta, out var outward, out var extraOutward, out var gap))
                throw new InvalidOperationException("Siren Song's alternate dock has no verified hull space within the bounded berth; existing boats and cleats were left unchanged.");
            var position = marker.position + new Vector3(delta.x, 0f, delta.y);
            // Place player/supplies inside the same straight dock section. The
            // actual standing height is still established by the live resolver.
            var midpoint = (points[2] + points[3]) * 0.5f;
            var shore = midpoint - outward * 1.5f;
            plan = new Plan
            {
                Position = position,
                ShorePosition = new Vector3(shore.x, selected.Recovery.transform.position.y, shore.y),
                Front = front, Back = back
            };
            Plugin.Instance?.DebugLog($"Siren Song alternate berth: {front.name} / {back.name}; outward adjustment {extraOutward:F2}m beyond hull clearance; nearest boat gap {gap:F2}m; native recovery berth left unchanged.");
            return true;
        }

        private static IEnumerable<BoatMooringRopes> SceneBoats() =>
            Resources.FindObjectsOfTypeAll<BoatMooringRopes>().Where(ropes => ropes != null &&
                ropes.gameObject.scene.IsValid() && ropes.gameObject.scene.isLoaded);

        private static bool TryReadCleats(Port port, RecoveryPort recovery,
            out GPButtonDockMooring[] cleats, out string reason)
        {
            cleats = null;
            reason = "Siren Song's inspected six-cleat dock layout is unavailable or changed.";
            var island = port.transform.parent;
            if (island == null || island.name != "island 21 M (Siren Song)" ||
                recovery == null || recovery.parentPort != port || recovery.boatPos == null) return false;
            var candidates = island.GetComponentsInChildren<GPButtonDockMooring>(true)
                .Where(dock => dock != null && dock.transform.parent == island).ToArray();
            var result = new List<GPButtonDockMooring>();
            foreach (var name in CleatNames)
            {
                var matches = candidates.Where(dock => dock.name == name).ToArray();
                if (matches.Length != 1 || matches[0].spring == null) return false;
                result.Add(matches[0]);
            }
            if (recovery.mooringFront != result[1].transform || recovery.mooringBack != result[0].transform)
                return false;
            cleats = result.ToArray();
            reason = null;
            return true;
        }

        private static BerthClearance.Footprint ReadHull(Rigidbody body, Vector3 position, Quaternion rotation)
        {
            // A hidden boat can have its collider disabled. Its authored capsule
            // still occupies the berth when the island and physics become live.
            var hulls = body.GetComponents<CapsuleCollider>().Where(hull => hull != null &&
                !hull.isTrigger && hull.attachedRigidbody == body).ToArray();
            if (hulls.Length != 1)
                throw new InvalidOperationException("Siren Song berth could not measure one root hull capsule on " + body.name + ".");
            var hull = hulls[0];
            return BerthClearance.MakeFootprint(position, rotation, body.transform.lossyScale,
                hull.center, hull.radius, hull.height, hull.direction);
        }

        // Pure XZ geometry, independent of hidden-island depth and world origin.
        internal static bool TryPosition(BerthClearance.Footprint own, BerthClearance.Footprint[] others,
            Vector2[] cleats, Vector2 originalPosition, out Vector2 delta, out Vector2 outward,
            out float extraOutward, out float gap)
        {
            delta = outward = Vector2.zero; extraOutward = 0f; gap = float.NegativeInfinity;
            if (cleats == null || cleats.Length != 6 || cleats.Any(p => !Finite(p)) || !Finite(originalPosition) ||
                !Finite(own.A) || !Finite(own.B) || float.IsNaN(own.Radius) ||
                float.IsInfinity(own.Radius) || own.Radius <= 0f) return false;
            var along = cleats[3] - cleats[2];
            var span = along.magnitude;
            if (span < 12f || span > 22f) return false;
            along /= span;
            outward = new Vector2(-along.y, along.x);
            var originalMidpoint = (cleats[0] + cleats[1]) * 0.5f;
            if (Vector2.Dot(outward, originalPosition - originalMidpoint) < 0f) outward = -outward;
            if (Vector2.Dot(outward, originalPosition - originalMidpoint) < 1f) return false;
            // Verify all six still form the inspected ordered dock, with the
            // first four straight and the final two bending toward the water.
            for (var index = 1; index < cleats.Length; ++index)
            {
                var step = cleats[index] - cleats[index - 1];
                if (Vector2.Dot(step, along) < 7f || step.magnitude > 22f) return false;
                if (index < 4 && Math.Abs(Vector2.Dot(step, outward)) > 1f) return false;
            }
            var center = (own.A + own.B) * 0.5f;
            var midpoint = (cleats[2] + cleats[3]) * 0.5f;
            // Cleats sit slightly inside the actual dock edge. Two metres from
            // the measured cleat corridor leaves room for that inset and a gap.
            var baseline = midpoint + outward * (own.Radius + 2f) - center;
            if (!Finite(baseline)) return false;
            for (var step = 0; step <= 48; ++step)
            {
                extraOutward = step * 0.25f;
                delta = baseline + outward * extraOutward;
                if (!Finite(delta)) return false;
                gap = BerthClearance.MinimumGap(own, others, delta, out _);
                if (gap < 1f) continue;
                var clear = true;
                for (var index = 1; index < cleats.Length; ++index)
                    if (BerthClearance.SegmentDistance(own.A + delta, own.B + delta,
                        cleats[index - 1], cleats[index]) - own.Radius < 2f)
                    { clear = false; break; }
                if (clear) return true;
            }
            return false;
        }

        private static bool Finite(Vector2 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    }
}
