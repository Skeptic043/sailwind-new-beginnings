using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    internal static partial class FixedStart
    {
        private static CustomStartSnapshot pendingCustomSnapshot;
        private static Action customCleanup;
        private static int customGeneration;
        private static int customFlightGeneration = -1;
        internal static bool CustomStartInFlight => customFlightGeneration == customGeneration;
        private static float startSelectedAt = -1f;

        internal static bool TryGetStartBlock(out string reason)
        {
            reason = CustomStartInFlight ? "A starting location is already being prepared." : null;
            return reason != null;
        }

        private static readonly System.Reflection.MethodInfo MovePlayerFactory = AccessTools.Method(typeof(StartMenu), "MovePlayerToStartPos");
        private static readonly System.Reflection.MethodInfo AnchorGroundedMethod = AccessTools.Method(typeof(Anchor), "IsTouchingGround");
        private static readonly System.Reflection.MethodInfo SetAnchorMethod = AccessTools.Method(typeof(Anchor), "SetAnchor");
        private static readonly System.Reflection.MethodInfo ReleaseAnchorMethod = AccessTools.Method(typeof(Anchor), "ReleaseAnchor");
        private static readonly System.Reflection.FieldInfo IslandMenuField = AccessTools.Field(typeof(StartMenu), "chooseIslandUI");
        private static readonly System.Reflection.FieldInfo LogoField = AccessTools.Field(typeof(StartMenu), "logo");
        private static readonly System.Reflection.FieldInfo CursorEnabledField = AccessTools.Field(typeof(MouseLook), "cursorEnabled");
        private static readonly System.Reflection.FieldInfo ControllerPointerField = AccessTools.Field(typeof(MouseButtonPointer), "controllerPointer");
        private static bool AnchorGrounded(Anchor anchor) => anchor != null && AnchorGroundedMethod != null &&
            (bool)AnchorGroundedMethod.Invoke(anchor, null);

        // Startup timing spans every mod's work, not only New Beginnings.
        internal static void MarkStartSelected() => startSelectedAt = Time.realtimeSinceStartup;
        internal static void ClearStartTiming() => startSelectedAt = -1f;
        private static string SinceSelection() => startSelectedAt < 0f ? "" :
            $" {Time.realtimeSinceStartup - startSelectedAt:F1} s after start selection";
        internal static void ReportGameplayReached()
        {
            if (startSelectedAt < 0f) return;
            // Sailwind's F prompt waits for the player, so stop timing there.
            Plugin.Instance?.Report("New game reached the start prompt or gameplay" + SinceSelection() + ".");
            startSelectedAt = -1f;
        }

        private static void CleanupCustom(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception exception) { Plugin.Instance?.Error("A disappeared object interrupted starting-location cleanup.", exception); }
        }

        internal static void CancelCustomStart()
        {
            PirateStationKeeper.RemoveActive();
            ++customGeneration;
            var cleanup = customCleanup; customCleanup = null;
            var snapshot = pendingCustomSnapshot;
            pendingCustomSnapshot = null;
            cleanup?.Invoke();
            CleanupCustom(() => snapshot?.Restore("Starting-location preparation was cancelled."));
        }

        [HarmonyPatch(typeof(SaveLoadManager), "LoadGame")]
        private static class CancelBeforeLoad
        {
            private static void Prefix() => CancelCustomStart();
        }

        // Native FadeIslandMenu owns a separate Juicebox tween and a delayed
        // hide. A cancelled start can restore the menu before either completes.
        // Hide this one menu immediately so that restore has no foreign
        // animation to race.
        [HarmonyPatch(typeof(StartMenu), "DisableIslandMenu")]
        private static class CustomMenuHide
        {
            private static bool Prefix(StartMenu __instance)
            {
                return pendingCustomSnapshot == null || !pendingCustomSnapshot.Hide(__instance);
            }
        }

        // Captured before StartNewGame's currency/region/menu writes. There are
        // no ownership writes in that method after its coroutine factory call.
        private sealed class CustomStartSnapshot
        {
            private readonly StartMenu menu;
            private readonly Transform observer, controller, observerParent, controllerParent;
            private readonly Vector3 observerLocal, controllerLocal;
            private readonly Quaternion observerRotation, controllerRotation;
            private readonly int[] currency;
            private readonly PortRegion region;
            private readonly int selectedRegion, animations;
            private readonly CharacterController character;
            private readonly Behaviour movement, mirror;
            private readonly bool characterEnabled, movementEnabled, mirrorEnabled;
            private readonly GameObject islandMenu;
            private readonly GameObject logo;
            private readonly bool logoActive;
            private readonly Vector3 islandMenuPosition;
            private readonly bool islandMenuActive;
            private readonly bool cursorEnabled, inCursorMenu, cursorVisible, pointerActive;
            private readonly CursorLockMode cursorLock;
            private readonly Transform pointer;
            private bool restored;

            internal CustomStartSnapshot(StartMenu menu, int originalRegion)
            {
                this.menu = menu;
                if (!TryGetNativeStartActors(menu, out observer, out var actor))
                    throw new InvalidOperationException("The native start actors are unavailable.");
                controller = actor.transform;
                character = actor.GetComponent<CharacterController>();
                movement = actor.GetComponent("OVRPlayerController") as Behaviour;
                mirror = observer.GetComponent("PlayerControllerMirror") as Behaviour;
                characterEnabled = character != null && character.enabled;
                movementEnabled = movement != null && movement.enabled;
                mirrorEnabled = mirror != null && mirror.enabled;
                observerParent = observer.parent; controllerParent = controller.parent;
                observerLocal = observer.localPosition; controllerLocal = controller.localPosition;
                observerRotation = observer.localRotation; controllerRotation = controller.localRotation;
                currency = (int[])PlayerGold.currency.Clone(); region = GameState.newGameRegion;
                selectedRegion = originalRegion;
                animations = (int)MenuAnimationsField.GetValue(menu);
                islandMenu = IslandMenuField.GetValue(menu) as GameObject;
                if (islandMenu == null) throw new InvalidOperationException("The native island menu is unavailable.");
                islandMenuPosition = islandMenu.transform.localPosition;
                islandMenuActive = islandMenu.activeSelf;
                logo = LogoField.GetValue(menu) as GameObject;
                logoActive = logo != null && logo.activeSelf;
                cursorEnabled = (bool)CursorEnabledField.GetValue(null);
                inCursorMenu = GameState.inCursorMenu;
                cursorVisible = Cursor.visible; cursorLock = Cursor.lockState;
                pointer = MouseButtonPointer.instance != null
                    ? ControllerPointerField.GetValue(MouseButtonPointer.instance) as Transform : null;
                pointerActive = pointer != null && pointer.gameObject.activeSelf;
            }

            internal bool Hide(StartMenu owner)
            {
                if (owner != menu || islandMenu == null) return false;
                islandMenu.SetActive(false);
                return true;
            }

            internal void Approach(PortChoice site)
            {
                var position = site.Island.GetPosition();
                // Distance hiding changes island Y. Approach at ocean level so
                // its ordinary distance/scene loader can emerge and align it.
                position.y = 8f;
                observer.position = position; controller.position = position;
            }

            internal void Restore(string reason)
            {
                if (restored) return;
                restored = true;
                // A foreign transition may have already begun replacing the
                // save. Never restore an old menu snapshot into that session.
                if (GameState.currentlyLoading) return;
                if (observer != null) { observer.SetParent(observerParent, false); observer.localPosition = observerLocal; observer.localRotation = observerRotation; }
                if (controller != null) { controller.SetParent(controllerParent, false); controller.localPosition = controllerLocal; controller.localRotation = controllerRotation; }
                if (character != null) character.enabled = characterEnabled;
                if (movement != null) movement.enabled = movementEnabled;
                if (mirror != null) mirror.enabled = mirrorEnabled;
                CursorEnabledField.SetValue(null, cursorEnabled);
                GameState.inCursorMenu = inCursorMenu;
                Cursor.visible = cursorVisible; Cursor.lockState = cursorLock;
                if (pointer != null) pointer.gameObject.SetActive(pointerActive);
                if (logo != null) logo.SetActive(logoActive);
                if (PlayerGold.currency != null && PlayerGold.currency.Length == currency.Length)
                    Array.Copy(currency, PlayerGold.currency, currency.Length);
                GameState.newGameRegion = region;
                if (menu == null || GameState.currentlyLoading) return;
                CurrentRegionField.SetValue(menu, selectedRegion);
                MenuAnimationsField.SetValue(menu, animations);
                if (islandMenu != null)
                {
                    islandMenu.transform.localPosition = islandMenuPosition;
                    islandMenu.SetActive(islandMenuActive);
                }
                CurrencySwitchButton.masterButton?.ForceSetCurrency(selectedRegion);
                MenuUi.ActiveFor(menu)?.ShowBlockedStart(reason);
            }
        }

        // nativeStart is the target Sailwind's start coroutine was called with.
        // The selected start continues there when no placement can be produced.
        internal static bool TryPreparePendingCustom(StartMenu menu, Transform nativeStart, out IEnumerator sequence,
            Func<Transform, IEnumerator> continuation = null)
        {
            sequence = null;
            if (pending == null || !pending.Port.UsesCustomBerth || !ReferenceEquals(menu, pendingMenu)) return false;
            var pair = pending; var options = pendingOptions; var snapshot = pendingCustomSnapshot;
            pending = null; pendingMenu = null; pendingOptions = null; pendingCustomSnapshot = null;
            sequence = PrepareCustomStart(menu, pair, options, snapshot, nativeStart, continuation);
            return true;
        }

        // Once a start is selected, a placement problem never returns the
        // player to the menu. Each problem is logged and the start continues
        // with the best available result: the planned berth, or Sailwind's own
        // start target when no placement could be produced. Only a real
        // cancellation (a saved-game load, another new game or plugin
        // teardown) unwinds the selected start.
        private static IEnumerator PrepareCustomStart(StartMenu menu, StartPair pair,
            StartingOptionsSettings options, CustomStartSnapshot snapshot, Transform nativeStart,
            Func<Transform, IEnumerator> continuation)
        {
            var generation = customGeneration;
            customFlightGeneration = generation;
            const string cancelled = "Starting-location preparation was cancelled.";
            CustomPlacement placement = null;
            IEnumerator native = null;
            var committed = false;
            var cleaned = false;
            var finished = false;
            Action cleanup = () =>
            {
                if (cleaned || committed) return;
                cleaned = true;
                try { placement?.Rollback(); }
                catch (Exception exception) { Plugin.Instance?.Error("Boat rollback could not fully restore a disappeared scene object.", exception); }
                finally
                {
                    StartupProfiler.Cancel();
                    DisarmCustomStart();
                    CleanupCustom(() => snapshot?.Restore(cancelled));
                    ClearStartTiming();
                    Plugin.Instance?.Warn(cancelled);
                }
            };
            customCleanup = cleanup;
            try
            {
                var target = nativeStart;
                if (snapshot == null)
                    Plugin.Instance?.Error("Native start state could not be preserved. Continuing with Sailwind's own start target.", null);
                else
                {
                    snapshot.Approach(pair.Port);
                    var deadline = Time.realtimeSinceStartup + 30f;
                    var readyFrame = -1;
                    while (generation == customGeneration && menu != null && !GameState.currentlyLoading &&
                        StartDestinations.IsCurrent(pair.Port) && Time.realtimeSinceStartup < deadline)
                    {
                        if (StartDestinations.SceneryReady(pair.Port))
                        {
                            if (readyFrame < 0) readyFrame = Time.frameCount;
                            if (Time.frameCount > readyFrame + 2) break;
                        }
                        else readyFrame = -1;
                        yield return null;
                    }
                    if (generation != customGeneration || menu == null || GameState.currentlyLoading) yield break;
                    if (readyFrame < 0 || Time.realtimeSinceStartup >= deadline)
                        Plugin.Instance?.Warn("The selected island scenery did not report ready" + SinceSelection() +
                            ". Preparing the start anyway.");
                    else Plugin.Instance?.Report("Starting-location scenery ready" + SinceSelection() + ".");
                    var profiled = StartupProfiler.Enter();
                    try { placement = TryPrepareCustom(menu, pair, options); }
                    finally { StartupProfiler.Exit(profiled); }
                    if (placement != null && placement.Selected.Berth.Anchored)
                    {
                        var anchor = placement.Selected.Ropes.anchor;
                        var anchorDeadline = Time.realtimeSinceStartup + 4f;
                        // OnLoad stores the normalized length. Native Update applies
                        // it to the physical joint, including when contact was immediate.
                        yield return null;
                        while (generation == customGeneration && anchor != null && !AnchorGrounded(anchor) &&
                            Time.realtimeSinceStartup < anchorDeadline && !GameState.currentlyLoading)
                            yield return new WaitForFixedUpdate();
                        if (generation != customGeneration || GameState.currentlyLoading) yield break;
                        if (anchor == null || !AnchorGrounded(anchor))
                        {
                            Plugin.Instance?.Warn("The anchor did not report seabed contact. The boat stays at its planned anchorage and the start continues.");
                            placement.ReleaseHull();
                        }
                        else if (!placement.SecureAnchor(out var anchorProblem))
                        {
                            Plugin.Instance?.Warn("The anchor could not be secured (" + anchorProblem +
                                "). The boat stays at its planned anchorage and the start continues.");
                            placement.ReleaseHull();
                        }
                    }
                    if (placement != null)
                    {
                        profiled = StartupProfiler.Enter();
                        try
                        {
                            if (placement.Commit(out var placed, out var problem)) target = placed;
                            else
                            {
                                // The handoff never reached Sailwind. Return the boat
                                // and continue with Sailwind's own start target.
                                Plugin.Instance?.Error("The starting boat could not be handed to Sailwind (" + problem +
                                    "). Continuing with Sailwind's own start target.", null);
                                var abandoned = placement; placement = null;
                                CleanupCustom(abandoned.Rollback);
                                DisarmCustomStart();
                            }
                        }
                        finally { StartupProfiler.Exit(profiled); }
                    }
                }
                if (target == null)
                {
                    Plugin.Instance?.Error("Sailwind's own start target is unavailable, so the start cannot continue.", null);
                    yield break;
                }
                TeleportNativeStartObserver(menu, target);
                native = continuation != null ? continuation(target) :
                    MovePlayerFactory.Invoke(menu, new object[] { target }) as IEnumerator;
                if (native == null)
                {
                    Plugin.Instance?.Error("The native start sequence is unavailable, so the start cannot continue.", null);
                    yield break;
                }
                var warnedOffshore = false;
                var warnedBoat = false;
                // Keep cancellation available until the native iterator actually
                // grants the boat, rather than only until its IEnumerator exists.
                while (generation == customGeneration && menu != null && !GameState.currentlyLoading)
                {
                    if (placement != null && !warnedOffshore && placement.Selected.Berth.OffshoreHeld &&
                        (!StartDestinations.IsCurrent(placement.Selected.Destination) || placement.Selected.Body == null ||
                         placement.Selected.Ropes == null || placement.Selected.Ropes.anchor == null ||
                         placement.Selected.Ropes.anchor.GetComponent<Rigidbody>() == null))
                    {
                        warnedOffshore = true;
                        Plugin.Instance?.Warn("The offshore starting boat or its native anchor disappeared before the native grant. The start continues.");
                    }
                    if (placement != null && !warnedBoat && (placement.Selected.Boat == null ||
                        !placement.Selected.Boat.isPurchased() && PurchaseUiField.GetValue(placement.Selected.Boat) as GameObject == null))
                    {
                        warnedBoat = true;
                        Plugin.Instance?.Warn("The selected boat became unavailable before the native grant. The start continues.");
                    }
                    if (!native.MoveNext()) { finished = true; break; }
                    if (!committed && Granted(menu, placement))
                    {
                        committed = true; customCleanup = null;
                        Plugin.Instance?.Report("Starting boat granted" + SinceSelection() + ".");
                    }
                    yield return native.Current;
                }
                if (Granted(menu, placement)) { committed = true; customCleanup = null; }
            }
            finally
            {
                if (Granted(menu, placement)) committed = true;
                if (ReferenceEquals(customCleanup, cleanup)) customCleanup = null;
                try { (native as IDisposable)?.Dispose(); }
                catch (Exception exception) { Plugin.Instance?.Error("Native start iterator cleanup failed.", exception); }
                try
                {
                    if (generation != customGeneration || menu == null || GameState.currentlyLoading) cleanup();
                    else if (!finished && !committed)
                    {
                        // An exception or an external stop ended the sequence.
                        // Leave the start as placed, without temporary holds.
                        CleanupCustom(() => placement?.ReleaseHull());
                        Plugin.Instance?.Warn("The start sequence stopped before it finished" + SinceSelection() +
                            ". The selected start was left as placed.");
                    }
                }
                finally { if (customFlightGeneration == generation) customFlightGeneration = -1; }
            }
        }

        private static bool Granted(StartMenu menu, CustomPlacement placement)
        {
            if (placement != null) return placement.Selected.Boat != null && placement.Selected.Boat.isPurchased();
            // With no placement, Sailwind grants its own regional starter.
            if (menu == null) return false;
            var starters = StartingBoatsField.GetValue(menu) as PurchasableBoat[];
            var region = CurrentRegionField.GetValue(menu) is int value ? value : -1;
            return starters != null && region >= 0 && region < starters.Length &&
                starters[region] != null && starters[region].isPurchased();
        }

        private static void DisarmCustomStart()
        {
            CleanupCustom(() => Plugin.Instance?.CancelBoatSettle());
            CleanupCustom(() => Plugin.Instance?.CancelPlayerStartTracking());
            CleanupCustom(() => Plugin.Instance?.ClearDocksideSurface());
            CleanupCustom(StarterCargo.Disarm);
            CleanupCustom(StartingValues.Cancel);
            CleanupCustom(LeopardStarterRig.Cancel);
        }

        // Returns null only when no placement could be produced. A problem that
        // still leaves a usable placement is logged and placement continues.
        private static CustomPlacement TryPrepareCustom(StartMenu menu, StartPair pair, StartingOptionsSettings options)
        {
            CustomPlacement placement = null;
            try
            {
                if (!TryResolve(pair.Port, pair.Boat.Index, pair.Boat.Size, out var selected, out var reason))
                    return NoPlacement("The selected start could not be planned: " + reason);
                if (selected.Boat != pair.Boat.Boat || selected.Saveable != pair.Boat.Saveable)
                    Plugin.Instance?.Warn("The selected boat changed while scenery loaded. Using the boat now loaded for that selection.");
                if (selected.Saveable.GetComponent<BoatDamage>() == null ||
                    selected.Saveable.GetComponent<BoatLocalItems>() == null ||
                    selected.Ropes == null || selected.Ropes.anchor == null || selected.Ropes.GetAnchorController() == null)
                    return NoPlacement("The selected boat lacks native shipwreck-recovery components.");
                if (selected.Berth.Anchored && (AnchorGroundedMethod == null || SetAnchorMethod == null || ReleaseAnchorMethod == null))
                    return NoPlacement("The native anchor lifecycle methods are unavailable in this game build.");
                selected.Options = options; selected.Size = pair.Boat.Size; selected.Menu = menu;
                if (!StarterCargo.TryPreflightShoreCapacity(selected, selected.Berth.ShorePosition, out reason))
                    Plugin.Instance?.Warn("Starter supplies may not all fit at the landing. The start continues. " + reason);
                placement = new CustomPlacement(selected, menu);
                placement.Move();
                return placement;
            }
            catch (Exception exception)
            {
                var failedPlacement = placement;
                CleanupCustom(() => failedPlacement?.Rollback());
                Plugin.Instance?.Error("Starting-location placement failed. Continuing with Sailwind's own start target.", exception);
                return null;
            }
        }

        private static CustomPlacement NoPlacement(string reason)
        {
            Plugin.Instance?.Error(reason + " Continuing with Sailwind's own start target.", null);
            return null;
        }

        private static bool NativeRecoveryReady(out string reason)
        {
            var ports = RecoveryPortsField.GetValue(null) as System.Collections.Generic.List<RecoveryPort>;
            var ready = ports != null && ports.Count > 0 && ports.All(port => port != null &&
                StartDestinations.Loaded(port) && port.parentPort != null && port.boatPos != null &&
                port.mooringFront != null && port.mooringBack != null);
            reason = ready ? null : "Native recovery destinations are incomplete, so shipwreck recovery may not find a port.";
            return ready;
        }

        private sealed class CustomPlacement
        {
            internal readonly ResolvedStart Selected;
            private readonly StartMenu menu;
            private readonly Transform parent;
            private readonly Vector3 localPosition, velocity, angularVelocity;
            private readonly Quaternion localRotation;
            private readonly bool bodyKinematic;
            private readonly RopePosition[] ropePositions;
            private readonly Rigidbody anchorBody;
            private readonly Transform anchorParent;
            private readonly Vector3 anchorLocal, anchorVelocity, anchorAngularVelocity;
            private readonly Quaternion anchorRotation;
            private readonly Vector3 anchorBoatLocal;
            private readonly Quaternion anchorBoatRotation;
            private readonly float anchorLength, anchorDrag;
            private readonly bool anchorKinematic, anchorWasSet;
            private readonly PurchasableBoat[] starters;
            private readonly Transform starterBoat, lastBoat, lastOwnedBoat;
            private readonly Port lastPort;
            private ProbeVelocityGuard guard;
            private PirateStationKeeper stationKeeper;
            private PickupableBoatMooringRope frontRope, backRope;
            private bool moved, rolledBack, changedNative;

            internal CustomPlacement(ResolvedStart selected, StartMenu menu)
            {
                Selected = selected; this.menu = menu;
                var body = selected.Body; parent = body.transform.parent;
                localPosition = body.transform.localPosition; localRotation = body.transform.localRotation;
                velocity = body.velocity; angularVelocity = body.angularVelocity; bodyKinematic = body.isKinematic;
                var anchor = selected.Ropes.anchor;
                if (anchor == null) throw new InvalidOperationException("The native anchor is unavailable.");
                anchorWasSet = anchor.IsSet();
                if (anchorWasSet && !selected.Berth.OffshoreHeld)
                    throw new InvalidOperationException("The original boat anchor must be unset.");
                anchorBody = anchor.GetComponent<Rigidbody>();
                if (anchorBody == null) throw new InvalidOperationException("The native anchor body is unavailable.");
                anchorParent = anchor.transform.parent; anchorLocal = anchor.transform.localPosition; anchorRotation = anchor.transform.localRotation;
                anchorBoatLocal = body.transform.InverseTransformPoint(anchor.transform.position);
                anchorBoatRotation = Quaternion.Inverse(body.rotation) * anchorBody.rotation;
                anchorVelocity = anchorBody.velocity; anchorAngularVelocity = anchorBody.angularVelocity;
                anchorKinematic = anchorBody.isKinematic; anchorDrag = anchorBody.drag; anchorLength = anchor.GetRopeLength();
                ropePositions = selected.Ropes.ropes.Select(rope => new RopePosition { Rope = rope,
                    Parent = rope.transform.parent, Position = rope.transform.localPosition,
                    Rotation = rope.transform.localRotation, Mooring = GetOriginalMooring(rope) }).ToArray();
                starters = (PurchasableBoat[])StartingBoatsField.GetValue(menu);
                if (starters == null || selected.Region < 0 || selected.Region >= starters.Length)
                    throw new InvalidOperationException("The native regional starter array is incomplete.");
                starterBoat = selected.StarterSet.starterBoat;
                lastBoat = GameState.lastBoat; lastOwnedBoat = GameState.lastOwnedBoat; lastPort = GameState.lastVisitedPort;
            }

            internal void Move()
            {
                var s = Selected;
                if (!s.Berth.Revalidate(s.Body, s.Ropes, out var reason))
                    Plugin.Instance?.Warn("The planned berth did not pass its final check (" + reason + "). The boat is placed there anyway.");
                guard = new ProbeVelocityGuard(s.Saveable);
                moved = true;
                if (anchorWasSet) ReleaseAnchorMethod.Invoke(s.Ropes.anchor, null);
                s.Ropes.UnmoorAllRopes(); s.Ropes.GetAnchorController().ResetAnchor();
                s.Body.transform.SetPositionAndRotation(s.Berth.Position, s.Berth.Rotation);
                s.Body.position = s.Berth.Position; s.Body.rotation = s.Berth.Rotation;
                s.Body.velocity = Vector3.zero; s.Body.angularVelocity = Vector3.zero;
                if (s.Berth.Anchored)
                {
                    // Only the bounded contact wait holds the hull. Successful
                    // completion releases it to normal native anchor physics.
                    s.Body.isKinematic = true;
                    anchorBody.rotation = s.Berth.AnchorModel.AnchorRotation;
                    anchorBody.position = s.Berth.AnchorPosition;
                    anchorBody.isKinematic = false;
                    s.Ropes.anchor.OnLoad(false, s.Berth.AnchorLength);
                }
                else if (s.Berth.Mode == StartBerthMode.OffshoreHeld)
                {
                    StowOffshoreAnchor();
                    stationKeeper = PirateStationKeeper.Attach(s.Body, s.Berth.Frame);
                }
                else
                {
                    anchorBody.position = s.Body.transform.TransformPoint(anchorBoatLocal);
                    frontRope = s.Berth.FrontRope; backRope = s.Berth.BackRope;
                    MoorAt(frontRope, s.FrontMooring, s.Body); MoorAt(backRope, s.BackMooring, s.Body);
                }
                anchorBody.velocity = Vector3.zero; anchorBody.angularVelocity = Vector3.zero;
                Physics.SyncTransforms();
            }

            private void StowOffshoreAnchor()
            {
                var s = Selected;
                var rotation = s.Body.rotation * anchorBoatRotation;
                var joint = AccessTools.Field(typeof(RopeControllerAnchor), "joint")?.GetValue(s.Ropes.GetAnchorController()) as ConfigurableJoint;
                if (!PirateStationKeeper.TryStowedPosition(s.Body, anchorBody, joint, rotation, out var position))
                    throw new InvalidOperationException("The native anchor joint cannot provide a finite stowed pose.");
                // A deployed anchor's original pose belongs only to rollback.
                // Retracting at the physical joint endpoint avoids a zero-length
                // line connected across the old seabed-to-hull separation.
                if (s.Ropes.anchor.IsSet()) ReleaseAnchorMethod.Invoke(s.Ropes.anchor, null);
                s.Ropes.GetAnchorController().ResetAnchor();
                anchorBody.rotation = rotation;
                anchorBody.position = position;
                anchorBody.velocity = Vector3.zero; anchorBody.angularVelocity = Vector3.zero;
                anchorBody.isKinematic = false;
                s.Body.isKinematic = false;
                Physics.SyncTransforms();
            }

            internal bool SecureAnchor(out string reason)
            {
                reason = null;
                var s = Selected;
                if (!AnchorGrounded(s.Ropes.anchor) || !StartDestinations.IsCurrent(s.Destination))
                { reason = "The anchorage changed before native anchor setup."; return false; }
                if (s.Berth.AnchorModel == null ||
                    !s.Berth.AnchorModel.ReadyToSecure(s.Ropes, s.Body, s.Berth.AnchorLength,
                        s.Berth.AnchorPosition, out reason)) return false;
                try { SetAnchorMethod.Invoke(s.Ropes.anchor, null); }
                catch (Exception exception)
                {
                    reason = "native anchor setup failed: " + (exception.InnerException ?? exception).Message;
                    return false;
                }
                s.Body.isKinematic = bodyKinematic;
                if (!s.Ropes.anchor.IsSet()) { reason = "the native anchor did not report set"; return false; }
                return true;
            }

            // Release temporary holds when setup cannot finish, so the hull
            // returns to normal physics where it was placed.
            internal void ReleaseHull()
            {
                if (Selected.Body != null) Selected.Body.isKinematic = bodyKinematic;
                guard?.Restore(); guard = null;
            }

            internal bool Commit(out Transform target, out string reason)
            {
                target = null; reason = null;
                try
                {
                    var s = Selected;
                    if (!StartDestinations.IsCurrent(s.Destination)) { reason = "the starting location is no longer loaded"; return false; }
                    if (!NativeRecoveryReady(out var recovery)) Plugin.Instance?.Warn(recovery + " The start continues.");
                    target = Plugin.Instance.MakeTemporaryStart(s, s.Berth.Rotation, s.Berth.ShorePosition, s.Berth.Position);
                    var replacement = (PurchasableBoat[])starters.Clone(); replacement[s.Region] = s.Boat;
                    changedNative = true;
                    StartingBoatsField.SetValue(menu, replacement); s.StarterSet.starterBoat = s.Saveable.transform;
                    GameState.lastBoat = s.Saveable.transform; GameState.lastOwnedBoat = s.Saveable.transform;
                    GameState.lastVisitedPort = s.Port;
                    Plugin.Instance.BeginPlayerStartTracking(menu, target);
                    StarterCargo.Arm(s); StartingValues.Arm(s);
                    if (s.Berth.Mode == StartBerthMode.Moored)
                    {
                        StarterMooring.Arm(menu, s, frontRope, backRope);
                    }
                    StarterBoatRepair.Arm(menu, s); LeopardStarterRig.Arm(menu, s);
                    guard?.Restore(); guard = null;
                    StarterMooring.MarkSettled(s); StarterBoatRepair.MarkSettled(s);
                    return true;
                }
                catch (Exception exception) { reason = "Starting-location handoff failed: " + exception.Message; return false; }
            }

            internal void Rollback()
            {
                if (rolledBack) return;
                rolledBack = true;
                PirateStationKeeper.Remove(stationKeeper);
                var s = Selected;
                try
                {
                    if (GameState.currentlyLoading) return;
                    if (changedNative)
                    {
                        StartingBoatsField.SetValue(menu, starters); s.StarterSet.starterBoat = starterBoat;
                        GameState.lastBoat = lastBoat; GameState.lastOwnedBoat = lastOwnedBoat; GameState.lastVisitedPort = lastPort;
                    }
                    if (!moved || s.Body == null) return;
                    if (s.Ropes != null) s.Ropes.UnmoorAllRopes();
                    if (s.Ropes?.anchor != null && s.Ropes.anchor.IsSet()) ReleaseAnchorMethod.Invoke(s.Ropes.anchor, null);
                    s.Body.transform.SetParent(parent, false); s.Body.transform.localPosition = localPosition; s.Body.transform.localRotation = localRotation;
                    s.Body.position = s.Body.transform.position; s.Body.rotation = s.Body.transform.rotation;
                    s.Body.velocity = velocity; s.Body.angularVelocity = angularVelocity; s.Body.isKinematic = bodyKinematic;
                    if (anchorBody != null)
                    {
                        anchorBody.transform.SetParent(anchorParent, false); anchorBody.transform.localPosition = anchorLocal; anchorBody.transform.localRotation = anchorRotation;
                        anchorBody.position = anchorBody.transform.position; anchorBody.rotation = anchorBody.transform.rotation;
                        anchorBody.velocity = anchorVelocity; anchorBody.angularVelocity = anchorAngularVelocity;
                        anchorBody.isKinematic = anchorKinematic; anchorBody.drag = anchorDrag;
                    }
                    if (s.Ropes?.anchor != null && (!anchorWasSet || anchorBody != null))
                        s.Ropes.anchor.OnLoad(anchorWasSet, anchorLength);
                    foreach (var saved in ropePositions)
                    {
                        if (saved.Rope == null) continue;
                        try
                        {
                            if (saved.Mooring != null) saved.Rope.MoorTo(saved.Mooring);
                            else { saved.Rope.transform.SetParent(saved.Parent, false); saved.Rope.transform.localPosition = saved.Position; saved.Rope.transform.localRotation = saved.Rotation; }
                        }
                        catch (Exception exception) { Plugin.Instance?.Warn("An original mooring could not be restored: " + exception.Message); }
                    }
                    Physics.SyncTransforms();
                }
                finally { guard?.Restore(); guard = null; }
            }
        }
    }
}
