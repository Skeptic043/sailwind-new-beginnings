using System;
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
    }

    internal sealed class ProbeVelocityGuard
    {
        private readonly Component probes;
        private readonly FieldInfo field;
        private readonly bool previous;
        private bool restored;

        internal static bool CanGuard(SaveableObject boat)
        {
            var component = boat != null ? boat.GetComponent("BoatProbes") : null;
            var velocityField = component != null
                ? AccessTools.Field(component.GetType(), "dontUpdateVelocity") : null;
            return velocityField != null && velocityField.FieldType == typeof(bool);
        }

        internal ProbeVelocityGuard(SaveableObject boat)
        {
            probes = boat.GetComponent("BoatProbes");
            field = probes != null ? AccessTools.Field(probes.GetType(), "dontUpdateVelocity") : null;
            if (field == null || field.FieldType != typeof(bool))
                throw new InvalidOperationException("Selected boat has no compatible BoatProbes velocity guard.");
            previous = (bool)field.GetValue(probes);
            field.SetValue(probes, true);
        }

        internal void Restore()
        {
            if (restored) return;
            try
            {
                field.SetValue(probes, previous);
                restored = true;
            }
            catch (Exception exception)
            {
                Plugin.Instance?.Error("Could not restore the selected boat's velocity guard.", exception);
            }
        }
    }

    internal static class FixedStart
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
        private static StartMenu nativeFallbackButtonMenu;
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
                ClearAcceptedButtonChoice();
                nativeFallbackButtonMenu = null;
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
                    if (activeUi.CatalogUnavailable)
                    {
                        nativeFallbackButtonMenu = __instance;
                        return true;
                    }
                    if (!plugin.TryChooseStart(out var pair, out reason))
                    {
                        activeUi.ShowBlockedStart(reason);
                        plugin.Warn("New game blocked by New Beginnings selection: " + reason);
                        return false;
                    }
                    acceptedButtonPair = pair;
                    acceptedButtonMenu = __instance;
                    return true;
                }
                catch (Exception exception)
                {
                    ClearAcceptedButtonChoice();
                    nativeFallbackButtonMenu = __instance;
                    activeUi?.ShowCatalogUnavailable();
                    plugin.Error("New Beginnings selection failed; Sailwind's normal start will run.", exception);
                    return true;
                }
            }

            private static void Postfix(StartMenu __instance)
            {
                // If ButtonClick returned without invoking StartNewGame, its
                // temporary choice must not leak into a later menu action.
                if (ReferenceEquals(acceptedButtonMenu, __instance))
                    ClearAcceptedButtonChoice();
                if (ReferenceEquals(nativeFallbackButtonMenu, __instance))
                    nativeFallbackButtonMenu = null;
            }
        }

        [HarmonyPatch(typeof(StartMenu), "StartNewGame")]
        [HarmonyBefore(ScrambledSeasIntegration.Id)]
        private static class BeginPatch
        {
            private static void Prefix(StartMenu __instance)
            {
                var buttonPair = ReferenceEquals(acceptedButtonMenu, __instance)
                    ? acceptedButtonPair : null;
                var useNativeStart = ReferenceEquals(nativeFallbackButtonMenu, __instance);
                nativeFallbackButtonMenu = null;
                ClearAcceptedButtonChoice();
                pending = null;
                pendingMenu = null;
                pendingOptions = null;
                StartingValues.Cancel();
                nativeTweenObserver = null;
                nativeTweenTarget = null;
                Plugin.Instance?.CancelBoatSettle();
                LeopardStarterRig.Cancel();
                Plugin.Instance?.CancelPlayerStartTracking();
                Plugin.Instance?.ClearDocksideSurface();
                StarterCargo.Disarm();
                var plugin = Plugin.Instance;
                if (plugin == null || !ScrambledSeasIntegration.CanSelect || useNativeStart) return;

                object previousRegion = null;
                var regionUpdated = false;
                try
                {
                    var pair = buttonPair;
                    if (pair == null && !plugin.TryChooseStart(out pair, out var reason))
                    {
                        plugin.Warn("New Beginnings skipped; vanilla new game will run. " + reason);
                        return;
                    }
                    // Scrambled Seas may move every island, recovery marker and boat
                    // in its own prefix. Keep identities here; resolve coordinates and
                    // mutate the boat only in the selected coroutine factory.
                    previousRegion = CurrentRegionField.GetValue(__instance);
                    CurrentRegionField.SetValue(__instance, (int)pair.Port.Port.region);
                    regionUpdated = true;
                    pending = pair;
                    pendingMenu = __instance;
                    pendingOptions = plugin.StartOptions.Copy();
                    if (!pendingOptions.TryValidate(out var optionsReason))
                        throw new InvalidOperationException(optionsReason);
                    plugin.Report($"New Beginnings selected port {pair.Port.Index} and boat scene {pair.Boat.Index}.");
                }
                catch (Exception exception)
                {
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
                    plugin.Error("New Beginnings selection failed; vanilla new game will run.", exception);
                }
            }

            private static Exception Finalizer(Exception __exception)
            {
                if (__exception != null)
                {
                    pending = null;
                    pendingMenu = null;
                    pendingOptions = null;
                    StartingValues.Cancel();
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
                pending = null;
                pendingMenu = null;
                pendingOptions = null;
                Plugin.Instance?.Warn("New Beginnings selection was cleared because no player-start coroutine was created.");
            }
        }

        [HarmonyPatch(typeof(StartMenu), "MovePlayerToStartPos")]
        private static class StartPositionPatch
        {
            private static void Prefix(StartMenu __instance, ref Transform startPos)
            {
                if (!nativeTweenSeamReady)
                {
                    if (pending != null && ReferenceEquals(__instance, pendingMenu))
                    {
                        pending = null;
                        pendingMenu = null;
                        pendingOptions = null;
                        Plugin.Instance?.Warn("New Beginnings skipped this native start because its camera tween seam was not verified; the regional game start will run.");
                    }
                    return;
                }
                if (pending != null && ReferenceEquals(__instance, pendingMenu) &&
                    !TryGetNativeStartActors(__instance, out _, out _))
                {
                    pending = null;
                    pendingMenu = null;
                    pendingOptions = null;
                    Plugin.Instance?.Warn("New Beginnings skipped this native start because its observer or controller is unavailable; the regional game start will run.");
                    return;
                }
                if (ApplyPendingAtStart(__instance, ref startPos))
                    TeleportNativeStartObserver(__instance, startPos);
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

        internal static bool ApplyPendingAtStart(StartMenu menu, ref Transform startPos)
        {
            if (pending == null || !ReferenceEquals(menu, pendingMenu)) return false;
            var pair = pending;
            var options = pendingOptions;
            pending = null;
            pendingMenu = null;
            pendingOptions = null;
            try
            {
                if (!TryResolve(pair.Port.Index, pair.Boat.Index, out var selected, out var reason) ||
                    !ReferenceEquals(selected.Port, pair.Port.Port) ||
                    !ReferenceEquals(selected.Recovery, pair.Port.Recovery) ||
                    !ReferenceEquals(selected.Boat, pair.Boat.Boat) ||
                    !ReferenceEquals(selected.Saveable, pair.Boat.Saveable))
                {
                    Plugin.Instance.Warn("New Beginnings placement skipped; the regional game start will run. " +
                        (reason ?? "Selected boat or berth changed after catalog discovery."));
                    return false;
                }
                // Use the accepted catalog classification, including exact-name
                // size overrides, only after its live identities are verified.
                selected.Size = pair.Boat.Size;
                selected.Options = options;
                Apply(selected, menu, ref startPos);
                StartingValues.Arm(selected);
                return true;
            }
            catch (Exception exception)
            {
                Plugin.Instance.Error("New Beginnings placement failed; the regional game start remains selected where possible.", exception);
                return false;
            }
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

        // Cheap per-pair gate for catalog reservoir sampling. Discovery already
        // validates the base identities and components; the click path re-resolves
        // the selected pair before any movement.
        internal static bool IsPairViable(PortChoice port, BoatChoice boat)
        {
            if (port?.Port == null || port.Recovery == null || boat?.Boat == null ||
                boat.Saveable == null || port.Recovery.parentPort != port.Port ||
                port.Recovery.boatPos == null || port.Recovery.mooringFront == null ||
                port.Recovery.mooringBack == null || boat.Boat.isPurchased()) return false;
            var body = boat.Saveable.GetComponent<Rigidbody>();
            var ropes = boat.Saveable.GetComponent<BoatMooringRopes>();
            var front = port.Recovery.mooringFront.GetComponent<GPButtonDockMooring>();
            var back = port.Recovery.mooringBack.GetComponent<GPButtonDockMooring>();
            if (body == null || !ProbeVelocityGuard.CanGuard(boat.Saveable) ||
                ropes == null || ropes.ropes == null || ropes.ropes.Length < 2 ||
                ropes.ropes.Any(item => item == null) || ropes.GetAnchorController() == null ||
                ropes.anchor != null && ropes.anchor.IsSet() ||
                front == null || back == null || front == back ||
                front.spring == null || back.spring == null) return false;
            if (SirenSongBerth.IsTarget(port.Port) &&
                !SirenSongBerth.TryGetMoorings(port.Port, port.Recovery, body,
                    out front, out back, out _)) return false;
            return (front.spring.connectedBody == null || front.spring.connectedBody == body) &&
                (back.spring.connectedBody == null || back.spring.connectedBody == body);
        }

        private static bool TryResolve(int portIndex, int boatSceneIndex, out ResolvedStart result, out string reason)
        {
            result = null;
            reason = null;
            var ports = Port.ports;
            if (ports == null || portIndex < 0 || portIndex >= ports.Length || ports[portIndex] == null ||
                ports[portIndex].portIndex != portIndex)
            {
                reason = $"Port {portIndex} is not registered.";
                return false;
            }
            var port = ports[portIndex];
            if (portIndex == 7 && string.Equals(port.gameObject.name,
                "port 7 (test port)", StringComparison.OrdinalIgnoreCase))
            {
                reason = "The scene test port is not a playable start.";
                return false;
            }
            var registered = RecoveryPortsField.GetValue(null) as List<RecoveryPort>;
            var recovery = registered?.FirstOrDefault(item => item != null && item.parentPort == port &&
                item.boatPos != null && item.mooringFront != null && item.mooringBack != null);
            if (recovery == null)
            {
                reason = $"Port {portIndex} has no complete registered recovery berth.";
                return false;
            }
            var frontMooring = recovery.mooringFront.GetComponent<GPButtonDockMooring>();
            var backMooring = recovery.mooringBack.GetComponent<GPButtonDockMooring>();
            if (frontMooring == null || backMooring == null || frontMooring == backMooring ||
                frontMooring.spring == null || backMooring.spring == null)
            {
                reason = $"Port {portIndex} has no initialized pair of dock springs.";
                return false;
            }
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
            if (PurchaseUiField.GetValue(boat) as GameObject == null || boat.isPurchased())
            {
                reason = $"Boat {boatSceneIndex} lacks its normal purchase UI or is already purchased.";
                return false;
            }
            var body = saveable.GetComponent<Rigidbody>();
            var ropes = saveable.GetComponent<BoatMooringRopes>();
            if (body == null || !ProbeVelocityGuard.CanGuard(saveable) ||
                ropes == null || ropes.ropes == null || ropes.ropes.Length < 2 ||
                ropes.ropes.Any(item => item == null) || ropes.GetAnchorController() == null)
            {
                reason = $"Boat {boatSceneIndex} lacks its normal rigidbody, velocity guard, mooring ropes, or anchor controller.";
                return false;
            }
            if (SirenSongBerth.IsTarget(port) &&
                !SirenSongBerth.TryGetMoorings(port, recovery, body,
                    out frontMooring, out backMooring, out reason)) return false;
            if ((frontMooring.spring.connectedBody != null && frontMooring.spring.connectedBody != body) ||
                (backMooring.spring.connectedBody != null && backMooring.spring.connectedBody != body))
            {
                reason = $"Port {portIndex} has a dock spring occupied by another boat.";
                return false;
            }
            if (ropes.anchor != null && ropes.anchor.IsSet())
            {
                reason = $"Boat {boatSceneIndex} is anchored; this fixed-start stage cannot safely restore that state on failure.";
                return false;
            }
            var region = (int)port.region;
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
                Port = port, Recovery = recovery, Boat = boat, Saveable = saveable,
                Body = body, Ropes = ropes, FrontMooring = frontMooring,
                BackMooring = backMooring, StarterSet = sets[0], Region = region
            };
            return true;
        }

        private static void Apply(ResolvedStart selected, StartMenu menu, ref Transform startPos)
        {
            // Resolve the berth while the selected boat is still at its old location. Native
            // GetBoatPos applies its normal occupied-berth offset when another boat is present.
            var alternateBerth = SirenSongBerth.TryCreate(selected, out var berthPlan);
            var berthPosition = alternateBerth ? berthPlan.Position : GetBerthPosition(selected);
            if (alternateBerth)
            {
                selected.FrontMooring = berthPlan.Front;
                selected.BackMooring = berthPlan.Back;
            }
            var berthRotation = selected.Recovery.boatPos.rotation;
            berthPosition = AdjustLargeBoatBerth(selected, berthPosition, berthRotation);
            var boatTransform = selected.Saveable.transform;
            var oldPosition = boatTransform.position;
            var oldRotation = boatTransform.rotation;
            if (!IsFinite(selected.Port.transform.position) ||
                !IsFinite(selected.Recovery.transform.position) ||
                !IsFinite(selected.Recovery.boatPos.position) ||
                !IsFinite(berthPosition) || !IsFinite(berthRotation) ||
                !IsFinite(oldPosition) || !IsFinite(oldRotation))
                throw new InvalidOperationException("A selected port, berth or boat has a non-finite transform.");
            Plugin.Instance.DebugLog($"Start placement coordinates: port {selected.Port.transform.position}, recovery dock marker {selected.Recovery.transform.position}, berth {berthPosition}, boat origin {oldPosition}.");
            var oldVelocity = selected.Body.velocity;
            var oldAngularVelocity = selected.Body.angularVelocity;
            var anchor = selected.Ropes.anchor;
            var anchorBody = anchor != null ? anchor.GetComponent<Rigidbody>() : null;
            var oldAnchorPosition = anchorBody != null ? anchorBody.position : Vector3.zero;
            var oldAnchorLength = anchor != null ? anchor.GetRopeLength() : 0f;
            var starters = (PurchasableBoat[])StartingBoatsField.GetValue(menu);
            if (starters == null || selected.Region >= starters.Length || starters[selected.Region] == null)
                throw new InvalidOperationException("Native regional starting boat array is incomplete.");
            var oldStarterBoat = selected.StarterSet.starterBoat;
            var oldLastBoat = GameState.lastBoat;
            var oldLastOwnedBoat = GameState.lastOwnedBoat;
            var oldLastVisitedPort = GameState.lastVisitedPort;
            var oldStartPos = startPos;
            var oldRopes = selected.Ropes.ropes.Select(rope => new RopePosition
            {
                Rope = rope, Parent = rope.transform.parent,
                Position = rope.transform.position, Rotation = rope.transform.rotation,
                Mooring = GetOriginalMooring(rope)
            }).ToArray();
            var moved = false;
            ProbeVelocityGuard probeGuard = null;
            try
            {
                probeGuard = new ProbeVelocityGuard(selected.Saveable);
                selected.Ropes.UnmoorAllRopes();
                selected.Ropes.GetAnchorController().ResetAnchor();

                moved = true;
                boatTransform.SetPositionAndRotation(berthPosition, berthRotation);
                selected.Body.position = berthPosition;
                selected.Body.rotation = berthRotation;
                selected.Body.velocity = Vector3.zero;
                selected.Body.angularVelocity = Vector3.zero;
                if (anchorBody != null)
                {
                    anchorBody.position = oldAnchorPosition + (berthPosition - oldPosition);
                    anchorBody.velocity = Vector3.zero;
                    anchorBody.angularVelocity = Vector3.zero;
                }
                if (!IsFinite(boatTransform.position) || !IsFinite(selected.Body.position) ||
                    anchorBody != null && !IsFinite(anchorBody.position))
                    throw new InvalidOperationException("Boat or anchor gained a non-finite position during placement.");
                Physics.SyncTransforms();

                ChooseMooringRopes(selected, out var frontRope, out var backRope);
                MoorAt(frontRope, selected.FrontMooring, selected.Body);
                MoorAt(backRope, selected.BackMooring, selected.Body);

                var replacement = (PurchasableBoat[])starters.Clone();
                replacement[selected.Region] = selected.Boat;
                // Port roots can be parked well below the water while their
                // islands are hidden. RecoveryPort.Start reparents its marker
                // to the shifting world, retaining the actual berth location.
                // Use that dock-side marker for the player, not Port.transform.
                var target = Plugin.Instance.MakeTemporaryStart(selected,
                    selected.Recovery.boatPos.rotation,
                    alternateBerth ? (Vector3?)berthPlan.ShorePosition : null, berthPosition);
                if (!IsFinite(target.position) || !IsFinite(target.rotation))
                    throw new InvalidOperationException("Selected player target gained a non-finite transform.");
                StartingBoatsField.SetValue(menu, replacement);
                selected.StarterSet.starterBoat = boatTransform;
                GameState.lastBoat = boatTransform;
                GameState.lastOwnedBoat = boatTransform;
                GameState.lastVisitedPort = selected.Port;
                startPos = target;
                selected.Menu = menu;
                Plugin.Instance.BeginPlayerStartTracking(menu, target);
                StarterCargo.Arm(selected);
                StarterMooring.Arm(menu, selected, frontRope, backRope);
                Plugin.Instance.DebugLog($"Placed boat {selected.Saveable.sceneIndex} at port {selected.Port.portIndex}; native new-game ownership grant will follow.");
                Plugin.Instance.BeginBoatSettle(menu, selected, berthPosition, berthRotation, probeGuard);
                probeGuard = null;
                StarterBoatRepair.Arm(menu, selected);
                LeopardStarterRig.Arm(menu, selected);
            }
            catch
            {
                try
                {
                    // Do not allow a partial field substitution to grant the wrong extra boat.
                    // An unmoored boat may still require a live recovery after an exceptional move.
                    StartingBoatsField.SetValue(menu, starters);
                    selected.StarterSet.starterBoat = oldStarterBoat;
                    GameState.lastBoat = oldLastBoat;
                    GameState.lastOwnedBoat = oldLastOwnedBoat;
                    GameState.lastVisitedPort = oldLastVisitedPort;
                    startPos = oldStartPos;
                    Plugin.Instance.CancelPlayerStartTracking();
                    StarterCargo.Disarm();
                    StarterMooring.Cancel();
                    StarterBoatRepair.Cancel();
                    if (moved)
                    {
                        boatTransform.SetPositionAndRotation(oldPosition, oldRotation);
                        selected.Body.position = oldPosition;
                        selected.Body.rotation = oldRotation;
                        selected.Body.velocity = oldVelocity;
                        selected.Body.angularVelocity = oldAngularVelocity;
                        if (anchorBody != null) anchorBody.position = oldAnchorPosition;
                        if (anchor != null) anchor.OnLoad(false, oldAnchorLength);
                    }
                    foreach (var rope in oldRopes)
                    {
                        try
                        {
                            rope.Rope.Unmoor();
                            if (rope.Mooring != null)
                            {
                                rope.Rope.MoorTo(rope.Mooring);
                            }
                            else
                            {
                                rope.Rope.transform.SetParent(rope.Parent, true);
                                rope.Rope.transform.SetPositionAndRotation(rope.Position, rope.Rotation);
                            }
                        }
                        catch (Exception exception)
                        {
                            Plugin.Instance.Warn("A rope could not be restored after failed placement: " + exception.Message);
                        }
                    }
                }
                finally
                {
                    // Keep the probe from interpreting the rollback as a voyage.
                    probeGuard?.Restore();
                }
                throw;
            }
        }

        private static GPButtonDockMooring GetOriginalMooring(PickupableBoatMooringRope rope)
        {
            if (!rope.IsMoored()) return null;
            var spring = MooredSpringField.GetValue(rope) as SpringJoint;
            var mooring = spring != null ? spring.GetComponent<GPButtonDockMooring>() : null;
            if (mooring == null || mooring.spring != spring)
                throw new InvalidOperationException("An existing mooring rope has no recoverable dock spring.");
            return mooring;
        }

        private static void ChooseMooringRopes(ResolvedStart selected,
            out PickupableBoatMooringRope front, out PickupableBoatMooringRope back)
        {
            // Native MoorClosestRope treats each dock point independently. On a
            // long boat both nearest ropes can belong to its stern. Pick one
            // rope from each longitudinal end before connecting either spring.
            var candidates = selected.Ropes.ropes.Select(rope => new RopeCandidate
            {
                Rope = rope,
                LocalPosition = selected.Saveable.transform.InverseTransformPoint(rope.transform.position)
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
            var dockMidpoint = (selected.FrontMooring.transform.position +
                selected.BackMooring.transform.position) * 0.5f;
            var localDockMidpoint = selected.Saveable.transform.InverseTransformPoint(dockMidpoint);
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
                var lowFrontDistance = HorizontalDistanceSquared(low.Rope.transform.position,
                    selected.FrontMooring.transform.position);
                var lowBackDistance = HorizontalDistanceSquared(low.Rope.transform.position,
                    selected.BackMooring.transform.position);
                var highFrontDistance = HorizontalDistanceSquared(high.Rope.transform.position,
                    selected.FrontMooring.transform.position);
                var highBackDistance = HorizontalDistanceSquared(high.Rope.transform.position,
                    selected.BackMooring.transform.position);
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
            if (bestFront == null || bestBack == null)
                throw new InvalidOperationException("Selected boat has no distinct mooring ropes at opposite ends.");
            front = bestFront.Rope;
            back = bestBack.Rope;
            Plugin.Instance.DebugLog($"Start mooring ropes: front {front.name} at {bestFront.LocalPosition}, back {back.name} at {bestBack.LocalPosition}; " +
                $"longitudinal axis {(useX ? "X" : "Z")} span {maximum - minimum:0.00} m; " +
                $"dock lateral offset {dockSide:0.00} m, rope lateral center {lateralCenter:0.00} m; " +
                $"{(bestPriority == 2 ? "mixed-side fallback" : bestPriority == 0 ? "dock-facing same-side pair" : "same-side pair")}, horizontal distance squared {bestDistance:0.00}.");
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

        private static void MoorAt(PickupableBoatMooringRope rope, GPButtonDockMooring dock,
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
            selected?.Saveable != null && IsLeopardIdentity(selected.Saveable.sceneIndex,
                selected.Saveable.name);

        private static bool IsLeopardIdentity(int sceneIndex, string name) =>
            sceneIndex == 207 && name == "BOAT LEOPARD (207)(Clone)";

        private static Vector3 AdjustLargeBoatBerth(ResolvedStart selected,
            Vector3 position, Quaternion rotation)
        {
            var distance = ExtraBerthDistance(IsLeopardStart(selected), selected.Size);
            if (distance == 0f) return position;
            if (!TryOutwardOffset(rotation * Vector3.right, position,
                selected.FrontMooring.transform.position, selected.BackMooring.transform.position,
                distance, out var delta))
            {
                Plugin.Instance.Warn("Large-boat extra berth distance skipped: the live dock-facing side could not be verified.");
                return position;
            }
            var candidate = position + delta;
            if (!IsFinite(candidate))
                throw new InvalidOperationException("Large-boat berth offset produced a non-finite position.");
            // Compose after native/Crab Beach/Siren Song placement. This extra
            // distance is unconditional on neighboring hulls; the
            // existing berth selection and occupied-pair checks still run.
            Plugin.Instance.DebugLog($"Boat {selected.Saveable.sceneIndex} berth moved an extra {distance:F2}m away from its live dock pair to {candidate}; existing berth adjustments and shore markers preserved.");
            return candidate;
        }

        private static float ExtraBerthDistance(bool leopard, BoatSize? size) =>
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
            return BerthClearance.AdjustCrabBeach(selected, nativePosition);
        }

        internal static bool IsSelectionStillActive(StartMenu menu, ResolvedStart selected)
        {
            if (menu == null || selected == null || selected.Boat == null || selected.Saveable == null ||
                selected.Port == null || selected.Recovery == null || selected.Body == null ||
                selected.Ropes == null || selected.StarterSet == null ||
                !selected.Port.gameObject.scene.isLoaded || !selected.Saveable.gameObject.scene.isLoaded ||
                selected.Recovery.parentPort != selected.Port ||
                selected.StarterSet.starterBoat != selected.Saveable.transform)
                return false;
            var starters = StartingBoatsField.GetValue(menu) as PurchasableBoat[];
            return starters != null && selected.Region < starters.Length &&
                starters[selected.Region] == selected.Boat;
        }
    }
}
