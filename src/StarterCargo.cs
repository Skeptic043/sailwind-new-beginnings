using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    // Starter supplies belong to Sailwind after its new-game coroutine has
    // sold and registered them. This class moves that exact set once to the
    // same loaded wharf/shore surface used for the player's start.
    internal static class StarterCargo
    {
        private sealed class ShipItemReferenceComparer : IEqualityComparer<ShipItem>
        {
            public bool Equals(ShipItem left, ShipItem right) => ReferenceEquals(left, right);
            public int GetHashCode(ShipItem item) => RuntimeHelpers.GetHashCode(item);
        }

        private sealed class CargoMove
        {
            public ShipItem Item;
            public ItemRigidbody Body;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 OriginalPosition;
            public Quaternion OriginalRotation;
        }

        private sealed class GroundSlot
        {
            public Vector3 Point;
            public float Radius;
            public bool LargeSupport;
        }

        private static readonly ShipItemReferenceComparer ReferenceComparer =
            new ShipItemReferenceComparer();
        private static readonly FieldInfo RegisteredField =
            AccessTools.Field(typeof(SaveablePrefab), "registered");
        private static readonly FieldInfo BodyOutOfRangeField =
            AccessTools.Field(typeof(ItemRigidbody), "outOfRange");
        private static readonly FieldInfo BodyDestroyFramesField =
            AccessTools.Field(typeof(ItemRigidbody), "framesUntilDestroy");
        private static readonly FieldInfo BodyDistanceTimerField =
            AccessTools.Field(typeof(ItemRigidbody), "distanceCheckTimer");
        private static readonly FieldInfo StayedEmbarkField =
            AccessTools.Field(typeof(ShipItem), "currentlyStayedEmbarkCol");
        private static readonly MethodInfo ExitBoatMethod =
            AccessTools.Method(typeof(ShipItem), "ExitBoat");
        private static readonly HashSet<ShipItem> Captured =
            new HashSet<ShipItem>(ReferenceComparer);
        private static readonly HashSet<ShipItem> DifficultyRemovalRequests =
            new HashSet<ShipItem>(ReferenceComparer);
        private static MethodInfo difficultyAdjustmentMethod;
        private static ResolvedStart difficultyAdjustmentScope;
        private static bool difficultyAdjustmentCompleted;
        private static bool difficultyAdjustmentFailed;
        private static int difficultyRemovalsApplied;
        private static bool nativeStarterCompleted;
        private static bool rosterFinalized;
        private static readonly Dictionary<ShipItem, Vector3> AuthoredPositions =
            new Dictionary<ShipItem, Vector3>(ReferenceComparer);
        private static readonly Dictionary<ShipItem, Quaternion> AuthoredRotations =
            new Dictionary<ShipItem, Quaternion>(ReferenceComparer);
        private static readonly Dictionary<ShipItem, string> AuthoredNames =
            new Dictionary<ShipItem, string>(ReferenceComparer);
        private static readonly List<Transform> BoatWalkRoots =
            new List<Transform>();
        private static readonly Dictionary<ShipItem, Vector3> PlannedGround =
            new Dictionary<ShipItem, Vector3>(ReferenceComparer);

        private static readonly List<Vector3> RejectedAnchorGround = new List<Vector3>();

        private static ResolvedStart armed;
        private static float readyAt = -1f;
        private static float cupProtectionEndsAt;
        private static bool wrapperObserved;
        private static bool placementClaimed;
        private static bool fallbackScheduled;
        private static bool scrambledSeasMapSkipped;
        private static string placementStage;
        private static float nextProgressReport;
        private static int layoutAttempts;
        internal static void Arm(ResolvedStart selected)
        {
            Disarm();
            armed = selected;
            cupProtectionEndsAt = Time.realtimeSinceStartup + 90f;
            placementStage = "waiting for native gameplay";
            foreach (var item in CollectItems(selected.StarterSet.transform))
                Capture(item);
            Plugin.Instance?.Report($"Dockside starter supplies armed for region {selected.Region}: {Captured.Count} native items captured.");
            Plugin.Instance?.Report("Native starter objects: " + CapturedNames());
        }

        internal static void Disarm()
        {
            if (armed != null && placementStage != null)
                Plugin.Instance?.Report("Dockside supply setup released at stage: " + placementStage + ".");
            armed = null;
            placementStage = null;
            nextProgressReport = 0f;
            layoutAttempts = 0;
            readyAt = -1f;
            cupProtectionEndsAt = 0f;
            wrapperObserved = false;
            placementClaimed = false;
            fallbackScheduled = false;
            scrambledSeasMapSkipped = false;
            Captured.Clear();
            DifficultyRemovalRequests.Clear();
            difficultyAdjustmentScope = null;
            difficultyAdjustmentCompleted = false;
            difficultyAdjustmentFailed = false;
            difficultyRemovalsApplied = 0;
            nativeStarterCompleted = false;
            rosterFinalized = false;
            AuthoredPositions.Clear();
            AuthoredRotations.Clear();
            AuthoredNames.Clear();
            BoatWalkRoots.Clear();
            PlannedGround.Clear();
            RejectedAnchorGround.Clear();
            Plugin.Instance?.ClearDocksideCargoSurface();
        }

        private static void FailAndDisarm(string reason)
        {
            Plugin.Instance?.Warn("Dockside starter supplies could not be confirmed: " +
                reason + ". Captured objects: " + CapturedNames() +
                ". Check your supplies before continuing.");
            placementStage = "failed: " + reason;
            var oar = Captured.FirstOrDefault(IsNativeOar);
            if (oar != null)
            {
                var planned = PlannedGround.TryGetValue(oar, out var point) ?
                    GroundToWorld(armed, point).ToString() : "<not placed>";
                Plugin.Instance?.Warn("Native starter oar last observed at " +
                    oar.transform.position + ", planned dockside ground " +
                    planned + ", active " + oar.gameObject.activeInHierarchy +
                    ".");
            }
            Disarm();
        }

        internal static void Tick()
        {
            var selected = armed;
            if (selected == null) return;
            if (GameState.currentlyLoading ||
                selected.Port == null || !selected.Port.gameObject.scene.isLoaded)
            {
                if (GameState.playing && !GameState.currentlyLoading)
                    FailAndDisarm("the selected game or port scene changed before supply placement");
                else
                    Disarm();
                return;
            }
            if (readyAt < 0f && GameState.playing)
                readyAt = Time.realtimeSinceStartup;
            if (readyAt >= 0f &&
                Time.realtimeSinceStartup - readyAt > 90f)
            {
                FailAndDisarm($"setup timed out with {CountLive()}/{Captured.Count} captured native items");
                return;
            }
            if (readyAt >= 0f && Time.realtimeSinceStartup >= nextProgressReport)
            {
                Plugin.Instance?.Report($"Dockside supply setup at {Time.realtimeSinceStartup - readyAt:F1}s: " +
                    $"{placementStage}; layout attempts {layoutAttempts}, live items {CountLive()}/{Captured.Count}.");
                nextProgressReport = Time.realtimeSinceStartup + 5f;
            }
            if (placementClaimed || fallbackScheduled || !GameState.playing) return;
            // A different mod can wrap StarterSet's IEnumerator and never
            // return from its own tail. The placement coroutine itself waits
            // for the native items' sale and registration before moving them.
            if (Time.realtimeSinceStartup - readyAt < 0.75f) return;
            fallbackScheduled = true;
            Plugin.Instance?.Report($"Dockside supply fallback scheduled (starter coroutine observed: {wrapperObserved}).");
            Plugin.Instance?.StartCoroutine(PlaceCaptured(selected));
        }

        // Observe only Difficulty's synchronous starter adjustment. Receipt of
        // DestroyItem is evidence of an intentional choice, not permission to
        // ignore other lost objects or recreate supplies removed by difficulty.
        [HarmonyPatch]
        private static class DifficultyStarterAdjustmentPatch
        {
            private static bool Prepare()
            {
                if (!Chainloader.PluginInfos.TryGetValue("com.nandbrew.sailwinddifficulty", out var info))
                    return false;
                var type = info.Instance?.GetType().Assembly.GetType(
                    "SailwindDifficulty.Patches+StarterSetPatches", false);
                difficultyAdjustmentMethod = type?.GetMethod("Prefix",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null, new[] { typeof(StarterSet), typeof(PortRegion) }, null);
                if (difficultyAdjustmentMethod == null || difficultyAdjustmentMethod.ReturnType != typeof(void))
                {
                    Plugin.Instance?.Warn("Sailwind Difficulty starter-adjustment observer is unavailable; unobserved missing supplies will remain an error.");
                    return false;
                }
                return true;
            }

            private static MethodBase TargetMethod() => difficultyAdjustmentMethod;

            private static void Prefix(StarterSet __0, out ResolvedStart __state)
            {
                __state = difficultyAdjustmentScope;
                difficultyAdjustmentScope = armed != null && !rosterFinalized &&
                    __0 == armed.StarterSet ? armed : null;
            }

            private static Exception Finalizer(Exception __exception, ResolvedStart __state)
            {
                try
                {
                    if (difficultyAdjustmentScope != null &&
                        ReferenceEquals(difficultyAdjustmentScope, armed))
                    {
                        if (__exception != null) difficultyAdjustmentFailed = true;
                        else
                        {
                            difficultyAdjustmentCompleted = true;
                            foreach (var item in CollectItems(armed.StarterSet.transform)) Capture(item);
                            Plugin.Instance?.Report($"Observed Sailwind Difficulty starter adjustment: {DifficultyRemovalRequests.Count} exact removal requests; current native additions captured.");
                        }
                    }
                }
                catch (Exception exception)
                {
                    difficultyAdjustmentFailed = true;
                    Plugin.Instance?.Error("Could not confirm the Sailwind Difficulty starter adjustment.", exception);
                }
                finally
                {
                    // Never leak the mutation scope into normal item destruction
                    // or the next new game, including a throwing Difficulty prefix.
                    difficultyAdjustmentScope = __state != null && ReferenceEquals(__state, armed)
                        ? __state : null;
                }
                return __exception;
            }
        }

        [HarmonyPatch(typeof(StarterSet), "InitiateStarterSet")]
        [HarmonyAfter("com.nandbrew.shipyardexpansion")]
        [HarmonyPriority(Priority.Last)]
        private static class StarterSetPatch
        {
            private static void Postfix(StarterSet __instance, ref IEnumerator __result)
            {
                if (armed == null || armed.StarterSet != __instance || __result == null)
                    return;
                try
                {
                    foreach (var item in CollectItems(__instance.transform))
                        Capture(item);
                    wrapperObserved = true;
                    __result = FollowNativeStarterSet(__result, __instance, armed);
                    Plugin.Instance?.Report($"Native regional starter coroutine wrapped with {Captured.Count} captured supply identities.");
                }
                catch (Exception exception)
                {
                    Plugin.Instance?.Error("Could not observe the native regional starter coroutine.", exception);
                }
            }
        }

        private static IEnumerator FollowNativeStarterSet(IEnumerator original,
            StarterSet set, ResolvedStart selected)
        {
            while (true)
            {
                bool moved;
                try
                {
                    moved = original.MoveNext();
                    if (IsCurrentCapture(armed, selected, rosterFinalized))
                        foreach (var item in CollectItems(set.transform)) Capture(item);
                }
                catch (Exception exception)
                {
                    Plugin.Instance?.Error("Regional starter coroutine failed before supplies could be moved.", exception);
                    yield break;
                }
                if (!moved) break;
                yield return original.Current;
            }
            if (ReferenceEquals(armed, selected)) nativeStarterCompleted = true;
            if (ReferenceEquals(armed, selected) && !placementClaimed)
            {
                var placement = PlaceCaptured(selected);
                while (placement.MoveNext()) yield return placement.Current;
            }
        }

        private static IEnumerator PlaceCaptured(ResolvedStart selected)
        {
            var operation = PlaceCapturedCore(selected);
            while (true)
            {
                bool moved;
                try
                {
                    moved = operation.MoveNext();
                }
                catch (Exception exception)
                {
                    Plugin.Instance?.Error("Dockside supply setup interrupted at " + placementStage + ".", exception);
                    if (ReferenceEquals(armed, selected))
                        FailAndDisarm("startup exception at " + placementStage + ": " + exception.Message);
                    yield break;
                }
                if (!moved) yield break;
                yield return operation.Current;
            }
        }

        private static IEnumerator PlaceCapturedCore(ResolvedStart selected)
        {
            if (!ReferenceEquals(armed, selected) || placementClaimed)
                yield break;
            placementClaimed = true;
            placementStage = "waiting for native item ownership and save registration";
            var deadline = Time.realtimeSinceStartup + 30f;
            var lastReason = "waiting for the native starter set";
            while (IsCurrent(selected) && Time.realtimeSinceStartup < deadline)
            {
                var placementAttempted = false;
                foreach (var item in CollectItems(selected.StarterSet.transform))
                    Capture(item);
                var nativeReady = NativeItemsReady(out lastReason);
                if (nativeReady && Captured.Count == 0)
                {
                    Plugin.Instance?.Report("Dockside starter supplies: the observed difficulty adjustment left an intentionally empty native set; no items were added.");
                    placementStage = "completed with intentionally empty starter set";
                    Disarm();
                    yield break;
                }
                if (nativeReady &&
                    Plugin.Instance.TryGetDocksideCargoSurface(out var ground, out var groundCollider))
                {
                    if (Camera.main == null ||
                        Vector3.Distance(Camera.main.transform.position, ground) > 500f)
                    {
                        lastReason = "the player camera has not arrived at the selected dock";
                        placementStage = lastReason;
                        yield return null;
                        continue;
                    }
                    placementAttempted = true;
                    layoutAttempts++;
                    placementStage = "planning a complete dockside layout";
                    if (TryPlaceAll(selected, ground, groundCollider, out lastReason))
                    {
                        var localGround = selected.Recovery.transform.InverseTransformPoint(ground);
                        placementStage = "waiting for native physics to settle the first layout";
                        // Allow the native physics body to take over before
                        // checking that every item is still at the wharf.
                        for (var frame = 0; frame < 50; frame++)
                            yield return new WaitForFixedUpdate();
                        if (!IsCurrent(selected)) yield break;
                        ground = GroundToWorld(selected, localGround);
                        if (TryConfirmAll(ground, out var failedItem,
                            out lastReason))
                        {
                            Plugin.Instance?.Report($"Dockside starter supplies initialized: {Captured.Count}/{Captured.Count} native items near the player. Sailwind now owns their physics and saves.");
                            ReportStarterCups();
                            placementStage = "completed";
                            Disarm();
                            yield break;
                        }
                        Plugin.Instance?.Warn("Dockside supplies shifted during startup (" + lastReason + "); making one placement retry.");
                        Vector3? avoidGround = null;
                        if (failedItem != null &&
                            PlannedGround.TryGetValue(failedItem, out var priorGround))
                            avoidGround = GroundToWorld(selected, priorGround);
                        if (avoidGround.HasValue)
                            Plugin.Instance?.Report("Trying alternate dockside ground for " +
                                AuthoredName(failedItem) + " away from " +
                                avoidGround.Value + ".");
                        var retryPlaced = TryPlaceAll(selected, ground,
                            groundCollider, out lastReason, failedItem,
                            avoidGround);
                        if (!retryPlaced && avoidGround.HasValue &&
                            Captured.Any(IsNativeOar) &&
                            !IsNativeOar(failedItem))
                        {
                            // A small wharf can have no second free position
                            // for the failed item. Re-place the exact native
                            // set on the verified first layout rather than
                            // leave items where the first physics settle sent
                            // them when the alternate plan could not fit.
                            Plugin.Instance?.Warn("Alternate dockside supply layout unavailable (" +
                                lastReason + "); retrying the native items without the failed-point exclusion.");
                            retryPlaced = TryPlaceAll(selected, ground,
                                groundCollider, out lastReason);
                        }
                        if (retryPlaced)
                        {
                            placementStage = "waiting for native physics to settle the retry";
                            for (var frame = 0; frame < 50; frame++)
                                yield return new WaitForFixedUpdate();
                            if (!IsCurrent(selected)) yield break;
                            ground = GroundToWorld(selected, localGround);
                            if (TryConfirmAll(ground, out _, out lastReason))
                            {
                                Plugin.Instance?.Report($"Dockside starter supplies initialized after retry: {Captured.Count}/{Captured.Count} native items near the player. Sailwind now owns their physics and saves.");
                                ReportStarterCups();
                                placementStage = "completed after retry";
                                Disarm();
                                yield break;
                            }
                        }
                        FailAndDisarm("items did not remain grouped after the final initialization attempt: " +
                            lastReason);
                        yield break;
                    }
                }
                else if (lastReason == null)
                    lastReason = Plugin.Instance.CargoSurfaceStatus ?? "the loaded dockside ground has not resolved";
                placementStage = lastReason;
                // A narrow dock can require many physics probes. Give scene
                // loading and boat settle time before trying the same layout.
                if (placementAttempted)
                    yield return new WaitForSecondsRealtime(0.25f);
                else
                    yield return null;
            }
            if (ReferenceEquals(armed, selected))
            {
                FailAndDisarm(Time.realtimeSinceStartup >= deadline ?
                    $"setup did not complete within 30 seconds ({lastReason}); live items: {CountLive()}/{Captured.Count}" :
                    "the selected new-game state changed before placement completed: " + placementStage);
            }
        }

        // Native Mug.Update can spill before the captured starter set has
        // reached the selected dock. Preserve that existing liquid only for
        // this bounded setup; never refill a cup or change its liquid fields.
        [HarmonyPatch(typeof(Mug), "Spill")]
        private static class ProtectStarterCupLiquidPatch
        {
            private static bool Prefix(Mug __instance)
            {
                if (armed == null || __instance == null) return true;
                var item = __instance.GetComponent<ShipItemBottle>();
                if (item == null || !Captured.Contains(item)) return true;
                if (item.held != null)
                {
                    ReleaseForCupPickup();
                    return true;
                }
                return !ShouldProtectCupLiquid(true, true, GameState.currentlyLoading,
                    false, Time.realtimeSinceStartup, cupProtectionEndsAt);
            }
        }

        [HarmonyPatch(typeof(ShipItem), "OnPickup")]
        private static class ReleaseStarterCupOnPickupPatch
        {
            private static void Prefix(ShipItem __instance)
            {
                if (armed != null && __instance != null &&
                    Captured.Contains(__instance) && IsNativeCup(__instance))
                    ReleaseForCupPickup();
            }
        }

        private static void ReleaseForCupPickup()
        {
            // Cancel the whole pending layout/retry so it cannot move the cup
            // after the player has claimed it, even if it is dropped again.
            placementStage = "player picked up a starter cup; native item control restored";
            Disarm();
        }

        private static bool ShouldProtectCupLiquid(bool active, bool captured,
            bool loading, bool held, float now, float expiresAt) =>
            active && captured && !loading && !held && now < expiresAt;

        private static bool IsNativeCup(ShipItem item) =>
            item is ShipItemBottle && item.GetComponent<Mug>() != null;

        private static bool IsCupUpright(float upY) => upY >= 0.98f;

        private static Quaternion CupUprightRotation(Quaternion authored)
        {
            // Keep the authored heading, but remove pitch and roll. Pure
            // quaternion components also make this startup pose testable
            // without requiring a running Unity engine.
            var yaw = Math.Atan2(2d * (authored.w * authored.y + authored.x * authored.z),
                1d - 2d * (authored.x * authored.x + authored.y * authored.y));
            return new Quaternion(0f, (float)Math.Sin(yaw * 0.5d), 0f,
                (float)Math.Cos(yaw * 0.5d));
        }

        private static Quaternion PlacementRotation(ShipItem item)
        {
            var authored = AuthoredRotations.TryGetValue(item, out var rotation) ?
                rotation : item.transform.rotation;
            if (IsNativeCup(item)) return CupUprightRotation(authored);
            return IsNativeOar(item) ? authored : item.transform.rotation;
        }

        private static void ReportStarterCups()
        {
            foreach (var item in Captured.Where(IsNativeCup))
                Plugin.Instance?.Report("Native starter cup settled upright: " +
                    AuthoredName(item) + $"; up {item.transform.up.y:F3}, remaining native liquid level {item.health}, liquid type {item.amount}.");
        }

        [HarmonyPatch(typeof(ShipItem), "DestroyItem")]
        private static class ProtectNativeStarterItemsPatch
        {
            private static bool Prefix(ShipItem __instance)
            {
                if (difficultyAdjustmentScope != null &&
                    ReferenceEquals(difficultyAdjustmentScope, armed) && !rosterFinalized &&
                    __instance != null && Captured.Contains(__instance) &&
                    __instance.transform.parent == armed.StarterSet.transform)
                {
                    DifficultyRemovalRequests.Add(__instance);
                    return true;
                }
                if (armed == null || !Captured.Contains(__instance) ||
                    __instance == null || !__instance.sold ||
                    __instance.currentWalkCol != null || GameState.recovering)
                    return true;
                var body = __instance.GetItemRigidbody();
                if (body == null || BodyOutOfRangeField == null ||
                    !(bool)BodyOutOfRangeField.GetValue(body))
                    return true;
                // ItemRigidbody destroys sold, unembarked items after ten
                // out-of-range frames. The source starter set may still be at
                // its distant home port while its selected port loads.
                return false;
            }
        }

        private static void Capture(ShipItem item)
        {
            if (rosterFinalized || ReferenceEquals(item, null) || item == null) return;
            if (IsIntentionalScrambledSeasMap(item))
            {
                if (!scrambledSeasMapSkipped)
                {
                    Plugin.Instance?.Report("Scrambled Seas will remove this region's starter map; excluding that one native object from the dockside supply count.");
                    scrambledSeasMapSkipped = true;
                }
                return;
            }
            if (!Captured.Add(item)) return;
            if (item != null)
            {
                AuthoredPositions[item] = item.transform.position;
                AuthoredRotations[item] = item.transform.rotation;
                // ShipItem declares its own `new string name`, hiding
                // Component.name. The native starter objects are named on
                // their GameObjects (for example, "10 barrel water").
                AuthoredNames[item] = item.gameObject.name;
            }
        }

        private static string AuthoredName(ShipItem item)
        {
            if (ReferenceEquals(item, null)) return "<null>";
            if (AuthoredNames.TryGetValue(item, out var name)) return name;
            return item == null ? "<destroyed>" : item.gameObject.name;
        }

        private static bool AuthoredNameContains(ShipItem item, string value) =>
            AuthoredName(item).IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string CapturedNames() =>
            string.Join(", ", Captured.Select(AuthoredName)
                .OrderBy(name => name, StringComparer.Ordinal).ToArray());

        private static bool IsIntentionalScrambledSeasMap(ShipItem item)
        {
            var set = armed?.StarterSet?.transform;
            if (set == null || item.transform.parent != set ||
                !IsScrambledSeasEnabled()) return false;
            // Scrambled Seas remembers the last direct child whose name
            // contains "map", then schedules GameObject.Destroy on it before
            // Sailwind's regional starter coroutine begins.
            for (var index = set.childCount - 1; index >= 0; index--)
            {
                var child = set.GetChild(index);
                if (child.name.IndexOf("map",
                    StringComparison.OrdinalIgnoreCase) < 0) continue;
                return child == item.transform;
            }
            return false;
        }

        private static ShipItem[] CollectItems(Transform set)
        {
            if (set == null) return Array.Empty<ShipItem>();
            var result = new List<ShipItem>();
            for (var index = 0; index < set.childCount; index++)
            {
                var item = set.GetChild(index).GetComponent<ShipItem>();
                if (item != null) result.Add(item);
            }
            return result.ToArray();
        }

        private static bool IsCurrent(ResolvedStart selected)
        {
            try
            {
                var current = ReferenceEquals(armed, selected) && Plugin.Instance != null &&
                    selected.StarterSet != null && selected.Boat != null &&
                    selected.Boat.isPurchased() && GameState.playing &&
                    !GameState.currentlyLoading &&
                    GameState.newGameRegion == selected.StarterSet.region &&
                    FixedStart.IsSelectionStillActive(selected.Menu, selected);
                if (!current && ReferenceEquals(armed, selected))
                {
                    placementStage = $"start-state changed (playing {GameState.playing}, loading {GameState.currentlyLoading}, " +
                        $"region {GameState.newGameRegion}, starter set present {selected.StarterSet != null}, boat present {selected.Boat != null})";
                    if (GameState.currentlyLoading || !GameState.playing)
                        Disarm();
                    else
                        FailAndDisarm(placementStage);
                }
                return current;
            }
            catch (Exception exception)
            {
                Plugin.Instance?.Warn("Dockside supply setup ended after a start-state change: " +
                    exception.Message);
                return false;
            }
        }

        private static bool NativeItemsReady(out string reason)
        {
            reason = null;
            ReconcileDifficultyRemovals();
            RemoveIntentionalScrambledSeasMap();
            if (difficultyAdjustmentFailed)
            {
                reason = "the Sailwind Difficulty starter adjustment failed before its roster could be confirmed";
                return false;
            }
            if (DifficultyRemovalRequests.Count > 0)
            {
                reason = "waiting for the exact Difficulty-requested native item removals to finish";
                return false;
            }
            if (Captured.Count == 0 && IsIntentionalEmptyRoster(nativeStarterCompleted,
                difficultyAdjustmentCompleted, difficultyRemovalsApplied))
                return true;
            if (Captured.Count == 0 || RegisteredField == null)
            {
                reason = "native starter items or save-registration field are unavailable";
                return false;
            }
            foreach (var item in Captured)
            {
                if (item == null)
                {
                    reason = "an unaccounted captured starter item was destroyed: " + AuthoredName(item);
                    return false;
                }
                if (item.GetItemRigidbody() == null || !item.sold)
                {
                    reason = "native body or sale has not completed for " + AuthoredName(item);
                    return false;
                }
                var saveable = item.GetComponent<SaveablePrefab>();
                if (saveable == null || !(bool)RegisteredField.GetValue(saveable))
                {
                    reason = "native save registration has not completed for " + AuthoredName(item);
                    return false;
                }
            }
            return true;
        }

        private static void ReconcileDifficultyRemovals()
        {
            var removed = ConfirmedIntentionalRemovals(Captured, DifficultyRemovalRequests,
                item => item == null, difficultyAdjustmentCompleted && !difficultyAdjustmentFailed,
                rosterFinalized);
            if (removed.Length == 0) return;
            var names = removed.Select(AuthoredName).ToArray();
            foreach (var item in removed)
            {
                Captured.Remove(item);
                AuthoredPositions.Remove(item);
                AuthoredRotations.Remove(item);
                AuthoredNames.Remove(item);
                DifficultyRemovalRequests.Remove(item);
                difficultyRemovalsApplied++;
            }
            Plugin.Instance?.Report("Sailwind Difficulty intentionally removed " +
                string.Join(", ", names) + $"; remaining starter roster {Captured.Count} native items.");
        }

        private static T[] ConfirmedIntentionalRemovals<T>(IEnumerable<T> captured,
            ISet<T> requested, Func<T, bool> destroyed, bool adjustmentCompleted, bool finalized) =>
            !adjustmentCompleted || finalized ? new T[0] :
                captured.Where(item => requested.Contains(item) && destroyed(item)).ToArray();

        private static bool IsIntentionalEmptyRoster(bool starterCompleted,
            bool adjustmentCompleted, int observedRemovals) =>
            starterCompleted && adjustmentCompleted && observedRemovals > 0;

        private static bool IsCurrentCapture(object currentSelection, object ownerSelection, bool finalized) =>
            currentSelection != null && ReferenceEquals(currentSelection, ownerSelection) && !finalized;

        private static void RemoveIntentionalScrambledSeasMap()
        {
            if (rosterFinalized || !wrapperObserved || !IsScrambledSeasEnabled()) return;
            var removedMaps = Captured.Where(item => item == null &&
                AuthoredNames.TryGetValue(item, out var name) &&
                name.IndexOf("map", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();
            // Scrambled Seas removes one selected region starter map via
            // GameObject.Destroy in its StarterSet prefix. Ignore only that
            // exact destroyed map identity, never another lost supply.
            if (removedMaps.Length != 1) return;
            var map = removedMaps[0];
            Captured.Remove(map);
            AuthoredPositions.Remove(map);
            AuthoredRotations.Remove(map);
            AuthoredNames.Remove(map);
            Plugin.Instance?.Report("Scrambled Seas intentionally removed the native regional starter map; the remaining starter set is intact.");
        }

        private static bool IsScrambledSeasEnabled()
        {
            if (!Chainloader.PluginInfos.TryGetValue(ScrambledSeasIntegration.Id,
                out var info)) return false;
            var type = info.Instance?.GetType().Assembly.GetType("ScrambledSeas.Main",
                false);
            var flag = type?.GetField("pluginEnabled",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return flag != null && flag.FieldType == typeof(bool) &&
                (bool)flag.GetValue(null);
        }

        private static bool TryPlaceAll(ResolvedStart selected, Vector3 ground,
            Collider groundCollider, out string reason,
            ShipItem avoidItem = null, Vector3? avoidGround = null)
        {
            reason = null;
            if (!NativeItemsReady(out reason)) return false;
            RefreshBoatWalkRoots();
            if (!IsVerifiedLandCollider(groundCollider))
            {
                reason = "the player's resolved surface is not stationary dock or shore ground: " +
                    (groundCollider == null ? "<none>" : groundCollider.name);
                return false;
            }
            Physics.SyncTransforms();
            if (!Plugin.Instance.TryProbeDocksideCargoSurface(ground,
                    out var liveGround, out var liveCollider) ||
                liveCollider != groundCollider ||
                !IsVerifiedLandCollider(liveCollider) ||
                Vector3.Distance(liveGround, ground) > 0.3f)
            {
                reason = "the player's resolved dockside surface changed before starter items moved";
                return false;
            }
            var items = Captured.ToArray();
            var barrel = items.FirstOrDefault(item => AuthoredNameContains(item, "barrel"));
            // Another mod can replace the region's starter set. Use its first
            // native item as the layout anchor when there is no water barrel.
            var anchor = barrel ?? items[0];
            var foodSupport = items.FirstOrDefault(item => item != barrel &&
                AuthoredNameContains(item, "table"));
            if (foodSupport == null)
                foodSupport = items.FirstOrDefault(item => item != barrel &&
                    AuthoredNameContains(item, "crate"));
            if (foodSupport == anchor) foodSupport = null;

            if (!TryChooseGround(ground, groundCollider, anchor, null,
                anchor == avoidItem ? avoidGround : null,
                out var anchorGround,
                out reason, true))
            {
                // All currently usable alternatives have been tried. Let a
                // later scene/boat settle re-evaluate them within the same
                // bounded startup deadline.
                RejectedAnchorGround.Clear();
                return false;
            }
            var foodGround = Vector3.zero;
            if (foodSupport != null &&
                !TryChooseGround(ground, groundCollider, foodSupport,
                    anchorGround,
                    foodSupport == avoidItem ? avoidGround : null,
                    out foodGround,
                    out reason))
            {
                RejectAnchorGround(selected, anchorGround);
                return false;
            }

            var authoredAnchor = AuthoredPositions[anchor];
            var authoredFood = foodSupport == null ? Vector3.zero :
                AuthoredPositions[foodSupport];
            var reserved = new List<GroundSlot>
            {
                new GroundSlot
                {
                    Point = anchorGround, Radius = FootprintRadius(anchor),
                    LargeSupport = true
                }
            };
            if (foodSupport != null)
                reserved.Add(new GroundSlot
                {
                    Point = foodGround, Radius = FootprintRadius(foodSupport),
                    LargeSupport = true
                });
            var moves = new List<CargoMove>(items.Length);
            var planned = new Dictionary<ShipItem, Vector3>(ReferenceComparer);
            // Reserve space for the native oar before the small loose items.
            // At Dead Cove a full retry could otherwise run out of dock before
            // reaching the oar, leaving the first, already shifted layout in
            // place. The barrel mesh is not a reliable physics shelf.
            foreach (var item in items.OrderBy(item => item == anchor ? 0 :
                item == foodSupport ? 1 : IsNativeOar(item) ? 2 : 3))
            {
                var body = item.GetItemRigidbody();
                if (body == null || body.GetBody() == null)
                {
                    reason = "the native physics body is unavailable for " + AuthoredName(item);
                    return false;
                }
                var itemGround = anchorGround;
                // The native oar is authored standing nearly upright. Place
                // it in that original pose so its physics can settle on the
                // dock without requiring a pre-cleared horizontal footprint.
                var itemRotation = PlacementRotation(item);
                if (item == foodSupport) itemGround = foodGround;
                else if (item != anchor)
                {
                    var groupGround = NearerToFood(item, authoredAnchor,
                        authoredFood, foodSupport) ? foodGround : anchorGround;
                    var foundGround = TryChooseLooseGround(item, groupGround, ground,
                        reserved, item == avoidItem ? avoidGround : null,
                        out itemGround, out itemRotation);
                    if (!foundGround && IsNativeOar(item) && foodSupport != null)
                        foundGround = TryChooseLooseGround(item,
                            groupGround == foodGround ? anchorGround : foodGround,
                            ground, reserved,
                            item == avoidItem ? avoidGround : null,
                            out itemGround, out itemRotation);
                    if (!foundGround)
                    {
                        reason = "no reachable dockside ground near the starter group for " +
                            AuthoredName(item);
                        RejectAnchorGround(selected, anchorGround);
                        return false;
                    }
                    reserved.Add(new GroundSlot
                    {
                        Point = IsNativeOar(item) ?
                            OarBaseGround(item, itemGround, itemRotation) :
                            itemGround,
                        Radius = LooseFootprintRadius(item)
                    });
                }
                planned[item] = itemGround;
                var destination = PutBottomOnSurface(item, itemGround,
                    itemRotation);
                moves.Add(new CargoMove
                {
                    Item = item, Body = body, Position = destination,
                    Rotation = itemRotation,
                    OriginalPosition = item.transform.position,
                    OriginalRotation = item.transform.rotation
                });
            }
            try
            {
                if (ExitBoatMethod == null || FloatingOriginManager.instance == null ||
                    StayedEmbarkField == null || BodyOutOfRangeField == null ||
                    BodyDestroyFramesField == null || BodyDistanceTimerField == null)
                {
                    reason = "required native item ownership or physics fields are unavailable";
                    return false;
                }
                var world = FloatingOriginManager.instance.transform;
                // Keep observing native additions while ground/layout is still
                // unresolved; freeze only when the complete plan begins moving.
                rosterFinalized = true;
                foreach (var move in moves)
                {
                    if (move.Item.currentActualBoat != null)
                        ExitBoatMethod.Invoke(move.Item, null);
                    else
                    {
                        move.Item.transform.SetParent(world, true);
                        move.Body.transform.SetParent(world, true);
                        var saveable = move.Item.GetComponent<SaveablePrefab>();
                        // ExitBoat performs this reset for boat items. A
                        // non-boat item still needs a world parent index if
                        // its prior native parent was a boat or house;
                        // PrepareSaveData uses positive indices as local
                        // coordinates relative to that parent.
                        if (saveable.GetParentObject() != -1)
                            saveable.SetParentObject(-1);
                    }
                    StayedEmbarkField.SetValue(move.Item, null);
                    move.Body.attached = false;
                    var rigidbody = move.Body.GetBody();
                    rigidbody.isKinematic = true;
                    rigidbody.velocity = Vector3.zero;
                    rigidbody.angularVelocity = Vector3.zero;
                    move.Item.transform.SetPositionAndRotation(move.Position, move.Rotation);
                    move.Body.ResetPos();
                    // Native distance checks are cached for 5-8 seconds. A
                    // body moved here from another archipelago can otherwise
                    // carry an old out-of-range flag into gameplay and be
                    // destroyed just after this setup releases it.
                    BodyOutOfRangeField.SetValue(move.Body, false);
                    BodyDestroyFramesField.SetValue(move.Body, 0);
                    BodyDistanceTimerField.SetValue(move.Body, 0f);
                }
                Physics.SyncTransforms();
                RejectedAnchorGround.Clear();
                PlannedGround.Clear();
                foreach (var plannedItem in planned)
                    PlannedGround[plannedItem.Key] = selected.Recovery.transform.InverseTransformPoint(plannedItem.Value);
                Plugin.Instance?.Report($"Dockside supply layout: {AuthoredName(anchor)} at {anchorGround}, " +
                    (foodSupport == null ? "no food support" :
                        $"food support {AuthoredName(foodSupport)} at {foodGround}") +
                    $", {moves.Count} registered items placed on ground from region {selected.Region}.");
                foreach (var move in moves.Where(move => IsNativeOar(move.Item)))
                    Plugin.Instance?.Report("Native oar placed on dockside ground at " +
                        planned[move.Item] + "; native rotation " +
                        move.Item.transform.rotation + ", item position " +
                        move.Item.transform.position + ".");
                return true;
            }
            catch (Exception exception)
            {
                Plugin.Instance?.Error("Dockside starter supply placement failed.", exception);
                reason = exception.Message;
                return false;
            }
        }

        private static bool NearerToFood(ShipItem item, Vector3 barrel,
            Vector3 food, ShipItem foodSupport)
        {
            if (foodSupport == null) return false;
            var original = AuthoredPositions[item];
            var barrelDistance = new Vector2(original.x - barrel.x,
                original.z - barrel.z).sqrMagnitude;
            var foodDistance = new Vector2(original.x - food.x,
                original.z - food.z).sqrMagnitude;
            return foodDistance < barrelDistance;
        }

        private static Vector3 PutBottomOnSurface(ShipItem item, Vector3 surface,
            Quaternion rotation)
        {
            var originalRotation = item.transform.rotation;
            try
            {
                // The native oar may have toppled before relocation. Measure
                // its bottom after restoring the captured upright rotation.
                if (rotation != originalRotation)
                {
                    item.transform.rotation = rotation;
                    Physics.SyncTransforms();
                }
                return new Vector3(surface.x,
                    surface.y + BottomOffset(item) + 0.04f, surface.z);
            }
            finally
            {
                if (rotation != originalRotation)
                {
                    item.transform.rotation = originalRotation;
                    Physics.SyncTransforms();
                }
            }
        }

        private static float BottomOffset(ShipItem item)
        {
            var collider = item.GetComponent<Collider>();
            if (collider != null && collider.enabled &&
                collider.gameObject.activeInHierarchy)
            {
                var bounds = collider.bounds;
                var offset = item.transform.position.y - bounds.min.y;
                if (!float.IsNaN(offset) && !float.IsInfinity(offset) &&
                    bounds.size.y > 0.02f &&
                    offset >= -0.1f && offset < 2f)
                    return Mathf.Clamp(offset, 0.05f, 1.5f);
            }
            return 0.35f;
        }

        private static float FootprintRadius(ShipItem item)
        {
            var collider = item.GetComponent<Collider>();
            if (collider != null && collider.enabled &&
                collider.gameObject.activeInHierarchy)
            {
                var bounds = collider.bounds;
                if (bounds.size.x > 0.02f && bounds.size.z > 0.02f)
                    return Mathf.Clamp(Mathf.Max(bounds.extents.x,
                        bounds.extents.z), 0.18f, 0.7f);
            }
            return 0.3f;
        }

        private static bool IsNativeOar(ShipItem item) =>
            AuthoredName(item).EndsWith(" oar",
                StringComparison.OrdinalIgnoreCase);

        private static float LooseFootprintRadius(ShipItem item) =>
            IsNativeOar(item) ? 0.4f : FootprintRadius(item);

        private static bool TryChooseLooseGround(ShipItem item, Vector3 groupGround,
            Vector3 playerGround, List<GroundSlot> reserved,
            Vector3? avoidGround, out Vector3 selected,
            out Quaternion selectedRotation)
        {
            selected = Vector3.zero;
            selectedRotation = PlacementRotation(item);
            var marker = armed?.Recovery?.transform;
            if (marker == null) return false;
            var directions = GroundDirections(marker);
            var radius = LooseFootprintRadius(item);
            var oar = IsNativeOar(item);
            // Narrow wharves may only have room lengthwise. Search farther
            // along the same ground while keeping every item within the
            // five-metre startup confirmation group around the player.
            foreach (var distance in new[]
            {
                0.6f, 0.9f, 1.2f, 1.55f, 1.9f, 2.3f, 2.7f, 3.2f, 3.7f
            })
            foreach (var direction in directions)
            {
                if (!Plugin.Instance.TryProbeDocksideCargoSurface(groupGround +
                    direction * distance, out var point, out var collider) ||
                    !IsVerifiedLandCollider(collider) ||
                    Mathf.Abs(point.y - groupGround.y) > 0.45f ||
                    Vector3.Distance(point, playerGround) > 4.75f ||
                    avoidGround.HasValue &&
                    new Vector2(point.x - avoidGround.Value.x,
                        point.z - avoidGround.Value.z).magnitude < 1.1f ||
                    IsInsideEmbarkVolume(item, point) ||
                    (oar ? !HasOarBaseSupport(item, point,
                        selectedRotation) : !HasSupportFootprint(item, point)) ||
                    !HasCargoClearance(item, point, selectedRotation, collider))
                    continue;
                if (!IsSlotClear(new GroundSlot
                {
                    Point = oar ? OarBaseGround(item, point,
                        selectedRotation) : point,
                    Radius = radius
                }, reserved)) continue;
                selected = point;
                return true;
            }
            return false;
        }

        private static bool HasOarBaseSupport(ShipItem item,
            Vector3 pivotGround, Quaternion rotation)
        {
            var box = item.GetComponent<BoxCollider>();
            if (box == null) return HasSupportFootprint(item, pivotGround);
            // The native oar's collider center is below its pivot. Check the
            // bottom of its authored upright pose, not the whole length it
            // might occupy after falling. A compact base can fit a narrow dock.
            var basePoint = OarBaseGround(item, pivotGround, rotation);
            foreach (var sampleOffset in new[]
            {
                Vector3.zero, Vector3.right * 0.2f,
                Vector3.left * 0.2f, Vector3.forward * 0.2f,
                Vector3.back * 0.2f
            })
            {
                if (!Plugin.Instance.TryProbeDocksideCargoSurface(basePoint +
                        sampleOffset, out var surface, out var collider) ||
                    !IsVerifiedLandCollider(collider) ||
                    Mathf.Abs(surface.y - pivotGround.y) > 0.45f)
                    return false;
            }
            return !IsInsideEmbarkVolume(item, basePoint);
        }

        private static Vector3 OarBaseGround(ShipItem item,
            Vector3 pivotGround, Quaternion rotation)
        {
            var box = item.GetComponent<BoxCollider>();
            if (box == null) return pivotGround;
            // Both installed regional oars have local +Y pointing upward in
            // their authored pose. Resolve the lower end from the captured
            // world rotation as well, in case another set rotates the prefab.
            var lowerSign = (rotation * Vector3.up).y >= 0f ? -1f : 1f;
            var localBottom = box.center + Vector3.up *
                (box.size.y * 0.5f * lowerSign);
            var offset = rotation * Vector3.Scale(localBottom,
                item.transform.lossyScale);
            return pivotGround + new Vector3(offset.x, 0f, offset.z);
        }

        private static bool IsSlotClear(GroundSlot candidate,
            List<GroundSlot> reserved)
        {
            foreach (var slot in reserved)
            {
                var horizontal = new Vector2(candidate.Point.x - slot.Point.x,
                    candidate.Point.z - slot.Point.z);
                var spacing = slot.LargeSupport ? 1.15f : 0.9f;
                if (horizontal.magnitude <
                    (candidate.Radius + slot.Radius) * spacing)
                    return false;
            }
            return true;
        }
        private static Vector3[] GroundDirections(Transform marker)
        {
            var forward = marker.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();
            var startingAngle = Mathf.Atan2(forward.z, forward.x);
            var result = new Vector3[16];
            for (var index = 0; index < result.Length; index++)
            {
                var angle = startingAngle + index * Mathf.PI * 2f / result.Length;
                result[index] = new Vector3(Mathf.Cos(angle), 0f,
                    Mathf.Sin(angle));
            }
            return result;
        }

        private static bool TryChooseGround(Vector3 playerGround,
            Collider playerCollider, ShipItem support, Vector3? otherGroup,
            Vector3? avoidGround,
            out Vector3 selected, out string reason, bool skipRejectedAnchors = false)
        {
            selected = Vector3.zero;
            reason = "no nearby clear wharf/shore point supports " +
                AuthoredName(support);
            var marker = armed?.Recovery?.transform;
            if (marker == null) return false;
            var directions = GroundDirections(marker);
            var radii = otherGroup.HasValue ?
                new[] { 1.9f, 2.3f, 2.7f, 3.2f } :
                new[] { 1.1f, 1.35f, 1.7f, 2.0f, 2.4f };
            var bestScore = float.PositiveInfinity;
            foreach (var radius in radii)
            foreach (var direction in directions)
            {
                var sample = playerGround + direction * radius;
                if (!Plugin.Instance.TryProbeDocksideCargoSurface(sample, out var surface,
                    out var collider) || !IsVerifiedLandCollider(collider)) continue;
                if (Mathf.Abs(surface.y - playerGround.y) > 0.45f ||
                    otherGroup.HasValue &&
                    Vector3.Distance(surface, otherGroup.Value) < 1.35f ||
                    avoidGround.HasValue &&
                    new Vector2(surface.x - avoidGround.Value.x,
                        surface.z - avoidGround.Value.z).magnitude < 0.9f)
                    continue;
                if (skipRejectedAnchors && RejectedAnchorGround.Any(point =>
                    AreAnchorCandidatesNear(surface, GroundToWorld(armed, point)))) continue;
                if (IsInsideEmbarkVolume(support, surface)) continue;
                if (!HasSupportFootprint(support, surface) ||
                    !HasCargoClearance(support, surface, PlacementRotation(support), collider)) continue;
                // Adjacent planks may be separate colliders, but the surface
                // should have comparable height and stay near the player.
                var score = radius + Mathf.Abs(surface.y - playerGround.y) * 3f +
                    (collider == playerCollider ? 0f : 0.2f);
                if (score >= bestScore) continue;
                bestScore = score;
                selected = surface;
            }
            return !float.IsPositiveInfinity(bestScore);
        }

        private static void RejectAnchorGround(ResolvedStart selected, Vector3 ground)
        {
            // A greedy first anchor can leave no complete layout even when
            // the same dock has room elsewhere. Advance the next attempt to
            // another anchor instead of repeating that exact dead end.
            if (RejectedAnchorGround.Count >= 12) RejectedAnchorGround.Clear();
            RejectedAnchorGround.Add(selected.Recovery.transform.InverseTransformPoint(ground));
        }

        private static bool AreAnchorCandidatesNear(Vector3 left, Vector3 right) =>
            new Vector2(left.x - right.x, left.z - right.z).sqrMagnitude < 0.1225f;

        private static bool HasCargoClearance(ShipItem item, Vector3 surface,
            Quaternion rotation, Collider ground)
        {
            // A supply needs its own physical space, not the player's taller
            // capsule. Keep fixture/hull collision checks when probing cargo
            // ground without the player-clearance policy.
            var source = item.GetComponent<Collider>();
            if (source == null || !source.enabled) return false;
            var previousRotation = item.transform.rotation;
            try
            {
                if (rotation != previousRotation)
                {
                    item.transform.rotation = rotation;
                    Physics.SyncTransforms();
                }
                var bounds = CargoClearanceBounds(source.bounds,
                    item.transform.position, surface, BottomOffset(item));
                foreach (var overlap in Physics.OverlapBox(bounds.center, bounds.extents,
                    Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (overlap == null || overlap == ground ||
                        overlap.CompareTag("Player") ||
                        overlap.bounds.max.y <= surface.y + 0.08f) continue;
                    var otherItem = overlap.GetComponentInParent<ShipItem>();
                    var otherBody = overlap.GetComponentInParent<ItemRigidbody>();
                    if (otherItem != null && Captured.Contains(otherItem) ||
                        otherBody != null && Captured.Contains(otherBody.GetShipItem())) continue;
                    return false;
                }
                return true;
            }
            finally
            {
                if (rotation != previousRotation)
                {
                    item.transform.rotation = previousRotation;
                    Physics.SyncTransforms();
                }
            }
        }

        private static Bounds CargoClearanceBounds(Bounds source, Vector3 itemPosition,
            Vector3 surface, float bottomOffset) =>
            new Bounds(surface + Vector3.up * (bottomOffset + 0.04f) +
                (source.center - itemPosition), source.size);

        private static bool HasSupportFootprint(ShipItem support, Vector3 center)
        {
            if (!IsNativeCup(support)) return HasSupportFootprintCore(support, center);
            var original = support.transform.rotation;
            try
            {
                support.transform.rotation = PlacementRotation(support);
                Physics.SyncTransforms();
                return HasSupportFootprintCore(support, center);
            }
            finally
            {
                support.transform.rotation = original;
                Physics.SyncTransforms();
            }
        }

        private static bool HasSupportFootprintCore(ShipItem support, Vector3 center)
        {
            // Test under the base's four edges, not just its pivot. Clamp
            // unusually large modded collider bounds to a local footprint so
            // oversized interaction colliders do not make every dock fail.
            var collider = support.GetComponent<Collider>();
            var bounds = collider != null && collider.enabled &&
                collider.gameObject.activeInHierarchy ? collider.bounds :
                new Bounds(support.transform.position, Vector3.one * 0.7f);
            var x = Mathf.Clamp(bounds.extents.x * 0.7f, 0.2f, 0.55f);
            var z = Mathf.Clamp(bounds.extents.z * 0.7f, 0.2f, 0.55f);
            var offsets = new[]
            {
                new Vector3(x, 0f, 0f), new Vector3(-x, 0f, 0f),
                new Vector3(0f, 0f, z), new Vector3(0f, 0f, -z)
            };
            // Open cups need a level base so native gravity will not tip
            // their initial water out on a steep or uneven shore patch.
            var maxHeightDifference = IsNativeCup(support) ? 0.02f : 0.45f;
            foreach (var offset in offsets)
            {
                if (!Plugin.Instance.TryProbeDocksideCargoSurface(center + offset,
                    out var edge, out var edgeCollider) ||
                    !IsVerifiedLandCollider(edgeCollider) ||
                    Mathf.Abs(edge.y - center.y) > maxHeightDifference)
                    return false;
            }
            return true;
        }

        private static bool TryConfirmAll(Vector3 ground,
            out ShipItem failedItem, out string reason)
        {
            failedItem = null;
            reason = null;
            foreach (var item in Captured.OrderBy(item =>
                IsNativeOar(item) ? 0 : 1))
            {
                if (item == null || !item.gameObject.activeInHierarchy)
                {
                    failedItem = item;
                    reason = "a starter item disappeared after placement";
                    return false;
                }
                if (IsNativeCup(item) && !IsCupUpright(item.transform.up.y))
                {
                    failedItem = item;
                    reason = "the native starter cup did not settle upright: " +
                        AuthoredName(item) + $" (up {item.transform.up.y:F3}, remaining liquid level {item.health})";
                    return false;
                }
                var body = item.GetItemRigidbody();
                if (body == null || body.GetBody() == null)
                {
                    failedItem = item;
                    reason = "a starter item lost its native physics body: " + AuthoredName(item);
                    return false;
                }
                if ((bool)BodyOutOfRangeField.GetValue(body) ||
                    (int)BodyDestroyFramesField.GetValue(body) != 0)
                {
                    failedItem = item;
                    reason = "a starter item retained a distant native physics state: " +
                        AuthoredName(item);
                    return false;
                }
                var saveable = item.GetComponent<SaveablePrefab>();
                if (saveable == null || saveable.GetParentObject() != -1)
                {
                    failedItem = item;
                    reason = "a starter item is still parented away from land: " +
                        AuthoredName(item);
                    return false;
                }
                var point = item.transform.position;
                var horizontal = new Vector2(point.x - ground.x, point.z - ground.z);
                // Candidate centers stay within 4.75m; allow some native
                // physics settling while still requiring local land support.
                if (horizontal.magnitude > 5.5f ||
                    point.y > ground.y + 3.5f || item.currentActualBoat != null)
                {
                    failedItem = item;
                    reason = "a starter item is outside the dockside group: " + AuthoredName(item) +
                        " at " + point;
                    return false;
                }
                if (!Plugin.Instance.TryProbeDocksideCargoSurface(point,
                        out var itemGround, out var support) ||
                    !IsVerifiedLandCollider(support))
                {
                    failedItem = item;
                    reason = "a starter item lost verified dockside support: " +
                        AuthoredName(item) + " at " + point;
                    return false;
                }
                if (IsInsideEmbarkVolume(item, itemGround) ||
                    Mathf.Abs(itemGround.y - ground.y) > 0.5f ||
                    point.y < itemGround.y - 0.1f ||
                    point.y > itemGround.y + 2f)
                {
                    failedItem = item;
                    reason = "a starter item settled away from its dockside surface: " +
                        AuthoredName(item) + " at " + point +
                        ", surface " + itemGround + " on " + support.name;
                    return false;
                }
                if (IsNativeOar(item))
                {
                    // Let the oar fall however native physics chooses, then
                    // verify that its body still rests on reachable land.
                    // A midpoint probe is enough here; demanding support
                    // under both ends would reject useful narrow-dock falls.
                    var oarCollider = item.GetComponent<Collider>();
                    var bodyCenter = oarCollider == null ? point :
                        oarCollider.bounds.center;
                    if (oarCollider == null ||
                        oarCollider.bounds.min.y < itemGround.y - 0.25f ||
                        oarCollider.bounds.min.y > itemGround.y + 0.5f ||
                        !Plugin.Instance.TryProbeDocksideCargoSurface(bodyCenter,
                            out var bodyGround, out var bodySupport) ||
                        !IsVerifiedLandCollider(bodySupport) ||
                        Mathf.Abs(bodyGround.y - itemGround.y) > 0.5f)
                    {
                        failedItem = item;
                        reason = "the native oar did not settle on reachable " +
                            "dockside ground at " + point +
                            ", collider center " + bodyCenter;
                        return false;
                    }
                }
            }
            return true;
        }

        private static void RefreshBoatWalkRoots()
        {
            BoatWalkRoots.Clear();
            foreach (var boat in Resources.FindObjectsOfTypeAll<BoatRefs>())
                if (boat != null && boat.walkCol != null)
                    BoatWalkRoots.Add(boat.walkCol);
        }

        private static bool IsVerifiedLandCollider(Collider collider)
        {
            if (collider == null || !collider.gameObject.activeInHierarchy ||
                !collider.gameObject.scene.isLoaded || collider.isTrigger ||
                collider.attachedRigidbody != null ||
                Plugin.IsUnsuitableStartSurface(collider) ||
                collider.CompareTag("Boat") || collider.CompareTag("Player"))
                return false;
            var transform = collider.transform;
            if (transform.GetComponentInParent<BoatRefs>() != null ||
                transform.GetComponentInParent<BoatMooringRopes>() != null ||
                transform.GetComponentInParent<PurchasableBoat>() != null ||
                transform.GetComponentInParent<ShipItem>() != null ||
                transform.GetComponentInParent<ItemRigidbody>() != null ||
                collider.name.IndexOf("hull player collider",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            foreach (var root in BoatWalkRoots)
                if (root != null && (transform == root || transform.IsChildOf(root)))
                    return false;
            return true;
        }

        private static bool IsInsideEmbarkVolume(ShipItem item, Vector3 surface)
        {
            // ShipItem.OnTriggerEnter remembers every collider tagged
            // EmbarkCol, and its next native update reparents the item to a
            // boat. Probe the space a placed item occupies, not merely the
            // floor point. Any boat's active embark trigger is unsafe here.
            var horizontal = LooseFootprintRadius(item) + 0.15f;
            var center = surface + Vector3.up * 1.2f;
            var halfExtents = new Vector3(horizontal, 1.2f, horizontal);
            foreach (var overlap in Physics.OverlapBox(center, halfExtents,
                Quaternion.identity, ~0, QueryTriggerInteraction.Collide))
                if (overlap != null && overlap.CompareTag("EmbarkCol"))
                    return true;
            return false;
        }

        // Both the recovery marker and native loose items follow world shifts.
        // Store evidence/retry points relative to that marker across physics yields.
        private static Vector3 GroundToWorld(ResolvedStart selected, Vector3 localGround) =>
            selected?.Recovery == null ? localGround :
                selected.Recovery.transform.TransformPoint(localGround);

        private static int CountLive() => Captured.Count(item => item != null);
    }
}
