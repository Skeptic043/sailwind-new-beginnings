using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    // Only the two dock lines created by this new start are eligible. Native
    // adjustment, cast-off, recovery and saved-game loading keep their ownership.
    internal static class StarterMooring
    {
        private const float AddedSlack = 1f;
        private const float ReadyTimeout = 20f;
        private static readonly FieldInfo SpringField = AccessTools.Field(typeof(PickupableBoatMooringRope), "mooredToSpring");
        private static readonly FieldInfo LengthField = AccessTools.Field(typeof(PickupableBoatMooringRope), "currentRopeLengthSquared");
        private static readonly FieldInfo WasKinematicField = AccessTools.Field(typeof(PickupableBoatMooringRope), "wasKinematic");
        private static readonly FieldInfo MaxLengthField = AccessTools.Field(typeof(PickupableBoatMooringRope), "maxLength");
        private static readonly MethodInfo DistanceMethod = AccessTools.Method(typeof(PickupableBoatMooringRope), "GetCurrentDistanceSquared");
        private static readonly FieldInfo DistanceTimerField = AccessTools.Field(typeof(RopeEffect), "distanceCheckTimer");
        private static readonly FieldInfo OutOfRangeField = AccessTools.Field(typeof(RopeEffect), "outOfRange");
        private static readonly FieldInfo ClothField = AccessTools.Field(typeof(RopeEffect), "clothRope");
        private static ResolvedStart selectedStart;
        private static Line[] lines;
        private static int generation;
        private static bool settled;

        private sealed class Line
        {
            internal PickupableBoatMooringRope Rope;
            internal GPButtonDockMooring Dock;
            internal bool LengthApplied;
            internal bool Finished;
        }

        internal static void Cancel()
        {
            ++generation;
            selectedStart = null;
            lines = null;
            settled = false;
        }

        internal static void MarkSettled(ResolvedStart selected)
        {
            if (ReferenceEquals(selectedStart, selected)) settled = true;
        }

        internal static void Arm(StartMenu menu, ResolvedStart selected,
            PickupableBoatMooringRope front, PickupableBoatMooringRope back)
        {
            Cancel();
            if (SpringField == null || LengthField == null || WasKinematicField == null ||
                MaxLengthField == null || DistanceMethod == null || DistanceTimerField == null)
            {
                Plugin.Instance.Warn("Starter mooring slack skipped: native rope fields are unavailable.");
                return;
            }
            selectedStart = selected;
            lines = new[]
            {
                new Line { Rope = front, Dock = selected.FrontMooring },
                new Line { Rope = back, Dock = selected.BackMooring }
            };
            var token = generation;
            try { Plugin.Instance.StartCoroutine(FinishStart(menu, selected, token)); }
            catch (Exception exception)
            {
                Cancel();
                Plugin.Instance.Warn("Starter mooring slack could not be scheduled: " + exception.Message);
            }
        }

        private static IEnumerator FinishStart(StartMenu menu, ResolvedStart selected, int token)
        {
            var gameplayElapsed = 0f;
            var preplayElapsed = 0f;
            var lastTick = Time.realtimeSinceStartup;
            var sawGameplay = false;
            while (token == generation && Plugin.Instance != null)
            {
                // Native Update resets rope length when the hull becomes dynamic.
                // Run after that Update, and after our final placement pass.
                yield return new WaitForEndOfFrame();
                if (token != generation || Plugin.Instance == null) yield break;
                var complete = false;
                try
                {
                    if (!FixedStart.IsSelectionStillActive(menu, selected))
                    {
                        Cancel();
                        yield break;
                    }
                    var now = Time.realtimeSinceStartup;
                    var elapsed = Math.Max(0f, now - lastTick);
                    lastTick = now;
                    if (sawGameplay && (!GameState.playing || GameState.currentlyLoading))
                    {
                        Cancel();
                        yield break;
                    }
                    if (GameState.playing)
                    {
                        // Ownership failure must not leave startup work alive.
                        // Native playing can precede the F prompt; time reading
                        // that prompt does not spend either startup deadline.
                        if (!Plugin.Instance.WaitingForPlayerInput()) gameplayElapsed += elapsed;
                        if (!GameState.currentlyLoading) sawGameplay = true;
                    }
                    else if (!Plugin.Instance.WaitingForPlayerInput())
                    {
                        preplayElapsed += elapsed;
                        if (preplayElapsed >= 90f)
                        {
                            Plugin.Instance.Warn("Starter mooring slack cancelled: new-game handoff did not reach the F prompt or gameplay within 90 seconds.");
                            Cancel();
                            yield break;
                        }
                    }
                    var ready = GameState.playing && !GameState.currentlyLoading &&
                        selected.Boat.isPurchased();
                    complete = true;
                    foreach (var line in lines)
                    {
                        if (line.Finished) continue;
                        if (!StillConnected(line, selected.Body))
                        {
                            var connected = line.Dock != null && line.Dock.spring != null ? line.Dock.spring.connectedBody : null;
                            Plugin.Instance.DebugLog($"Starter mooring ended before settling: {(line.Rope != null ? line.Rope.name : "missing rope")} -> {(line.Dock != null ? line.Dock.name : "missing dock")}, connected hull {(connected != null ? connected.name : "none")}; no connection was restored.");
                            line.Finished = true;
                            continue;
                        }
                        var lengthWasApplied = line.LengthApplied;
                        // RopeEffect otherwise retains an old >250 m culling result
                        // for 5-8 seconds after the boat/player/island teleport.
                        // Invalidate only this startup cache; native distance and
                        // cloth/line-renderer policy still decide visibility.
                        var effect = line.Rope.GetComponent<RopeEffect>();
                        if (effect != null) DistanceTimerField.SetValue(effect, 0f);
                        if (!line.LengthApplied && ready && settled && !selected.Body.isKinematic &&
                            line.Rope.isActiveAndEnabled && line.Dock.gameObject.activeInHierarchy &&
                            !(bool)WasKinematicField.GetValue(line.Rope))
                        {
                            var distanceSquared = (float)DistanceMethod.Invoke(line.Rope, null);
                            var maximumSquared = (float)MaxLengthField.GetValue(null) + 25f;
                            var lengthSquared = SlackLengthSquared(distanceSquared, maximumSquared);
                            if (!float.IsNaN(lengthSquared))
                            {
                                // Both native length representations must agree.
                                // ChangeRopeLength squares its signed amount and refuses
                                // to release a stale, over-limit hidden-island line.
                                // Rebase once from the now-live endpoint separation.
                                LengthField.SetValue(line.Rope, lengthSquared);
                                line.Dock.spring.maxDistance = (float)Math.Sqrt(lengthSquared);
                                line.LengthApplied = true;
                                Plugin.Instance.DebugLog($"Starter mooring slack: {line.Rope.name}, distance {Math.Sqrt(distanceSquared):F2} m, length {Math.Sqrt(lengthSquared):F2} m (up to {AddedSlack:F1} m extra).");
                            }
                        }
                        // Physics readiness can precede the observer mirror's
                        // arrival. Keep invalidating this cache until the exact
                        // observer/distance used by native CheckDistance is near;
                        // never reapply the length while waiting for visibility.
                        // The length changes at end of frame. Observe the next
                        // native LateUpdate before reporting the finished line.
                        if (lengthWasApplied && effect != null && effect.isActiveAndEnabled &&
                            Refs.observerMirror != null &&
                            Vector3.Distance(effect.transform.position,
                                Refs.observerMirror.transform.position) <= 250f)
                        {
                            ReportState(selected, line, effect);
                            line.Finished = true;
                        }
                        if (!line.Finished)
                        {
                            // Disabled/missing visuals cannot satisfy the normal
                            // finish condition. Capture them at the existing deadline.
                            if (lengthWasApplied && gameplayElapsed >= ReadyTimeout)
                                ReportState(selected, line, effect);
                            complete = false;
                        }
                    }
                    if (!complete && gameplayElapsed >= ReadyTimeout)
                    {
                        Plugin.Instance.Warn("Starter mooring startup timed out waiting for settled dock lines and nearby rope visibility; remaining startup work stopped.");
                        complete = true;
                    }
                }
                catch (Exception exception)
                {
                    Plugin.Instance.Warn("Starter mooring slack stopped: " + exception.Message);
                    complete = true;
                }
                if (complete)
                {
                    Cancel();
                    yield break;
                }
            }
        }

        private static bool StillConnected(Line line, Rigidbody body) =>
            line.Rope != null && line.Dock != null && line.Dock.spring != null &&
            line.Rope.GetBoatRigidbody() == body &&
            SpringField.GetValue(line.Rope) as SpringJoint == line.Dock.spring &&
            line.Dock.spring.connectedBody == body;

        private static void ReportState(ResolvedStart selected, Line line, RopeEffect effect)
        {
            // Diagnostics never take ownership of a changed spring or affect
            // completion if a native visual component is unavailable.
            try
            {
                if (!StillConnected(line, selected.Body)) return;
                var spring = line.Dock.spring;
                var anchor = spring.transform.TransformPoint(spring.anchor);
                var connectedAnchor = selected.Body.transform.TransformPoint(spring.connectedAnchor);
                var distance = Math.Sqrt((float)DistanceMethod.Invoke(line.Rope, null));
                var length = Math.Sqrt((float)LengthField.GetValue(line.Rope));
                string force;
                try { force = spring.currentForce.ToString(); }
                catch (Exception exception) { force = "unavailable (" + exception.Message + ")"; }
                Plugin.Instance.DebugLog($"Starter mooring state: boat {selected.Body.name}, {line.Rope.name} -> {line.Dock.name}, connected hull {spring.connectedBody.name}; anchors {anchor} / {connectedAnchor}, span {Vector3.Distance(anchor, connectedAnchor):F2} m; min/max {spring.minDistance:F2}/{spring.maxDistance:F2} m, spring/damper {spring.spring:F1}/{spring.damper:F1}, mass {selected.Body.mass:F1}, force {force}; native distance/length {distance:F2}/{length:F2} m, kinematic {selected.Body.isKinematic}.");
                var renderer = line.Rope.GetComponent<LineRenderer>();
                var cloth = effect != null ? ClothField?.GetValue(effect) as ClothRope : null;
                var renderState = renderer == null ? "missing" :
                    $"active {renderer.gameObject.activeInHierarchy}, enabled {renderer.enabled}, visible {renderer.isVisible}, points {renderer.positionCount}, bounds Y {renderer.bounds.min.y:F2}..{renderer.bounds.max.y:F2}";
                var attachment = effect != null && effect.attachment != null ? $"{effect.attachment.name} at {effect.attachment.position}" : "none";
                var outOfRange = effect != null ? OutOfRangeField?.GetValue(effect) : null;
                Plugin.Instance.DebugLog($"Starter mooring visual: {line.Rope.name}, effect active {effect != null && effect.isActiveAndEnabled}, attachment {attachment}, outOfRange {outOfRange ?? "unknown"}; renderer {renderState}; cloth setting {Settings.clothRopes}, cloth active {(cloth != null ? cloth.gameObject.activeInHierarchy.ToString() : "missing")}; shipyardUpdatingOrder {GameState.shipyardUpdatingOrder}.");
            }
            catch (Exception exception)
            {
                Plugin.Instance.DebugLog("Starter mooring state could not be observed: " + exception.Message);
            }
        }

        internal static float SlackLengthSquared(float distanceSquared, float maximumSquared)
        {
            if (float.IsNaN(distanceSquared) || float.IsInfinity(distanceSquared) || distanceSquared < 0f ||
                float.IsNaN(maximumSquared) || float.IsInfinity(maximumSquared) || maximumSquared <= 0f ||
                distanceSquared >= maximumSquared) return float.NaN;
            var length = Math.Min(Math.Sqrt(distanceSquared) + AddedSlack, Math.Sqrt(maximumSquared));
            return (float)(length * length);
        }

        private static void Relinquish(PickupableBoatMooringRope rope)
        {
            if (lines == null) return;
            foreach (var line in lines)
                if (ReferenceEquals(line.Rope, rope)) line.Finished = true;
        }

        [HarmonyPatch(typeof(PickupableBoatMooringRope), "ChangeRopeLength")]
        private static class PlayerAdjustment
        {
            private static void Prefix(PickupableBoatMooringRope __instance) => Relinquish(__instance);
        }

        [HarmonyPatch(typeof(PickupableBoatMooringRope), "Unmoor")]
        private static class CastOff
        {
            private static void Prefix(PickupableBoatMooringRope __instance) => Relinquish(__instance);
        }

        [HarmonyPatch(typeof(SaveLoadManager), "LoadGame", new[] { typeof(int) })]
        private static class CancelOnLoad
        {
            private static void Prefix() => Cancel();
        }
    }
}
