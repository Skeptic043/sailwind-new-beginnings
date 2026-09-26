using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    // Repair this new boat once after preserved or relocated placement is ready.
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
            Cancel("startup operation reset");
        }

        private static void Cancel(string reason)
        {
            var selected = pending;
            ClearPending();
            if (selected != null)
                Plugin.Instance?.Report($"Startup hull repair cancelled for boat {BoatLabel(selected)}: {reason}.");
        }

        private static void ClearPending()
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
            Cancel("superseded by a new start");
            if (selected?.Body == null || Plugin.Instance == null) return;
            var damage = selected.Body.GetComponent<BoatDamage>();
            if (damage == null)
            {
                Plugin.Instance.Warn("Startup hull repair skipped: selected boat has no native BoatDamage component.");
                return;
            }
            pending = selected;
            var token = generation;
            try
            {
                Plugin.Instance.Report($"Startup hull repair scheduled for boat {BoatLabel(selected)}; waiting for settled ownership and active physics.");
                Plugin.Instance.StartCoroutine(RepairAfterPlacement(menu, selected, damage, token));
            }
            catch (Exception exception)
            {
                Cancel("coroutine could not be scheduled");
                Plugin.Instance?.Warn("Startup hull repair could not be scheduled: " + exception.Message);
            }
        }

        [HarmonyPatch(typeof(SaveLoadManager), "LoadGame", new[] { typeof(int) })]
        private static class CancelOnLoad
        {
            private static void Prefix() => Cancel("saved-game load");
        }

        private static string BoatLabel(ResolvedStart selected) => selected?.Saveable != null
            ? selected.Saveable.sceneIndex.ToString() : "unknown";

        private static IEnumerator RepairAfterPlacement(StartMenu menu, ResolvedStart selected,
            BoatDamage damage, int token)
        {
            var waitingElapsed = 0f;
            var lastTick = Time.realtimeSinceStartup;
            var repairAt = -1f;
            var sawGameplay = false;
            var sawOwnership = false;
            var reportedDelay = false;
            var reportedReset = false;
            string stopReason = null;
            try
            {
                while (token == generation && ReferenceEquals(pending, selected))
                {
                    var stop = false;
                    try
                    {
                        if (Plugin.Instance == null) stopReason = "plugin unavailable";
                        else if (GameState.currentlyLoading) stopReason = "saved-game loading began";
                        else if (!FixedStart.IsSelectionStillActive(menu, selected)) stopReason = "selected start is no longer active";
                        else if (damage == null || selected.Body.GetComponent<BoatDamage>() != damage)
                            stopReason = "native damage component changed";
                        else if (sawGameplay && !GameState.playing) stopReason = "gameplay ended";
                        else if (sawOwnership && !selected.Boat.isPurchased()) stopReason = "boat ownership was lost";
                        if (stopReason != null) stop = true;
                        else
                        {
                            var now = Time.realtimeSinceStartup;
                            var elapsed = Math.Max(0f, now - lastTick);
                            lastTick = now;
                            if (GameState.playing) sawGameplay = true;
                            var purchased = selected.Boat.isPurchased();
                            if (purchased) sawOwnership = true;
                            var awaitingInput = Plugin.Instance.WaitingForPlayerInput();
                            var ready = settled && GameState.playing && !awaitingInput &&
                                purchased && !selected.Body.isKinematic &&
                                selected.Body.gameObject.activeInHierarchy;
                            if (!ready)
                            {
                                // Native BoatHorizon temporarily makes a hull
                                // kinematic during pause and physics handoff.
                                // Restart the full delay after it becomes ready,
                                // rather than silently losing this one-time job.
                                if (repairAt >= 0f && !reportedReset)
                                {
                                    reportedReset = true;
                                    Plugin.Instance.Report($"Startup hull repair waiting again for boat {BoatLabel(selected)}; readiness changed, so its eight-second delay will restart.");
                                }
                                repairAt = -1f;
                                if (!awaitingInput) waitingElapsed += elapsed;
                                if (waitingElapsed >= ReadinessTimeout)
                                {
                                    stopReason = $"readiness timed out after 90 seconds (settled={settled}, playing={GameState.playing}, owned={purchased}, kinematic={selected.Body.isKinematic}, active={selected.Body.gameObject.activeInHierarchy})";
                                    stop = true;
                                }
                            }
                            else if (repairAt < 0f)
                            {
                                repairAt = now + RepairDelay;
                                if (!reportedDelay)
                                {
                                    reportedDelay = true;
                                    Plugin.Instance.Report($"Startup hull repair delay started for boat {BoatLabel(selected)}; repair is due after eight seconds of readiness.");
                                }
                            }
                            else if (now >= repairAt)
                            {
                                // Consume before the native-state write so a
                                // later exception cannot repeat the repair.
                                ClearPending();
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
                        stopReason = "exception while checking readiness or repairing";
                        Plugin.Instance?.Warn("Startup hull repair stopped: " + exception.Message);
                        stop = true;
                    }
                    if (stop) yield break;
                    yield return null;
                }
            }
            finally
            {
                if (token == generation) Cancel(stopReason ?? "startup coroutine ended");
            }
        }
    }
}
