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
    // Preserved leaves a boat moored at its own authored berth untouched.
    internal enum StartBerthMode { Moored, OffshoreHeld, ShipyardHeld, Preserved }

    internal sealed class StartBerthPlan
    {
        internal PortChoice Destination;
        internal Transform Frame;
        internal Vector3 LocalPosition, LocalShorePosition;
        internal Quaternion LocalRotation;
        // An offshore plan keeps its dock cleats as landing references only.
        internal GPButtonDockMooring Front, Back;
        internal PickupableBoatMooringRope FrontRope, BackRope;
        internal StartBerthMode Mode;
        internal bool OffshoreHeld => Mode == StartBerthMode.OffshoreHeld || Mode == StartBerthMode.ShipyardHeld;
        internal string OffshoreDiagnostics;
        internal Vector3 Position => Frame.TransformPoint(LocalPosition);
        internal Quaternion Rotation => Frame.rotation * LocalRotation;
        internal Vector3 ShorePosition => Frame.TransformPoint(LocalShorePosition);
    }

    internal static class StartBerthPlanner
    {
        private static readonly FieldInfo RopeParent = AccessTools.Field(typeof(PickupableBoatMooringRope), "initialParent");
        private static readonly FieldInfo RopeRest = AccessTools.Field(typeof(PickupableBoatMooringRope), "initialPos");
        private static readonly FieldInfo RopeMaximum = AccessTools.Field(typeof(PickupableBoatMooringRope), "maxLength");
        private const float HullMargin = .75f;
        private const float DepthMargin = 1f;

        private sealed class Survey
        {
            private int queries, candidates;
            private readonly int maximumCandidates;
            internal Survey(bool pirate = false)
            {
                queries = pirate ? 8000 : 50000;
                maximumCandidates = pirate ? 48 : 96;
            }
            internal bool Exhausted => queries <= 0 || candidates >= maximumCandidates;
            internal bool Query() { if (queries <= 0) return false; --queries; return true; }
            internal bool Candidate() { if (Exhausted) return false; ++candidates; return true; }
        }

        private enum DockRejection { DockSupport, RopeReach, HullClearance, Departure, DryShore }
        // Keep the failed stage when the survey moves on to another candidate.
        // This is bounded per attempt and does not change candidate acceptance.
        private sealed class DockReport
        {
            internal int Cleats, Pairs, Poses;
            private readonly int[] rejected = new int[5];
            private string last;
            internal void Reject(DockRejection stage, string reason)
            {
                ++rejected[(int)stage];
                last = stage + ": " + reason;
            }
            internal string Describe(bool exhausted)
            {
                var stages = string.Join(", ", Enum.GetValues(typeof(DockRejection)).Cast<DockRejection>()
                    .Select(stage => stage + "=" + rejected[(int)stage]));
                return "No clear, reachable moored berth with nearby dry shore fits this boat within the bounded survey. " +
                    $"Survey: cleats={Cleats}, pairs={Pairs}, poses={Poses}, rejected {stages}, budgetExhausted={exhausted}. " +
                    (last == null ? "No eligible dock pair was surveyed." : "Last rejection: " + last);
            }
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

        internal static bool TryPlan(PortChoice destination, Rigidbody body, BoatMooringRopes ropes, BoatSize? size,
            out StartBerthPlan plan, out string reason)
        {
            var accepted = TryPlanCore(destination, body, ropes, size, out plan, out reason);
            if (accepted && !string.IsNullOrEmpty(plan.OffshoreDiagnostics)) Plugin.Instance.Warn(plan.OffshoreDiagnostics);
            return accepted;
        }

        private static bool PirateRequiresOffshore(PortChoice destination, BoatSize? size) =>
            destination.Hidden == HiddenSite.Pirate && size != BoatSize.Small;

        private static bool TryPlanCore(PortChoice destination, Rigidbody body, BoatMooringRopes ropes, BoatSize? size,
            out StartBerthPlan plan, out string reason)
        {
            plan = null;
            reason = null;
            // Scenery readiness is a diagnostic here. The caller has already
            // warned when the deadline passed, and the survey still runs.
            if (!StartDestinations.IsCurrent(destination) || Plugin.Instance == null)
                return Fail("The starting location is no longer loaded.", out reason);
            if (!TryHull(body, ropes, out var hull, out reason) || !TryHeight(body, out var height, out reason)) return false;
            if (KnownSites.IsSirenSong(destination))
                return TrySirenSong(destination, body, ropes, hull, height, out plan, out reason);
            if (KnownSites.IsFortJuniper(destination) &&
                TryFortOuter(destination, body, ropes, hull, height, out plan, out reason)) return true;
            var survey = new Survey(destination.Hidden == HiddenSite.Pirate);
            if (destination.Hidden == HiddenSite.Saffron)
            {
                // Use the east beach for arrival and hold the boat nearby, bow north.
                // An explicitly registered shore marker still takes precedence.
                if (destination.Marker == null) return Fail("The Saffron starting marker is missing.", out reason);
                var seed = destination.ShoreMarker != null ? destination.ShoreMarker.position :
                    destination.Island.transform.TransformPoint(new Vector3(220f, 1.1f, 169.9f));
                var landingVerified = ShoreProbe.TryFindStartShore(destination, seed, seed, out var shore, out _, out var shoreReason);
                if (!landingVerified) shore = seed;
                if (!TryFixedOffshoreHold(destination, body, hull, height, SaffronOffshoreCenter, Vector3.forward,
                    survey, Make(destination, seed, Quaternion.identity, shore), out plan, out reason)) return false;
                if (!landingVerified)
                {
                    Note(plan, "Best-effort landing: Saffron's dry landing was not verified, so the player starts at the selected beach position. " + shoreReason);
                }
                return true;
            }

            var cleats = destination.DockRoot.GetComponentsInChildren<GPButtonDockMooring>(true)
                .Where(item => ValidCleat(destination, item, body, ropes)).Distinct().ToArray();
            string scope = null;
            if (destination.PreferredFront != null || destination.PreferredBack != null)
            {
                if (!cleats.Contains(destination.PreferredFront) || !cleats.Contains(destination.PreferredBack) ||
                    destination.PreferredFront == destination.PreferredBack)
                    scope = "The author's preferred moorings are unavailable, so the other dock cleats were surveyed.";
                else cleats = new[] { destination.PreferredFront, destination.PreferredBack };
            }
            if (cleats.Length > MaximumCleats)
            {
                // Keep a very large harbour's survey bounded by using the
                // cleats nearest the location's own reference point.
                var reference = destination.ShoreMarker != null ? destination.ShoreMarker.position :
                    destination.Marker != null ? destination.Marker.position : destination.DockRoot.position;
                cleats = cleats.OrderBy(item => (item.transform.position - reference).sqrMagnitude).Take(MaximumCleats).ToArray();
                scope = (scope == null ? "" : scope + " ") +
                    $"This location has more than {MaximumCleats} dock cleats, so only the nearest {MaximumCleats} were surveyed.";
            }
            if (scope != null) Plugin.Instance.Warn(scope);
            if (PirateRequiresOffshore(destination, size))
                return TryPirateOffshore(destination, body, ropes, hull, height, cleats, survey, out plan, out reason);
            return TrySurveyDock(destination, body, ropes, hull, height, cleats, survey, out plan, out reason);
        }

        private const int MaximumCleats = 128;

        // Only the chosen starter moves. The resident Cog and the vanilla
        // recovery berth keep their authored positions and load behavior.
        private static bool TrySirenSong(PortChoice destination, Rigidbody body, BoatMooringRopes ropes,
            Hull hull, float height, out StartBerthPlan plan, out string reason)
        {
            plan = null; reason = null;
            var cleats = destination.DockRoot.GetComponentsInChildren<GPButtonDockMooring>(true)
                .Where(c => StartDestinations.Loaded(c) && c.transform.parent == destination.Island.transform).ToArray();
            var a = cleats.Where(c => c.name == "dock_mooring M (5)").ToArray();
            var b = cleats.Where(c => c.name == "dock_mooring M (4)").ToArray();
            var water = destination.Recovery != null ? destination.Recovery.boatPos : null;
            if (a.Length != 1 || b.Length != 1 || water == null)
                return Fail("Siren Song's end-of-dock cleats or water reference are unavailable.", out reason);
            var bestDistance = float.PositiveInfinity;
            for (var reverse = 0; reverse < 2; ++reverse)
            {
                if (!TrySirenSongPose(hull.Center, hull.Radius, height, a[0].transform.position,
                    b[0].transform.position, water.position, reverse != 0, out var position, out var rotation, out var seed))
                    return Fail("Siren Song's end-of-dock berth geometry is invalid.", out reason);
                var front = reverse == 0 ? a[0] : b[0];
                var back = reverse == 0 ? b[0] : a[0];
                PickupableBoatMooringRope frontRope = null, backRope = null;
                var canMoor = ValidCleat(destination, front, body, ropes) && ValidCleat(destination, back, body, ropes) &&
                    front.spring != back.spring && RopePair(hull, position, rotation, front, back, out frontRope, out backRope);
                var distance = float.PositiveInfinity;
                if (canMoor)
                    distance = Vector3.Distance(position + rotation * hull.RopePositions[Array.IndexOf(hull.Ropes, frontRope)], front.transform.position) +
                        Vector3.Distance(position + rotation * hull.RopePositions[Array.IndexOf(hull.Ropes, backRope)], back.transform.position);
                if (plan != null && distance >= bestDistance) continue;
                var verified = ShoreProbe.TryFindStartShore(destination, seed, position,
                    out var shore, out _, out _, front.transform, back.transform);
                if (!verified && !DeckSupport(seed, new Survey(), out shore)) shore = seed;
                plan = Make(destination, position, rotation, shore);
                plan.Front = front; plan.Back = back;
                plan.FrontRope = frontRope; plan.BackRope = backRope;
                if (!canMoor)
                {
                    plan.Mode = StartBerthMode.OffshoreHeld;
                    plan.OffshoreDiagnostics = "Siren Song's starter uses the end of the dock and is held until boarding because its mooring ropes could not be connected there.";
                }
                if (!verified) Note(plan, "Siren Song's dry landing was not verified; the player uses the end-of-dock reference.");
                bestDistance = distance;
            }
            return true;
        }

        // Selected hull's measured side plus two metres from the live cleat
        // line. The native recovery marker identifies the water side only.
        internal static bool TrySirenSongPose(Vector3 hullCenter, float radius, float height, Vector3 a,
            Vector3 b, Vector3 water, bool reverse, out Vector3 position, out Quaternion rotation, out Vector3 shore)
        {
            position = shore = Vector3.zero; rotation = Quaternion.identity;
            var forward = Vector3.ProjectOnPlane(a - b, Vector3.up);
            if (!Finite(hullCenter) || !Finite(radius) || radius <= 0f || !Finite(height) ||
                !Finite(a) || !Finite(b) || !Finite(water) || forward.sqrMagnitude < 1f) return false;
            forward = forward.normalized;
            var outward = Vector3.Cross(Vector3.up, forward);
            var side = Vector3.Dot(water - a, outward);
            if (Mathf.Abs(side) < 1f) return false;
            if (side < 0f) outward *= -1f;
            if (reverse) forward *= -1f;
            var yaw = (float)Math.Atan2(forward.x, forward.z) * .5f;
            rotation = new Quaternion(0f, (float)Math.Sin(yaw), 0f, (float)Math.Cos(yaw));
            var midpoint = (a + b) * .5f;
            position = midpoint + outward * (radius + 2f) - rotation * hullCenter;
            position.y = height;
            shore = midpoint - outward * 2f;
            return Finite(position) && Finite(shore);
        }

        private static bool TryFortOuter(PortChoice destination, Rigidbody body, BoatMooringRopes ropes,
            Hull hull, float height, out StartBerthPlan plan, out string reason)
        {
            plan = null; reason = null;
            var cleats = destination.DockRoot.GetComponentsInChildren<GPButtonDockMooring>(true)
                .Where(c => StartDestinations.Loaded(c) && c.transform.parent != null &&
                    c.transform.parent.name == "FORT_dock4").ToArray();
            var front = cleats.FirstOrDefault(c => c.name == "FORT_dock_mooring_4S5");
            var back = cleats.FirstOrDefault(c => c.name == "FORT_dock_mooring_4S1");
            var forward = front != null && back != null ?
                Vector3.ProjectOnPlane(front.transform.position - back.transform.position, Vector3.up) : Vector3.zero;
            if (!Finite(forward) || forward.sqrMagnitude < .01f)
            {
                Plugin.Instance.Warn("Fort Juniper's outer dock cleats are unavailable; using the ordinary dock placement.");
                return false;
            }
            forward = forward.normalized;
            var outward = Vector3.Cross(forward, Vector3.up);
            var midpoint = (front.transform.position + back.transform.position) * .5f;
            var rotation = Quaternion.LookRotation(forward, Vector3.up);
            // Every hull lies alongside Dock4's southwest face, bow northwest. Measure
            // width and capsule-centre offset instead of choosing by size class.
            var position = midpoint + outward * (hull.Radius + 2f) - rotation * hull.Center;
            position.y = height;
            var seed = midpoint - outward * 2f;
            var verified = ShoreProbe.TryFindStartShore(destination, seed, position,
                out var shore, out _, out _, front.transform, back.transform);
            if (!verified && !DeckSupport(seed, new Survey(), out shore)) shore = seed;
            plan = Make(destination, position, rotation, shore);
            plan.Front = front; plan.Back = back;
            // Reach or an occupied cleat changes securing only. Neither can
            // move this start back into the inner harbour or prevent it.
            if (ValidCleat(destination, front, body, ropes) && ValidCleat(destination, back, body, ropes) &&
                front.spring != back.spring && RopePair(hull, position, rotation, front, back, out var frontRope, out var backRope))
            {
                plan.FrontRope = frontRope; plan.BackRope = backRope;
            }
            else
            {
                plan.Mode = StartBerthMode.OffshoreHeld;
                plan.OffshoreDiagnostics = "Fort Juniper's starter uses the outer dock edge and is held until boarding because its mooring ropes could not be connected there.";
            }
            if (!verified) Note(plan, "Fort Juniper's dry landing was not verified; the player uses the outer dock reference.");
            return true;
        }

        internal static bool TryFortShipyardShore(PortChoice destination, Vector3 release, out Vector3 shore)
        {
            shore = Vector3.zero;
            var cleats = destination.DockRoot.GetComponentsInChildren<GPButtonDockMooring>(true);
            var left = cleats.FirstOrDefault(c => StartDestinations.Loaded(c) && c.name == "FORT_dock_mooring_1L6");
            var right = cleats.FirstOrDefault(c => StartDestinations.Loaded(c) && c.name == "FORT_dock_mooring_1R6");
            if (left == null || right == null) return false;
            var midpoint = (left.transform.position + right.transform.position) * .5f;
            var seaward = Vector3.ProjectOnPlane(release - midpoint, Vector3.up);
            shore = midpoint - seaward.normalized * 2f;
            return Finite(shore);
        }

        // Choose native lines at the already selected pose. Failure changes
        // securing only: the boat remains here under the existing hold.
        internal static void PrepareShipyardMoorings(StartBerthPlan plan, Rigidbody body, BoatMooringRopes ropes)
        {
            if (!TryHull(body, ropes, out var hull, out _)) return;
            var cleats = plan.Destination.DockRoot.GetComponentsInChildren<GPButtonDockMooring>(true)
                .Where(c => ValidCleat(plan.Destination, c, body, ropes)).ToArray();
            // Nearest to the hull root can reverse the bow and stern lines.
            // Compare the actual assigned rope endpoints across all pairs.
            var best = float.PositiveInfinity;
            foreach (var front in cleats)
                foreach (var back in cleats)
                {
                    if (front == back || front.spring == back.spring ||
                        !RopePair(hull, plan.Position, plan.Rotation, front, back, out var frontRope, out var backRope)) continue;
                    var frontPoint = plan.Position + plan.Rotation * hull.RopePositions[Array.IndexOf(hull.Ropes, frontRope)];
                    var backPoint = plan.Position + plan.Rotation * hull.RopePositions[Array.IndexOf(hull.Ropes, backRope)];
                    var distance = Vector3.Distance(frontPoint, front.transform.position) + Vector3.Distance(backPoint, back.transform.position);
                    if (distance >= best) continue;
                    best = distance;
                    plan.Front = front; plan.Back = back;
                    plan.FrontRope = frontRope; plan.BackRope = backRope;
                }
        }

        internal static bool TryNativeShipyard(PortChoice destination, Shipyard yard, Rigidbody body, BoatMooringRopes ropes,
            out StartBerthPlan plan, out string reason)
        {
            plan = null;
            if (!TryHull(body, ropes, out var hull, out reason) || !TryHeight(body, out var height, out reason)) return false;
            string frontName, backName;
            switch (destination.Index)
            {
                // Inner/top face of the broad lower leg of Gold Rock's L-shaped yard dock.
                case KnownSites.GoldRockPort: frontName = "dock_spring_attachment (5)"; backName = "dock_spring_attachment (4)"; break;
                case KnownSites.DragonCliffsPort: frontName = "dock_mooring E (16)"; backName = "dock_mooring E (15)"; break;
                case KnownSites.KiciaBayPort: frontName = "dock_mooring E (10)"; backName = "dock_mooring E (15)"; break;
                default: return Fail("No measured native yard dock is defined here.", out reason);
            }
            var cleats = destination.Island.GetComponentsInChildren<GPButtonDockMooring>(true);
            var front = cleats.FirstOrDefault(c => StartDestinations.Loaded(c) && c.name == frontName);
            var back = cleats.FirstOrDefault(c => StartDestinations.Loaded(c) && c.name == backName);
            var span = front != null && back != null ?
                Vector3.ProjectOnPlane(front.transform.position - back.transform.position, Vector3.up) : Vector3.zero;
            if (!Finite(span) || span.sqrMagnitude < .01f)
                return Fail("The shipyard dock's landing cleats are unavailable.", out reason);
            var outward = Vector3.Cross(Vector3.up, span.normalized);
            var midpoint = (front.transform.position + back.transform.position) * .5f;
            var marker = yard.shipReleasePosition;
            var center = marker.position;
            // Dragon Cliffs discharges beyond its service trigger. Keep the
            // starting boat inside that trigger, between the same two piers.
            if (destination.Index == KnownSites.DragonCliffsPort) center -= marker.forward * 8f;
            // Preserve the authored release channel and heading. Wide hulls
            // need their physical side, rather than their root, clear of the pier.
            var distance = Vector3.Dot(center - midpoint, outward);
            var offset = hull.Radius + 2f - distance;
            // Gold Rock starts alongside the player's dock, including when
            // the native release marker is farther into the yard channel.
            center += outward * (destination.Index == KnownSites.GoldRockPort ? offset : Mathf.Max(0f, offset));
            var position = center - marker.rotation * hull.Center;
            position.y = height;
            // This point remains on the dock regardless of the chosen hull.
            // Use its deck height, not the elevated yard trigger's origin.
            var landing = midpoint - outward * 2f;
            // Dragon Cliffs' outer pier avoids the crane on the opposite pier.
            // Half a metre along its deck places the seed on a plank, not its seam.
            if (destination.Index == KnownSites.DragonCliffsPort) landing -= span.normalized * .5f;
            var landingFront = front.transform; var landingBack = back.transform;
            // Kicia Bay shares the ordinary player's dock, while the boat
            // remains at the shipyard. Recovery's root is the shore marker.
            if (destination.Index == KnownSites.KiciaBayPort && StartDestinations.Loaded(destination.Recovery))
            {
                landing = destination.Recovery.transform.position;
                landingFront = destination.Recovery.mooringFront;
                landingBack = destination.Recovery.mooringBack;
            }
            var seed = destination.ShoreMarker != null ? destination.ShoreMarker.position : landing;
            var verified = ShoreProbe.TryFindStartShore(destination, seed, seed,
                out var shore, out _, out _, landingFront, landingBack);
            if (!verified && !DeckSupport(seed, new Survey(), out shore)) shore = seed;
            plan = Make(destination, position, marker.rotation, shore);
            plan.Mode = StartBerthMode.ShipyardHeld;
            plan.Front = front; plan.Back = back;
            if (!verified) Plugin.Instance.Warn("The shipyard landing was not verified; the player uses its dock reference.");
            return true;
        }

        private static bool TrySurveyDock(PortChoice destination, Rigidbody body, BoatMooringRopes ropes, Hull hull, float height,
            GPButtonDockMooring[] cleats, Survey survey, out StartBerthPlan plan, out string reason)
        {
            plan = null; reason = null;
            var report = new DockReport { Cleats = cleats.Length };
            var shoreFailures = new ShoreFailures();
            var best = new BestDock();
            var pairs = (from a in cleats from b in cleats where a != b && a.spring != b.spring
                         let span = Vector3.ProjectOnPlane(a.transform.position - b.transform.position, Vector3.up).magnitude
                         where span >= 3f && span <= 80f && Mathf.Abs(a.transform.position.y - b.transform.position.y) <= 3f
                         orderby Mathf.Abs(span - 2f * (hull.HalfSegment + hull.Radius))
                         select new DockPair { A = a, B = b });
            // Pair nearby real dock endpoints, then check both water-facing sides.
            // The dock continuity test prevents treating separate shores as one quay.
            // Keep the first 64 supported pairs, nearest hull length first.
            foreach (var candidate in DockCandidates(SupportedDockPairs(pairs, survey, report).Take(64),
                destination.Hidden == HiddenSite.Pirate))
                {
                    if (best.FirstPair == null) best.FirstPair = candidate.Pair;
                    if (destination.Hidden == HiddenSite.Pirate && !shoreFailures.CanTry(candidate.Pair)) continue;
                    if (survey.Exhausted) return TryDockFallback(destination, body, ropes, hull, height, cleats,
                        report.Describe(true), best, pairs, out plan, out reason);
                    var a = candidate.Pair.A; var b = candidate.Pair.B;
                    var delta = Vector3.ProjectOnPlane(a.transform.position - b.transform.position, Vector3.up);
                    var forward = delta.normalized;
                    var right = Vector3.Cross(Vector3.up, forward);
                    var midpoint = (a.transform.position + b.transform.position) * .5f;
                    var rotation = Quaternion.LookRotation(forward, Vector3.up);
                    var extra = candidate.Offset.x; var side = candidate.Offset.y; var shift = candidate.Offset.z;
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
                    if (destination.Hidden == HiddenSite.Pirate && report.Poses >= 24)
                        return TryDockFallback(destination, body, ropes, hull, height, cleats,
                            report.Describe(true), best, pairs, out plan, out reason);
                    ++report.Poses;
                    if (!RopePair(hull, position, candidateRotation, a, b, out var frontRope, out var backRope))
                    { report.Reject(DockRejection.RopeReach, "No opposite-end rope pair reaches these cleats with native slack."); continue; }
                    best.Offer(1, candidate.Pair, position, candidateRotation, midpoint, frontRope, backRope);
                    if (!survey.Candidate()) return TryDockFallback(destination, body, ropes, hull, height, cleats,
                        report.Describe(true), best, pairs, out plan, out reason);
                    if (!Clear(body, hull, position, candidateRotation, survey, out reason))
                    { report.Reject(DockRejection.HullClearance, reason); continue; }
                    best.Offer(2, candidate.Pair, position, candidateRotation, midpoint, frontRope, backRope);
                    if (!DepartureClear(body, hull, position, candidateRotation, survey, out reason))
                    { report.Reject(DockRejection.Departure, reason); continue; }
                    best.Offer(3, candidate.Pair, position, candidateRotation, midpoint, frontRope, backRope);
                    var seed = destination.ShoreMarker != null ? destination.ShoreMarker.position : midpoint;
                    if (!ShoreProbe.TryFindStartShore(destination, seed, position, out var shore, out _,
                        out var shoreReason, a.transform, b.transform))
                    {
                        report.Reject(DockRejection.DryShore, shoreReason);
                        if (destination.Hidden == HiddenSite.Pirate)
                        {
                            shoreFailures.Reject(candidate.Pair);
                            if (shoreFailures.Exhausted)
                                return TryDockFallback(destination, body, ropes, hull, height, cleats,
                                    report.Describe(false), best, pairs, out plan, out reason);
                        }
                        continue;
                    }
                    plan = Make(destination, position, candidateRotation, shore);
                    plan.Front = a; plan.Back = b;
                    plan.FrontRope = frontRope; plan.BackRope = backRope;
                    reason = null;
                    return true;
                }
            return TryDockFallback(destination, body, ropes, hull, height, cleats,
                report.Describe(survey.Exhausted), best, pairs, out plan, out reason);
        }

        // The highest survey stage any moored pose passed, first in survey
        // order on ties: 1 rope reach, 2 hull clearance, 3 departure.
        private sealed class BestDock
        {
            internal int Stage;
            internal DockPair Pair, FirstPair;
            internal Vector3 Position, Midpoint;
            internal Quaternion Rotation;
            internal PickupableBoatMooringRope FrontRope, BackRope;
            internal void Offer(int stage, DockPair pair, Vector3 position, Quaternion rotation, Vector3 midpoint,
                PickupableBoatMooringRope front, PickupableBoatMooringRope back)
            {
                if (stage <= Stage) return;
                Stage = stage; Pair = pair; Position = position; Rotation = rotation; Midpoint = midpoint;
                FrontRope = front; BackRope = back;
            }
        }

        // Pirate keeps a moored pose that failed only its dry-shore check.
        // Otherwise a Pirate boat uses the fixed Pirate offshore hold.
        private static bool TryDockFallback(PortChoice destination, Rigidbody body, BoatMooringRopes ropes, Hull hull, float height,
            GPButtonDockMooring[] cleats, string dockReason, BestDock best,
            IEnumerable<DockPair> pairs, out StartBerthPlan plan, out string reason)
        {
            if (destination.Hidden != HiddenSite.Pirate || best.Stage >= 3)
                return TryBestEffort(destination, hull, height, best, best.FirstPair ?? pairs.FirstOrDefault(), cleats,
                    dockReason, out plan, out reason);
            // Same budget as a Medium or Large Pirate start, not what the dock survey left.
            if (!TryPirateOffshore(destination, body, ropes, hull, height, cleats, new Survey(true), out plan, out reason)) return false;
            Note(plan, dockReason);
            return true;
        }

        // No surveyed pose passed every check. Keep the start going with the
        // closest match and carry the survey report as a warning. Only an
        // unusable location (no cleat and no marker, or non-finite geometry)
        // still fails.
        private static bool TryBestEffort(PortChoice destination, Hull hull, float height, BestDock best,
            DockPair closest, GPButtonDockMooring[] cleats, string failure, out StartBerthPlan plan, out string reason)
        {
            plan = null; reason = null;
            if (best.Stage >= 1)
            {
                var a = best.Pair.A; var b = best.Pair.B;
                var shore = Vector3.zero;
                var verified = false;
                if (best.Stage < 3)
                {
                    var seed = destination.ShoreMarker != null ? destination.ShoreMarker.position : best.Midpoint;
                    verified = ShoreProbe.TryFindStartShore(destination, seed, best.Position, out shore, out _, out _,
                        a.transform, b.transform);
                }
                if (!verified) shore = DockLanding(a, b);
                if (!Finite(best.Position) || !Finite(shore))
                    return Fail(failure + " The closest surveyed berth is not finite.", out reason);
                plan = Make(destination, best.Position, best.Rotation, shore);
                plan.Front = a; plan.Back = b;
                plan.FrontRope = best.FrontRope; plan.BackRope = best.BackRope;
                plan.OffshoreDiagnostics = "Best-effort moored start: no berth passed every check, so the boat uses the closest surveyed berth, which passed " +
                    (best.Stage == 1 ? "rope reach only" : best.Stage == 2 ? "rope reach and hull clearance" :
                        "rope reach, hull clearance and departure clearance") +
                    (verified ? "." : ". The player lands on the dock between its cleats.") + " " + failure;
                return true;
            }
            GPButtonDockMooring front = null, back = null;
            if (closest != null) { front = closest.A; back = closest.B; }
            else if (cleats.Length > 0) front = cleats[0];
            if (!TryBestEffortOffshore(destination, hull, height, front, back, failure, out plan, out reason)) return false;
            // Separate posts need not have a continuous quay between them.
            // When support rejected every pair before any pose was tried,
            // test native rope reach once at this same best-effort pose.
            if (best.FirstPair == null && front != null && back != null)
            {
                if (RopePair(hull, plan.Position, plan.Rotation, front, back, out var frontRope, out var backRope))
                {
                    plan.Mode = StartBerthMode.Moored;
                    plan.FrontRope = frontRope; plan.BackRope = backRope;
                    plan.OffshoreDiagnostics = "Best-effort moored start: continuous dock support was not verified, but the native ropes reach the closest pair at this pose. " + failure;
                }
                else plan.OffshoreDiagnostics += " The closest-pair rope test also failed.";
            }
            return true;
        }

        // Hold the boat beside the dock where a moored hull would lie, or at
        // the author's marker, and land the player on that dock.
        private static bool TryBestEffortOffshore(PortChoice destination, Hull hull, float height,
            GPButtonDockMooring front, GPButtonDockMooring back, string failure, out StartBerthPlan plan, out string reason)
        {
            plan = null; reason = null;
            Vector3 shore;
            var landingVerified = false;
            if (front == null)
            {
                if (destination.Marker == null)
                    return Fail(failure + " No dock cleat or starting marker is available.", out reason);
                var seed = destination.ShoreMarker != null ? destination.ShoreMarker.position : destination.Marker.position;
                landingVerified = ShoreProbe.TryFindStartShore(destination, seed, destination.Marker.position, out shore, out _, out _);
                if (!landingVerified)
                {
                    if (destination.ShoreMarker == null)
                        return Fail(failure + " No dock cleat or dry landing is available.", out reason);
                    shore = destination.ShoreMarker.position;
                }
            }
            else shore = DockLanding(front, back);
            var dock = front == null ? destination.Marker.position :
                back == null ? front.transform.position : (front.transform.position + back.transform.position) * .5f;
            var away = destination.Island != null ?
                Vector3.ProjectOnPlane(dock - destination.Island.transform.position, Vector3.up) : Vector3.zero;
            var forward = back != null ? Vector3.ProjectOnPlane(front.transform.position - back.transform.position, Vector3.up) :
                Vector3.Cross(away, Vector3.up);
            if (!Finite(forward) || forward.sqrMagnitude < .01f) forward = Vector3.ProjectOnPlane(destination.DockRoot.forward, Vector3.up);
            if (!Finite(forward) || forward.sqrMagnitude < .01f)
                return Fail(failure + " The dock has no finite level heading for an offshore hold.", out reason);
            forward = forward.normalized;
            var rotation = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 position;
            if (destination.Marker != null)
            {
                var heading = Vector3.ProjectOnPlane(destination.Marker.forward, Vector3.up);
                if (Finite(heading) && heading.sqrMagnitude >= .5f) rotation = Quaternion.LookRotation(heading.normalized, Vector3.up);
                position = destination.Marker.position;
            }
            else
            {
                // Prefer the side with measured water depth, starting with the
                // side that faces away from the island.
                var right = Vector3.Cross(Vector3.up, forward);
                var offset = hull.Radius + 2f;
                var side = Vector3.Dot(right, away) >= 0f ? 1f : -1f;
                var survey = new Survey();
                if (!OpenWater(dock + right * side * offset, height, hull, survey) &&
                    OpenWater(dock - right * side * offset, height, hull, survey)) side = -side;
                position = dock + right * side * offset - rotation * hull.Center;
            }
            position.y = height;
            if (!Finite(position) || !Finite(shore))
                return Fail(failure + " The offshore hold or its landing is not finite.", out reason);
            var landing = Make(destination, shore, Quaternion.identity, shore);
            if (front != null && back != null)
            {
                landing.Front = front; landing.Back = back;
            }
            plan = OffshorePlan(destination, position, rotation, landing);
            plan.OffshoreDiagnostics = "Best-effort offshore start: no moored berth was selected, so the boat is held in open water " +
                (front == null ? "at the starting marker" : "beside the closest dock cleats, and the player lands on that dock") + ". " + failure;
            return true;
        }

        // Best-effort landing on the dock deck: the dock-continuity ground
        // query at the pair midpoint, then beneath each cleat, then the cleat.
        private static Vector3 DockLanding(GPButtonDockMooring a, GPButtonDockMooring b)
        {
            var survey = new Survey();
            var points = b == null ? new[] { a.transform.position } :
                new[] { (a.transform.position + b.transform.position) * .5f, a.transform.position, b.transform.position };
            foreach (var point in points)
                if (DeckSupport(point, survey, out var deck)) return deck;
            return a.transform.position;
        }

        private static bool OpenWater(Vector3 point, float height, Hull hull, Survey survey) =>
            Ground(point, height + hull.Radius + 20f, 300f, survey, out var ground) &&
            HasDepth(height + hull.Center.y - hull.Radius, ground.point.y);

        private static void Note(StartBerthPlan plan, string message)
        {
            plan.OffshoreDiagnostics = string.IsNullOrEmpty(plan.OffshoreDiagnostics) ? message : message + " " + plan.OffshoreDiagnostics;
        }

        private static bool TryPirateOffshore(PortChoice destination, Rigidbody body, BoatMooringRopes ropes,
            Hull hull, float height, GPButtonDockMooring[] cleats, Survey survey,
            out StartBerthPlan plan, out string reason)
        {
            plan = null;
            DockPair firstPair = null;
            var landingFailure = "no local dock group supplies a landing reference";
            if (TryLocalCleatCenter(cleats.Select(item => item.transform.position).ToArray(), out var origin))
            {
                var local = cleats.Where(item => Vector3.Distance(item.transform.position, origin) <= 20f).ToArray();
                var pairs = (from a in local from b in local where a != b && a.spring != b.spring
                             let span = Vector3.ProjectOnPlane(a.transform.position - b.transform.position, Vector3.up).magnitude
                             where span >= 3f && span <= 20f
                             orderby span descending
                             select new DockPair { A = a, B = b }).ToArray();
                landingFailure = "no validated dock landing was found within the bounded survey";
                var shoreAttempts = 0;
                foreach (var pair in pairs)
                {
                    if (survey.Exhausted || shoreAttempts >= 3) break;
                    if (!DockContinuous(pair.A, pair.B, survey, out reason)) continue;
                    if (firstPair == null) firstPair = pair;
                    ++shoreAttempts;
                    var seed = (pair.A.transform.position + pair.B.transform.position) * .5f;
                    if (!ShoreProbe.TryFindStartShore(destination, seed, seed, out var shore, out _,
                            out reason, pair.A.transform, pair.B.transform))
                    { landingFailure = reason; continue; }
                    // This is a landing proof only, never a candidate boat berth.
                    var landing = Make(destination, seed, Quaternion.identity, shore);
                    landing.Front = pair.A; landing.Back = pair.B;
                    if (TryFixedOffshoreHold(destination, body, hull, height, PirateOffshoreCenter, Vector3.right,
                            survey, landing, out plan, out var anchorReason))
                    { reason = null; return true; }
                    return Fail("Pirate requires an offshore start for this boat. " + anchorReason, out reason);
                }
                if (firstPair == null && pairs.Length > 0) firstPair = pairs[0];
            }
            // Best effort: keep the fixed hold and land the player on the dock deck.
            var front = firstPair != null ? firstPair.A : cleats.Length > 0 ? cleats[0] : null;
            var back = firstPair != null ? firstPair.B : null;
            if (front == null)
                return Fail("Pirate requires an offshore start for this boat, but no dock cleat supplies a landing.", out reason);
            var deck = DockLanding(front, back);
            var dockLanding = Make(destination, deck, Quaternion.identity, deck);
            if (back != null)
            {
                dockLanding.Front = front; dockLanding.Back = back;
            }
            if (!TryFixedOffshoreHold(destination, body, hull, height, PirateOffshoreCenter, Vector3.right,
                    survey, dockLanding, out plan, out var holdReason))
                return Fail("Pirate requires an offshore start for this boat. " + holdReason, out reason);
            Note(plan, "Best-effort landing: Pirate's dry landing was not verified (" + landingFailure + "), so the player lands on the dock deck.");
            reason = null;
            return true;
        }

        private static StartBerthPlan OffshorePlan(PortChoice destination, Vector3 position,
            Quaternion rotation, StartBerthPlan landing)
        {
            var plan = Make(destination, position, rotation, landing.ShorePosition);
            plan.Mode = StartBerthMode.OffshoreHeld;
            plan.Front = landing.Front; plan.Back = landing.Back;
            return plan;
        }

        // Island-local hull centers for the fixed offshore holds, chosen from
        // installed terrain. Pirate's lies in deep water southeast of its
        // enclosed inner lagoon with the bow east. Saffron's lies off the east
        // coast, about 22.5 m from the beach at this latitude, with the bow north.
        private static readonly Vector3 PirateOffshoreCenter = new Vector3(60.7f, 0f, -77.8f);
        private static readonly Vector3 SaffronOffshoreCenter = new Vector3(250f, 0f, 169.9f);

        private static bool TryFixedOffshoreHold(PortChoice destination, Rigidbody body, Hull hull, float height,
            Vector3 localCenter, Vector3 localForward, Survey survey,
            StartBerthPlan landing, out StartBerthPlan plan, out string reason)
        {
            plan = null; reason = null;
            var frame = destination.Island.transform;
            var heading = frame.rotation * localForward;
            heading.y = 0f;
            if (!Finite(heading) || heading.sqrMagnitude < .5f)
                return Fail(destination.DisplayName + "'s island frame is not level enough for the offshore hold.", out reason);
            var rotation = Quaternion.LookRotation(heading.normalized, Vector3.up);
            var position = frame.TransformPoint(localCenter) - rotation * hull.Center;
            position.y = height;
            if (!Finite(position) || !Finite(landing.ShorePosition))
                return Fail(destination.DisplayName + " offshore pose or landing is not finite.", out reason);
            plan = OffshorePlan(destination, position, rotation, landing);
            // Safety checks only report on this fixed hold. They never cancel it.
            string problem = "the bounded geometry survey is exhausted";
            if (!survey.Candidate() || !Clear(body, hull, position, rotation, survey, out problem))
                plan.OffshoreDiagnostics = destination.DisplayName + " offshore hold is using its fixed open-water position with hull clearance unverified: " + problem;
            else if (!DepartureClear(body, hull, position, rotation, survey, out problem))
                plan.OffshoreDiagnostics = destination.DisplayName + " offshore hold has clear hull space, but departure clearance was not verified: " + problem;
            return true;
        }

        private sealed class DockPair { internal GPButtonDockMooring A, B; }
        private sealed class DockCandidate { internal DockPair Pair; internal Vector3 Offset; }

        private sealed class ShoreFailures
        {
            // Try a few different dock/heading references, but do not repeat
            // the same shore search for each small offset of a failed pair.
            private readonly HashSet<DockPair> pairs = new HashSet<DockPair>();
            internal bool Exhausted => pairs.Count >= 3;
            internal bool CanTry(DockPair pair) => !Exhausted && !pairs.Contains(pair);
            internal void Reject(DockPair pair) => pairs.Add(pair);
        }

        private const int MaximumCheckedPairs = 2048;

        private static IEnumerable<DockPair> SupportedDockPairs(IEnumerable<DockPair> pairs, Survey survey, DockReport report)
        {
            // Both orders of one physical pair share the same dock between them.
            // Check every physical pair once. At a harbour of short piers, such as
            // Fort Juniper, every pair near hull length spans water between piers
            // and the only supported pairs are shorter. The geometry survey budget
            // still bounds the total work.
            var checkedPairs = new Dictionary<long, bool>();
            foreach (var pair in pairs)
            {
                if (survey.Exhausted) yield break;
                var a = pair.A.GetInstanceID(); var b = pair.B.GetInstanceID();
                var key = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
                if (!checkedPairs.TryGetValue(key, out var supported))
                {
                    if (checkedPairs.Count >= MaximumCheckedPairs) yield break;
                    supported = DockContinuous(pair.A, pair.B, survey, out var reason);
                    checkedPairs[key] = supported;
                    if (!supported) report.Reject(DockRejection.DockSupport, reason);
                }
                ++report.Pairs;
                if (supported) yield return pair;
            }
        }

        private static IEnumerable<DockCandidate> DockCandidates(IEnumerable<DockPair> pairs, bool pirate)
        {
            if (pirate)
            {
                var supported = pairs.ToArray();
                // Spend the smaller budget across docks and both headings
                // before trying more offsets at one pair.
                foreach (var offset in DockOffsets(true))
                    foreach (var pair in supported)
                        yield return new DockCandidate { Pair = pair, Offset = offset };
            }
            else
                foreach (var pair in pairs)
                    foreach (var offset in DockOffsets(false))
                        yield return new DockCandidate { Pair = pair, Offset = offset };
        }

        private static IEnumerable<Vector3> DockOffsets(bool pirate)
        {
            // Components are extra separation, dock side and along-dock shift.
            // Pirate's narrow dock lies against terrain. Survey its widest
            // supported separations first so the smaller pose budget reaches
            // clear water. Other destinations retain their original order.
            for (var major = 0; major < (pirate ? 5 : 2); ++major)
                for (var minor = 0; minor < (pirate ? 2 : 5); ++minor)
                    foreach (var shift in new[] { 0f, -3f, 3f })
                        yield return new Vector3((pirate ? 4 - major : minor) * 1.5f,
                            (pirate ? minor : major) == 0 ? 1f : -1f, shift);
        }

        private static bool TryLocalCleatCenter(Vector3[] points, out Vector3 center)
        {
            center = Vector3.zero;
            if (points == null || points.Length < 2 || points.Length > 128 || points.Any(point => !Finite(point))) return false;
            Vector3[] best = null;
            foreach (var point in points.OrderBy(value => value.x).ThenBy(value => value.z).ThenBy(value => value.y))
            {
                var group = points.Where(other => Vector3.Distance(point, other) <= 20f).ToArray();
                // Complete-link bound excludes chains between separate docks.
                if (group.Length < 2 || group.Any(a => group.Any(b => Vector3.Distance(a, b) > 20f))) continue;
                if (best == null || group.Length > best.Length) best = group;
            }
            if (best == null) return false;
            foreach (var point in best) center += point;
            center /= best.Length;
            return Finite(center);
        }

        private static StartBerthPlan Make(PortChoice destination, Vector3 position, Quaternion rotation, Vector3 shore)
        {
            var frame = destination.DockRoot;
            return new StartBerthPlan { Destination = destination, Frame = frame,
                LocalPosition = frame.InverseTransformPoint(position),
                LocalRotation = Quaternion.Inverse(frame.rotation) * rotation,
                LocalShorePosition = frame.InverseTransformPoint(shore) };
        }

        private static bool TryHull(Rigidbody body, BoatMooringRopes ropes, out Hull hull, out string reason)
        {
            hull = null; reason = null;
            if (!StartDestinations.Loaded(body) || ropes == null || ropes.GetComponent<Rigidbody>() != body ||
                ropes.ropes == null || ropes.ropes.Length < 2 || RopeParent == null || RopeRest == null)
                return Fail("Boat hull or initialized mooring ropes are missing.", out reason);
            var capsules = BerthClearance.RootHulls(body, true);
            var scale = body.transform.lossyScale;
            if (capsules.Length != 1 || capsules[0].direction != 2 || !Finite(scale) ||
                scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
                return Fail("This boat needs one measurable forward hull capsule.", out reason);
            var measured = BerthClearance.Measure(capsules[0], scale);
            var radius = measured.Radius;
            var length = measured.Length;
            var center = measured.Center;
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
            hull = new Hull { Center = center, Radius = radius, HalfSegment = measured.HalfSegment,
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

        private static bool DockContinuous(GPButtonDockMooring a, GPButtonDockMooring b, Survey survey, out string reason)
        {
            reason = null;
            var steps = Mathf.CeilToInt(Vector3.Distance(a.transform.position, b.transform.position) / 2f);
            var lowerCleat = Mathf.Min(a.transform.position.y, b.transform.position.y);
            if (!SeaLevel.TryStartDockSupportFloor(lowerCleat, out var floor))
                return Fail("Sea level at the dock is unavailable.", out reason);
            for (var i = 0; i <= steps; ++i)
            {
                var point = Vector3.Lerp(a.transform.position, b.transform.position, (float)i / steps);
                if (!DeckSupport(point, survey, out var deck))
                    return Fail(survey.Exhausted ? "The bounded geometry survey is exhausted." :
                        "No continuous static support was found between the cleats.", out reason);
                if (deck.y < floor)
                    return Fail("Support between the cleats is below the dock deck clearance.", out reason);
            }
            return true;
        }

        // Static deck support beneath a dock point. Dock continuity and
        // best-effort dock landings share this query.
        private static bool DeckSupport(Vector3 point, Survey survey, out Vector3 deck)
        {
            var found = Ground(point, point.y + 2f, 6f, survey, out var support);
            deck = found ? support.point : Vector3.zero;
            return found;
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

        private static string DescribeObstacle(Collider obstacle)
        {
            if (obstacle == null) return "an unavailable collider";
            var parts = new List<string>();
            var current = obstacle.transform;
            for (var depth = 0; current != null && depth < 8; ++depth, current = current.parent)
                parts.Add(DiagnosticLabel(current.name));
            if (current != null) parts.Add("...");
            parts.Reverse();
            return DiagnosticLabel(obstacle.GetType().Name) + " '" +
                DiagnosticLabel(obstacle.gameObject.scene.name) + ":" + string.Join("/", parts) + "'";
        }

        private static string DiagnosticLabel(string value)
        {
            if (string.IsNullOrEmpty(value)) return "?";
            return new string(value.Take(48).Select(character => char.IsControl(character) ? '?' : character).ToArray()) +
                (value.Length > 48 ? "..." : "");
        }

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
                    return Fail("The planned hull overlaps " + DescribeObstacle(obstacle) + ".", out reason);
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
                var capsules = BerthClearance.RootHulls(body, true);
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
                    return Fail("The immediate departure corridor is blocked by " + DescribeObstacle(hit.collider) + ".", out reason);
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
