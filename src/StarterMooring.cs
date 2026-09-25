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
        private const float AddedSlack = 3f;
        private const float LeopardAddedSlack = 5f;
        private const float ReadyTimeout = 20f;
        private static readonly FieldInfo SpringField = AccessTools.Field(typeof(PickupableBoatMooringRope), "mooredToSpring");
        private static readonly FieldInfo LengthField = AccessTools.Field(typeof(PickupableBoatMooringRope), "currentRopeLengthSquared");
        private static readonly FieldInfo WasKinematicField = AccessTools.Field(typeof(PickupableBoatMooringRope), "wasKinematic");
        private static readonly FieldInfo MaxLengthField = AccessTools.Field(typeof(PickupableBoatMooringRope), "maxLength");
        private static readonly MethodInfo DistanceMethod = AccessTools.Method(typeof(PickupableBoatMooringRope), "GetCurrentDistanceSquared");
        private static readonly FieldInfo DistanceTimerField = AccessTools.Field(typeof(RopeEffect), "distanceCheckTimer");
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
                            line.Finished = true;
                            continue;
                        }
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
                            var addedSlack = FixedStart.IsLeopardStart(selected) ? LeopardAddedSlack : AddedSlack;
                            var lengthSquared = SlackLengthSquaredWithAllowance(distanceSquared, maximumSquared, addedSlack);
                            if (!float.IsNaN(lengthSquared))
                            {
                                // Both native length representations must agree.
                                // ChangeRopeLength squares its signed amount and refuses
                                // to release a stale, over-limit hidden-island line.
                                // Rebase once from the now-live endpoint separation.
                                LengthField.SetValue(line.Rope, lengthSquared);
                                line.Dock.spring.maxDistance = (float)Math.Sqrt(lengthSquared);
                                line.LengthApplied = true;
                                Plugin.Instance.Report($"Starter mooring slack: {line.Rope.name}, distance {Math.Sqrt(distanceSquared):F2} m, length {Math.Sqrt(lengthSquared):F2} m (up to {addedSlack:F1} m extra).");
                            }
                        }
                        // Physics readiness can precede the observer mirror's
                        // arrival. Keep invalidating this cache until the exact
                        // observer/distance used by native CheckDistance is near;
                        // never reapply the length while waiting for visibility.
                        if (line.LengthApplied && effect != null && effect.isActiveAndEnabled &&
                            Refs.observerMirror != null &&
                            Vector3.Distance(effect.transform.position,
                                Refs.observerMirror.transform.position) <= 250f)
                            line.Finished = true;
                        if (!line.Finished) complete = false;
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

        internal static float SlackLengthSquared(float distanceSquared, float maximumSquared) =>
            SlackLengthSquaredWithAllowance(distanceSquared, maximumSquared, AddedSlack);

        private static float SlackLengthSquaredWithAllowance(float distanceSquared,
            float maximumSquared, float addedSlack)
        {
            if (float.IsNaN(distanceSquared) || float.IsInfinity(distanceSquared) || distanceSquared < 0f ||
                float.IsNaN(maximumSquared) || float.IsInfinity(maximumSquared) || maximumSquared <= 0f ||
                distanceSquared >= maximumSquared || float.IsNaN(addedSlack) ||
                float.IsInfinity(addedSlack) || addedSlack < 0f) return float.NaN;
            var length = Math.Min(Math.Sqrt(distanceSquared) + addedSlack, Math.Sqrt(maximumSquared));
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
