using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NewBeginnings
{
    // Owns the live start site and the player's arrival on it: the temporary
    // player target, snapping the native actors to it during Sailwind's
    // new-game handoff, the brief control hold while selected scenery loads,
    // the follow window after arrival, and the cargo surface memory that
    // StarterCargo places supplies from.
    internal static class StartArrival
    {
        // Snapping keeps both actors on the target until play begins.
        // HoldingControl delays player control, up to five seconds, while no
        // dry surface has resolved. Following carries the arrived player with
        // late dock-marker shifts for two seconds. Idle tracks nothing.
        private enum Phase { Idle, Snapping, HoldingControl, Following }

        private static readonly FieldInfo WaitingForFInputField =
            AccessTools.Field(typeof(StartMenu), "waitingForFInput");

        // The live site. Its frame is the private alternate shore anchor, and
        // the temporary player target is a child of that anchor.
        private static ShoreProbe site;
        private static Transform temporaryStart;
        private static Collider docksideSurfaceCollider;
        private static Vector3 docksideSurfaceLocal;
        private static bool hasDocksideSurfacePoint;
        private static float nextDocksideSurfaceProbe;
        private static float distantSurfaceSeenAt = -1f;
        private static string lastSurfaceProbeSummary;

        private static Phase phase;
        private static StartMenu trackedStartMenu;
        private static Transform startObserver;
        private static Transform startController;
        private static Vector3 lastStartTargetPosition;
        private static Vector3 lastStartControllerPosition;
        private static float trackingStartedAt;
        private static float holdStartedAt;
        private static float followStartedAt;
        private static bool playerControlHeld;

        private static Collider cargoSurfaceCollider;
        private static Vector3 cargoSurfaceLocal;
        private static bool hasCargoSurfacePoint;
        private static float nextCargoSurfaceProbe;
        internal static string CargoSurfaceStatus { get; private set; }

        internal static bool HasSite => site != null && site.Frame != null;

        internal static Transform MakeTarget(ResolvedStart selected, Quaternion rotation,
            Vector3 shorePosition, Vector3 berthPosition)
        {
            var shoreFrame = selected.ShoreFrame;
            if (shoreFrame == null) throw new ArgumentNullException(nameof(shoreFrame));
            if (temporaryStart != null) Object.Destroy(temporaryStart.gameObject);
            ClearSite();
            // A private anchor follows the real recovery object's world shifts.
            // Never alter the native recovery marker or its mooring references.
            var dockMarker = new GameObject("New Beginnings alternate shore anchor").transform;
            dockMarker.SetParent(shoreFrame, false);
            dockMarker.SetPositionAndRotation(shorePosition, rotation);
            var marker = new GameObject("New Beginnings temporary player target");
            marker.transform.SetParent(dockMarker, false);
            // The recovery root is the dock-side marker. Its boatPos child is
            // at hull height, often several metres lower than the pier. The
            // port component can itself be buried while the island is hidden.
            marker.transform.SetPositionAndRotation(
                dockMarker.position + Vector3.up * 1.05f, rotation);
            temporaryStart = marker.transform;
            site = new ShoreProbe(dockMarker, selected.Destination, selected.Island,
                selected.FrontMooring != null ? selected.FrontMooring.transform : null,
                selected.BackMooring != null ? selected.BackMooring.transform : null,
                berthPosition,
                KnownSites.IsOasis(selected.Port),
                -1);
            // A new target starts any pending control hold again.
            if (phase == Phase.HoldingControl) phase = Phase.Snapping;
            return temporaryStart;
        }

        internal static void BeginTracking(StartMenu menu, Transform target)
        {
            if (target == null || !FixedStart.TryGetNativeStartActors(menu, out var observer,
                out var controller))
                throw new InvalidOperationException(
                    "Player start actors were unavailable for dock-marker tracking.");
            startObserver = observer;
            startController = controller.transform;
            phase = Phase.Snapping;
            trackingStartedAt = Time.realtimeSinceStartup;
            site?.ExcludePlayer(startObserver, startController);
            PlayerStartEmbarkGuard.Begin(startObserver, startController);
            trackedStartMenu = menu;
            lastStartTargetPosition = target.position;
            lastStartControllerPosition = startController.position;
            Plugin.Instance?.DebugLog($"Player start target anchored to {target.parent.name} at {target.position}; observer {observer.position}, controller {startController.position}.");
        }

        internal static void CancelTracking()
        {
            PlayerStartEmbarkGuard.Release();
            ReleasePlayerControl();
            startObserver = null;
            startController = null;
            trackedStartMenu = null;
            phase = Phase.Idle;
            if (temporaryStart == null) return;
            Object.Destroy(temporaryStart.gameObject);
            temporaryStart = null;
        }

        private static void ReleasePlayerControl()
        {
            if (!playerControlHeld) return;
            try { Refs.SetPlayerControl(true); }
            catch (Exception exception)
            {
                Plugin.Instance?.Warn("Native player control restore failed: " + exception.Message);
            }
            playerControlHeld = false;
        }

        internal static void ClearSite()
        {
            if (site != null && site.Frame != null) Object.Destroy(site.Frame.gameObject);
            site = null;
            docksideSurfaceCollider = null;
            docksideSurfaceLocal = Vector3.zero;
            hasDocksideSurfacePoint = false;
            ClearCargoSurface();
            nextDocksideSurfaceProbe = 0f;
            distantSurfaceSeenAt = -1f;
            lastSurfaceProbeSummary = null;
        }

        internal static bool WaitingForPlayerInput()
        {
            try
            {
                return trackedStartMenu != null && WaitingForFInputField != null &&
                    WaitingForFInputField.GetValue(trackedStartMenu) is bool waiting && waiting;
            }
            catch { return false; }
        }

        internal static void Tick()
        {
            // The target lives under the site's frame, so a cleared site
            // also ends tracking.
            if (temporaryStart == null || site == null || startObserver == null || startController == null)
            {
                // Scene unloading can destroy the marker before the actor. Do
                // not leave its temporary embark suppression alive afterward.
                if (phase != Phase.Idle || startObserver != null || startController != null)
                    CancelTracking();
                return;
            }
            if (phase == Phase.Following && GameState.currentBoat != null)
            {
                // Gameplay has moved the controller into a boat's walk scene.
                // Do not apply any later world-space arrival correction there.
                CancelTracking();
                return;
            }
            if (!GameState.playing || phase != Phase.Following)
                LiftPlayerTargetToNearbySurface();
            if (GameState.playing && phase != Phase.Following &&
                docksideSurfaceCollider != null &&
                !TryGetDocksideSurface(true, out _, out _))
            {
                docksideSurfaceCollider = null;
                if (site.Frame != null)
                    temporaryStart.position = site.Frame.position + Vector3.up * 1.05f;
            }
            if (phase == Phase.Following && docksideSurfaceCollider == null)
            {
                Plugin.Instance?.Warn("The selected dock/shore support disappeared during new-game handoff; " +
                    "player tracking stopped and starter supplies will be rechecked.");
                CancelTracking();
                return;
            }
            if (!GameState.playing &&
                Time.realtimeSinceStartup - trackingStartedAt > 90f &&
                !WaitingForPlayerInput())
            {
                Plugin.Instance?.Warn("Player start did not reach Sailwind's F prompt or gameplay within " +
                    "90 seconds; dock tracking stopped. " +
                    (lastSurfaceProbeSummary ?? "No physics probe completed."));
                CancelTracking();
                return;
            }
            var targetPosition = temporaryStart.position;
            if (!ShoreProbe.IsFinite(targetPosition) || !IsFinite(temporaryStart.rotation))
            {
                Plugin.Instance?.Warn("Selected player target became non-finite during new-game setup; player tracking stopped.");
                CancelTracking();
                return;
            }
            if (!GameState.playing)
            {
                // The island/scene origin can move between the coroutine factory,
                // camera tween seam and the moment the controller becomes playable.
                // Re-evaluate the live recovery marker through that transition.
                if (!SnapStartActors()) return;
            }
            else
            {
                switch (phase)
                {
                    case Phase.Snapping:
                    case Phase.HoldingControl:
                        if (docksideSurfaceCollider == null)
                        {
                            HoldControlWhileSceneryLoads(targetPosition);
                            return;
                        }
                        if (!SnapStartActors()) return;
                        PlayerStartEmbarkGuard.Release();
                        ReleasePlayerControl();
                        phase = Phase.Following;
                        followStartedAt = Time.realtimeSinceStartup;
                        Plugin.Instance?.DebugLog($"Player start finalized on loaded dock/shore at {targetPosition}.");
                        // Freeze this chosen point. Only its parent's common
                        // world displacement may be followed during the existing
                        // brief handoff; never choose another surface after control.
                        break;
                    case Phase.Following:
                        if (Time.realtimeSinceStartup - followStartedAt >= 2f)
                        {
                            CancelTracking();
                            return;
                        }
                        if (!FollowLateMarkerShift(targetPosition)) return;
                        break;
                    default:
                        return;
                }
            }
            lastStartTargetPosition = targetPosition;
            lastStartControllerPosition = startController.position;
        }

        // Sailwind enables movement before the F prompt. Hold it briefly
        // while the selected scenery catches up, without fighting an active
        // controller every Update. If no site appears, return control at
        // Sailwind's recovery marker.
        private static void HoldControlWhileSceneryLoads(Vector3 targetPosition)
        {
            if (phase == Phase.Snapping)
            {
                phase = Phase.HoldingControl;
                holdStartedAt = Time.realtimeSinceStartup;
                if (!SnapStartActors()) return;
                playerControlHeld = true;
                try
                {
                    Refs.SetPlayerControl(false);
                }
                catch (Exception exception)
                {
                    Plugin.Instance?.Warn("Could not delay player control for scene loading: " +
                        exception.Message);
                    CancelTracking();
                    return;
                }
            }
            else if ((targetPosition - lastStartTargetPosition).sqrMagnitude > 0.04f)
            {
                if (!SnapStartActors()) return;
            }
            if (Time.realtimeSinceStartup - holdStartedAt >= 5f)
            {
                var marker = site.Frame;
                Plugin.Instance?.Warn($"No dry dock/shore resolved near {marker?.name} " +
                    $"at {(marker != null ? marker.position.ToString() : "unknown")}; " +
                    (lastSurfaceProbeSummary ?? "no physics probe completed") +
                    ". Player control restored at the native recovery marker; starter supplies will be rechecked nearby.");
                // An earlier pre-handoff candidate may have vanished.
                // The actor remains at the marker on this timeout, so
                // cargo must not reuse that abandoned distant site.
                hasDocksideSurfacePoint = false;
                docksideSurfaceLocal = Vector3.zero;
                ClearCargoSurface();
                CancelTracking();
                return;
            }
            lastStartTargetPosition = targetPosition;
            lastStartControllerPosition = startController.position;
        }

        // A late floating-origin move should carry the new player with the
        // dock without overriding their own movement after play starts.
        private static bool FollowLateMarkerShift(Vector3 targetPosition)
        {
            var displacement = targetPosition - lastStartTargetPosition;
            var controllerMovement = startController.position - lastStartControllerPosition;
            if (controllerMovement.sqrMagnitude > 0.25f &&
                (controllerMovement - displacement).sqrMagnitude > 0.25f)
            {
                // Gameplay, gravity or another system has taken control of
                // the actor. Stop correction instead of dragging them back.
                CancelTracking();
                return false;
            }
            if ((displacement.sqrMagnitude > 0.25f ||
                Mathf.Abs(displacement.y) > 0.05f) &&
                ((controllerMovement - displacement).sqrMagnitude > 0.25f ||
                Mathf.Abs(controllerMovement.y - displacement.y) > 0.05f))
            {
                startController.position += displacement;
                startObserver.position += displacement;
                Physics.SyncTransforms();
                Plugin.Instance?.DebugLog($"Player start followed a late dock-marker shift by {displacement}.");
            }
            return true;
        }

        private static bool SnapStartActors()
        {
            if (temporaryStart == null || startObserver == null || startController == null) return false;
            try { PlayerStartEmbarkGuard.EnsureWorldSpace(); }
            catch (Exception exception)
            {
                Plugin.Instance?.Warn("Selected shore start could not restore native player coordinates: " + exception.Message);
                CancelTracking();
                return false;
            }
            startController.SetPositionAndRotation(temporaryStart.position, temporaryStart.rotation);
            startObserver.SetPositionAndRotation(temporaryStart.position, temporaryStart.rotation);
            Physics.SyncTransforms();
            return true;
        }

        private static void LiftPlayerTargetToNearbySurface()
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
            var best = site.FindArrivalSurface(center, probeSummary);
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
                var sceneryReady = site.IsSelectedSceneryReady(out sceneryState);
                if (distantSurfaceSeenAt < 0f)
                {
                    distantSurfaceSeenAt = Time.realtimeSinceStartup;
                    Plugin.Instance?.DebugLog($"Distant shore candidate {horizontalDistanceToBest:F1}m from the dock; waiting within the existing player hold for nearby scenery ({sceneryState}). " + lastSurfaceProbeSummary);
                }
                // Stay inside the existing five-second player hold. A distant
                // fallback is re-probed each scan, never held as a stale hit.
                if ((!sceneryReady || Time.realtimeSinceStartup - distantSurfaceSeenAt < 1f) &&
                    (phase != Phase.HoldingControl || Time.realtimeSinceStartup - holdStartedAt < 4.5f))
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
                Plugin.Instance?.DebugLog($"Distant shore fallback selected at {horizontalDistanceToBest:F1}m ({sceneryState}); " + lastSurfaceProbeSummary);
            temporaryStart.position = destination;
            docksideSurfaceCollider = bestSurface;
            docksideSurfaceLocal = dockMarker.InverseTransformPoint(bestPoint);
            hasDocksideSurfacePoint = true;
            Plugin.Instance?.DebugLog($"Player dockside surface resolved on {ShoreProbe.TransformPath(bestSurface.transform)} ({bestSurface.GetType().Name}) at {bestPoint}; target {destination}, marker {center}.");
        }

        private static bool TryGetDocksideSurface(bool requirePlayerClearance,
            out Vector3 surface, out Collider collider)
        {
            surface = Vector3.zero;
            collider = null;
            // Cargo can finish after the two-second player tracking window.
            // Keep a local-space ground point on the recovery marker through
            // that bounded startup operation, even when the temporary player
            // target is destroyed.
            if (!HasSite || !site.IsLoadedCollider(docksideSurfaceCollider))
                return false;
            surface = site.Frame.TransformPoint(docksideSurfaceLocal);
            if (!ShoreProbe.IsFinite(surface) ||
                !site.TryProbe(surface, out var livePoint, out var liveCollider,
                    new SurfaceProbeSummary(), requirePlayerClearance) ||
                liveCollider != docksideSurfaceCollider ||
                Mathf.Abs(livePoint.y - surface.y) > 0.35f)
                return false;
            surface = livePoint;
            collider = liveCollider;
            return true;
        }

        internal static bool TryGetCargoSurface(out Vector3 surface, out Collider collider)
        {
            surface = Vector3.zero;
            collider = null;
            if (!HasSite)
            {
                CargoSurfaceStatus = "the selected shore anchor is unavailable";
                return false;
            }
            var probe = site;
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
            var reference = probe.Frame.TransformPoint(hasCargoSurfacePoint
                ? cargoSurfaceLocal : hasDocksideSurfacePoint ? docksideSurfaceLocal : Vector3.zero);
            // Re-probe the point, not the old Collider identity. A loaded scene
            // may replace its support while preserving the same usable ground.
            if ((hasCargoSurfacePoint || hasDocksideSurfacePoint) &&
                probe.TryProbe(reference, out surface, out collider, new SurfaceProbeSummary(), false))
            {
                RememberCargoSurface(surface, collider);
                return true;
            }
            if (Time.realtimeSinceStartup < nextCargoSurfaceProbe) return false;
            nextCargoSurfaceProbe = Time.realtimeSinceStartup + 0.25f;
            var summary = new SurfaceProbeSummary();
            var searchOrigin = hasDocksideSurfacePoint
                ? probe.Frame.TransformPoint(docksideSurfaceLocal) : probe.Frame.position;
            var candidates = ShoreProbe.NearbySurfaceSamples(searchOrigin, 6f);
            var found = ShoreProbe.FindClosestSurface(candidates, searchOrigin, sample =>
            {
                summary.Samples++;
                return probe.TryProbe(sample, out var point, out var support, summary, false)
                    ? Tuple.Create(point, support) : null;
            }, ShoreProbe.IsTerrainCollider);
            if (found == null)
            {
                CargoSurfaceStatus = "no loaded safe cargo ground near the last player site (" + summary + ")";
                return false;
            }
            surface = found.Item1;
            collider = found.Item2;
            RememberCargoSurface(surface, collider);
            Plugin.Instance?.DebugLog($"Cargo dock/shore support reacquired on {ShoreProbe.TransformPath(collider.transform)} at {surface}; player position unchanged.");
            return true;
        }

        internal static bool TryProbeCargoSurface(Vector3 horizontalSample,
            out Vector3 surface, out Collider collider)
        {
            if (site != null)
                return site.TryProbe(horizontalSample, out surface, out collider,
                    new SurfaceProbeSummary(), false);
            surface = Vector3.zero;
            collider = null;
            return false;
        }

        private static void RememberCargoSurface(Vector3 surface, Collider collider)
        {
            if (hasCargoSurfacePoint && cargoSurfaceCollider != collider)
                Plugin.Instance?.DebugLog($"Cargo support changed to {ShoreProbe.TransformPath(collider.transform)} at {surface}; player position unchanged.");
            cargoSurfaceCollider = collider;
            cargoSurfaceLocal = site.Frame.InverseTransformPoint(surface);
            hasCargoSurfacePoint = true;
            CargoSurfaceStatus = "cargo ground ready on " + collider.name;
        }

        internal static void ClearCargoSurface()
        {
            cargoSurfaceCollider = null;
            cargoSurfaceLocal = Vector3.zero;
            hasCargoSurfacePoint = false;
            nextCargoSurfaceProbe = 0f;
            CargoSurfaceStatus = "waiting for loaded dockside ground";
        }

        private static bool IsFinite(Quaternion value) =>
            ShoreProbe.IsFinite(value.x) && ShoreProbe.IsFinite(value.y) &&
            ShoreProbe.IsFinite(value.z) && ShoreProbe.IsFinite(value.w);
    }
}
