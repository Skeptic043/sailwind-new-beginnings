using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    // A scene-bound preflight, held in a live frame through origin/scenery movement.
    // It does not own or mutate boats, native recovery registrations or providers.
    internal sealed class StartBerthPlan
    {
        internal PortChoice Destination;
        internal Transform Frame;
        internal Vector3 LocalPosition, LocalShorePosition, LocalAnchorPosition;
        internal Quaternion LocalRotation;
        internal GPButtonDockMooring Front, Back;
        internal PickupableBoatMooringRope FrontRope, BackRope;
        internal SpringJoint FrontSpring, BackSpring;
        internal bool Anchored;
        internal float AnchorLength;
        internal Vector3 Position => Frame.TransformPoint(LocalPosition);
        internal Quaternion Rotation => Frame.rotation * LocalRotation;
        internal Vector3 ShorePosition => Frame.TransformPoint(LocalShorePosition);
        internal Vector3 AnchorPosition => Frame.TransformPoint(LocalAnchorPosition);
        internal bool Revalidate(Rigidbody body, BoatMooringRopes ropes, out string reason) =>
            StartBerthPlanner.Revalidate(this, body, ropes, out reason);
    }

    internal static class StartBerthPlanner
    {
        private static readonly FieldInfo RopeParent = AccessTools.Field(typeof(PickupableBoatMooringRope), "initialParent");
        private static readonly FieldInfo RopeRest = AccessTools.Field(typeof(PickupableBoatMooringRope), "initialPos");
        private static readonly FieldInfo RopeMaximum = AccessTools.Field(typeof(PickupableBoatMooringRope), "maxLength");
        private const float HullMargin = .75f;
        private const float DepthMargin = 1f;
        private const float MaximumSearchRadius = 60f;

        private sealed class Survey
        {
            private int queries = 50000, candidates;
            internal bool Exhausted => queries <= 0 || candidates >= 96;
            internal bool Query() { if (queries <= 0) return false; --queries; return true; }
            internal bool Candidate() { if (Exhausted) return false; ++candidates; return true; }
        }

        private sealed class Hull
        {
            internal Vector3 Center;
            internal float Radius, HalfSegment;
            internal Vector3[] RopePositions;
            internal PickupableBoatMooringRope[] Ropes;
        }

        internal static bool CanInspect(Rigidbody body, BoatMooringRopes ropes, out string reason) =>
            TryHull(body, ropes, out _, out reason);

        internal static bool TryPlan(PortChoice destination, Rigidbody body, BoatMooringRopes ropes,
            out StartBerthPlan plan, out string reason)
        {
            plan = null;
            reason = null;
            if (!StartDestinations.IsCurrent(destination) || !StartDestinations.SceneryReady(destination) || Plugin.Instance == null)
                return Fail("Starting location scenery is not ready.", out reason);
            if (!TryHull(body, ropes, out var hull, out reason) || !TryHeight(body, out var height, out reason)) return false;
            var survey = new Survey();
            if (destination.HiddenKey == "saffron")
                return TryAnchored(destination, body, ropes, hull, height, survey, out plan, out reason);

            var cleats = destination.DockRoot.GetComponentsInChildren<GPButtonDockMooring>(true)
                .Where(item => ValidCleat(destination, item, body, ropes)).Distinct().ToArray();
            if (destination.PreferredFront != null || destination.PreferredBack != null)
            {
                if (!cleats.Contains(destination.PreferredFront) || !cleats.Contains(destination.PreferredBack) ||
                    destination.PreferredFront == destination.PreferredBack)
                    return Fail("The author's preferred moorings are unavailable.", out reason);
                cleats = new[] { destination.PreferredFront, destination.PreferredBack };
            }
            if (cleats.Length < 2) return Fail("No unclaimed pair of initialized dock cleats is available.", out reason);
            if (cleats.Length > 128) return Fail("This location needs an author-supplied dock scope to bound its berth survey.", out reason);
            var pairs = (from a in cleats from b in cleats where a != b && a.spring != b.spring
                         let span = Vector3.ProjectOnPlane(a.transform.position - b.transform.position, Vector3.up).magnitude
                         where span >= 3f && span <= 80f && Mathf.Abs(a.transform.position.y - b.transform.position.y) <= 3f
                         orderby Mathf.Abs(span - 2f * (hull.HalfSegment + hull.Radius))
                         select new { A = a, B = b }).Take(64);
            // Pair nearby real dock endpoints, then check both water-facing sides.
            // The dock continuity test prevents treating separate shores as one quay.
            foreach (var pair in pairs)
                {
                    if (survey.Exhausted) return Fail("No safe berth was found within the bounded dock survey.", out reason);
                    var a = pair.A; var b = pair.B;
                    var delta = Vector3.ProjectOnPlane(a.transform.position - b.transform.position, Vector3.up);
                    if (!DockContinuous(a, b, survey)) continue;
                    var forward = delta.normalized;
                    var right = Vector3.Cross(Vector3.up, forward);
                    var midpoint = (a.transform.position + b.transform.position) * .5f;
                    var rotation = Quaternion.LookRotation(forward, Vector3.up);
                    foreach (var side in new[] { 1f, -1f })
                        for (var extra = 0f; extra <= 6f; extra += 1.5f)
                            foreach (var shift in new[] { 0f, -3f, 3f })
                            {
                                if (destination.Marker != null && (side != 1f || extra != 0f || shift != 0f)) continue;
                                var center = midpoint + right * side * (hull.Radius + 2f + extra) + forward * shift;
                                var position = center - rotation * hull.Center;
                                var candidateRotation = rotation;
                                if (destination.Marker != null)
                                {
                                    var heading = Vector3.ProjectOnPlane(destination.Marker.forward, Vector3.up);
                                    if (!Finite(heading) || heading.sqrMagnitude < .5f) continue;
                                    candidateRotation = Quaternion.LookRotation(heading.normalized, Vector3.up);
                                    position = destination.Marker.position;
                                }
                                position.y = height;
                                if (!RopePair(hull, position, candidateRotation, a, b, out var frontRope, out var backRope)) continue;
                                if (!survey.Candidate()) return Fail("No safe berth was found within the bounded dock survey.", out reason);
                                if (!Clear(body, hull, position, candidateRotation, survey, out reason) ||
                                    !DepartureClear(body, hull, position, candidateRotation, survey, out reason)) continue;
                                var seed = destination.ShoreMarker != null ? destination.ShoreMarker.position : midpoint;
                                if (!Plugin.Instance.TryFindStartShore(destination, seed, position, out var shore, out _)) continue;
                                plan = Make(destination, position, candidateRotation, shore);
                                plan.Front = a; plan.Back = b;
                                plan.FrontSpring = a.spring; plan.BackSpring = b.spring;
                                plan.FrontRope = frontRope; plan.BackRope = backRope;
                                reason = null;
                                return true;
                            }
                }
            return Fail("No clear, reachable moored berth with nearby dry shore fits this boat within the bounded survey.", out reason);
        }

        private static StartBerthPlan Make(PortChoice destination, Vector3 position, Quaternion rotation, Vector3 shore)
        {
            var frame = destination.DockRoot;
            return new StartBerthPlan { Destination = destination, Frame = frame,
                LocalPosition = frame.InverseTransformPoint(position),
                LocalRotation = Quaternion.Inverse(frame.rotation) * rotation,
                LocalShorePosition = frame.InverseTransformPoint(shore) };
        }

        private static bool TryAnchored(PortChoice destination, Rigidbody body, BoatMooringRopes ropes,
            Hull hull, float height, Survey survey, out StartBerthPlan plan, out string reason)
        {
            plan = null; reason = null;
            if (destination.Marker == null) return Fail("The offshore starting marker is missing.", out reason);
            if (!AnchorModel(ropes, out _, out _, out reason)) return false;
            var origin = destination.Marker.position;
            var seed = destination.ShoreMarker != null ? destination.ShoreMarker.position : origin;
            if (hull.Radius + 8f > MaximumSearchRadius)
                return Fail("This hull exceeds the bounded offshore survey.", out reason);
            for (var ring = 0; ring < 8; ++ring)
                for (var bearing = 0; bearing < 12; ++bearing)
                {
                    // Spread the candidate budget over the whole radial range,
                    // so a blocked inner ring cannot consume every outer sample.
                    var radius = Mathf.Lerp(hull.Radius + 8f, MaximumSearchRadius, ring / 7f);
                    var angle = bearing * Mathf.PI * 2f / 12f;
                    var radial = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                    var rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, radial), Vector3.up);
                    var center = origin + radial * radius;
                    var position = center - rotation * hull.Center;
                    position.y = height;
                    if (!survey.Candidate()) return Fail("No safe anchorage was found within the bounded offshore survey.", out reason);
                    if (!Clear(body, hull, position, rotation, survey, out reason) ||
                        !TryAnchor(ropes, body, position, rotation, survey, out var anchor, out var length, out reason) ||
                        !AnchorSwingClear(body, ropes, hull, position, rotation, anchor, length, survey, out reason) ||
                        !DepartureClear(body, hull, position, rotation, survey, out reason) ||
                        !Plugin.Instance.TryFindStartShore(destination, seed, position, out var shore, out _)) continue;
                    plan = Make(destination, position, rotation, shore);
                    plan.Anchored = true;
                    plan.LocalAnchorPosition = plan.Frame.InverseTransformPoint(anchor);
                    plan.AnchorLength = length;
                    reason = null;
                    return true;
                }
            return Fail("No safe anchorage and nearby dry landing fit this boat at this location.", out reason);
        }

        internal static bool Revalidate(StartBerthPlan plan, Rigidbody body, BoatMooringRopes ropes, out string reason)
        {
            reason = null;
            var survey = new Survey();
            if (plan == null || !StartDestinations.IsCurrent(plan.Destination) ||
                !StartDestinations.SceneryReady(plan.Destination) || plan.Frame != plan.Destination.DockRoot ||
                plan.Frame == null || !TryHull(body, ropes, out var hull, out reason))
                return Fail(reason ?? "Starting location changed before placement.", out reason);
            if (!TryHeight(body, out var height, out reason) || Mathf.Abs(height - plan.Position.y) > .5f)
                return Fail(reason ?? "The berth moved away from the verified waterline.", out reason);
            if (!Clear(body, hull, plan.Position, plan.Rotation, survey, out reason) ||
                !DepartureClear(body, hull, plan.Position, plan.Rotation, survey, out reason)) return false;
            if (plan.Anchored)
            {
                if (plan.Destination.HiddenKey != "saffron" ||
                    !TryAnchor(ropes, body, plan.Position, plan.Rotation, survey, out var anchor, out var length, out reason) ||
                    Vector3.Distance(anchor, plan.AnchorPosition) > .25f || Mathf.Abs(length - plan.AnchorLength) > .005f ||
                    !AnchorSwingClear(body, ropes, hull, plan.Position, plan.Rotation, anchor, length, survey, out reason))
                    return Fail(reason ?? "The planned anchor support changed.", out reason);
            }
            else if (!ValidCleat(plan.Destination, plan.Front, body, ropes) ||
                !ValidCleat(plan.Destination, plan.Back, body, ropes) || plan.Front == plan.Back ||
                plan.Front.spring != plan.FrontSpring || plan.Back.spring != plan.BackSpring ||
                plan.FrontSpring == plan.BackSpring ||
                !RopePair(hull, plan.Position, plan.Rotation, plan.Front, plan.Back, out var front, out var back) ||
                front != plan.FrontRope || back != plan.BackRope)
                return Fail("A planned mooring or its rope reach changed.", out reason);
            if (Plugin.Instance == null || !Plugin.Instance.TryFindStartShore(plan.Destination, plan.ShorePosition,
                    plan.Position, out var shore, out _) || Vector3.Distance(shore, plan.ShorePosition) > .5f)
                return Fail("The planned dry landing is no longer supported.", out reason);
            return true;
        }

        private static bool TryHull(Rigidbody body, BoatMooringRopes ropes, out Hull hull, out string reason)
        {
            hull = null; reason = null;
            if (!StartDestinations.Loaded(body) || ropes == null || ropes.GetComponent<Rigidbody>() != body ||
                ropes.ropes == null || ropes.ropes.Length < 2 || RopeParent == null || RopeRest == null)
                return Fail("Boat hull or initialized mooring ropes are missing.", out reason);
            var capsules = body.GetComponents<CapsuleCollider>().Where(item => !item.isTrigger).ToArray();
            var scale = body.transform.lossyScale;
            if (capsules.Length != 1 || capsules[0].direction != 2 || !Finite(scale) ||
                scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
                return Fail("This boat needs one measurable forward hull capsule.", out reason);
            var capsule = capsules[0];
            var radius = capsule.radius * Mathf.Max(scale.x, scale.y);
            var length = Mathf.Max(capsule.height * scale.z, radius * 2f);
            var center = Vector3.Scale(capsule.center, scale);
            if (!Finite(radius) || !Finite(length) || !Finite(center) || radius <= 0f || length <= 0f || length > 150f)
                return Fail("Boat hull geometry is invalid or exceeds the bounded berth survey.", out reason);
            var positions = new Vector3[ropes.ropes.Length];
            for (var index = 0; index < positions.Length; ++index)
            {
                var rope = ropes.ropes[index];
                var parent = rope != null ? RopeParent.GetValue(rope) as Transform : null;
                if (parent == null || !parent.IsChildOf(body.transform) || rope.GetBoatRigidbody() != body ||
                    !(RopeRest.GetValue(rope) is Vector3 rest))
                    return Fail("A boat rope has no verified native rest position.", out reason);
                positions[index] = Vector3.Scale(body.transform.InverseTransformPoint(parent.TransformPoint(rest)), scale);
                if (!Finite(positions[index])) return Fail("A boat rope rest position is invalid.", out reason);
            }
            hull = new Hull { Center = center, Radius = radius, HalfSegment = Mathf.Max(0f, length * .5f - radius),
                Ropes = ropes.ropes.ToArray(), RopePositions = positions };
            return true;
        }

        private static bool ValidCleat(PortChoice site, GPButtonDockMooring cleat, Rigidbody body, BoatMooringRopes ropes)
        {
            if (!StartDestinations.Loaded(cleat) || !cleat.enabled || !cleat.gameObject.activeInHierarchy ||
                !cleat.transform.IsChildOf(site.DockRoot) || cleat.GetComponentInParent<IslandHorizon>() != site.Island ||
                cleat.spring == null || cleat.GetComponent<SpringJoint>() != cleat.spring ||
                cleat.spring.connectedBody != null && cleat.spring.connectedBody != body || !Finite(cleat.transform.position)) return false;
            return !Resources.FindObjectsOfTypeAll<BoatMooringRopes>().Any(other => other != null && other != ropes &&
                StartDestinations.Loaded(other) &&
                (other.mooringFront == cleat.transform || other.mooringBack == cleat.transform));
        }

        private static bool DockContinuous(GPButtonDockMooring a, GPButtonDockMooring b, Survey survey)
        {
            var steps = Mathf.CeilToInt(Vector3.Distance(a.transform.position, b.transform.position) / 2f);
            for (var i = 0; i <= steps; ++i)
            {
                var point = Vector3.Lerp(a.transform.position, b.transform.position, (float)i / steps);
                if (!Ground(point, point.y + 2f, 6f, survey, out var support) || Plugin.Instance == null ||
                    !Plugin.Instance.TryStartSeaHeight(support.point, out var sea) || support.point.y < sea + .1f)
                    return false;
            }
            return true;
        }

        private static bool RopePair(Hull hull, Vector3 position, Quaternion rotation,
            GPButtonDockMooring front, GPButtonDockMooring back,
            out PickupableBoatMooringRope frontRope, out PickupableBoatMooringRope backRope)
        {
            frontRope = backRope = null;
            if (front == null || back == null || !(RopeMaximum?.GetValue(null) is float maxSquared) ||
                !Finite(maxSquared) || maxSquared <= 0f) return false;
            var best = float.PositiveInfinity;
            for (var i = 0; i < hull.Ropes.Length; ++i)
                for (var j = 0; j < hull.Ropes.Length; ++j)
                {
                    if (i == j || hull.Ropes[i] == hull.Ropes[j] ||
                        hull.RopePositions[i].z <= hull.Center.z || hull.RopePositions[j].z >= hull.Center.z) continue;
                    var one = Vector3.Distance(position + rotation * hull.RopePositions[i], front.transform.position);
                    var two = Vector3.Distance(position + rotation * hull.RopePositions[j], back.transform.position);
                    if (!RopeFits(one, maxSquared) || !RopeFits(two, maxSquared) || one + two >= best) continue;
                    best = one + two; frontRope = hull.Ropes[i]; backRope = hull.Ropes[j];
                }
            return frontRope != null && backRope != null;
        }

        internal static bool RopeFits(float distance, float maximumSquared) => Finite(distance) && distance >= 0f &&
            Finite(maximumSquared) && maximumSquared > 0f && distance + 1f <= Mathf.Sqrt(maximumSquared);

        private static bool Clear(Rigidbody own, Hull hull, Vector3 position, Quaternion rotation, Survey survey, out string reason)
        {
            reason = null;
            if (!Finite(position) || !Finite(rotation) || Vector3.Dot(rotation * Vector3.up, Vector3.up) < .999f)
                return Fail("The berth pose is invalid or no longer level.", out reason);
            var center = position + rotation * hull.Center;
            var offset = rotation * Vector3.forward * hull.HalfSegment;
            var a = center + offset; var b = center - offset;
            if (!survey.Query()) return Fail("The bounded geometry survey is exhausted.", out reason);
            foreach (var obstacle in Physics.OverlapCapsule(a, b, hull.Radius + HullMargin, ~0, QueryTriggerInteraction.Ignore))
                if (obstacle != null && obstacle.attachedRigidbody != own && !obstacle.transform.IsChildOf(own.transform))
                    return Fail("The planned hull overlaps scenery or another physical object.", out reason);
            // Disabled resident hull capsules still reserve space on loaded islands.
            var footprint = new BerthClearance.Footprint { A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z), Radius = hull.Radius };
            if (!ResidentsClear(own, footprint, out reason)) return false;
            // Survey the entire capsule footprint at <=1m spacing, including its
            // rounded ends. Every point must have measured seabed clearance.
            foreach (var local in DepthSamples(hull.HalfSegment, hull.Radius))
            {
                var sample = center + rotation * local;
                if (!Ground(sample, center.y + hull.Radius + 20f, 300f, survey, out var ground) ||
                    !HasDepth(center.y - hull.Radius, ground.point.y))
                    return Fail("The full hull does not have verified seabed clearance.", out reason);
            }
            return true;
        }

        private static bool ResidentsClear(Rigidbody own, BerthClearance.Footprint footprint, out string reason)
        {
            reason = null;
            var midpoint = (footprint.A + footprint.B) * .5f;
            var center = new Vector3(midpoint.x, 0f, midpoint.y);
            var reach = Vector2.Distance(footprint.A, footprint.B) * .5f + footprint.Radius + HullMargin;
            foreach (var other in Resources.FindObjectsOfTypeAll<BoatMooringRopes>())
            {
                if (other == null || !StartDestinations.Loaded(other)) continue;
                var body = other.GetComponent<Rigidbody>();
                if (body == null || body == own) continue;
                var capsules = body.GetComponents<CapsuleCollider>().Where(item => !item.isTrigger).ToArray();
                if (capsules.Length != 1)
                {
                    if (NearbyBounds(body, center, reach))
                        return Fail("A nearby resident boat has unmeasurable hull bounds.", out reason);
                    continue;
                }
                var c = capsules[0];
                BerthClearance.Footprint otherHull;
                try { otherHull = BerthClearance.MakeFootprint(body.transform.position, body.transform.rotation,
                    body.transform.lossyScale, c.center, c.radius, c.height, c.direction); }
                catch (InvalidOperationException)
                {
                    if (NearbyBounds(body, center, reach))
                        return Fail("A nearby resident hull has invalid geometry.", out reason);
                    continue;
                }
                if (BerthClearance.SegmentDistance(footprint.A, footprint.B, otherHull.A, otherHull.B) -
                    footprint.Radius - otherHull.Radius < HullMargin)
                    return Fail("The planned hull overlaps a resident boat's berth.", out reason);
            }
            return true;
        }

        private static bool AnchorSwingClear(Rigidbody own, BoatMooringRopes ropes, Hull hull,
            Vector3 position, Quaternion rotation, Vector3 anchor, float length, Survey survey, out string reason)
        {
            if (!AnchorModel(ropes, out var maximum, out _, out reason)) return false;
            var hookLocal = Vector3.Scale(own.transform.InverseTransformPoint(ropes.GetAnchorController().transform.position),
                own.transform.lossyScale);
            var hook = position + rotation * hookLocal;
            var center = position + rotation * hull.Center;
            var endOffset = rotation * Vector3.forward * hull.HalfSegment;
            var reach = Mathf.Max(Vector3.ProjectOnPlane(center + endOffset - hook, Vector3.up).magnitude,
                Vector3.ProjectOnPlane(center - endOffset - hook, Vector3.up).magnitude) + hull.Radius;
            var radius = AnchorSwingRadius(maximum * length, hook.y - anchor.y, reach);
            if (!Finite(radius) || radius <= 0f || radius > 45f)
                return Fail("The anchor swing area exceeds the bounded shoreline survey.", out reason);
            var swingCenter = new Vector3(anchor.x, center.y, anchor.z);
            // This box encloses the swing circle at the hull's actual height.
            // Conservative extra clearance at its corners is intentional.
            if (!survey.Query()) return Fail("The bounded geometry survey is exhausted.", out reason);
            foreach (var obstacle in Physics.OverlapBox(swingCenter,
                new Vector3(radius + HullMargin, hull.Radius + HullMargin, radius + HullMargin),
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                if (obstacle != null && obstacle.attachedRigidbody != own && !obstacle.transform.IsChildOf(own.transform))
                    return Fail("The native anchor's swing area overlaps shore or scenery.", out reason);
            var flat = new Vector2(anchor.x, anchor.z);
            if (!ResidentsClear(own, new BerthClearance.Footprint { A = flat, B = flat, Radius = radius }, out reason)) return false;
            foreach (var local in DepthSamples(0f, radius))
            {
                if (!Ground(swingCenter + local, center.y + hull.Radius + 20f, 300f, survey, out var ground) ||
                    !HasDepth(center.y - hull.Radius, ground.point.y))
                    return Fail("The anchor swing area lacks verified seabed clearance.", out reason);
            }
            return true;
        }

        internal static float AnchorSwingRadius(float lineLength, float verticalDrop, float hullReach) =>
            !Finite(lineLength) || !Finite(verticalDrop) || !Finite(hullReach) || lineLength <= 0f || hullReach <= 0f ||
            Mathf.Abs(verticalDrop) > lineLength ? float.NaN :
            Mathf.Sqrt(Mathf.Max(0f, lineLength * lineLength - verticalDrop * verticalDrop)) + hullReach;

        private static bool NearbyBounds(Rigidbody body, Vector3 center, float reach)
        {
            foreach (var collider in body.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger) continue;
                var bounds = collider.bounds;
                if (Finite(bounds.min) && Finite(bounds.max) && bounds.size.sqrMagnitude > 0f)
                {
                    if (bounds.max.x >= center.x - reach && bounds.min.x <= center.x + reach &&
                        bounds.max.z >= center.z - reach && bounds.min.z <= center.z + reach) return true;
                }
                else if (Vector3.ProjectOnPlane(body.transform.position - center, Vector3.up).magnitude < reach + 100f)
                    return true;
            }
            return Vector3.ProjectOnPlane(body.transform.position - center, Vector3.up).magnitude < reach + 100f;
        }

        private static bool DepartureClear(Rigidbody own, Hull hull, Vector3 position, Quaternion rotation, Survey survey, out string reason)
        {
            reason = null;
            var forward = rotation * Vector3.forward;
            var center = position + rotation * hull.Center;
            var offset = forward * hull.HalfSegment;
            var distance = Mathf.Min(10f, hull.HalfSegment + hull.Radius);
            if (!survey.Query()) return Fail("The bounded geometry survey is exhausted.", out reason);
            foreach (var hit in Physics.CapsuleCastAll(center + offset, center - offset,
                hull.Radius + HullMargin, forward, distance, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider != null && hit.collider.attachedRigidbody != own && !hit.collider.transform.IsChildOf(own.transform))
                    return Fail("The immediate departure corridor is blocked.", out reason);
            // Endpoint occupancy also includes disabled resident hulls, which
            // physics casts alone cannot establish are absent.
            return Clear(own, hull, position + forward * distance, rotation, survey, out reason);
        }

        internal static IEnumerable<Vector3> DepthSamples(float halfSegment, float radius)
        {
            var along = Mathf.CeilToInt((halfSegment + radius) * 2f);
            var across = Mathf.CeilToInt(radius * 2f);
            for (var z = 0; z <= along; ++z)
            {
                var forward = Mathf.Lerp(-halfSegment - radius, halfSegment + radius, (float)z / along);
                var cap = Mathf.Max(0f, Mathf.Abs(forward) - halfSegment);
                var halfWidth = Mathf.Sqrt(Mathf.Max(0f, radius * radius - cap * cap));
                for (var x = 0; x <= across; ++x)
                    yield return new Vector3(Mathf.Lerp(-halfWidth, halfWidth, (float)x / across), 0f, forward);
            }
        }

        internal static bool HasDepth(float hullBottom, float ground) =>
            Finite(hullBottom) && Finite(ground) && hullBottom - ground >= DepthMargin;

        private static bool Ground(Vector3 sample, float top, float distance, Survey survey, out RaycastHit ground)
        {
            ground = default(RaycastHit);
            if (!survey.Query()) return false;
            var found = false;
            foreach (var hit in Physics.RaycastAll(new Vector3(sample.x, top, sample.z), Vector3.down,
                distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == null || hit.collider.attachedRigidbody != null ||
                    !StartDestinations.Loaded(hit.collider) || !Finite(hit.point) || hit.normal.y < .5f ||
                    hit.collider.GetComponentInParent<BoatMooringRopes>() != null ||
                    hit.collider.GetComponentInParent<ShipItem>() != null) continue;
                if (!found || hit.point.y > ground.point.y) { found = true; ground = hit; }
            }
            return found;
        }

        private static bool AnchorModel(BoatMooringRopes ropes, out float maximum, out Collider collider, out string reason)
        {
            maximum = 0f; collider = null; reason = null;
            var controller = ropes.GetAnchorController();
            if (ropes.anchor == null || controller == null || ropes.anchor.GetComponent<Rigidbody>() == null ||
                !controller.transform.IsChildOf(ropes.transform) ||
                Read(controller, "anchorRope") is UnityEngine.Object special && special != null ||
                !(Read(controller, "maxLength") is float max) || !Finite(max) || max < 9f)
                return Fail("The boat has no supported native anchor model.", out reason);
            var colliders = ropes.anchor.GetComponentsInChildren<Collider>().Where(item => item.enabled && !item.isTrigger &&
                item.attachedRigidbody == ropes.anchor.GetComponent<Rigidbody>()).ToArray();
            if (colliders.Length != 1) return Fail("The native anchor contact shape is ambiguous.", out reason);
            maximum = max; collider = colliders[0];
            return true;
        }

        private static bool TryAnchor(BoatMooringRopes ropes, Rigidbody body, Vector3 position, Quaternion rotation,
            Survey survey, out Vector3 anchorPosition, out float length, out string reason)
        {
            anchorPosition = Vector3.zero; length = 0f;
            if (!AnchorModel(ropes, out var max, out var collider, out reason)) return false;
            // The native controller is at the boat's anchor-line origin.
            var local = Vector3.Scale(body.transform.InverseTransformPoint(ropes.GetAnchorController().transform.position),
                body.transform.lossyScale);
            var origin = position + rotation * local;
            if (!Finite(origin) || !Ground(origin, position.y + 20f, 300f, survey, out var ground) || ground.normal.y < .85f)
                return Fail("The anchor has no verified, sufficiently level seabed contact.", out reason);
            var bottomOffset = ropes.anchor.transform.position.y - collider.bounds.min.y;
            if (!Finite(bottomOffset) || bottomOffset < 0f || bottomOffset > 5f)
                return Fail("The anchor contact offset is invalid.", out reason);
            anchorPosition = ground.point + Vector3.up * (bottomOffset + .02f);
            var distance = Vector3.Distance(origin, anchorPosition);
            if (!TryAnchorLength(distance, max, out length))
                return Fail("The native anchor line cannot reach the seabed safely.", out reason);
            return true;
        }

        internal static bool TryAnchorLength(float distance, float maximum, out float normalized)
        {
            normalized = 0f;
            if (!Finite(distance) || !Finite(maximum) || distance < 0f || maximum <= 0f) return false;
            var line = Mathf.Max(9f, distance + 2f);
            if (line > maximum) return false;
            normalized = line / maximum;
            return Finite(normalized) && normalized > 0f && normalized <= 1f;
        }

        private static object Read(object owner, string field) => owner != null ? AccessTools.Field(owner.GetType(), field)?.GetValue(owner) : null;

        private static bool TryHeight(Rigidbody body, out float height, out string reason)
        {
            height = 0f; reason = null;
            var probes = body.GetComponent("BoatProbes");
            if (probes == null || Read(probes, "_rb") as Rigidbody != body || !body.useGravity ||
                !Finite(Physics.gravity) || Physics.gravity.y >= 0f || Mathf.Abs(Physics.gravity.x) > .00001f ||
                Mathf.Abs(Physics.gravity.z) > .00001f || !(Read(probes, "_forcePoints") is Array points) ||
                !(Read(probes, "_queryPoints") is Vector3[] queries) || queries.Length != points.Length + 1 ||
                !(Read(probes, "_centerOfMass") is Vector3 center) || !Finite(center) ||
                !(Read(probes, "_forceMultiplier") is float multiplier) || !(Read(probes, "_totalWeight") is float total))
                return Fail("Native buoyancy has not initialized for this boat.", out reason);
            var offsets = new float[points.Length]; var weights = new float[points.Length];
            for (var i = 0; i < points.Length; ++i)
            {
                if (!(Read(points.GetValue(i), "_offsetPosition") is Vector3 offset) || !Finite(offset) ||
                    !(Read(points.GetValue(i), "_weight") is float weight))
                    return Fail("Native buoyancy points are invalid.", out reason);
                offsets[i] = (offset.y + center.y) * body.transform.lossyScale.y;
                weights[i] = weight;
            }
            var oceanType = probes.GetType().Assembly.GetType("Crest.OceanRenderer");
            var ocean = oceanType != null ? AccessTools.Property(oceanType, "Instance")?.GetValue(null, null) : null;
            var sea = ocean != null ? AccessTools.Property(oceanType, "SeaLevel")?.GetValue(ocean, null) : null;
            if (!(sea is float seaLevel) || !TryBuoyancyHeight(offsets, weights, body.mass, multiplier, total, seaLevel, out height))
                return Fail("Native buoyancy could not establish a finite starting waterline.", out reason);
            return true;
        }

        // Same native flat-water force equation used by Ship Shuffle's SpawnHeight
        // at 99619bea7959ed4aa4fdb593f872cc3826e3c1a8. No fleet lifecycle dependency.
        internal static bool TryBuoyancyHeight(float[] offsets, float[] weights, float mass, float multiplier,
            float totalWeight, float seaLevel, out float height)
        {
            height = 0f;
            if (offsets == null || weights == null || offsets.Length == 0 || offsets.Length != weights.Length ||
                !Finite(mass) || mass <= 0f || !Finite(multiplier) || multiplier <= 0f ||
                !Finite(totalWeight) || totalWeight <= 0f || !Finite(seaLevel)) return false;
            double sum = 0, minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
            for (var i = 0; i < offsets.Length; ++i)
            {
                if (!Finite(offsets[i]) || !Finite(weights[i]) || weights[i] < 0f) return false;
                sum += weights[i]; minimum = Math.Min(minimum, offsets[i]); maximum = Math.Max(maximum, offsets[i]);
            }
            if (sum <= 0 || Math.Abs(sum - totalWeight) > Math.Max(.0001, sum * .0001)) return false;
            var low = seaLevel - maximum - mass * totalWeight / (1000.0 * multiplier * sum);
            var high = seaLevel - minimum;
            for (var step = 0; step < 48; ++step)
            {
                var middle = (low + high) * .5;
                double lift = 0;
                for (var i = 0; i < offsets.Length; ++i) lift += weights[i] * Math.Max(0, seaLevel - middle - offsets[i]);
                if (1000.0 * multiplier * lift / totalWeight > mass) low = middle; else high = middle;
            }
            height = (float)((low + high) * .5);
            return Finite(height);
        }

        private static bool Fail(string message, out string reason) { reason = message; return false; }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
    }
}
