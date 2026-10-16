using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    internal sealed class ResolvedStart
    {
        public Port Port;
        public RecoveryPort Recovery;
        internal PortChoice Destination;
        internal StartBerthPlan Berth;
        internal bool UseShipyardBerth;
        internal Transform ShoreFrame => Recovery != null ? Recovery.transform : Destination?.DockRoot;
        internal IslandHorizon Island => Destination?.Island ?? Port?.GetComponentInParent<IslandHorizon>();
        public PurchasableBoat Boat;
        public SaveableObject Saveable;
        public Rigidbody Body;
        public BoatMooringRopes Ropes;
        public GPButtonDockMooring FrontMooring;
        public GPButtonDockMooring BackMooring;
        public StarterSet StarterSet;
        public StartMenu Menu;
        public int Region;
        public BoatSize? Size;
        internal StartingOptionsSettings Options;
        internal NativeBerth.Plan NativeBerth;
    }

    internal static partial class FixedStart
    {
        private sealed class RopePosition
        {
            public PickupableBoatMooringRope Rope;
            public Transform Parent;
            public Vector3 Position;
            public Quaternion Rotation;
            public GPButtonDockMooring Mooring;
        }

        private sealed class RopeCandidate
        {
            public PickupableBoatMooringRope Rope;
            public Vector3 LocalPosition;
            public float AlongHull;
            public float AcrossHull;
        }

        private static readonly FieldInfo CurrentRegionField = AccessTools.Field(typeof(StartMenu), "currentRegion");
        private static readonly FieldInfo StartingBoatsField = AccessTools.Field(typeof(StartMenu), "startingBoats");
        private static readonly FieldInfo SaveableField = AccessTools.Field(typeof(PurchasableBoat), "saveable");
        private static readonly FieldInfo PurchaseUiField = AccessTools.Field(typeof(PurchasableBoat), "purchaseUI");
        private static readonly FieldInfo MooredSpringField = AccessTools.Field(typeof(PickupableBoatMooringRope), "mooredToSpring");
        private static readonly FieldInfo RecoveryPortsField = AccessTools.Field(typeof(Recovery), "ports");
        private static readonly FieldInfo PlayerObserverField = AccessTools.Field(typeof(StartMenu), "playerObserver");
        private static readonly FieldInfo PlayerControllerField = AccessTools.Field(typeof(StartMenu), "playerController");
        private static readonly FieldInfo MenuAnimationsField = AccessTools.Field(typeof(StartMenu), "animsPlaying");
        private static StartPair pending;
        private static StartMenu pendingMenu;
        private static StartingOptionsSettings pendingOptions;
        private static StartPair acceptedButtonPair;
        private static StartMenu acceptedButtonMenu;
        private static GameObject nativeTweenObserver;
        private static Transform nativeTweenTarget;
        private static bool nativeTweenSeamReady;

        private static void ClearAcceptedButtonChoice()
        {
            acceptedButtonPair = null;
            acceptedButtonMenu = null;
        }

        // Sailwind's RegionConfirm button synchronously calls StartNewGame.
        // Guard at this caller: HarmonyX still runs every StartNewGame prefix
        // after one returns false, and Scrambled Seas' prefix moves the world.
        [HarmonyPatch(typeof(StartMenu), "ButtonClick", new[] { typeof(StartMenuButtonType) })]
        private static class ConfirmChoicePatch
        {
            private static bool Prefix(StartMenu __instance, StartMenuButtonType button)
            {
                if (button != StartMenuButtonType.RegionConfirm) return true;
                // Block at the caller before any StartNewGame prefix (including
                // Scrambled Seas world movement) can run a second attempt.
                if (TryGetStartBlock(out var blocked))
                { MenuUi.ActiveFor(__instance)?.ShowBlockedStart(blocked); return false; }
                ClearAcceptedButtonChoice();
                var plugin = Plugin.Instance;
                if (plugin == null || !ScrambledSeasIntegration.CanSelect) return true;
                MenuUi activeUi = null;
                try
                {
                    activeUi = MenuUi.ActiveFor(__instance);
                    if (activeUi == null) return true;
                    // Native ButtonClick returns before StartNewGame while a
                    // menu animation is running. Do not retain a choice then.
                    if (MenuAnimationsField?.GetValue(__instance) is int animations &&
                        animations > 0) return true;
                    if (!activeUi.CanStart(out var reason))
                    {
                        activeUi.ShowBlockedStart(reason);
                        plugin.Warn("New game blocked by New Beginnings selection: " + reason);
                        return false;
                    }
                    if (!plugin.TryChooseStart(out var pair, out reason))
                    {
                        activeUi.ShowBlockedStart(reason);
                        plugin.Warn("New game blocked by New Beginnings selection: " + reason);
                        return false;
                    }
                    // The menu lists every structurally usable pair. Check an
                    // ordinary port's recovery berth here, so a problem shows
                    // its reason before the start begins.
                    if (!pair.Port.NeedsPlannedBerth && !pair.UseShipyardBerth &&
                        !TryResolve(pair.Port, pair.Boat.Index, pair.Boat.Size, out _, out reason))
                    {
                        activeUi.ShowBlockedStart(reason);
                        plugin.Warn("New game blocked by New Beginnings selection: " + reason);
                        return false;
                    }
                    if (!nativeTweenSeamReady || !TryGetNativeStartActors(__instance, out _, out _))
                    {
                        activeUi.ShowBlockedStart("The native player-start sequence is unavailable.");
                        return false;
                    }
                    acceptedButtonPair = pair;
                    acceptedButtonMenu = __instance;
                    return true;
                }
                catch (Exception exception)
                {
                    ClearAcceptedButtonChoice();
                    activeUi?.ShowBlockedStart(exception.Message);
                    plugin.Error("New Beginnings selection failed; the new game was not started.", exception);
                    return false;
                }
            }

            private static void Postfix(StartMenu __instance)
            {
                // If ButtonClick returned without invoking StartNewGame, its
                // temporary choice must not leak into a later menu action.
                if (ReferenceEquals(acceptedButtonMenu, __instance))
                    ClearAcceptedButtonChoice();
            }
        }

        [HarmonyPatch(typeof(StartMenu), "StartNewGame")]
        [HarmonyBefore(ScrambledSeasIntegration.Id)]
        private static class BeginPatch
        {
            private static bool Prefix(StartMenu __instance)
            {
                if (TryGetStartBlock(out _)) return false;
                var buttonPair = ReferenceEquals(acceptedButtonMenu, __instance)
                    ? acceptedButtonPair : null;
                ClearAcceptedButtonChoice();
                pending = null;
                pendingMenu = null;
                pendingOptions = null;
                CancelStartWork();
                nativeTweenObserver = null;
                nativeTweenTarget = null;
                var plugin = Plugin.Instance;
                if (plugin == null || !ScrambledSeasIntegration.CanSelect) return true;

                object previousRegion = null;
                var regionUpdated = false;
                try
                {
                    var pair = buttonPair;
                    if (pair == null && !plugin.TryChooseStart(out pair, out var reason))
                    {
                        // Our Continue button accepts its pair before this call,
                        // so this caller started the new game without one (for
                        // example another mod calling StartNewGame directly).
                        // Leave that caller's start alone. Returning false could
                        // not stop it cleanly: HarmonyX still runs the other
                        // StartNewGame prefixes, and Scrambled Seas' prefix
                        // moves the world.
                        plugin.Error("New Beginnings could not choose a start for a new game it did not begin; that caller's start was left unchanged. " + reason, null);
                        return true;
                    }
                    if (!nativeTweenSeamReady)
                        throw new InvalidOperationException("The native player-start seam is unavailable.");
                    // Scrambled Seas may move every island, recovery marker and boat
                    // in its own prefix. Keep identities here; resolve coordinates and
                    // mutate the boat only in the selected coroutine factory.
                    previousRegion = CurrentRegionField.GetValue(__instance);
                    CurrentRegionField.SetValue(__instance, pair.Port.Region);
                    regionUpdated = true;
                    pending = pair;
                    pendingMenu = __instance;
                    pendingOptions = plugin.StartOptions.Copy();
                    if (!pendingOptions.TryValidate(out var optionsReason))
                        throw new InvalidOperationException(optionsReason);
                    pendingSnapshot = new StartSnapshot(__instance, (int)previousRegion);
                    flightGeneration = startGeneration;
                    plugin.Report($"New Beginnings selected port {pair.Port.Index} and boat scene {pair.Boat.Index}.");
                    MarkStartSelected();
                }
                catch (Exception exception)
                {
                    ClearStartTiming();
                    pending = null;
                    pendingMenu = null;
                    pendingOptions = null;
                    if (regionUpdated)
                    {
                        try { CurrentRegionField.SetValue(__instance, previousRegion); }
                        catch (Exception restoreException)
                        {
                            plugin.Error("Native start region could not be restored after selection failed.", restoreException);
                        }
                    }
                    CancelStart();
                    if (buttonPair == null)
                    {
                        // As above, false could not stop this caller's start.
                        plugin.Error("New Beginnings could not prepare a start for a new game it did not begin; that caller's start was left unchanged.", exception);
                        return true;
                    }
                    MenuUi.ActiveFor(__instance)?.ShowBlockedStart(exception.Message);
                    plugin.Error("The selected starting location could not be prepared. No regional start was substituted.", exception);
                    return false;
                }
                return true;
            }

            private static Exception Finalizer(Exception __exception)
            {
                if (__exception != null)
                {
                    CancelStartWork();
                    pending = null;
                    pendingMenu = null;
                    pendingOptions = null;
                }
                return __exception;
            }
        }

        [HarmonyPatch(typeof(StartMenu), "StartNewGame")]
        private static class EndPatch
        {
            private static void Postfix(StartMenu __instance)
            {
                // Scrambled Seas can abort an external scramble before it creates
                // its player coroutine. A normal start consumes pending synchronously.
                if (!ReferenceEquals(pendingMenu, __instance)) return;
                CancelStart();
                pending = null;
                pendingMenu = null;
                pendingOptions = null;
                Plugin.Instance?.Warn("New Beginnings selection was cleared because no player-start coroutine was created.");
            }
        }

        [HarmonyPatch(typeof(StartMenu), "MovePlayerToStartPos")]
        private static class StartPositionPatch
        {
            private static bool Prefix(StartMenu __instance, ref IEnumerator __result)
            {
                if (TryPreparePendingStart(__instance, out var sequence))
                { __result = sequence; return false; }
                return true;
            }
        }

        internal static bool TryGetNativeStartActors(StartMenu menu, out Transform observer,
            out GameObject controller)
        {
            observer = null;
            controller = null;
            try
            {
                observer = PlayerObserverField?.GetValue(menu) as Transform;
                controller = PlayerControllerField?.GetValue(menu) as GameObject;
                return observer != null && controller != null;
            }
            catch (Exception exception)
            {
                Plugin.Instance?.Error("Native start actor fields could not be read.", exception);
                return false;
            }
        }

        [HarmonyPatch]
        private static class NativeStartTweenPatch
        {
            private static MethodBase targetMethod;

            private static bool Prepare()
            {
                nativeTweenSeamReady = false;
                try
                {
                    var factory = AccessTools.Method(typeof(StartMenu), "MovePlayerToStartPos",
                        new[] { typeof(Transform) });
                    targetMethod = factory != null ? AccessTools.EnumeratorMoveNext(factory) : null;
                }
                catch (Exception exception)
                {
                    targetMethod = null;
                    Plugin.Instance?.Warn("Native player-start iterator could not be inspected: " +
                        exception.Message);
                }
                if (targetMethod != null) return true;
                Plugin.Instance?.Warn("Native player-start iterator is unavailable; " +
                    "the selected-port camera seam is disabled for this Sailwind build.");
                return false;
            }

            private static MethodBase TargetMethod() => targetMethod;

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source)
            {
                var original = AccessTools.Method(typeof(Juicebox), "TweenPosition",
                    new[] { typeof(GameObject), typeof(Vector3), typeof(float), typeof(JuiceboxTween) });
                var replacement = AccessTools.Method(typeof(FixedStart), nameof(NativeStartTweenOrSnap));
                var selectDuration = AccessTools.Method(typeof(FixedStart), nameof(NativeStartAnimationSeconds));
                var instructions = source.ToList();
                var matches = instructions.Where(instruction =>
                    (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                    Equals(instruction.operand, original)).ToArray();
                var durationSites = new List<int>();
                for (var index = 0; index < instructions.Count - 1; index++)
                {
                    if (instructions[index].opcode != OpCodes.Ldc_R4 ||
                        !(instructions[index].operand is float seconds) ||
                        seconds != 7f && seconds != 1f ||
                        instructions[index + 1].opcode != OpCodes.Stfld ||
                        !(instructions[index + 1].operand is FieldInfo field) ||
                        !field.Name.Contains("<animTime>")) continue;
                    durationSites.Add(index);
                }
                if (original == null || replacement == null || selectDuration == null ||
                    matches.Length != 1 || durationSites.Count != 2)
                {
                    nativeTweenSeamReady = false;
                    Plugin.Instance?.Warn("Native start tween and timing calls could not be isolated; Sailwind's original camera movement remains active.");
                    return instructions;
                }
                matches[0].opcode = OpCodes.Call;
                matches[0].operand = replacement;
                // Preserve Sailwind's seven-second build and one-second editor
                // values for ordinary starts. Our selected start skips this loop
                // after the observer and controller are placed at the target.
                foreach (var index in durationSites.OrderByDescending(value => value))
                    instructions.Insert(index + 1, new CodeInstruction(OpCodes.Call, selectDuration));
                nativeTweenSeamReady = true;
                return instructions;
            }
        }

        private static float NativeStartAnimationSeconds(float vanillaSeconds) =>
            nativeTweenObserver != null ? 0f : vanillaSeconds;

        private static void NativeStartTweenOrSnap(Juicebox tween, GameObject observer,
            Vector3 destination, float duration, JuiceboxTween easing)
        {
            if (nativeTweenObserver != null && observer == nativeTweenObserver)
            {
                if (nativeTweenTarget != null)
                    observer.transform.SetPositionAndRotation(nativeTweenTarget.position,
                        nativeTweenTarget.rotation);
                else
                    Plugin.Instance?.Warn("Selected player marker disappeared before the native camera tween; preserving the already teleported observer position.");
                Plugin.Instance?.DebugLog("Suppressed native new-game camera tween for the selected port.");
                nativeTweenObserver = null;
                nativeTweenTarget = null;
                return;
            }
            tween.TweenPosition(observer, destination, duration, easing);
        }

        private static void TeleportNativeStartObserver(StartMenu menu, Transform target)
        {
            try
            {
                if (!TryGetNativeStartActors(menu, out var observer, out var controller) ||
                    target == null ||
                    !IsFinite(target.position) || !IsFinite(target.rotation))
                {
                    Plugin.Instance.Warn("Native start observer or target is unavailable; Sailwind's normal camera movement will run.");
                    return;
                }
                // Native MovePlayerToStartPos otherwise tweens this observer for
                // seven seconds from the menu island to the selected port. Move
                // both objects before its iterator starts; the targeted iterator
                // patch then skips that tween and wait while retaining its grant,
                // disclaimer and save flow.
                observer.SetPositionAndRotation(target.position, target.rotation);
                controller.transform.SetPositionAndRotation(target.position, target.rotation);
                nativeTweenObserver = observer.gameObject;
                nativeTweenTarget = target;
                Plugin.Instance.DebugLog("Native new-game observer and controller moved directly to the selected port.");
            }
            catch (Exception exception)
            {
                Plugin.Instance.Error("Could not move the native start camera directly; Sailwind's normal camera movement will run.", exception);
            }
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(Quaternion value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        // Identity facts only, for catalog reservoir sampling. Berth placement
        // checks run when the player starts, where a failure shows its reason
        // instead of quietly removing the boat from the menu.
        internal static bool IsPairViable(PortChoice port, BoatChoice boat)
        {
            if (boat?.Boat == null || boat.Saveable == null || !boat.Saveable.gameObject.scene.isLoaded ||
                boat.Boat.isPurchased()) return false;
            var body = boat.Saveable.GetComponent<Rigidbody>();
            var ropes = boat.Saveable.GetComponent<BoatMooringRopes>();
            if (body == null || ropes == null) return false;
            if (port != null && port.NeedsPlannedBerth)
                return StartDestinations.IsCurrent(port) && StartBerthPlanner.CanInspect(body, ropes, out _);
            return port?.Port != null && port.Port.gameObject.scene.isLoaded && port.Recovery != null &&
                port.Recovery.parentPort == port.Port && port.Recovery.boatPos != null &&
                port.Recovery.mooringFront != null && port.Recovery.mooringBack != null;
        }

        // No boat mutation is permitted until these relocation-only guards pass.
        private static bool TryResolveRelocation(ResolvedStart selected, out string reason)
        {
            if (selected.UseShipyardBerth && ShipyardStart.TryPlan(selected.Destination, out var yardPlan, selected.Body, selected.Ropes))
            {
                selected.Berth = yardPlan;
                selected.Recovery = null;
                selected.FrontMooring = yardPlan.Front;
                selected.BackMooring = yardPlan.Back;
                reason = null;
                return true;
            }
            if (selected.UseShipyardBerth)
            {
                Plugin.Instance.Warn("The selected shipyard's release marker is unavailable; using this port's ordinary berth.");
                selected.UseShipyardBerth = false;
            }
            if (selected.Destination != null && selected.Destination.NeedsPlannedBerth)
            {
                if (!StartBerthPlanner.TryPlan(selected.Destination, selected.Body, selected.Ropes, selected.Size,
                    out var plan, out reason)) return false;
                selected.Berth = plan;
                selected.FrontMooring = plan.Front;
                selected.BackMooring = plan.Back;
                return true;
            }
            reason = null;
            var recovery = selected.Recovery;
            var ropes = selected.Ropes;
            if (selected.Body == null || selected.Saveable == null || ropes == null ||
                ropes.ropes == null || ropes.ropes.Length < 2 || ropes.ropes.Any(item => item == null) ||
                ropes.GetAnchorController() == null || recovery == null || recovery.parentPort != selected.Port ||
                recovery.boatPos == null || recovery.mooringFront == null || recovery.mooringBack == null)
            {
                reason = "Selected boat or recovery berth lacks its normal relocation components.";
                return false;
            }
            var front = recovery.mooringFront.GetComponent<GPButtonDockMooring>();
            var back = recovery.mooringBack.GetComponent<GPButtonDockMooring>();
            // Connection is best effort at placement time. An occupied spring
            // must not change which starter the native sequence grants.
            selected.FrontMooring = front;
            selected.BackMooring = back;
            return true;
        }

        private static bool TryResolve(PortChoice destination, int boatSceneIndex, BoatSize? size, out ResolvedStart result, out string reason,
            bool useShipyardBerth = false)
        {
            result = null;
            reason = null;
            if (destination == null || !StartDestinations.IsCurrent(destination))
            { reason = "The selected starting location is no longer loaded."; return false; }
            var portIndex = destination.Index;
            var yardStart = useShipyardBerth && ShipyardStart.Available(destination);
            var skipsNativeBerth = destination.NeedsPlannedBerth || yardStart;
            var ports = Port.ports;
            if (!skipsNativeBerth && (ports == null || portIndex < 0 || portIndex >= ports.Length || ports[portIndex] == null ||
                ports[portIndex].portIndex != portIndex))
            {
                reason = $"Port {portIndex} is not registered.";
                return false;
            }
            var port = destination.Port;
            if (KnownSites.IsTestPort(port))
            {
                reason = "The scene test port is not a playable start.";
                return false;
            }
            var registered = RecoveryPortsField.GetValue(null) as List<RecoveryPort>;
            var recovery = destination.NeedsPlannedBerth ? null : registered?.FirstOrDefault(item => item != null && item.parentPort == port &&
                item.boatPos != null && item.mooringFront != null && item.mooringBack != null);
            if (!skipsNativeBerth && recovery == null)
            {
                reason = $"Port {portIndex} has no complete registered recovery berth.";
                return false;
            }
            var frontMooring = recovery != null ? recovery.mooringFront.GetComponent<GPButtonDockMooring>() : null;
            var backMooring = recovery != null ? recovery.mooringBack.GetComponent<GPButtonDockMooring>() : null;
            var matches = Resources.FindObjectsOfTypeAll<PurchasableBoat>()
                .Where(item => item != null && item.enabled && item.gameObject.scene.isLoaded &&
                    item.gameObject.activeInHierarchy)
                .Select(item => new { Boat = item, Saveable = SaveableField.GetValue(item) as SaveableObject })
                .Where(item => item.Saveable != null && item.Saveable.sceneIndex == boatSceneIndex &&
                    SelectionCatalog.IsBoatLike(item.Saveable))
                .ToArray();
            if (matches.Length != 1)
            {
                reason = $"Boat scene index {boatSceneIndex} resolved to {matches.Length} candidates; expected exactly one.";
                return false;
            }
            var boat = matches[0].Boat;
            var saveable = matches[0].Saveable;
            if (boat.isPurchased())
            {
                reason = $"Boat {boatSceneIndex} is already purchased.";
                return false;
            }
            var body = saveable.GetComponent<Rigidbody>();
            var ropes = saveable.GetComponent<BoatMooringRopes>();
            NativeBerth.Plan nativeBerth = null;
            if (!skipsNativeBerth) NativeBerth.TryResolve(port, boat, saveable, body, ropes, out nativeBerth);
            var relocation = new ResolvedStart { Port = port, Recovery = recovery,
                Destination = destination, Boat = boat, Saveable = saveable, Body = body, Ropes = ropes, Size = size,
                UseShipyardBerth = yardStart };
            if (nativeBerth == null && !TryResolveRelocation(relocation, out reason)) return false;
            frontMooring = nativeBerth != null ? nativeBerth.Front : relocation.FrontMooring;
            backMooring = nativeBerth != null ? nativeBerth.Back : relocation.BackMooring;
            var region = destination.Region;
            if (region < 0 || region > 2)
            {
                reason = $"Port {portIndex} has unsupported region {port.region}.";
                return false;
            }
            var sets = Resources.FindObjectsOfTypeAll<StarterSet>()
                .Where(item => item != null && item.gameObject.scene.IsValid() && (int)item.region == region)
                .ToArray();
            if (sets.Length != 1)
            {
                reason = $"Region {region} has {sets.Length} starter sets; expected exactly one.";
                return false;
            }
            result = new ResolvedStart
            {
                Port = port, Recovery = relocation.Recovery, Destination = destination, Berth = relocation.Berth, Boat = boat, Saveable = saveable,
                Body = body, Ropes = ropes, FrontMooring = frontMooring,
                BackMooring = backMooring, StarterSet = sets[0], Region = region, NativeBerth = nativeBerth,
                Size = size, UseShipyardBerth = relocation.UseShipyardBerth
            };
            return true;
        }

        private static void ChooseMooringRopes(ResolvedStart selected,
            out PickupableBoatMooringRope front, out PickupableBoatMooringRope back)
        {
            if (!TryPairMooringRopes(selected.Saveable.transform, selected.Ropes.ropes,
                selected.FrontMooring.transform.position, selected.BackMooring.transform.position,
                out front, out back, out _, out _, out var detail))
                throw new InvalidOperationException("Selected boat has no distinct mooring ropes at opposite ends.");
            Plugin.Instance.DebugLog("Start mooring ropes: " + detail);
        }

        // Priority 0 is a dock-facing same-side pair, 1 another same-side pair
        // and 2 a pair crossing the hull. Lower distance breaks a tie.
        internal static bool TryPairMooringRopes(Transform boat, PickupableBoatMooringRope[] ropes,
            Vector3 frontDock, Vector3 backDock, out PickupableBoatMooringRope front,
            out PickupableBoatMooringRope back, out int pairPriority, out float distanceSquared, out string detail)
        {
            // Native MoorClosestRope treats each dock point independently. On a
            // long boat both nearest ropes can belong to its stern. Pick one
            // rope from each longitudinal end before connecting either spring.
            var candidates = ropes.Select(rope => new RopeCandidate
            {
                Rope = rope,
                LocalPosition = boat.InverseTransformPoint(rope.transform.position)
            }).ToArray();
            var xSpan = candidates.Max(item => item.LocalPosition.x) -
                candidates.Min(item => item.LocalPosition.x);
            var zSpan = candidates.Max(item => item.LocalPosition.z) -
                candidates.Min(item => item.LocalPosition.z);
            var useX = xSpan > zSpan;
            foreach (var item in candidates)
            {
                item.AlongHull = useX ? item.LocalPosition.x : item.LocalPosition.z;
                item.AcrossHull = useX ? item.LocalPosition.z : item.LocalPosition.x;
            }
            var minimum = candidates.Min(item => item.AlongHull);
            var maximum = candidates.Max(item => item.AlongHull);
            var lateralMinimum = candidates.Min(item => item.AcrossHull);
            var lateralMaximum = candidates.Max(item => item.AcrossHull);
            var lateralCenter = (lateralMinimum + lateralMaximum) * 0.5f;
            var sideTolerance = Mathf.Max(0.1f, (lateralMaximum - lateralMinimum) * 0.1f);
            var dockMidpoint = (frontDock + backDock) * 0.5f;
            var localDockMidpoint = boat.InverseTransformPoint(dockMidpoint);
            var dockSide = (useX ? localDockMidpoint.z : localDockMidpoint.x) - lateralCenter;
            var endTolerance = Mathf.Max(0.15f, (maximum - minimum) * 0.12f);
            var lowEnd = candidates.Where(item => item.AlongHull <= minimum + endTolerance).ToArray();
            var highEnd = candidates.Where(item => item.AlongHull >= maximum - endTolerance).ToArray();
            var bestDistance = float.PositiveInfinity;
            var bestPriority = int.MaxValue;
            RopeCandidate bestFront = null;
            RopeCandidate bestBack = null;
            foreach (var low in lowEnd)
            foreach (var high in highEnd)
            {
                if (low.Rope == high.Rope) continue;
                // Treat fore and aft as a pair. Independent nearest-rope choices
                // can cross the hull even when the boat offers both dock-facing
                // ropes. A boat with only one rope at each end still falls back
                // to that pair, regardless of its lateral layout.
                var lowSide = low.AcrossHull - lateralCenter;
                var highSide = high.AcrossHull - lateralCenter;
                var crossesHull = lowSide > sideTolerance && highSide < -sideTolerance ||
                    lowSide < -sideTolerance && highSide > sideTolerance;
                var pairSide = lowSide + highSide;
                var facesDock = Mathf.Abs(dockSide) > sideTolerance &&
                    pairSide * dockSide > sideTolerance * sideTolerance;
                var priority = crossesHull ? 2 : facesDock ? 0 : 1;
                var lowFrontDistance = HorizontalDistanceSquared(low.Rope.transform.position, frontDock);
                var lowBackDistance = HorizontalDistanceSquared(low.Rope.transform.position, backDock);
                var highFrontDistance = HorizontalDistanceSquared(high.Rope.transform.position, frontDock);
                var highBackDistance = HorizontalDistanceSquared(high.Rope.transform.position, backDock);
                if (priority < bestPriority || priority == bestPriority &&
                    lowFrontDistance + highBackDistance < bestDistance)
                {
                    bestPriority = priority;
                    bestDistance = lowFrontDistance + highBackDistance;
                    bestFront = low;
                    bestBack = high;
                }
                if (priority < bestPriority || priority == bestPriority &&
                    highFrontDistance + lowBackDistance < bestDistance)
                {
                    bestPriority = priority;
                    bestDistance = highFrontDistance + lowBackDistance;
                    bestFront = high;
                    bestBack = low;
                }
            }
            front = bestFront?.Rope;
            back = bestBack?.Rope;
            pairPriority = bestPriority;
            distanceSquared = bestDistance;
            detail = null;
            if (bestFront == null || bestBack == null) return false;
            detail = $"front {front.name} at {bestFront.LocalPosition}, back {back.name} at {bestBack.LocalPosition}; " +
                $"longitudinal axis {(useX ? "X" : "Z")} span {maximum - minimum:0.00} m; " +
                $"dock lateral offset {dockSide:0.00} m, rope lateral center {lateralCenter:0.00} m; " +
                $"{(bestPriority == 2 ? "mixed-side fallback" : bestPriority == 0 ? "dock-facing same-side pair" : "same-side pair")}, horizontal distance squared {bestDistance:0.00}.";
            return true;
        }

        private static float HorizontalDistanceSquared(Vector3 rope, Vector3 dock)
        {
            // IslandHorizon can temporarily lower the dock hundreds of metres
            // during startup. Its height must not favor a lower rope on the
            // opposite side of the hull. Keep this in world X/Z, even if tilted.
            var x = rope.x - dock.x;
            var z = rope.z - dock.z;
            return x * x + z * z;
        }

        internal static void MoorAt(PickupableBoatMooringRope rope, GPButtonDockMooring dock,
            Rigidbody body)
        {
            if (dock.spring.connectedBody != null)
                throw new InvalidOperationException("Dock spring became occupied during boat placement.");
            if (rope == null || rope.IsMoored())
                throw new InvalidOperationException("Chosen mooring rope is unavailable.");
            rope.MoorTo(dock);
            if (!rope.IsMoored() || dock.spring.connectedBody != body)
                throw new InvalidOperationException("Boat mooring did not connect to the selected hull.");
        }

        internal static bool IsLeopardStart(ResolvedStart selected) =>
            selected?.Saveable != null && KnownSites.IsLeopard(selected.Saveable.sceneIndex,
                selected.Saveable.name);

        private static Vector3 AdjustLargeBoatBerth(ResolvedStart selected,
            Vector3 position, Quaternion rotation)
        {
            var distance = ExtraBerthDistance(IsLeopardStart(selected), selected.Size);
            if (distance == 0f) return position;
            if (selected.FrontMooring == null || selected.BackMooring == null ||
                !TryOutwardOffset(rotation * Vector3.right, position,
                selected.FrontMooring.transform.position, selected.BackMooring.transform.position,
                distance, out var delta))
            {
                Plugin.Instance.Warn("Large-boat extra berth distance skipped: the live dock-facing side could not be verified.");
                return position;
            }
            var candidate = position + delta;
            if (!IsFinite(candidate))
                throw new InvalidOperationException("Large-boat berth offset produced a non-finite position.");
            // Compose after native/Crab Beach placement. This extra
            // distance is unconditional on neighboring hulls; the
            // existing berth selection and occupied-pair checks still run.
            Plugin.Instance.DebugLog($"Boat {selected.Saveable.sceneIndex} berth moved an extra {distance:F2}m away from its live dock pair to {candidate}; existing berth adjustments and shore markers preserved.");
            return candidate;
        }

        internal static float ExtraBerthDistance(bool leopard, BoatSize? size) =>
            leopard ? 3f : size == BoatSize.Large ? 1f : 0f;

        private static bool TryOutwardOffset(Vector3 right, Vector3 berth,
            Vector3 front, Vector3 back, float distance, out Vector3 delta)
        {
            delta = Vector3.zero;
            if (!IsFinite(right) || !IsFinite(berth) || !IsFinite(front) || !IsFinite(back) ||
                float.IsNaN(distance) || float.IsInfinity(distance) || distance <= 0f) return false;
            right.y = 0f;
            if (right.sqrMagnitude < 0.5f) return false;
            right.Normalize();
            var side = Vector3.Dot((front + back) * 0.5f - berth, right);
            if (Mathf.Abs(side) < 0.5f) return false;
            if (float.IsNaN(side) || float.IsInfinity(side)) return false;
            delta = right * (side > 0f ? -distance : distance);
            return IsFinite(delta);
        }

        private static Vector3 GetBerthPosition(ResolvedStart selected)
        {
            var marker = selected.Recovery.boatPos;
            var mask = LayerMask.GetMask("Ignore Raycast");
            var nearbyBoats = Physics.OverlapSphere(marker.position, 5f, mask)
                .Where(collider => collider != null && collider.CompareTag("Boat"))
                .ToArray();
            var nativePosition = nearbyBoats.Length > 0 && nearbyBoats.All(collider =>
                collider.attachedRigidbody == selected.Body ||
                collider.transform.IsChildOf(selected.Saveable.transform))
                ? marker.position : selected.Recovery.GetBoatPos();
            return nativePosition;
        }

        internal static bool IsSelectionStillActive(StartMenu menu, ResolvedStart selected)
        {
            if (menu == null || selected == null || selected.Boat == null || selected.Saveable == null ||
                selected.ShoreFrame == null || selected.Body == null ||
                selected.Ropes == null || selected.StarterSet == null ||
                !selected.ShoreFrame.gameObject.scene.isLoaded || !selected.Saveable.gameObject.scene.isLoaded ||
                (selected.Recovery != null ? selected.Recovery.parentPort != selected.Port : !StartDestinations.IsCurrent(selected.Destination)) ||
                selected.StarterSet.starterBoat != selected.Saveable.transform)
                return false;
            var starters = StartingBoatsField.GetValue(menu) as PurchasableBoat[];
            return starters != null && selected.Region < starters.Length &&
                starters[selected.Region] == selected.Boat;
        }
    }
}
