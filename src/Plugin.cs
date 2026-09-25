using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    [BepInPlugin(Id, "New Beginnings", "1.0.1")]
    [BepInDependency(ScrambledSeasIntegration.Id, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.nandbrew.sailwinddifficulty", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "com.skeptic043.sailwind.newbeginnings";
        private Harmony harmony;
        private SelectionConfig selection;
        private readonly System.Random random = new System.Random();
        private Transform temporaryStart;
        private StartMenu trackedStartMenu;
        private Transform startObserver;
        private Transform startController;
        private Transform docksidePlayerObserver;
        private Transform docksidePlayerController;
        private Vector3 lastStartTargetPosition;
        private Vector3 lastStartControllerPosition;
        private float startTrackingAt = -1f;
        private float startPlayingAt = -1f;
        private int settleGeneration;
        private ProbeVelocityGuard activeProbeGuard;
        private Collider docksideSurfaceCollider;
        private Transform docksideMarker;
        private Transform alternateDocksideAnchor;
        private Transform docksideFrontMooring;
        private Transform docksideBackMooring;
        private Vector3 docksideBerthLocal;
        private Vector3 docksideSurfaceLocal;
        private bool hasDocksideSurfacePoint;
        private Collider cargoSurfaceCollider;
        private Vector3 cargoSurfaceLocal;
        private bool hasCargoSurfacePoint;
        private float nextCargoSurfaceProbe;
        internal string CargoSurfaceStatus { get; private set; }
        private float nextDocksideSurfaceProbe;
        private float distantSurfaceSeenAt = -1f;
        private IslandHorizon docksideIsland;
        private bool searchOasisPierExtension;
        private bool oasisPierExtensionReported;
        private int sceneryLoadedFrame = -1;
        private readonly List<Transform> boatWalkRoots = new List<Transform>();
        private int boatWalkRootsFrame = -1;
        private object seaHeightSampler;
        private MethodInfo seaHeightInitMethod;
        private MethodInfo seaHeightSampleMethod;
        private bool seaHeightInitialized;
        private bool seaFallbackReported;
        private bool seaBoundInitialized;
        private PropertyInfo oceanInstanceProperty;
        private PropertyInfo oceanSeaLevelProperty;
        private PropertyInfo oceanMaxDisplacementProperty;
        private string lastSurfaceProbeSummary;
        private bool playerControlHeld;
        private Transform heldControlTarget;
        private float nextControlRestoreRetry;
        private float unresolvedPlayingAt = -1f;
        private static readonly FieldInfo WaitingForFInputField =
            AccessTools.Field(typeof(StartMenu), "waitingForFInput");
        private static readonly string[] FurniturePrefixes =
            { "bench", "chair", "stool", "table", "desk", "counter", "shelf", "cabinet" };

        private sealed class SurfaceProbeSummary
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

        internal static Plugin Instance { get; private set; }
        internal SelectionSettings Settings => selection.Settings;
        internal SelectionOverrides Overrides => selection.Overrides;

        private void Awake()
        {
            Instance = this;
            try
            {
                selection = new SelectionConfig(Config);
                harmony = new Harmony(Id);
                harmony.PatchAll(typeof(Plugin).Assembly);
                ScrambledSeasIntegration.Initialize(harmony);
                Logger.LogInfo("New Beginnings loaded with port and boat selection.");
            }
            catch (Exception exception)
            {
                // PatchAll may have installed earlier patches before a changed
                // Sailwind method causes a later one to fail. Leave the native
                // start path alone instead of running with an incomplete set.
                Instance = null;
                enabled = false;
                try { harmony?.UnpatchSelf(); }
                catch (Exception unpatchException)
                {
                    Logger.LogError("New Beginnings could not remove its partial patches: " +
                        unpatchException);
                }
                Logger.LogError("New Beginnings disabled after initialization failed; " +
                    "check game/mod compatibility and configuration: " + exception);
            }
        }

        [HarmonyPatch(typeof(StartMenu), "Awake")]
        [HarmonyAfter("com.nandbrew.scrambledseas")]
        private static class MenuPatch
        {
            private static void Postfix(StartMenu __instance)
            {
                var plugin = Instance;
                if (plugin == null) return;
                // An unknown Scrambled Seas build keeps its own and Sailwind's
                // menu controls, matching the untouched startup fallback.
                if (!ScrambledSeasIntegration.CanSelect) return;
                try
                {
                    if (MenuUi.Attach(__instance, plugin.Settings, plugin.Overrides,
                        plugin.selection.IndividualExclusions,
                        plugin.SaveSelection) == null)
                        plugin.Warn("Start menu controls could not be attached; saved settings remain available.");
                }
                catch (Exception exception)
                {
                    plugin.Error("Start menu controls could not be built; saved settings remain available.", exception);
                }
            }
        }

        private void SaveSelection() => selection.Save();

        internal bool TryChooseStart(out StartPair pair, out string reason)
        {
            pair = null;
            reason = null;
            selection.RefreshOverrides();
            var catalog = SelectionCatalog.Discover(selection.Overrides);
            if (catalog.TryChoose(selection.Settings,
                selection.IndividualExclusions, random, out pair, out reason))
                return true;
            if (catalog.Rejections.Count > 0)
                reason += " " + catalog.Rejections[0];
            return false;
        }

        private void Update()
        {
            if (docksideMarker != null && GameState.currentlyLoading)
            {
                // A cargo anchor can outlive the temporary player target.
                // Loading any save must invalidate both lifetimes before the
                // old scene and its recovery marker disappear.
                CancelPlayerStartTracking();
                ClearDocksideSurface();
            }
            if (playerControlHeld && temporaryStart == null &&
                Time.realtimeSinceStartup >= nextControlRestoreRetry)
            {
                nextControlRestoreRetry = Time.realtimeSinceStartup + 2f;
                ReleasePlayerControl();
            }
            UpdatePlayerStartTracking();
            StarterCargo.Tick();
        }

        private void UpdatePlayerStartTracking()
        {
            if (temporaryStart == null || startObserver == null || startController == null)
            {
                // Scene unloading can destroy the marker before the actor. Do
                // not leave its temporary embark suppression alive afterward.
                if (startTrackingAt >= 0f || startObserver != null || startController != null)
                    CancelPlayerStartTracking();
                return;
            }
            if (startPlayingAt >= 0f && GameState.currentBoat != null)
            {
                // Gameplay has moved the controller into a boat's walk scene.
                // Do not apply any later world-space arrival correction there.
                CancelPlayerStartTracking();
                return;
            }
            if (!GameState.playing || startPlayingAt < 0f)
                LiftPlayerTargetToNearbySurface();
            if (GameState.playing && startPlayingAt < 0f &&
                docksideSurfaceCollider != null &&
                !TryGetDocksideSurface(out _, out _))
            {
                docksideSurfaceCollider = null;
                if (docksideMarker != null)
                    temporaryStart.position = docksideMarker.position + Vector3.up * 1.05f;
            }
            if (startPlayingAt >= 0f && docksideSurfaceCollider == null)
            {
                Warn("The selected dock/shore support disappeared during new-game handoff; " +
                    "player tracking stopped and starter supplies will be rechecked.");
                CancelPlayerStartTracking();
                return;
            }
            if (!GameState.playing && startTrackingAt >= 0f &&
                Time.realtimeSinceStartup - startTrackingAt > 90f &&
                !WaitingForPlayerInput())
            {
                Warn("Player start did not reach Sailwind's F prompt or gameplay within " +
                    "90 seconds; dock tracking stopped. " +
                    (lastSurfaceProbeSummary ?? "No physics probe completed."));
                CancelPlayerStartTracking();
                return;
            }
            var targetPosition = temporaryStart.position;
            if (!IsFinite(targetPosition) || !IsFinite(temporaryStart.rotation))
            {
                Warn("Selected player target became non-finite during new-game setup; player tracking stopped.");
                CancelPlayerStartTracking();
                return;
            }
            if (!GameState.playing || startPlayingAt < 0f)
            {
                // The island/scene origin can move between the coroutine factory,
                // camera tween seam and the moment the controller becomes playable.
                // Re-evaluate the live recovery marker through that transition.
                if (!GameState.playing)
                {
                    if (!SnapStartActors()) return;
                }
                else if (docksideSurfaceCollider == null)
                {
                    // Sailwind enables movement before the F prompt. Hold it
                    // briefly while the selected scenery catches up, without
                    // fighting an active controller every Update. If no site
                    // appears, return control at Sailwind's recovery marker.
                    if (unresolvedPlayingAt < 0f)
                    {
                        unresolvedPlayingAt = Time.realtimeSinceStartup;
                        if (!SnapStartActors()) return;
                        heldControlTarget = startController;
                        playerControlHeld = true;
                        try
                        {
                            Refs.SetPlayerControl(false);
                        }
                        catch (Exception exception)
                        {
                            Warn("Could not delay player control for scene loading: " +
                                exception.Message);
                            CancelPlayerStartTracking();
                            return;
                        }
                    }
                    else if ((targetPosition - lastStartTargetPosition).sqrMagnitude > 0.04f)
                    {
                        if (!SnapStartActors()) return;
                    }
                    if (Time.realtimeSinceStartup - unresolvedPlayingAt >= 5f)
                    {
                        Warn($"No dry dock/shore resolved near {docksideMarker?.name} " +
                            $"at {(docksideMarker != null ? docksideMarker.position.ToString() : "unknown")}; " +
                            (lastSurfaceProbeSummary ?? "no physics probe completed") +
                            ". Player control restored at the native recovery marker; starter supplies will be rechecked nearby.");
                        // An earlier pre-handoff candidate may have vanished.
                        // The actor remains at the marker on this timeout, so
                        // cargo must not reuse that abandoned distant site.
                        hasDocksideSurfacePoint = false;
                        docksideSurfaceLocal = Vector3.zero;
                        ClearDocksideCargoSurface();
                        CancelPlayerStartTracking();
                        return;
                    }
                    lastStartTargetPosition = targetPosition;
                    lastStartControllerPosition = startController.position;
                    return;
                }
                else
                {
                    if (!SnapStartActors()) return;
                    PlayerStartEmbarkGuard.Release();
                    ReleasePlayerControl();
                    unresolvedPlayingAt = -1f;
                    startPlayingAt = Time.realtimeSinceStartup;
                    Report($"Player start finalized on loaded dock/shore at {targetPosition}.");
                    // Freeze this chosen point. Only its parent's common
                    // world displacement may be followed during the existing
                    // brief handoff; never choose another surface after control.
                }
            }
            else if (Time.realtimeSinceStartup - startPlayingAt < 2f)
            {
                // A late floating-origin move should carry the new player with
                // the dock without overriding their own movement after play starts.
                var displacement = targetPosition - lastStartTargetPosition;
                var controllerMovement = startController.position - lastStartControllerPosition;
                if (controllerMovement.sqrMagnitude > 0.25f &&
                    (controllerMovement - displacement).sqrMagnitude > 0.25f)
                {
                    // Gameplay, gravity or another system has taken control of
                    // the actor. Stop correction instead of dragging them back.
                    CancelPlayerStartTracking();
                    return;
                }
                if ((displacement.sqrMagnitude > 0.25f ||
                    Mathf.Abs(displacement.y) > 0.05f) &&
                    ((controllerMovement - displacement).sqrMagnitude > 0.25f ||
                    Mathf.Abs(controllerMovement.y - displacement.y) > 0.05f))
                {
                    startController.position += displacement;
                    startObserver.position += displacement;
                    Physics.SyncTransforms();
                    Report($"Player start followed a late dock-marker shift by {displacement}.");
                }
            }
            else
            {
                CancelPlayerStartTracking();
                return;
            }
            lastStartTargetPosition = targetPosition;
            lastStartControllerPosition = startController.position;
        }

        private bool SnapStartActors()
        {
            if (temporaryStart == null || startObserver == null || startController == null) return false;
            try { PlayerStartEmbarkGuard.EnsureWorldSpace(); }
            catch (Exception exception)
            {
                Warn("Selected shore start could not restore native player coordinates: " + exception.Message);
                CancelPlayerStartTracking();
                return false;
            }
            startController.SetPositionAndRotation(temporaryStart.position, temporaryStart.rotation);
            startObserver.SetPositionAndRotation(temporaryStart.position, temporaryStart.rotation);
            Physics.SyncTransforms();
            return true;
        }

        internal bool WaitingForPlayerInput()
        {
            try
            {
                return trackedStartMenu != null && WaitingForFInputField != null &&
                    WaitingForFInputField.GetValue(trackedStartMenu) is bool waiting && waiting;
            }
            catch { return false; }
        }

        private void LiftPlayerTargetToNearbySurface()
        {
            // The recovery marker is an X/Z hint, not a reliable ground height.
            // Modded wharves can stand several metres above it. Resolve the
            // loaded collider top near the marker after world movement, keeping
            // the target parented so later origin shifts carry it along.
            if (Time.realtimeSinceStartup < nextDocksideSurfaceProbe) return;
            nextDocksideSurfaceProbe = Time.realtimeSinceStartup + 0.2f;
            var dockMarker = temporaryStart.parent;
            if (dockMarker == null) return;
            Physics.SyncTransforms();
            var center = dockMarker.position;
            var probeSummary = new SurfaceProbeSummary();
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
            if (docksideFrontMooring != null && docksideBackMooring != null)
            {
                var midpoint = (docksideFrontMooring.position +
                    docksideBackMooring.position) * 0.5f;
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
            if (docksideMarker != null)
            {
                var landward = Vector3.ProjectOnPlane(center - docksideMarker.TransformPoint(docksideBerthLocal),
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
            var best = FindClosestSurface(samples, center, sample =>
            {
                probeSummary.Samples++;
                return ProbeDocksideSurface(sample, out var point, out var surface, probeSummary)
                    ? Tuple.Create(point, surface) : null;
            }, IsTerrainCollider);
            if (searchOasisPierExtension && docksideFrontMooring != null &&
                docksideBackMooring != null && (best == null ||
                Vector3.ProjectOnPlane(best.Item1 - center, Vector3.up).sqrMagnitude > 64f))
            {
                // Oasis's narrow pier continues along its mooring line. The
                // ordinary six-metre rings and inland fallback miss those dock
                // sections when the near berth is obstructed. Keep an already
                // safe nearby arrival, and validate every extra candidate with
                // exactly the same live scene, dry-ground and clearance gates.
                var previousBest = best;
                var extension = new List<Vector3>(OasisPierExtensionSamples(center,
                    docksideFrontMooring.position, docksideBackMooring.position,
                    docksideMarker.TransformPoint(docksideBerthLocal)));
                if (previousBest != null) extension.Add(previousBest.Item1);
                best = FindClosestSurface(extension, center, sample =>
                {
                    if (previousBest != null && sample == previousBest.Item1) return previousBest;
                    probeSummary.Samples++;
                    return ProbeDocksideSurface(sample, out var point, out var surface, probeSummary)
                        ? Tuple.Create(point, surface) : null;
                }, IsTerrainCollider);
                if (!oasisPierExtensionReported && best != null &&
                    (previousBest == null || best.Item1 != previousBest.Item1))
                {
                    oasisPierExtensionReported = true;
                    Report($"Oasis dock extension offers validated ground {Vector3.ProjectOnPlane(best.Item1 - center, Vector3.up).magnitude:F1}m from the recovery marker; nearby berth candidates were unavailable.");
                }
            }
            var bestPoint = best == null ? Vector3.zero : best.Item1;
            var bestSurface = best == null ? null : best.Item2;
            lastSurfaceProbeSummary = probeSummary.ToString();
            if (bestSurface == null)
            {
                docksideSurfaceCollider = null;
                temporaryStart.position = dockMarker.position + Vector3.up * 1.05f;
                return;
            }
            var horizontalDistanceToBest = new Vector2(bestPoint.x - center.x,
                bestPoint.z - center.z).magnitude;
            var sceneryState = "";
            if (horizontalDistanceToBest > 8f)
            {
                var sceneryReady = IsSelectedSceneryReady(out sceneryState);
                if (distantSurfaceSeenAt < 0f)
                {
                    distantSurfaceSeenAt = Time.realtimeSinceStartup;
                    Report($"Distant shore candidate {horizontalDistanceToBest:F1}m from the dock; waiting within the existing player hold for nearby scenery ({sceneryState}). " + lastSurfaceProbeSummary);
                }
                // Stay inside the existing five-second player hold. A distant
                // fallback is re-probed each scan, never held as a stale hit.
                if (ShouldWaitForNearbySurface(horizontalDistanceToBest,
                    Time.realtimeSinceStartup - distantSurfaceSeenAt,
                    unresolvedPlayingAt < 0f ? -1f : Time.realtimeSinceStartup - unresolvedPlayingAt,
                    sceneryReady))
                {
                    docksideSurfaceCollider = null;
                    temporaryStart.position = dockMarker.position + Vector3.up * 1.05f;
                    return;
                }
            }
            else distantSurfaceSeenAt = -1f;
            var destination = bestPoint + Vector3.up * 1.1f;
            if (docksideSurfaceCollider == bestSurface &&
                (temporaryStart.position - destination).sqrMagnitude < 0.01f)
                return;
            if (horizontalDistanceToBest > 8f)
                Report($"Distant shore fallback selected at {horizontalDistanceToBest:F1}m ({sceneryState}); " + lastSurfaceProbeSummary);
            temporaryStart.position = destination;
            docksideSurfaceCollider = bestSurface;
            docksideSurfaceLocal = dockMarker.InverseTransformPoint(bestPoint);
            hasDocksideSurfacePoint = true;
            Report($"Player dockside surface resolved on {TransformPath(bestSurface.transform)} ({bestSurface.GetType().Name}) at {bestPoint}; target {destination}, marker {center}.");
        }

        internal bool TryGetDocksideSurface(out Vector3 surface, out Collider collider)
            => TryGetDocksideSurface(true, out surface, out collider);

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

        internal bool TryGetDocksideCargoSurface(out Vector3 surface, out Collider collider)
        {
            surface = Vector3.zero;
            collider = null;
            if (docksideMarker == null)
            {
                CargoSurfaceStatus = "the selected shore anchor is unavailable";
                return false;
            }
            // Before player handoff, never send cargo to a different site from
            // the one still being chosen for the player.
            if (temporaryStart != null)
            {
                if (!TryGetDocksideSurface(false, out surface, out collider))
                {
                    CargoSurfaceStatus = "waiting for the player's initial dock/shore selection";
                    return false;
                }
                RememberCargoSurface(surface, collider);
                return true;
            }
            var reference = docksideMarker.TransformPoint(hasCargoSurfacePoint
                ? cargoSurfaceLocal : hasDocksideSurfacePoint ? docksideSurfaceLocal : Vector3.zero);
            // Re-probe the point, not the old Collider identity. A loaded scene
            // may replace its support while preserving the same usable ground.
            if ((hasCargoSurfacePoint || hasDocksideSurfacePoint) &&
                ProbeDocksideSurface(reference, out surface, out collider, null, false))
            {
                RememberCargoSurface(surface, collider);
                return true;
            }
            if (Time.realtimeSinceStartup < nextCargoSurfaceProbe) return false;
            nextCargoSurfaceProbe = Time.realtimeSinceStartup + 0.25f;
            var summary = new SurfaceProbeSummary();
            var searchOrigin = hasDocksideSurfacePoint
                ? docksideMarker.TransformPoint(docksideSurfaceLocal) : docksideMarker.position;
            var candidates = NearbySurfaceSamples(searchOrigin, 6f);
            var found = FindClosestSurface(candidates, searchOrigin, sample =>
            {
                summary.Samples++;
                return ProbeDocksideSurface(sample, out var point, out var support, summary, false)
                    ? Tuple.Create(point, support) : null;
            }, IsTerrainCollider);
            if (found == null)
            {
                CargoSurfaceStatus = "no loaded safe cargo ground near the last player site (" + summary + ")";
                return false;
            }
            surface = found.Item1;
            collider = found.Item2;
            RememberCargoSurface(surface, collider);
            Report($"Cargo dock/shore support reacquired on {TransformPath(collider.transform)} at {surface}; player position unchanged.");
            return true;
        }

        private void RememberCargoSurface(Vector3 surface, Collider collider)
        {
            if (hasCargoSurfacePoint && cargoSurfaceCollider != collider)
                Report($"Cargo support changed to {TransformPath(collider.transform)} at {surface}; player position unchanged.");
            cargoSurfaceCollider = collider;
            cargoSurfaceLocal = docksideMarker.InverseTransformPoint(surface);
            hasCargoSurfacePoint = true;
            CargoSurfaceStatus = "cargo ground ready on " + collider.name;
        }

        internal void ClearDocksideCargoSurface()
        {
            cargoSurfaceCollider = null;
            cargoSurfaceLocal = Vector3.zero;
            hasCargoSurfacePoint = false;
            nextCargoSurfaceProbe = 0f;
            CargoSurfaceStatus = "waiting for loaded dockside ground";
        }

        // Shared pure selection math; the caller supplies the same validated
        // live-physics probe used elsewhere. This never writes actor transforms.
        private static Tuple<Vector3, T> FindClosestSurface<T>(IEnumerable<Vector3> samples,
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

        private bool IsSelectedSceneryReady(out string state)
        {
            if (docksideIsland == null)
            {
                state = "island scenery identity unavailable";
                return false;
            }
            // IslandHorizon.SceneLoaded flips before LoadSceneAsync finishes.
            // Follow its native RegisterLoadingFinished check, then allow the
            // scenery root's first Update to align with its live island.
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByBuildIndex(docksideIsland.islandIndex);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                sceneryLoadedFrame = -1;
                state = "selected scenery scene " + docksideIsland.islandIndex + " not loaded";
                return false;
            }
            if (sceneryLoadedFrame < 0) sceneryLoadedFrame = Time.frameCount;
            var ready = Time.frameCount > sceneryLoadedFrame;
            state = ready ? "selected scenery loaded" : "waiting for loaded scenery alignment";
            return ready;
        }

        private static bool ShouldWaitForNearbySurface(float distance, float distantAge, float unresolvedAge,
            bool sceneryReady) =>
            distance > 8f && (!sceneryReady || distantAge < 1f) &&
                (unresolvedAge < 0f || unresolvedAge < 4.5f);

        private static List<Vector3> NearbySurfaceSamples(Vector3 center, float maximumRadius)
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

        private bool TryGetDocksideSurface(bool requirePlayerClearance,
            out Vector3 surface, out Collider collider)
        {
            surface = Vector3.zero;
            collider = null;
            // Cargo can finish after the two-second player tracking window.
            // Keep a local-space ground point on the recovery marker through
            // that bounded startup operation, even when the temporary player
            // target is destroyed.
            if (docksideMarker == null || !IsLoadedDocksideCollider(docksideSurfaceCollider))
                return false;
            surface = docksideMarker.TransformPoint(docksideSurfaceLocal);
            if (!IsFinite(surface) ||
                !ProbeDocksideSurface(surface, out var livePoint, out var liveCollider,
                    null, requirePlayerClearance) ||
                liveCollider != docksideSurfaceCollider ||
                Mathf.Abs(livePoint.y - surface.y) > 0.35f)
                return false;
            surface = livePoint;
            collider = liveCollider;
            return true;
        }

        internal bool TryProbeDocksideSurface(Vector3 horizontalSample, out Vector3 surface,
            out Collider collider)
            => ProbeDocksideSurface(horizontalSample, out surface, out collider, null);

        internal bool TryProbeDocksideCargoSurface(Vector3 horizontalSample,
            out Vector3 surface, out Collider collider)
            => ProbeDocksideSurface(horizontalSample, out surface, out collider, null, false);

        private bool ProbeDocksideSurface(Vector3 horizontalSample, out Vector3 surface,
            out Collider collider, SurfaceProbeSummary summary, bool requirePlayerClearance = true)
        {
            surface = Vector3.zero;
            collider = null;
            var marker = docksideMarker;
            if (marker == null || !IsFinite(horizontalSample)) return false;
            // Main-scene terrain can be present before the selected island's
            // buildings and docks. Even a nearby candidate must wait for their
            // collision geometry before it can be judged safe for player or cargo.
            if (!IsSelectedSceneryReady(out var sceneryState))
            {
                if (summary != null && summary.Example == null)
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
                if (summary != null) summary.Hits++;
                var candidate = hit.collider;
                if (candidate != null && IsMooringFixture(candidate))
                {
                    if (summary != null)
                    {
                        summary.MooringFixture++;
                        if (summary.Example == null)
                            summary.Example = TransformPath(candidate.transform) +
                                $" at {hit.point} (mooring fixture, not dock ground)";
                    }
                    continue;
                }
                if (candidate == null || !IsStaticDocksideCollider(candidate))
                {
                    if (summary != null)
                    {
                        summary.BoatOrItem++;
                        if (candidate != null && (summary.Example == null ||
                            string.Equals(candidate.name, "hull player collider",
                                StringComparison.OrdinalIgnoreCase)))
                            summary.Example = $"{candidate.name} layer {candidate.gameObject.layer}, " +
                                $"tag {candidate.gameObject.tag} at {hit.point}";
                    }
                    continue;
                }
                // Roofs, building interiors and furniture can be static and
                // level without being safe outdoor arrival ground. Keep them
                // static for clearance and occlusion, but never use their tops.
                if (IsUnsuitableStartSurface(candidate))
                {
                    if (summary != null)
                    {
                        summary.StructureOrFurniture++;
                        if (summary.Example == null)
                            summary.Example = TransformPath(candidate.transform) +
                                $" at {hit.point} (structure/furniture, not dock ground)";
                    }
                    continue;
                }
                if (hit.normal.y < 0.75f)
                {
                    if (summary != null) summary.Steep++;
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
                    if (summary != null)
                    {
                        summary.Height++;
                        if (summary.Example == null)
                            summary.Example = TransformPath(candidate.transform) +
                                $" at {hit.point} below marker {marker.position.y:F1}";
                    }
                    continue;
                }
                if (IsOccludedGround(hit, hits))
                {
                    if (summary != null)
                    {
                        summary.Height++;
                        if (summary.Example == null)
                            summary.Example = TransformPath(candidate.transform) +
                                $" at {hit.point} beneath an overhead collider";
                    }
                    continue;
                }
                if (TrySeaHeight(hit.point, out var seaHeight))
                {
                    if (hit.point.y < seaHeight + 0.35f)
                    {
                        if (summary != null) summary.Underwater++;
                        continue;
                    }
                }
                else if (!IsAboveMooringFallback(candidate, hit.point,
                    lowerMooringDock && hit.point.y < marker.position.y - 2f))
                {
                    if (summary != null) summary.Underwater++;
                    continue;
                }
                else if (!seaFallbackReported)
                {
                    seaFallbackReported = true;
                    Warn("Ocean height has not sampled yet; requiring dry-looking dock or shore " +
                        "ground above this port's live moorings for the new-game arrival.");
                }
                // Cargo checks its own volume and item embark triggers. Requiring
                // a human-sized capsule at every cargo sample rejects usable dock
                // beside nearby hulls, even when the native items fit safely.
                if (requirePlayerClearance && !HasPlayerClearance(hit.point, candidate,
                    out var clearanceReason))
                {
                    if (summary != null)
                    {
                        summary.Clearance++;
                        if (summary.ClearanceExample == null)
                            summary.ClearanceExample = TransformPath(candidate.transform) +
                                $" at {hit.point}: {clearanceReason}";
                        if (summary.Example == null)
                            summary.Example = TransformPath(candidate.transform) +
                                $" at {hit.point}";
                    }
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
            if (docksideMarker == null) return false;
            var inland = Vector3.ProjectOnPlane(docksideMarker.position -
                docksideMarker.TransformPoint(docksideBerthLocal), Vector3.up);
            if (inland.sqrMagnitude < 1f) return false;
            var offset = Vector3.ProjectOnPlane(sample - docksideMarker.position, Vector3.up);
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
            if (docksideMarker == null) return false;
            if (docksideFrontMooring == null || docksideBackMooring == null) return false;
            return IsLowerMooringDockPoint(docksideMarker.position, docksideFrontMooring.position,
                docksideBackMooring.position, sample, point);
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
            if (docksideMarker == null) return false;
            if (docksideFrontMooring == null || docksideBackMooring == null) return false;
            // Crest can fail to sample while the new-game scene settles. Native
            // berth moorings move with Scrambled Seas and provide a local height
            // reference. Shore terrain must rise above them; a dock collider may
            // be slightly lower because its top is below the rope fixture.
            var mooringHeight = Mathf.Min(docksideFrontMooring.position.y,
                docksideBackMooring.position.y);
            var terrain = IsTerrainCollider(candidate);
            var minimum = mooringHeight + (terrain ? 0.35f : -1f);
            if (terrain || newlyAdmittedLowDock)
            {
                // A mooring alone cannot bound a wave crest. Terrain and newly
                // admitted low berth surfaces need the live ocean renderer's
                // displacement bound while precise sampling is unavailable.
                if (!TrySeaUpperBound(out var upperBound)) return false;
                minimum = Mathf.Max(minimum, upperBound + 0.35f);
            }
            return point.y >= minimum &&
                point.y <= docksideMarker.position.y + (terrain ? 6f : 3f);
        }

        private bool TrySeaUpperBound(out float upperBound)
        {
            upperBound = 0f;
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
            if (oceanInstanceProperty == null || oceanSeaLevelProperty == null ||
                oceanMaxDisplacementProperty == null) return false;
            try
            {
                var ocean = oceanInstanceProperty.GetValue(null, null);
                if (ocean == null ||
                    !(oceanSeaLevelProperty.GetValue(ocean, null) is float seaLevel) ||
                    !(oceanMaxDisplacementProperty.GetValue(ocean, null) is float maxVertical) ||
                    !IsFinite(seaLevel) || !IsFinite(maxVertical) || maxVertical < 0f)
                    return false;
                // Wave providers may not have reported displacement on the
                // first startup frame. Keep a metre of clearance in that case.
                upperBound = seaLevel + Mathf.Max(1f, maxVertical);
                return IsFinite(upperBound);
            }
            catch { return false; }
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

        private bool TrySeaHeight(Vector3 point, out float height)
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
                    Warn("Native ocean height sampler could not initialize: " + exception.Message);
                }
                if (seaHeightInitMethod == null || seaHeightSampleMethod == null ||
                    seaHeightSampler == null)
                    Warn("Native ocean height sampler is unavailable; waiting for a verifiable dry start.");
            }
            if (seaHeightInitMethod == null || seaHeightSampleMethod == null ||
                seaHeightSampler == null) return false;
            try
            {
                seaHeightInitMethod.Invoke(seaHeightSampler, new object[] {
                    point, 0.2f, false, null });
                var arguments = new object[] { 0f };
                if (!(seaHeightSampleMethod.Invoke(seaHeightSampler, arguments) is bool sampled) ||
                    !sampled || !(arguments[0] is float value) || !IsFinite(value))
                    return false;
                height = value;
                return true;
            }
            catch (Exception exception)
            {
                Warn("Native ocean height sample failed: " + exception.Message);
                seaHeightSampleMethod = null;
                return false;
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
            foreach (var walkRoot in boatWalkRoots)
                if (walkRoot != null && (transform == walkRoot || transform.IsChildOf(walkRoot)))
                    return false;
            if (transform.GetComponentInParent<BoatMooringRopes>() != null ||
                transform.GetComponentInParent<BoatRefs>() != null ||
                transform.GetComponentInParent<ShipItem>() != null ||
                transform.GetComponentInParent<ItemRigidbody>() != null) return false;
            // Cargo still probes after temporary player tracking ends. Keep
            // the actual actor roots excluded for the whole shore-anchor lifetime.
            return !IsPlayerHierarchy(transform, docksidePlayerController, docksidePlayerObserver);
        }

        private static bool IsPlayerHierarchy(Transform candidate, Transform controller,
            Transform observer) => candidate != null &&
            (controller != null && (candidate == controller || candidate.IsChildOf(controller)) ||
                observer != null && (candidate == observer || candidate.IsChildOf(observer)));

        private bool IsLoadedDocksideCollider(Collider collider) =>
            collider != null && collider.enabled && collider.gameObject.activeInHierarchy &&
            collider.gameObject.scene.IsValid() && collider.gameObject.scene.isLoaded &&
            IsStaticDocksideCollider(collider);

        private void RefreshBoatWalkRoots()
        {
            if (boatWalkRootsFrame == Time.frameCount) return;
            boatWalkRootsFrame = Time.frameCount;
            boatWalkRoots.Clear();
            // Sailwind keeps boat walk colliders under a separate scene root.
            // Ancestry from the hull collider to BoatRefs therefore misses it.
            foreach (var refs in Resources.FindObjectsOfTypeAll<BoatRefs>())
                if (refs != null && refs.walkCol != null)
                    boatWalkRoots.Add(refs.walkCol);
            foreach (var embark in Resources.FindObjectsOfTypeAll<BoatEmbarkCollider>())
                if (embark != null && embark.walkCollider != null)
                    boatWalkRoots.Add(embark.walkCollider);
        }

        // Terrain physics lives in an optional Unity module not needed by the
        // rest of this mod; inspect the installed collider's concrete name.
        private static bool IsTerrainCollider(Collider collider) =>
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

        private static string TransformPath(Transform transform)
        {
            var names = new List<string>();
            for (var current = transform; current != null; current = current.parent)
                names.Add(current.name);
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        internal Transform MakeTemporaryStart(ResolvedStart selected, Quaternion rotation,
            Vector3? shorePosition, Vector3 berthPosition)
        {
            var dockMarker = selected.Recovery.transform;
            if (dockMarker == null) throw new ArgumentNullException(nameof(dockMarker));
            if (temporaryStart != null) Destroy(temporaryStart.gameObject);
            ClearDocksideSurface();
            if (shorePosition.HasValue)
            {
                // A private anchor follows the real recovery object's world shifts.
                // Never alter the native recovery marker or its mooring references.
                alternateDocksideAnchor = new GameObject("New Beginnings alternate shore anchor").transform;
                alternateDocksideAnchor.SetParent(dockMarker, false);
                alternateDocksideAnchor.SetPositionAndRotation(shorePosition.Value, rotation);
                dockMarker = alternateDocksideAnchor;
            }
            var marker = new GameObject("New Beginnings temporary player target");
            marker.transform.SetParent(dockMarker, false);
            // The recovery root is the dock-side marker. Its boatPos child is
            // at hull height, often several metres lower than the pier. The
            // port component can itself be buried while the island is hidden.
            marker.transform.SetPositionAndRotation(
                dockMarker.position + Vector3.up * 1.05f, rotation);
            temporaryStart = marker.transform;
            docksideMarker = dockMarker;
            docksideIsland = selected.Port.GetComponentInParent<IslandHorizon>();
            searchOasisPierExtension = selected.Port.portIndex == 6 &&
                string.Equals(selected.Port.gameObject.name, "port A/M 6 (Oasis)", StringComparison.Ordinal);
            docksideFrontMooring = selected.FrontMooring.transform;
            docksideBackMooring = selected.BackMooring.transform;
            // Preserve the native probing direction outside alternate berths.
            docksideBerthLocal = dockMarker.InverseTransformPoint(shorePosition.HasValue
                ? berthPosition : selected.Recovery.boatPos.position);
            docksideSurfaceCollider = null;
            nextDocksideSurfaceProbe = 0f;
            unresolvedPlayingAt = -1f;
            lastSurfaceProbeSummary = null;
            seaFallbackReported = false;
            return temporaryStart;
        }

        internal void BeginPlayerStartTracking(StartMenu menu, Transform target)
        {
            if (target == null || !FixedStart.TryGetNativeStartActors(menu, out var observer,
                out var controller))
                throw new InvalidOperationException(
                    "Player start actors were unavailable for dock-marker tracking.");
            startObserver = observer;
            startController = controller.transform;
            docksidePlayerObserver = startObserver;
            docksidePlayerController = startController;
            PlayerStartEmbarkGuard.Begin(startObserver, startController);
            trackedStartMenu = menu;
            lastStartTargetPosition = target.position;
            lastStartControllerPosition = startController.position;
            startTrackingAt = Time.realtimeSinceStartup;
            startPlayingAt = -1f;
            Report($"Player start target anchored to {target.parent.name} at {target.position}; observer {observer.position}, controller {startController.position}.");
        }

        internal void CancelPlayerStartTracking()
        {
            PlayerStartEmbarkGuard.Release();
            ReleasePlayerControl();
            startObserver = null;
            startController = null;
            trackedStartMenu = null;
            startTrackingAt = -1f;
            startPlayingAt = -1f;
            unresolvedPlayingAt = -1f;
            if (temporaryStart == null) return;
            Destroy(temporaryStart.gameObject);
            temporaryStart = null;
        }

        private void ReleasePlayerControl()
        {
            if (!playerControlHeld) return;
            try
            {
                Refs.SetPlayerControl(true);
                playerControlHeld = false;
                heldControlTarget = null;
                return;
            }
            catch (Exception exception)
            {
                Warn("Native player control restore failed; enabling its components directly: " +
                    exception.Message);
            }
            try
            {
                var controller = heldControlTarget != null ? heldControlTarget : startController;
                if (controller == null)
                {
                    playerControlHeld = false;
                    heldControlTarget = null;
                    return;
                }
                var character = controller.GetComponent<CharacterController>();
                var movement = controller.GetComponent("OVRPlayerController") as Behaviour;
                if (character != null) character.enabled = true;
                if (movement != null) movement.enabled = true;
                if (character != null && movement != null && character.enabled &&
                    movement.enabled)
                {
                    playerControlHeld = false;
                    heldControlTarget = null;
                }
            }
            catch (Exception exception)
            {
                Warn("Could not directly restore player controller components: " +
                    exception.Message);
            }
            if (playerControlHeld)
                nextControlRestoreRetry = Time.realtimeSinceStartup + 2f;
        }

        internal void ClearDocksideSurface()
        {
            if (alternateDocksideAnchor != null) Destroy(alternateDocksideAnchor.gameObject);
            alternateDocksideAnchor = null;
            docksideMarker = null;
            docksidePlayerObserver = null;
            docksidePlayerController = null;
            docksideFrontMooring = null;
            docksideBackMooring = null;
            docksideBerthLocal = Vector3.zero;
            docksideSurfaceCollider = null;
            docksideSurfaceLocal = Vector3.zero;
            hasDocksideSurfacePoint = false;
            ClearDocksideCargoSurface();
            nextDocksideSurfaceProbe = 0f;
            distantSurfaceSeenAt = -1f;
            docksideIsland = null;
            searchOasisPierExtension = false;
            oasisPierExtensionReported = false;
            sceneryLoadedFrame = -1;
            lastSurfaceProbeSummary = null;
        }

        internal void CancelBoatSettle()
        {
            ++settleGeneration;
            StarterMooring.Cancel();
            StarterBoatRepair.Cancel();
            activeProbeGuard?.Restore();
            activeProbeGuard = null;
        }

        internal void BeginBoatSettle(StartMenu menu, ResolvedStart selected, Vector3 position,
            Quaternion rotation, ProbeVelocityGuard probeGuard)
        {
            // RecoveryPort is moved independently from its island by Scrambled
            // Seas. Retain the berth offset in the recovery marker's frame so
            // later floating-origin shifts cannot pull the boat back to old
            // world coordinates.
            var marker = selected.Recovery.boatPos;
            var berthOffset = marker.InverseTransformPoint(position);
            var berthRotation = Quaternion.Inverse(marker.rotation) * rotation;
            activeProbeGuard?.Restore();
            activeProbeGuard = probeGuard;
            var generation = ++settleGeneration;
            try
            {
                StartCoroutine(SettleBoat(menu, selected, berthOffset, berthRotation,
                    generation, probeGuard));
            }
            catch
            {
                if (ReferenceEquals(activeProbeGuard, probeGuard)) activeProbeGuard = null;
                probeGuard?.Restore();
                throw;
            }
        }

        private IEnumerator SettleBoat(StartMenu menu, ResolvedStart selected, Vector3 berthOffset,
            Quaternion berthRotation, int generation, ProbeVelocityGuard probeGuard)
        {
            var placementCompleted = false;
            try
            {
                // Native recovery gives physics multiple steps before and after
                // final placement. Keep BoatProbes from deriving a travel velocity
                // from the hull's relocation throughout those steps.
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();
                var stillActive = false;
                try
                {
                    stillActive = generation == settleGeneration &&
                        FixedStart.IsSelectionStillActive(menu, selected);
                }
                catch (Exception exception)
                {
                    Warn("Boat settling was cancelled after selection state changed: " + exception.Message);
                }
                if (!stillActive)
                    yield break;
                try
                {
                    var marker = selected.Recovery.boatPos;
                    if (marker == null)
                    {
                        Warn("Boat settling was cancelled because the selected recovery marker disappeared.");
                        yield break;
                    }
                    var position = marker.TransformPoint(berthOffset);
                    var rotation = marker.rotation * berthRotation;
                    if (!IsFinite(position) || !IsFinite(rotation) ||
                        !IsFinite(selected.Port.transform.position) ||
                        temporaryStart != null && !IsFinite(temporaryStart.position))
                    {
                        Warn("Boat settling stopped because a port, berth or player target became non-finite after world movement.");
                        yield break;
                    }
                    Report($"Start settle coordinates: port {selected.Port.transform.position}, berth {position}, hull {selected.Body.position}, player target {(temporaryStart != null ? temporaryStart.position.ToString() : "gone")}.");
                    selected.Saveable.transform.SetPositionAndRotation(position, rotation);
                    selected.Body.position = position;
                    selected.Body.rotation = rotation;
                    selected.Body.velocity = Vector3.zero;
                    selected.Body.angularVelocity = Vector3.zero;
                    Physics.SyncTransforms();
                    if (selected.FrontMooring.spring.connectedBody != selected.Body ||
                        selected.BackMooring.spring.connectedBody != selected.Body)
                        Warn("Boat moved after placement, but one of its dock springs disconnected before ownership.");
                    placementCompleted = true;
                }
                catch (Exception exception)
                {
                    Warn("Boat settling or rope placement needs an in-game check: " + exception);
                }
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();
                if (placementCompleted && generation == settleGeneration)
                {
                    StarterMooring.MarkSettled(selected);
                    StarterBoatRepair.MarkSettled(selected);
                }
            }
            finally
            {
                probeGuard?.Restore();
                if (ReferenceEquals(activeProbeGuard, probeGuard)) activeProbeGuard = null;
            }
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(Quaternion value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        internal void Report(string message) => Logger.LogInfo(message);
        internal void Warn(string message) => Logger.LogWarning(message);
        internal void Error(string message, Exception exception) => Logger.LogError(message + " " + exception);

        private void OnDestroy()
        {
            CancelBoatSettle();
            CancelPlayerStartTracking();
            ClearDocksideSurface();
            StarterCargo.Disarm();
            harmony?.UnpatchSelf();
            if (ReferenceEquals(Instance, this)) Instance = null;
        }
    }
}
