using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    internal static partial class FixedStart
    {
        private static StartSnapshot pendingSnapshot;
        private static Action startCleanup;
        private static int startGeneration;
        private static int flightGeneration = -1;
        internal static bool StartInFlight => flightGeneration == startGeneration;
        private static float startSelectedAt = -1f;

        internal static bool TryGetStartBlock(out string reason)
        {
            reason = StartInFlight ? "A starting location is already being prepared." : null;
            return reason != null;
        }

        private static readonly System.Reflection.MethodInfo MovePlayerFactory = AccessTools.Method(typeof(StartMenu), "MovePlayerToStartPos");
        private static readonly System.Reflection.MethodInfo ReleaseAnchorMethod = AccessTools.Method(typeof(Anchor), "ReleaseAnchor");
        private static readonly System.Reflection.FieldInfo IslandMenuField = AccessTools.Field(typeof(StartMenu), "chooseIslandUI");
        private static readonly System.Reflection.FieldInfo LogoField = AccessTools.Field(typeof(StartMenu), "logo");
        private static readonly System.Reflection.FieldInfo CursorEnabledField = AccessTools.Field(typeof(MouseLook), "cursorEnabled");
        private static readonly System.Reflection.FieldInfo ControllerPointerField = AccessTools.Field(typeof(MouseButtonPointer), "controllerPointer");

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

        private static void CleanupStart(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception exception) { Plugin.Instance?.Error("A disappeared object interrupted starting-location cleanup.", exception); }
        }

        internal static bool TryMoorAt(PickupableBoatMooringRope rope, GPButtonDockMooring dock, Rigidbody body)
            => TryStartupSetup("connect a mooring rope", () => MoorAt(rope, dock, body));

        private static GPButtonDockMooring GetOriginalMooring(PickupableBoatMooringRope rope)
        {
            if (!rope.IsMoored()) return null;
            var spring = MooredSpringField.GetValue(rope) as SpringJoint;
            var mooring = spring != null ? spring.GetComponent<GPButtonDockMooring>() : null;
            if (mooring == null || mooring.spring != spring)
            {
                Plugin.Instance?.Warn("An original mooring has no recoverable dock spring. Its rope pose is retained for cancellation and the selected start continues.");
                return null;
            }
            return mooring;
        }

        private static bool TryStartupSetup(string step, Action action)
        {
            try { action(); return true; }
            catch (Exception exception)
            {
                Plugin.Instance?.Warn("Could not " + step + ". The selected start continues. " + exception.Message);
                return false;
            }
        }

        internal static void CancelStart()
        {
            StarterOwnership.Cancel();
            PirateStationKeeper.RemoveActive();
            ++startGeneration;
            var cleanup = startCleanup; startCleanup = null;
            var snapshot = pendingSnapshot;
            pendingSnapshot = null;
            cleanup?.Invoke();
            CleanupStart(() => snapshot?.Restore("Starting-location preparation was cancelled."));
        }

        // The one owner of "cancel everything for the current start". Each
        // subsystem keeps its own Arm/Cancel and generation; this only fixes
        // which of them are cancelled and in what order. CancelStart runs
        // first, so an in-flight placement rolls back before the armed work
        // it scheduled is cancelled.
        internal static void CancelStartWork(string reason = null)
        {
            CancelStart();
            CancelArmedSystems(reason);
        }

        // Also run by an in-flight start's cleanup, which CancelStart may
        // invoke, so it must not call CancelStart itself. Every Cancel is
        // idempotent; a second pass after that cleanup changes nothing.
        private static void CancelArmedSystems(string reason = null)
        {
            CleanupStart(StarterMooring.Cancel);
            CleanupStart(() => StarterBoatRepair.Cancel(reason));
            CleanupStart(StartArrival.CancelTracking);
            CleanupStart(StartArrival.ClearSite);
            CleanupStart(StarterCargo.Disarm);
            CleanupStart(StartingValues.Cancel);
            CleanupStart(StarterOwnership.Cancel);
            CleanupStart(LeopardStarterRig.Cancel);
        }

        // Loading a save never arms startup work. Cancel an interrupted
        // new-game start before native loading raises its own justStarted.
        [HarmonyPatch(typeof(SaveLoadManager), "LoadGame", new[] { typeof(int) })]
        private static class CancelBeforeLoad
        {
            private static void Prefix() => CancelStartWork("saved-game load");
        }

        // Native FadeIslandMenu owns a separate Juicebox tween and a delayed
        // hide. A cancelled start can restore the menu before either completes.
        // Hide this one menu immediately so that restore has no foreign
        // animation to race.
        [HarmonyPatch(typeof(StartMenu), "DisableIslandMenu")]
        private static class StartMenuHide
        {
            private static bool Prefix(StartMenu __instance)
            {
                return pendingSnapshot == null || !pendingSnapshot.Hide(__instance);
            }
        }

        // Captured before StartNewGame's currency/region/menu writes. There are
        // no ownership writes in that method after its coroutine factory call.
        private sealed class StartSnapshot
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

            internal StartSnapshot(StartMenu menu, int originalRegion)
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

        // Keep the original factory signature, but never replace an accepted
        // selection with the regional starter when its setup cannot complete.
        internal static bool TryPreparePendingStart(StartMenu menu, out IEnumerator sequence,
            Func<Transform, IEnumerator> continuation = null)
        {
            sequence = null;
            if (pending == null || !ReferenceEquals(menu, pendingMenu)) return false;
            var pair = pending; var options = pendingOptions; var snapshot = pendingSnapshot;
            pending = null; pendingMenu = null; pendingOptions = null; pendingSnapshot = null;
            sequence = PrepareStart(menu, pair, options, snapshot, continuation);
            return true;
        }

        // Once a start is selected, a placement problem never returns the
        // player to the menu. Each problem is logged and the start continues
        // with the planned berth. A missing boat, actor or finite target is
        // reported as an incomplete start, never a different starter. Only a real
        // cancellation (a saved-game load, another new game or plugin
        // teardown) unwinds the selected start.
        private static IEnumerator PrepareStart(StartMenu menu, StartPair pair,
            StartingOptionsSettings options, StartSnapshot snapshot,
            Func<Transform, IEnumerator> continuation)
        {
            var generation = startGeneration;
            flightGeneration = generation;
            const string cancelled = "Starting-location preparation was cancelled.";
            StartPlacement placement = null;
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
                    CancelArmedSystems();
                    CleanupStart(() => snapshot?.Restore(cancelled));
                    ClearStartTiming();
                    Plugin.Instance?.Warn(cancelled);
                }
            };
            startCleanup = cleanup;
            try
            {
                Transform target = null;
                if (snapshot == null)
                {
                    Plugin.Instance?.Error("The selected start cannot continue because its native start state is unavailable. No regional start was substituted.", null);
                    yield break;
                }
                else
                {
                    snapshot.Approach(pair.Port);
                    var deadline = Time.realtimeSinceStartup + 30f;
                    var readyFrame = -1;
                    while (generation == startGeneration && menu != null && !GameState.currentlyLoading &&
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
                    if (generation != startGeneration || menu == null || GameState.currentlyLoading) yield break;
                    if (readyFrame < 0 || Time.realtimeSinceStartup >= deadline)
                        Plugin.Instance?.Warn("The selected island scenery did not report ready" + SinceSelection() +
                            ". Preparing the start anyway.");
                    else Plugin.Instance?.Report("Starting-location scenery ready" + SinceSelection() + ".");
                    var profiled = StartupProfiler.Enter();
                    try { placement = TryPrepareStart(menu, pair, options); }
                    finally { StartupProfiler.Exit(profiled); }
                    if (placement == null) yield break;
                    if (placement != null)
                    {
                        profiled = StartupProfiler.Enter();
                        try
                        {
                            if (placement.Commit(out var placed, out var problem)) target = placed;
                            else
                            {
                                Plugin.Instance?.Error("The selected start cannot be handed to Sailwind (" + problem +
                                    "). No regional start was substituted.", null);
                                yield break;
                            }
                        }
                        finally { StartupProfiler.Exit(profiled); }
                    }
                }
                if (target == null)
                {
                    Plugin.Instance?.Error("The selected player target is unavailable, so the start cannot continue.", null);
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
                while (generation == startGeneration && menu != null && !GameState.currentlyLoading)
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
                    if (!committed && Granted(placement))
                    {
                        committed = true; startCleanup = null;
                        Plugin.Instance?.Report("Starting boat granted" + SinceSelection() + ".");
                    }
                    yield return native.Current;
                }
                if (Granted(placement)) { committed = true; startCleanup = null; }
                else if (finished)
                    Plugin.Instance?.Error("The native start sequence ended without granting the selected boat. This start is incomplete.", null);
            }
            finally
            {
                if (Granted(placement)) committed = true;
                if (ReferenceEquals(startCleanup, cleanup)) startCleanup = null;
                try { (native as IDisposable)?.Dispose(); }
                catch (Exception exception) { Plugin.Instance?.Error("Native start iterator cleanup failed.", exception); }
                try
                {
                    if (generation != startGeneration || menu == null || GameState.currentlyLoading) cleanup();
                    else if (!finished && !committed)
                    {
                        // An exception or an external stop ended the sequence.
                        // Leave the start as placed, without temporary holds.
                        CleanupStart(() => placement?.ReleaseHull());
                        Plugin.Instance?.Warn("The start sequence stopped before it finished" + SinceSelection() +
                            ". The selected start was left as placed.");
                    }
                }
                finally
                {
                    StarterOwnership.Cancel(placement?.Selected);
                    if (flightGeneration == generation) flightGeneration = -1;
                }
            }
        }

        private static bool Granted(StartPlacement placement)
        {
            if (placement != null) return placement.Selected.Boat != null && placement.Selected.Boat.isPurchased();
            return false;
        }

        // Returns null only when no placement could be produced. A problem that
        // still leaves a usable placement is logged and placement continues.
        private static StartPlacement TryPrepareStart(StartMenu menu, StartPair pair, StartingOptionsSettings options)
        {
            StartPlacement placement = null;
            try
            {
                if (!TryResolve(pair.Port, pair.Boat.Index, pair.Boat.Size, out var selected, out var reason, pair.UseShipyardBerth))
                    return NoPlacement("The selected start could not be planned: " + reason);
                if (selected.Berth == null)
                {
                    // An ordinary port, or a selected yard that disappeared
                    // during scenery loading. Use the berth chosen by the
                    // native resolver and keep its recovery marker as the
                    // player and supply frame.
                    if (pair.UseShipyardBerth)
                        Plugin.Instance?.Warn("The shipyard berth is unavailable. Using this location's ordinary starting berth.");
                    selected.Berth = OrdinaryBerthPlan(selected);
                }
                if (selected.Boat != pair.Boat.Boat || selected.Saveable != pair.Boat.Saveable)
                    Plugin.Instance?.Warn("The selected boat changed while scenery loaded. Using the boat now loaded for that selection.");
                if (selected.Ropes == null || selected.Ropes.anchor == null || selected.Ropes.GetAnchorController() == null)
                    return NoPlacement("The selected boat lacks native shipwreck-recovery components.");
                selected.Options = options; selected.Size = pair.Boat.Size; selected.Menu = menu;
                placement = new StartPlacement(selected, menu);
                placement.Move();
                return placement;
            }
            catch (Exception exception)
            {
                var failedPlacement = placement;
                CleanupStart(() => failedPlacement?.Rollback());
                Plugin.Instance?.Error("The selected boat could not be placed. This start is incomplete and no regional start was substituted.", exception);
                return null;
            }
        }

        private static StartPlacement NoPlacement(string reason)
        {
            Plugin.Instance?.Error(reason + " This start is incomplete and no regional start was substituted.", null);
            return null;
        }

        private static StartBerthPlan OrdinaryBerthPlan(ResolvedStart selected)
        {
            var native = selected.NativeBerth;
            var rotation = native != null ? selected.Body.rotation : selected.Recovery.boatPos.rotation;
            var position = native != null ? selected.Body.position : GetBerthPosition(selected);
            if (native == null)
            {
                // Optional berth spacing never prevents the start. A failed
                // adjustment keeps the position chosen before it.
                try { position = BerthClearance.AdjustCrabBeach(selected, position); }
                catch (Exception exception)
                { Plugin.Instance?.Warn("Crab Beach berth clearance was skipped; the native berth is used. " + exception.Message); }
                try { position = AdjustLargeBoatBerth(selected, position, rotation); }
                catch (Exception exception)
                { Plugin.Instance?.Warn("Large-boat berth distance was skipped; the unadjusted berth is used. " + exception.Message); }
            }
            var shore = native != null ? native.ShorePosition : selected.Recovery.transform.position;
            var frame = selected.Destination.DockRoot;
            return new StartBerthPlan { Destination = selected.Destination, Frame = frame,
                LocalPosition = frame.InverseTransformPoint(position), LocalRotation = Quaternion.Inverse(frame.rotation) * rotation,
                LocalShorePosition = frame.InverseTransformPoint(shore),
                Mode = native != null ? StartBerthMode.Preserved : StartBerthMode.Moored,
                Front = selected.FrontMooring, Back = selected.BackMooring,
                FrontRope = native?.FrontRope, BackRope = native?.BackRope };
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

        private sealed class StartPlacement
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
            private PirateStationKeeper stationKeeper;
            private PickupableBoatMooringRope frontRope, backRope;
            private bool moved, rolledBack, changedNative;

            internal StartPlacement(ResolvedStart selected, StartMenu menu)
            {
                Selected = selected; this.menu = menu;
                var body = selected.Body; parent = body.transform.parent;
                localPosition = body.transform.localPosition; localRotation = body.transform.localRotation;
                velocity = body.velocity; angularVelocity = body.angularVelocity; bodyKinematic = body.isKinematic;
                var anchor = selected.Ropes.anchor;
                if (anchor == null) throw new InvalidOperationException("The native anchor is unavailable.");
                anchorWasSet = anchor.IsSet();
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
                // The boat already lies at its own authored berth, as resolved
                // this frame. Leave its hull, anchor and dock lines untouched.
                if (s.Berth.Mode == StartBerthMode.Preserved) return;
                moved = true;
                if (anchorWasSet)
                    TryStartupSetup("release the starter anchor", () => ReleaseAnchorMethod.Invoke(s.Ropes.anchor, null));
                TryStartupSetup("release the original moorings", s.Ropes.UnmoorAllRopes);
                TryStartupSetup("retract the starter anchor", () => s.Ropes.GetAnchorController().ResetAnchor());
                s.Body.transform.SetPositionAndRotation(s.Berth.Position, s.Berth.Rotation);
                s.Body.position = s.Berth.Position; s.Body.rotation = s.Berth.Rotation;
                s.Body.velocity = Vector3.zero; s.Body.angularVelocity = Vector3.zero;
                if (s.Berth.OffshoreHeld)
                {
                    StowOffshoreAnchor();
                    if (s.Berth.Mode == StartBerthMode.ShipyardHeld &&
                        s.Berth.FrontRope != null && s.Berth.BackRope != null)
                    {
                        frontRope = s.Berth.FrontRope; backRope = s.Berth.BackRope;
                        var frontReady = TryMoorAt(frontRope, s.FrontMooring, s.Body);
                        var backReady = TryMoorAt(backRope, s.BackMooring, s.Body);
                        if (frontReady && backReady) s.Berth.Mode = StartBerthMode.Moored;
                        else
                        {
                            if (frontRope.IsMoored()) TryStartupSetup("release an incomplete shipyard mooring", frontRope.Unmoor);
                            if (backRope.IsMoored()) TryStartupSetup("release an incomplete shipyard mooring", backRope.Unmoor);
                            frontRope = null; backRope = null;
                        }
                    }
                    if (s.Berth.OffshoreHeld) stationKeeper = PirateStationKeeper.Attach(s.Body, s.Berth.Frame);
                }
                else
                {
                    anchorBody.position = s.Body.transform.TransformPoint(anchorBoatLocal);
                    frontRope = s.Berth.FrontRope; backRope = s.Berth.BackRope;
                    if (frontRope == null || backRope == null)
                        TryStartupSetup("choose starter mooring ropes", () => ChooseMooringRopes(s, out frontRope, out backRope));
                    var frontReady = TryMoorAt(frontRope, s.FrontMooring, s.Body);
                    var backReady = TryMoorAt(backRope, s.BackMooring, s.Body);
                    if (!frontReady || !backReady) { frontRope = null; backRope = null; }
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
                // Move already attempted native release and retraction. Only
                // align the physical endpoint here, without repeating setup.
                anchorBody.rotation = rotation;
                anchorBody.position = position;
                anchorBody.velocity = Vector3.zero; anchorBody.angularVelocity = Vector3.zero;
                anchorBody.isKinematic = false;
                s.Body.isKinematic = false;
                Physics.SyncTransforms();
            }

            // Release temporary holds when setup cannot finish, so the hull
            // returns to normal physics where it was placed.
            internal void ReleaseHull()
            {
                if (moved && Selected.Body != null) Selected.Body.isKinematic = bodyKinematic;
            }

            internal bool Commit(out Transform target, out string reason)
            {
                target = null; reason = null;
                try
                {
                    var s = Selected;
                    if (!StartDestinations.IsCurrent(s.Destination)) { reason = "the starting location is no longer loaded"; return false; }
                    if (!NativeRecoveryReady(out var recovery)) Plugin.Instance?.Warn(recovery + " The start continues.");
                    // A relocated ordinary port keeps Sailwind's own berth marker as the
                    // probe reference, so an occupied-berth or Crab Beach offset of
                    // the hull does not turn the inland probe direction.
                    var probeBerth = s.NativeBerth == null && s.Recovery != null ? s.Recovery.boatPos.position : s.Berth.Position;
                    var playerTarget = StartArrival.MakeTarget(s, s.Berth.Rotation, s.Berth.ShorePosition, probeBerth);
                    target = playerTarget;
                    var replacement = (PurchasableBoat[])starters.Clone(); replacement[s.Region] = s.Boat;
                    changedNative = true;
                    StartingBoatsField.SetValue(menu, replacement); s.StarterSet.starterBoat = s.Saveable.transform;
                    GameState.lastBoat = s.Saveable.transform; GameState.lastOwnedBoat = s.Saveable.transform;
                    GameState.lastVisitedPort = s.Port;
                    StarterOwnership.Arm(s);
                    StartingValues.Arm(s);
                    TryStartupSetup("track the player's landing", () => StartArrival.BeginTracking(menu, playerTarget));
                    TryStartupSetup("prepare starter supplies", () => StarterCargo.Arm(s));
                    // Only lines this start connected receive slack. A preserved
                    // berth keeps its native lines as authored.
                    if (s.Berth.Mode == StartBerthMode.Moored && frontRope != null && backRope != null)
                    {
                        TryStartupSetup("settle starter moorings", () => StarterMooring.Arm(menu, s, frontRope, backRope));
                    }
                    TryStartupSetup("schedule starter hull repair", () => StarterBoatRepair.Arm(menu, s));
                    TryStartupSetup("prepare the starter rig", () => LeopardStarterRig.Arm(menu, s));
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
                StarterOwnership.Cancel(s);
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
        }
    }
}
