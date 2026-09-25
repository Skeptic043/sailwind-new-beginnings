using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    // Compensate once for collision damage during this new boat's placement.
    // Native damage and physics remain active before and after the single repair.
    internal static class StarterBoatRepair
    {
        private const float RepairDelay = 8f;
        private const float ReadinessTimeout = 90f;
        private static int generation;
        private static ResolvedStart pending;
        private static bool settled;

        internal static void Cancel()
        {
            ++generation;
            pending = null;
            settled = false;
        }

        internal static void MarkSettled(ResolvedStart selected)
        {
            if (ReferenceEquals(pending, selected)) settled = true;
        }

        internal static void Arm(StartMenu menu, ResolvedStart selected)
        {
            Cancel();
            if (selected?.Body == null || Plugin.Instance == null) return;
            var damage = selected.Body.GetComponent<BoatDamage>();
            if (damage == null)
            {
                Plugin.Instance.Warn("Startup hull repair skipped: selected boat has no native BoatDamage component.");
                return;
            }
            pending = selected;
            var token = generation;
            try { Plugin.Instance.StartCoroutine(RepairAfterPlacement(menu, selected, damage, token)); }
            catch (Exception exception)
            {
                Cancel();
                Plugin.Instance?.Warn("Startup hull repair could not be scheduled: " + exception.Message);
            }
        }

        [HarmonyPatch(typeof(SaveLoadManager), "LoadGame", new[] { typeof(int) })]
        private static class CancelOnLoad
        {
            private static void Prefix() => Cancel();
        }

        private static IEnumerator RepairAfterPlacement(StartMenu menu, ResolvedStart selected,
            BoatDamage damage, int token)
        {
            var waitingElapsed = 0f;
            var lastTick = Time.realtimeSinceStartup;
            var repairAt = -1f;
            var sawGameplay = false;
            try
            {
                while (token == generation && ReferenceEquals(pending, selected))
                {
                    var stop = false;
                    try
                    {
                        if (Plugin.Instance == null || GameState.currentlyLoading ||
                            !FixedStart.IsSelectionStillActive(menu, selected) || damage == null ||
                            selected.Body.GetComponent<BoatDamage>() != damage ||
                            sawGameplay && !GameState.playing)
                            stop = true;
                        else
                        {
                            var now = Time.realtimeSinceStartup;
                            var elapsed = Math.Max(0f, now - lastTick);
                            lastTick = now;
                            if (GameState.playing) sawGameplay = true;
                            var awaitingInput = Plugin.Instance.WaitingForPlayerInput();
                            var ready = settled && GameState.playing && !awaitingInput &&
                                selected.Boat.isPurchased() && !selected.Body.isKinematic &&
                                selected.Body.gameObject.activeInHierarchy;
                            if (repairAt < 0f)
                            {
                                if (ready) repairAt = now + RepairDelay;
                                else if (!awaitingInput) waitingElapsed += elapsed;
                                if (waitingElapsed >= ReadinessTimeout)
                                {
                                    Plugin.Instance.Warn("Startup hull repair cancelled: selected boat did not finish placement and native ownership within 90 seconds.");
                                    stop = true;
                                }
                            }
                            else if (!ready) stop = true;
                            else if (now >= repairAt)
                            {
                                // Consume this operation before the native-state
                                // write, including when logging or a future hook fails.
                                Cancel();
                                // Native Shipyard.ConfirmOrder repairs hullDamage
                                // directly. Do only that write: no fee, bilge drain,
                                // oakum, inventory, position or buoyancy changes.
                                var previous = damage.hullDamage;
                                damage.hullDamage = 0f;
                                Plugin.Instance.Report($"Startup hull repair completed once for boat {selected.Saveable.sceneIndex}, eight seconds after settled ownership; hull damage {previous:F3} -> 0.");
                                stop = true;
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Plugin.Instance?.Warn("Startup hull repair stopped: " + exception.Message);
                        stop = true;
                    }
                    if (stop) yield break;
                    yield return null;
                }
            }
            finally
            {
                if (token == generation) Cancel();
            }
        }
    }
}
