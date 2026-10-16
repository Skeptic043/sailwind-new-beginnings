using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NewBeginnings
{
    // Root hull capsules: the one selector and size math shared by boat
    // sizing, berth planning and resident clearance. Crab Beach's recovery
    // sphere also misses the neighboring starter hull's BoatCapsule layer, so
    // its start checks root hull capsules, without moving that boat.
    internal static class BerthClearance
    {
        private const float Margin = 1f;
        private const float MaximumSide = 12f;
        private const float MaximumRear = 4f;

        internal struct Footprint
        {
            internal Vector2 A, B;
            internal float Radius;
        }

        // One root capsule in body space: world-metre radius, axial length
        // (never shorter than the diameter) and half-segment, and its scaled,
        // unrotated center. Callers validate the inputs they accept.
        internal struct HullDimensions
        {
            internal Vector3 Center;
            internal float Radius, Length, HalfSegment;
        }

        internal static Vector3 AdjustCrabBeach(ResolvedStart selected, Vector3 nativePosition)
        {
            if (!KnownSites.IsCrabBeach(selected.Port)) return nativePosition;

            var marker = selected.Recovery.boatPos;
            var own = ReadHull(selected.Body, nativePosition, marker.rotation);
            var others = new List<Footprint>();
            var names = new List<string>();
            foreach (var ropes in Resources.FindObjectsOfTypeAll<BoatMooringRopes>())
            {
                if (ropes == null || !ropes.gameObject.scene.IsValid() ||
                    !ropes.gameObject.scene.isLoaded || !ropes.gameObject.activeInHierarchy) continue;
                var body = ropes.GetComponent<Rigidbody>();
                var saveable = ropes.GetComponent<SaveableObject>();
                if (body == null || body == selected.Body || saveable == null) continue;
                var hulls = RootHulls(body, false);
                if (hulls.Length != 1)
                {
                    // An unrelated mod boat elsewhere must not disable this start.
                    // Its actual physical bounds determine whether it could occupy
                    // any part of our bounded horizontal search area.
                    if (HasNearbyPhysicalBounds(body, own))
                        throw new InvalidOperationException("Crab Beach hull clearance could not measure nearby boat " + body.name + ".");
                    continue;
                }
                others.Add(ReadHull(body, body.transform.position, body.transform.rotation));
                names.Add(body.name);
            }

            var obstacles = others.ToArray();
            var baseline = MinimumGap(own, obstacles, Vector2.zero, out var blocker);
            if (baseline >= Margin) return nativePosition;
            var dockMidpoint = (selected.Recovery.mooringFront.position +
                selected.Recovery.mooringBack.position) * 0.5f;
            if (!TryDirections(marker.right, marker.forward, marker.position, dockMidpoint,
                out var outward, out var rear))
                throw new InvalidOperationException("Crab Beach hull clearance could not verify the direction away from the dock.");
            if (!TryOffset(own, obstacles, outward, rear, out var delta,
                out var side, out var back, out var gap))
                throw new InvalidOperationException("Crab Beach hull clearance found no space within 12m sideways and 4m rearward; neighboring boats were left unchanged.");
            var result = nativePosition + new Vector3(delta.x, 0f, delta.y);
            if (!Finite(result)) throw new InvalidOperationException("Crab Beach hull clearance produced an invalid position.");
            Plugin.Instance?.DebugLog($"Crab Beach hull clearance: moved {side:F2}m away from dock and {back:F2}m rearward; blocker {names[blocker]}, capsule gap {baseline:F2}m -> {gap:F2}m. Dock and terrain clearance still require an in-game check.");
            return result;
        }

        // Non-trigger capsules on the body's own GameObject. Distance hiding
        // disables authored hulls that still reserve space, so sizing, berth
        // planning and residents include them; Crab Beach reads only live
        // capsules attached to the body.
        internal static CapsuleCollider[] RootHulls(Rigidbody body, bool includeDisabled) =>
            body.GetComponents<CapsuleCollider>().Where(c => c != null && !c.isTrigger &&
                (includeDisabled || c.enabled && c.attachedRigidbody == body)).ToArray();

        private static Footprint ReadHull(Rigidbody body, Vector3 position, Quaternion rotation)
        {
            var hulls = RootHulls(body, false);
            if (hulls.Length != 1)
                throw new InvalidOperationException("Crab Beach hull clearance requires one measurable root capsule on " + body.name + ".");
            var hull = hulls[0];
            return MakeFootprint(position, rotation, body.transform.lossyScale,
                hull.center, hull.radius, hull.height, hull.direction);
        }

        private static bool HasNearbyPhysicalBounds(Rigidbody body, Footprint own)
        {
            var reach = own.Radius + Margin + MaximumSide + MaximumRear;
            var minX = Math.Min(own.A.x, own.B.x) - reach;
            var maxX = Math.Max(own.A.x, own.B.x) + reach;
            var minZ = Math.Min(own.A.y, own.B.y) - reach;
            var maxZ = Math.Max(own.A.y, own.B.y) + reach;
            foreach (var collider in body.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger || collider.attachedRigidbody != body) continue;
                var bounds = collider.bounds;
                if (!Finite(bounds.min) || !Finite(bounds.max))
                    throw new InvalidOperationException("Crab Beach hull clearance found invalid collider bounds on " + body.name + ".");
                if (bounds.max.x >= minX && bounds.min.x <= maxX &&
                    bounds.max.z >= minZ && bounds.min.z <= maxZ) return true;
            }
            return false;
        }

        // Pure transform/geometry functions also exercised outside the game.
        // World metres are intentional: the native recovery marker has scale 0.5.
        internal static Footprint MakeFootprint(Vector3 position, Quaternion rotation, Vector3 scale,
            Vector3 center, float radius, float height, int direction)
        {
            if (!Finite(position) || !Finite(scale) || !Finite(center) ||
                !Finite(rotation.x) || !Finite(rotation.y) || !Finite(rotation.z) || !Finite(rotation.w) ||
                !Finite(radius) || !Finite(height) || radius <= 0f || height <= 0f ||
                direction < 0 || direction > 2 || scale.x == 0f || scale.y == 0f || scale.z == 0f)
                throw new InvalidOperationException("Crab Beach hull capsule has invalid geometry.");
            var axis = direction == 0 ? Vector3.right : direction == 1 ? Vector3.up : Vector3.forward;
            var hull = Measure(scale, center, radius, height, direction);
            var worldCenter = position + rotation * hull.Center;
            var offset = rotation * axis * hull.HalfSegment;
            var a = worldCenter + offset;
            var b = worldCenter - offset;
            if (!Finite(a) || !Finite(b) || !Finite(hull.Radius) || hull.Radius <= 0f)
                throw new InvalidOperationException("Crab Beach hull capsule overflowed its geometry.");
            return new Footprint { A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z), Radius = hull.Radius };
        }

        internal static HullDimensions Measure(CapsuleCollider capsule, Vector3 scale) =>
            Measure(scale, capsule.center, capsule.radius, capsule.height, capsule.direction);

        // Radius scales by the larger cross-axis scale, length by the axial one.
        internal static HullDimensions Measure(Vector3 scale, Vector3 center, float radius,
            float height, int direction)
        {
            var radialScale = Math.Max(Math.Abs(scale[(direction + 1) % 3]), Math.Abs(scale[(direction + 2) % 3]));
            var worldRadius = radius * radialScale;
            var length = Math.Max(height * Math.Abs(scale[direction]), 2f * worldRadius);
            return new HullDimensions
            {
                Center = Vector3.Scale(center, scale), Radius = worldRadius, Length = length,
                HalfSegment = Math.Max(0f, length * 0.5f - worldRadius)
            };
        }

        internal static bool TryDirections(Vector3 right, Vector3 forward, Vector3 marker,
            Vector3 dock, out Vector2 outward, out Vector2 rear)
        {
            outward = new Vector2(-right.x, -right.z);
            rear = new Vector2(-forward.x, -forward.z);
            if (!Finite(right) || !Finite(forward) || !Finite(marker) || !Finite(dock) ||
                outward.sqrMagnitude < 0.5f || rear.sqrMagnitude < 0.5f) return false;
            outward.Normalize(); rear.Normalize();
            var toDock = new Vector2(dock.x - marker.x, dock.z - marker.z);
            return toDock.sqrMagnitude > 0.01f &&
                Vector2.Dot(outward, toDock.normalized) < -0.5f;
        }

        internal static bool TryOffset(Footprint own, Footprint[] others, Vector2 outward, Vector2 rear,
            out Vector2 delta, out float side, out float back, out float gap)
        {
            delta = Vector2.zero; side = back = 0f;
            gap = MinimumGap(own, others, delta, out _);
            if (gap >= Margin) return true;
            for (var step = 1; step <= 48; ++step)
            {
                side = step * 0.25f;
                back = Math.Min(side * 0.5f, MaximumRear);
                delta = outward * side + rear * back;
                gap = MinimumGap(own, others, delta, out _);
                if (gap >= Margin) return true;
            }
            return false;
        }

        internal static float MinimumGap(Footprint own, Footprint[] others, Vector2 delta, out int closest)
        {
            var gap = float.PositiveInfinity;
            closest = -1;
            for (var i = 0; i < others.Length; ++i)
            {
                var candidate = SegmentDistance(own.A + delta, own.B + delta, others[i].A, others[i].B)
                    - own.Radius - others[i].Radius;
                if (!Finite(candidate))
                    throw new InvalidOperationException("Crab Beach hull clearance could not measure a finite gap between boats.");
                if (candidate < gap) { gap = candidate; closest = i; }
            }
            return gap;
        }

        internal static float SegmentDistance(Vector2 p, Vector2 q, Vector2 r, Vector2 s)
        {
            var d1 = q - p; var d2 = s - r; var v = p - r;
            double a = Vector2.Dot(d1, d1), e = Vector2.Dot(d2, d2), f = Vector2.Dot(d2, v);
            double first, second;
            if (a <= 1e-10 && e <= 1e-10) return v.magnitude;
            if (a <= 1e-10) { first = 0; second = Clamp(f / e); }
            else
            {
                double c = Vector2.Dot(d1, v);
                if (e <= 1e-10) { second = 0; first = Clamp(-c / a); }
                else
                {
                    double b = Vector2.Dot(d1, d2), denominator = a * e - b * b;
                    first = denominator > 1e-10 ? Clamp((b * f - c * e) / denominator) : 0;
                    second = (b * first + f) / e;
                    if (second < 0) { second = 0; first = Clamp(-c / a); }
                    else if (second > 1) { second = 1; first = Clamp((b - c) / a); }
                }
            }
            return (p + d1 * (float)first - r - d2 * (float)second).magnitude;
        }

        private static double Clamp(double value) => Math.Max(0, Math.Min(1, value));
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
