using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    // Rejection counts for one dockside surface search. Every probe receives
    // one; a caller that does not report diagnostics passes a fresh summary.
    internal sealed class SurfaceProbeSummary
    {
        public int Samples;
        public int Hits;
        public int Steep;
        public int BoatOrItem;
        public int MooringFixture;
        public int StructureOrFurniture;
        public int Height;
        public int Underwater;
        public int Clearance;
        public string Example;
        public string ClearanceExample;

        public override string ToString() =>
            $"samples {Samples}, hits {Hits}, steep {Steep}, boat/item {BoatOrItem}, " +
            $"mooring fixture {MooringFixture}, structure/furniture {StructureOrFurniture}, " +
            $"height {Height}, submerged {Underwater}, clearance {Clearance}" +
            (Example == null ? "" : $", first rejected {Example}") +
            (ClearanceExample == null ? "" : $", clearance blocked {ClearanceExample}");
    }

    // Decides where the player and supplies can safely stand near one start
    // site. A context holds the site's frame (which carries it through
    // floating-origin moves), destination, island, moorings and berth, plus
    // the scenery-readiness and player exclusions its probes consult. The
    // live start and every berth preflight build separate contexts, so a
    // preflight never reads or rewrites the live arrival.
    internal sealed class ShoreProbe
    {
        private static readonly string[] FurniturePrefixes =
            { "bench", "chair", "stool", "table", "desk", "counter", "shelf", "cabinet" };
        // Boat walk roots are scene state, not site state. Every context
        // shares one per-frame cache, so repeated preflights in one frame
        // search the scene once, as the single cache did before.
        private static readonly List<Transform> BoatWalkRoots = new List<Transform>();
        private static int boatWalkRootsFrame = -1;
        private static Transform preflightAnchor;
        // Each preflight builds a fresh context, so carry the once-only sea
        // fallback warning across them instead of repeating it per call.
        private static bool preflightSeaFallbackReported;

        private readonly PortChoice destination;
        private readonly IslandHorizon island;
        private readonly Transform frontMooring;
        private readonly Transform backMooring;
        private readonly Vector3 berthLocal;
        private readonly bool searchOasisPierExtension;
        private int sceneryLoadedFrame;
        private Transform playerObserver;
        private Transform playerController;
        private bool seaFallbackReported;

        // The berth is a world position, stored relative to the frame so it
        // follows the frame's later world shifts.
        internal ShoreProbe(Transform frame, PortChoice destination, IslandHorizon island,
            Transform frontMooring, Transform backMooring, Vector3 berth,
            bool searchOasisPierExtension, int sceneryLoadedFrame)
        {
            Frame = frame;
            this.destination = destination;
            this.island = island;
            this.frontMooring = frontMooring;
            this.backMooring = backMooring;
            berthLocal = frame.InverseTransformPoint(berth);
            this.searchOasisPierExtension = searchOasisPierExtension;
            this.sceneryLoadedFrame = sceneryLoadedFrame;
        }

        internal Transform Frame { get; }

        // Cargo still probes after temporary player tracking ends. Keep the
        // actual actor roots excluded for the whole shore-anchor lifetime.
        internal void ExcludePlayer(Transform observer, Transform controller)
        {
            playerObserver = observer;
            playerController = controller;
        }

        // Reuse the dry-ground/player-clearance validator before any boat or
        // ownership change. This private probe frame is discarded on failure.
        internal static bool TryFindStartShore(PortChoice destination, Vector3 seed, Vector3 berth,
            out Vector3 ground, out Collider support, out string failureReason,
            Transform frontMooring = null, Transform backMooring = null)
        {
            ground = Vector3.zero; support = null; failureReason = null;
            if (!StartDestinations.SceneryReady(destination))
            { failureReason = "Selected scenery is not ready for the shore probe."; return false; }
            if (preflightAnchor == null)
                preflightAnchor = new GameObject("New Beginnings shore preflight")
                    { hideFlags = HideFlags.HideInHierarchy }.transform;
            preflightAnchor.SetParent(destination.DockRoot, true);
            preflightAnchor.position = seed;
            // Automatically discovered ports have no author Preferred moorings.
            // Keep the actual validated pair so the existing dry-ground fallback
            // has the same physical references that the berth survey used.
            var probe = new ShoreProbe(preflightAnchor, destination, destination.Island,
                frontMooring != null ? frontMooring :
                    destination.PreferredFront != null ? destination.PreferredFront.transform : null,
                backMooring != null ? backMooring :
                    destination.PreferredBack != null ? destination.PreferredBack.transform : null,
                berth, false, Time.frameCount - 2)
            { seaFallbackReported = preflightSeaFallbackReported };
            try
            {
                var summary = new SurfaceProbeSummary();
                var tooFar = 0;
                foreach (var sample in StartShoreSamples(seed, berth, destination.HiddenKey == "pirate"))
                {
                    ++summary.Samples;
                    if (!probe.TryProbe(sample, out ground, out support, summary)) continue;
                    if (Vector3.ProjectOnPlane(ground - berth, Vector3.up).magnitude <= 45f) return true;
                    ++tooFar;
                }
                failureReason = "No supported nearby dry landing: " + summary + $", beyond boat access distance {tooFar}.";
                return false;
            }
            finally
            {
                preflightSeaFallbackReported = probe.seaFallbackReported;
                // Do not leave our anchor inside a port's or mod's hierarchy.
                preflightAnchor.SetParent(null, false);
            }
        }

        // Candidate player arrival around the target's dock marker: fixed
        // rings, the live mooring corridor, a landward fan and, for Oasis,
        // its pier extension. The closest validated surface wins.
        internal Tuple<Vector3, Collider> FindArrivalSurface(Vector3 center, SurfaceProbeSummary summary)
        {
            var best = FindClosestSurface(ArrivalSamples(center), center, sample =>
            {
                summary.Samples++;
                return TryProbe(sample, out var point, out var surface, summary)
                    ? Tuple.Create(point, surface) : null;
            }, IsTerrainCollider);
            if (searchOasisPierExtension && frontMooring != null &&
                backMooring != null && (best == null ||
                Vector3.ProjectOnPlane(best.Item1 - center, Vector3.up).sqrMagnitude > 64f))
            {
                // Oasis's narrow pier continues along its mooring line. The
                // ordinary six-metre rings and inland fallback miss those dock
                // sections when the near berth is obstructed. Keep an already
                // safe nearby arrival, and validate every extra candidate with
                // exactly the same live scene, dry-ground and clearance gates.
                var previousBest = best;
                var extension = new List<Vector3>(OasisPierExtensionSamples(center,
                    frontMooring.position, backMooring.position,
                    Frame.TransformPoint(berthLocal)));
                if (previousBest != null) extension.Add(previousBest.Item1);
                best = FindClosestSurface(extension, center, sample =>
                {
                    if (previousBest != null && sample == previousBest.Item1) return previousBest;
                    summary.Samples++;
                    return TryProbe(sample, out var point, out var surface, summary)
                        ? Tuple.Create(point, surface) : null;
                }, IsTerrainCollider);
            }
            return best;
        }

        private List<Vector3> ArrivalSamples(Vector3 center)
        {
            var samples = new List<Vector3>();
            for (var ring = 0; ring <= 4; ring++)
            {
                var count = ring == 0 ? 1 : ring * 8;
                var radius = ring == 0 ? 0f : ring == 1 ? 1.25f :
                    ring == 2 ? 2.75f : ring == 3 ? 4.5f : 6f;
                for (var index = 0; index < count; index++)
                {
                    var angle = count == 1 ? 0f : index * Mathf.PI * 2f / count;
                    samples.Add(center + new Vector3(Mathf.Cos(angle) * radius, 0f,
                        Mathf.Sin(angle) * radius));
                }
            }

            // A recovery marker can sit inland from the actual pier. In Fort
            // Aestrin its moorings are about five metres seaward, beyond the
            // old four-metre search. Sample the live mooring corridor rather
            // than assuming an island-space direction or fixed coordinates.
            if (frontMooring != null && backMooring != null)
            {
                var midpoint = (frontMooring.position +
                    backMooring.position) * 0.5f;
                var towardsPier = Vector3.ProjectOnPlane(midpoint - center, Vector3.up);
                if (towardsPier.sqrMagnitude > 0.25f && towardsPier.sqrMagnitude < 400f)
                {
                    var side = new Vector3(-towardsPier.z, 0f, towardsPier.x).normalized;
                    for (var step = 1; step <= 5; step++)
                    {
                        var along = center + towardsPier * (step / 5f);
                        samples.Add(along);
                        samples.Add(along + side * 0.8f);
                        samples.Add(along - side * 0.8f);
                    }
                }
            }
            if (Frame != null)
            {
                var landward = Vector3.ProjectOnPlane(center - Frame.TransformPoint(berthLocal),
                    Vector3.up);
                if (landward.sqrMagnitude > 1f)
                {
                    landward.Normalize();
                    var across = new Vector3(-landward.z, 0f, landward.x);
                    foreach (var distance in new[] { 4f, 7f, 10f, 14f, 18f, 24f, 30f })
                    {
                        var inland = center + landward * distance;
                        samples.Add(inland);
                        samples.Add(inland + across * 1.5f);
                        samples.Add(inland - across * 1.5f);
                        samples.Add(inland + across * 3f);
                        samples.Add(inland - across * 3f);
                    }
                }
            }
            return samples;
        }

        private static IEnumerable<Vector3> OasisPierExtensionSamples(Vector3 marker,
            Vector3 front, Vector3 back, Vector3 berth)
        {
            if (!IsFinite(marker) || !IsFinite(front) || !IsFinite(back) || !IsFinite(berth))
                yield break;
            var axis = Vector3.ProjectOnPlane(front - back, Vector3.up);
            var inland = Vector3.ProjectOnPlane(marker - berth, Vector3.up);
            if (axis.sqrMagnitude < 1f || axis.sqrMagnitude > 1600f || inland.sqrMagnitude < 1f ||
                Vector3.ProjectOnPlane(front - marker, Vector3.up).sqrMagnitude > 1600f ||
                Vector3.ProjectOnPlane(back - marker, Vector3.up).sqrMagnitude > 1600f)
                yield break;
            axis.Normalize();
            inland.Normalize();
            foreach (var distance in new[] { 8f, 12f, 16f })
                foreach (var direction in new[] { -1f, 1f })
                    foreach (var offset in new[] { 0f, 0.75f, 1.5f })
                        yield return marker + axis * (distance * direction) + inland * offset;
        }

        // The caller supplies the same validated
        // live-physics probe used elsewhere. This never writes actor transforms.
        internal static Tuple<Vector3, T> FindClosestSurface<T>(IEnumerable<Vector3> samples,
            Vector3 reference, Func<Vector3, Tuple<Vector3, T>> probe, Func<T, bool> isTerrain)
        {
            Tuple<Vector3, T> best = null;
            var bestScore = float.PositiveInfinity;
            foreach (var sample in samples)
            {
                var candidate = probe(sample);
                if (candidate == null) continue;
                var point = candidate.Item1;
                var height = point.y - reference.y;
                var score = new Vector2(point.x - reference.x, point.z - reference.z).magnitude * 2f +
                    Mathf.Abs(height) + Mathf.Max(0f, height - 1.5f) * 4f +
                    (isTerrain(candidate.Item2) ? 1f : 0f);
                if (score >= bestScore) continue;
                bestScore = score;
                best = candidate;
            }
            return best;
        }

        internal bool IsSelectedSceneryReady(out string state)
        {
            if (island == null)
            {
                state = "island scenery identity unavailable";
                return false;
            }
            if (destination != null && destination.NeedsPlannedBerth)
            {
                if (!StartDestinations.SceneryReady(destination))
                { sceneryLoadedFrame = -1; state = "waiting for selected island scenery"; return false; }
                if (sceneryLoadedFrame < 0) sceneryLoadedFrame = Time.frameCount;
                state = "waiting for selected scenery alignment";
                return Time.frameCount > sceneryLoadedFrame + 1;
            }
            // IslandHorizon.SceneLoaded flips before LoadSceneAsync finishes.
            // Follow its native RegisterLoadingFinished check, then allow the
            // scenery root's first Update to align with its live island.
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByBuildIndex(island.islandIndex);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                sceneryLoadedFrame = -1;
                state = "selected scenery scene " + island.islandIndex + " not loaded";
                return false;
            }
            if (sceneryLoadedFrame < 0) sceneryLoadedFrame = Time.frameCount;
            var ready = Time.frameCount > sceneryLoadedFrame;
            state = ready ? "selected scenery loaded" : "waiting for loaded scenery alignment";
            return ready;
        }

        internal static List<Vector3> NearbySurfaceSamples(Vector3 center, float maximumRadius)
        {
            var samples = new List<Vector3> { center };
            foreach (var radius in new[] { 1.25f, 2.75f, 4.5f, 6f })
            {
                if (radius > maximumRadius) continue;
                var count = Mathf.CeilToInt(radius * 8f);
                for (var index = 0; index < count; index++)
                {
                    var angle = index * Mathf.PI * 2f / count;
                    samples.Add(center + new Vector3(Mathf.Cos(angle) * radius, 0f,
                        Mathf.Sin(angle) * radius));
                }
            }
            return samples;
        }

        internal bool TryProbe(Vector3 horizontalSample, out Vector3 surface,
            out Collider collider, SurfaceProbeSummary summary, bool requirePlayerClearance = true)
        {
            surface = Vector3.zero;
            collider = null;
            var marker = Frame;
            if (marker == null || !IsFinite(horizontalSample))
            {
                if (summary.Example == null)
                    summary.Example = marker == null ? "dockside marker missing" :
                        "non-finite surface sample " + horizontalSample;
                return false;
            }
            // Main-scene terrain can be present before the selected island's
            // buildings and docks. Even a nearby candidate must wait for their
            // collision geometry before it can be judged safe for player or cargo.
            if (!IsSelectedSceneryReady(out var sceneryState))
            {
                if (summary.Example == null)
                    summary.Example = sceneryState;
                return false;
            }
            var landward = IsLandwardSample(horizontalSample);
            var origin = new Vector3(horizontalSample.x, marker.position.y + 20f,
                horizontalSample.z);
            var hits = Physics.RaycastAll(origin, Vector3.down, 32f, ~0,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                summary.Hits++;
                var candidate = hit.collider;
                if (candidate != null && IsMooringFixture(candidate))
                {
                    summary.MooringFixture++;
                    if (summary.Example == null)
                        summary.Example = TransformPath(candidate.transform) +
                            $" at {hit.point} (mooring fixture, not dock ground)";
                    continue;
                }
                if (candidate == null || !IsStaticDocksideCollider(candidate))
                {
                    summary.BoatOrItem++;
                    if (candidate != null && (summary.Example == null ||
                        string.Equals(candidate.name, "hull player collider",
                            StringComparison.OrdinalIgnoreCase)))
                        summary.Example = $"{candidate.name} layer {candidate.gameObject.layer}, " +
                            $"tag {candidate.gameObject.tag} at {hit.point}";
                    continue;
                }
                // Roofs, building interiors and furniture can be static and
                // level without being safe outdoor arrival ground. Keep them
                // static for clearance and occlusion, but never use their tops.
                if (IsUnsuitableStartSurface(candidate))
                {
                    summary.StructureOrFurniture++;
                    if (summary.Example == null)
                        summary.Example = TransformPath(candidate.transform) +
                            $" at {hit.point} (structure/furniture, not dock ground)";
                    continue;
                }
                if (hit.normal.y < 0.75f)
                {
                    summary.Steep++;
                    continue;
                }
                // Some high shore markers look down onto a lower pier. Admit
                // that pier only close to this port's live mooring segment;
                // ordinary terrain retains its stricter height rule below.
                var lowerMooringDock = !landward && !IsTerrainCollider(candidate) &&
                    IsLowerMooringDockSurface(horizontalSample, hit.point);
                if ((hit.point.y < marker.position.y - (landward ? 6f : 2f) &&
                    !lowerMooringDock) ||
                    hit.point.y > marker.position.y + 10f ||
                    IsTerrainCollider(candidate) &&
                    hit.point.y < marker.position.y - 0.75f && !landward)
                {
                    summary.Height++;
                    if (summary.Example == null)
                        summary.Example = TransformPath(candidate.transform) +
                            $" at {hit.point} below marker {marker.position.y:F1}";
                    continue;
                }
                if (IsOccludedGround(hit, hits))
                {
                    summary.Height++;
                    if (summary.Example == null)
                        summary.Example = TransformPath(candidate.transform) +
                            $" at {hit.point} beneath an overhead collider";
                    continue;
                }
                if (SeaLevel.TrySeaHeight(hit.point, out var seaHeight))
                {
                    if (hit.point.y < seaHeight + 0.35f)
                    {
                        summary.Underwater++;
                        continue;
                    }
                }
                else if (!IsAboveMooringFallback(candidate, hit.point,
                    lowerMooringDock && hit.point.y < marker.position.y - 2f))
                {
                    summary.Underwater++;
                    continue;
                }
                else if (!seaFallbackReported)
                {
                    seaFallbackReported = true;
                    Plugin.Instance?.Warn("Ocean height has not sampled yet; requiring dry-looking dock or shore " +
                        "ground above this port's live moorings for the new-game arrival.");
                }
                // Cargo checks its own volume and item embark triggers. Requiring
                // a human-sized capsule at every cargo sample rejects usable dock
                // beside nearby hulls, even when the native items fit safely.
                if (requirePlayerClearance && !HasPlayerClearance(hit.point, candidate,
                    out var clearanceReason))
                {
                    summary.Clearance++;
                    if (summary.ClearanceExample == null)
                        summary.ClearanceExample = TransformPath(candidate.transform) +
                            $" at {hit.point}: {clearanceReason}";
                    if (summary.Example == null)
                        summary.Example = TransformPath(candidate.transform) +
                            $" at {hit.point}";
                    continue;
                }
                surface = hit.point;
                collider = candidate;
                return true;
            }
            return false;
        }

        private bool IsLandwardSample(Vector3 sample)
        {
            if (Frame == null) return false;
            var inland = Vector3.ProjectOnPlane(Frame.position -
                Frame.TransformPoint(berthLocal), Vector3.up);
            if (inland.sqrMagnitude < 1f) return false;
            var offset = Vector3.ProjectOnPlane(sample - Frame.position, Vector3.up);
            return Vector3.Dot(offset, inland.normalized) >= 8f;
        }

        private bool IsOccludedGround(RaycastHit ground, RaycastHit[] hits)
        {
            foreach (var above in hits)
            {
                if (above.distance >= ground.distance) break;
                if (above.collider != null && !IsTerrainCollider(above.collider) &&
                    IsStaticDocksideCollider(above.collider) &&
                    above.point.y > ground.point.y + 0.3f &&
                    // Terrain and indoor building floors stay blocked. An
                    // outdoor market canopy can cover a real dock: it is not
                    // support itself, but player/item clearance still checks
                    // its roof, posts and counter at their actual heights.
                    (IsTerrainCollider(ground.collider) ||
                        IsUnsuitableStartSurface(above.collider) &&
                        !IsOutdoorMarketStall(above.collider)))
                    return true;
            }
            return false;
        }

        private bool IsLowerMooringDockSurface(Vector3 sample, Vector3 point)
        {
            if (Frame == null) return false;
            if (frontMooring == null || backMooring == null) return false;
            return IsLowerMooringDockPoint(Frame.position, frontMooring.position,
                backMooring.position, sample, point);
        }

        private static bool IsLowerMooringDockPoint(Vector3 marker, Vector3 front, Vector3 back,
            Vector3 sample, Vector3 point)
        {
            var lowestMooring = Mathf.Min(front.y, back.y);
            var highestMooring = Mathf.Max(front.y, back.y);
            // A modestly raised recovery marker can still be more than two
            // metres above a usable pier (Mount Malefic is such a case).
            // Admit only a point needing this exception, near real moorings;
            // the caller still applies dry-ground and player/cargo clearance.
            if (point.y >= marker.y - 2f ||
                point.y < lowestMooring - 1f ||
                point.y > highestMooring + 4f) return false;
            var segment = Vector3.ProjectOnPlane(front - back, Vector3.up);
            if (segment.sqrMagnitude < 1f) return false;
            var offset = Vector3.ProjectOnPlane(sample - back, Vector3.up);
            var along = Mathf.Clamp01(Vector3.Dot(offset, segment) /
                segment.sqrMagnitude);
            var closest = back + segment * along;
            return (Vector3.ProjectOnPlane(sample - closest, Vector3.up)).sqrMagnitude <= 16f;
        }

        private bool IsAboveMooringFallback(Collider candidate, Vector3 point,
            bool newlyAdmittedLowDock)
        {
            if (Frame == null) return false;
            if (frontMooring == null || backMooring == null)
            {
                // A held start can have no dock pair. It still needs a
                // verified wave upper bound, never just the sampler's mean sea
                // level output. All ordinary surface and clearance rules apply.
                if (destination == null ||
                    !SeaLevel.TrySeaUpperBound(out var upperBound)) return false;
                return point.y >= upperBound + .35f &&
                    point.y <= Frame.position.y + (IsTerrainCollider(candidate) ? 6f : 3f);
            }
            // Crest can fail to sample while the new-game scene settles. Native
            // berth moorings move with Scrambled Seas and provide a local height
            // reference. Shore terrain must rise above them; a dock collider may
            // be slightly lower because its top is below the rope fixture.
            var mooringHeight = Mathf.Min(frontMooring.position.y,
                backMooring.position.y);
            var terrain = IsTerrainCollider(candidate);
            var minimum = mooringHeight + (terrain ? 0.35f : -1f);
            if (terrain || newlyAdmittedLowDock)
            {
                // A mooring alone cannot bound a wave crest. Terrain and newly
                // admitted low berth surfaces need the live ocean renderer's
                // displacement bound while precise sampling is unavailable.
                if (!SeaLevel.TrySeaUpperBound(out var upperBound)) return false;
                minimum = Mathf.Max(minimum, upperBound + 0.35f);
            }
            return point.y >= minimum &&
                point.y <= Frame.position.y + (terrain ? 6f : 3f);
        }

        private static bool IsMooringFixture(Collider collider) =>
            collider != null &&
            collider.GetComponentInParent<GPButtonDockMooring>() != null;

        private static bool IsRoofSurface(Collider collider)
        {
            for (var current = collider.transform; current != null; current = current.parent)
                if (current.name.IndexOf("roof", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        internal static bool IsUnsuitableStartSurface(Collider collider)
        {
            if (collider == null) return true;
            if (IsRoofSurface(collider) || IsOutdoorMarketStall(collider)) return true;
            for (var current = collider.transform; current != null; current = current.parent)
            {
                var name = current.name;
                // Native Aestrin buildings combine exterior walls and roof in
                // one collider. Its roof is not a separate object named roof.
                // Classify the shell too, so it also occludes ground beneath it.
                if (name.StartsWith("ita_building_", StringComparison.OrdinalIgnoreCase) ||
                    name.IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    IsFurnitureName(name)) return true;
            }
            return false;
        }

        private static bool IsOutdoorMarketStall(Collider collider)
        {
            if (collider == null) return false;
            var stall = false;
            for (var current = collider.transform; current != null; current = current.parent)
            {
                var name = current.name;
                if (name.StartsWith("ita_building_", StringComparison.OrdinalIgnoreCase) ||
                    name.IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
                if (string.Equals(name, "market stall", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("market stall ", StringComparison.OrdinalIgnoreCase))
                    stall = true;
            }
            return stall;
        }

        private static bool IsFurnitureName(string name)
        {
            foreach (var prefix in FurniturePrefixes)
                if (string.Equals(name, prefix, StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static IEnumerable<Vector3> StartShoreSamples(Vector3 seed, Vector3 berth, bool pirate)
        {
            yield return seed;
            if (pirate)
            {
                // Search toward land from the real dock/boat relationship first.
                // These points move with the selected site's live world frame.
                var inland = Vector3.ProjectOnPlane(seed - berth, Vector3.up);
                if (inland.sqrMagnitude > 1f)
                {
                    inland.Normalize();
                    var across = Vector3.Cross(Vector3.up, inland);
                    foreach (var distance in new[] { 4f, 8f, 12f, 18f, 24f })
                        foreach (var side in new[] { 0f, -2f, 2f })
                            yield return seed + inland * distance + across * side;
                }
            }
            foreach (var radius in new[] { 2f, 4f, 8f, 12f, 18f, 24f })
            {
                var count = pirate ? 8 : 16;
                for (var index = 0; index < count; ++index)
                {
                    var angle = index * Mathf.PI * 2f / count;
                    yield return seed + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                }
            }
        }

        private bool IsStaticDocksideCollider(Collider collider)
        {
            if (collider.isTrigger ||
                IsMooringFixture(collider) ||
                collider.attachedRigidbody != null && !collider.attachedRigidbody.isKinematic ||
                collider.CompareTag("Boat") || collider.CompareTag("Player") ||
                collider.gameObject.layer == 8 ||
                string.Equals(collider.name, "hull player collider",
                    StringComparison.OrdinalIgnoreCase)) return false;
            var transform = collider.transform;
            for (var current = transform; current != null; current = current.parent)
                if (current.gameObject.tag == "WalkColBoat") return false;
            RefreshBoatWalkRoots();
            foreach (var walkRoot in BoatWalkRoots)
                if (walkRoot != null && (transform == walkRoot || transform.IsChildOf(walkRoot)))
                    return false;
            if (transform.GetComponentInParent<BoatMooringRopes>() != null ||
                transform.GetComponentInParent<BoatRefs>() != null ||
                transform.GetComponentInParent<ShipItem>() != null ||
                transform.GetComponentInParent<ItemRigidbody>() != null) return false;
            return !IsPlayerHierarchy(transform, playerController, playerObserver);
        }

        private static bool IsPlayerHierarchy(Transform candidate, Transform controller,
            Transform observer) => candidate != null &&
            (controller != null && (candidate == controller || candidate.IsChildOf(controller)) ||
                observer != null && (candidate == observer || candidate.IsChildOf(observer)));

        internal bool IsLoadedCollider(Collider collider) =>
            collider != null && collider.enabled && collider.gameObject.activeInHierarchy &&
            collider.gameObject.scene.IsValid() && collider.gameObject.scene.isLoaded &&
            IsStaticDocksideCollider(collider);

        private static void RefreshBoatWalkRoots()
        {
            if (boatWalkRootsFrame == Time.frameCount) return;
            boatWalkRootsFrame = Time.frameCount;
            BoatWalkRoots.Clear();
            // Sailwind keeps boat walk colliders under a separate scene root.
            // Ancestry from the hull collider to BoatRefs therefore misses it.
            foreach (var refs in Resources.FindObjectsOfTypeAll<BoatRefs>())
                if (refs != null && refs.walkCol != null)
                    BoatWalkRoots.Add(refs.walkCol);
            foreach (var embark in Resources.FindObjectsOfTypeAll<BoatEmbarkCollider>())
                if (embark != null && embark.walkCollider != null)
                    BoatWalkRoots.Add(embark.walkCollider);
        }

        // Terrain physics lives in an optional Unity module not needed by the
        // rest of this mod; inspect the installed collider's concrete name.
        internal static bool IsTerrainCollider(Collider collider) =>
            collider != null && string.Equals(collider.GetType().Name, "TerrainCollider",
                StringComparison.Ordinal);

        private bool HasPlayerClearance(Vector3 point, Collider support, out string reason)
        {
            reason = null;
            // The native controller is roughly human sized. Reject a shelf
            // beneath a solid overhang and a cramped ledge before teleporting.
            var foot = point + Vector3.up * 0.4f;
            var head = point + Vector3.up * 1.5f;
            // Player embark triggers switch the controller into a remote walk
            // scene even when the terrain underneath is otherwise standable.
            foreach (var overlap in Physics.OverlapCapsule(foot, head, 0.4f, ~0,
                QueryTriggerInteraction.Collide))
                if (overlap != null && (overlap.CompareTag("EmbarkCol") ||
                    overlap.CompareTag("EmbarkColPlayer")))
                {
                    reason = "embark trigger " + TransformPath(overlap.transform);
                    return false;
                }
            foreach (var overlap in Physics.OverlapCapsule(foot, head, 0.28f, ~0,
                QueryTriggerInteraction.Ignore))
                if (overlap != support &&
                    (IsStaticDocksideCollider(overlap) || IsMooringFixture(overlap)) &&
                    overlap.bounds.max.y > point.y + 0.45f)
                {
                    reason = "standing capsule intersects " + TransformPath(overlap.transform);
                    return false;
                }
            foreach (var hit in Physics.RaycastAll(point + Vector3.up * 0.15f,
                Vector3.up, 1.85f, ~0, QueryTriggerInteraction.Ignore))
                if (IsStaticDocksideCollider(hit.collider) ||
                    IsMooringFixture(hit.collider))
                {
                    reason = "headroom intersects " + TransformPath(hit.collider.transform);
                    return false;
                }
            return true;
        }

        internal static string TransformPath(Transform transform)
        {
            var names = new List<string>();
            for (var current = transform; current != null; current = current.parent)
                names.Add(current.name);
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        internal static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    // Crest ocean height through reflection. The reflected members are
    // resolved once per process; no per-start state lives here.
    internal static class SeaLevel
    {
        private static object seaHeightSampler;
        private static MethodInfo seaHeightInitMethod;
        private static MethodInfo seaHeightSampleMethod;
        private static bool seaHeightInitialized;
        private static bool seaBoundInitialized;
        private static PropertyInfo oceanInstanceProperty;
        private static PropertyInfo oceanSeaLevelProperty;
        private static PropertyInfo oceanMaxDisplacementProperty;

        // Dock continuity classifies static structure, not wave clearance, so
        // it uses mean sea level. A deck may sit up to a metre below its cleats,
        // as the dock-landing rule allows. Player and cargo surfaces keep their
        // own wave checks.
        internal static bool TryStartDockSupportFloor(float lowerCleat, out float floor)
        {
            floor = 0f;
            if (!ShoreProbe.IsFinite(lowerCleat) || !TryMeanSeaLevel(out var seaLevel)) return false;
            floor = Mathf.Max(seaLevel + .1f, lowerCleat - 1f);
            return true;
        }

        internal static bool TrySeaUpperBound(out float upperBound)
        {
            upperBound = 0f;
            if (!TryMeanSeaLevel(out var seaLevel) || oceanMaxDisplacementProperty == null) return false;
            try
            {
                var ocean = oceanInstanceProperty.GetValue(null, null);
                if (ocean == null || !(oceanMaxDisplacementProperty.GetValue(ocean, null) is float maxVertical) ||
                    !ShoreProbe.IsFinite(maxVertical) || maxVertical < 0f)
                    return false;
                // Wave providers may not have reported displacement on the
                // first startup frame. Keep a metre of clearance in that case.
                upperBound = seaLevel + Mathf.Max(1f, maxVertical);
                return ShoreProbe.IsFinite(upperBound);
            }
            catch { return false; }
        }

        private static bool TryMeanSeaLevel(out float seaLevel)
        {
            seaLevel = 0f;
            if (!seaBoundInitialized)
            {
                seaBoundInitialized = true;
                var type = AccessTools.TypeByName("Crest.OceanRenderer");
                if (type != null)
                {
                    oceanInstanceProperty = AccessTools.Property(type, "Instance");
                    oceanSeaLevelProperty = AccessTools.Property(type, "SeaLevel");
                    oceanMaxDisplacementProperty = AccessTools.Property(type,
                        "MaxVertDisplacement");
                }
            }
            if (oceanInstanceProperty == null || oceanSeaLevelProperty == null) return false;
            try
            {
                var ocean = oceanInstanceProperty.GetValue(null, null);
                if (ocean == null || !(oceanSeaLevelProperty.GetValue(ocean, null) is float level) ||
                    !ShoreProbe.IsFinite(level))
                    return false;
                seaLevel = level;
                return true;
            }
            catch { return false; }
        }

        internal static bool TrySeaHeight(Vector3 point, out float height)
        {
            height = 0f;
            if (!seaHeightInitialized)
            {
                seaHeightInitialized = true;
                try
                {
                    var helperType = AccessTools.TypeByName("Crest.SampleHeightHelper");
                    if (helperType != null)
                    {
                        seaHeightInitMethod = AccessTools.Method(helperType, "Init",
                            new[] { typeof(Vector3), typeof(float), typeof(bool),
                                typeof(UnityEngine.Object) });
                        seaHeightSampleMethod = AccessTools.Method(helperType, "Sample",
                            new[] { typeof(float).MakeByRefType() });
                        if (seaHeightInitMethod != null && seaHeightSampleMethod != null)
                            seaHeightSampler = Activator.CreateInstance(helperType);
                    }
                }
                catch (Exception exception)
                {
                    Plugin.Instance?.Warn("Native ocean height sampler could not initialize: " + exception.Message);
                }
                if (seaHeightInitMethod == null || seaHeightSampleMethod == null ||
                    seaHeightSampler == null)
                    Plugin.Instance?.Warn("Native ocean height sampler is unavailable; waiting for a verifiable dry start.");
            }
            if (seaHeightInitMethod == null || seaHeightSampleMethod == null ||
                seaHeightSampler == null) return false;
            try
            {
                seaHeightInitMethod.Invoke(seaHeightSampler, new object[] {
                    point, 0.2f, false, null });
                var arguments = new object[] { 0f };
                if (!(seaHeightSampleMethod.Invoke(seaHeightSampler, arguments) is bool sampled) ||
                    !sampled || !(arguments[0] is float value) || !ShoreProbe.IsFinite(value))
                    return false;
                height = value;
                return true;
            }
            catch (Exception exception)
            {
                Plugin.Instance?.Warn("Native ocean height sample failed: " + exception.Message);
                seaHeightSampleMethod = null;
                return false;
            }
        }
    }
}
