using System.Collections;
using NewBeginnings;
using UnityEngine;

static class Checks
{
    static int assertions;
    static void Check(bool value, string label)
    {
        assertions++;
        if (!value)
            throw new Exception(label);
    }

    static Transform At(float x, float y = 0, float z = 0)
    {
        var t = new GameObject().transform;
        t.position = new(x, y, z);
        return t;
    }

    static T On<T>(Transform t)
        where T : Component, new() => t.gameObject.Add<T>();
    sealed class Fixture
    {
        internal StartMenu Menu;
        internal ResolvedStart Selected;
        internal IEnumerator Routine;
        internal Transform World, OldBoat, OldOwned;
        internal Port OldPort;
        internal PurchasableBoat[] Starters;
        internal GPButtonDockMooring OldCleat;
        internal Rigidbody AnchorBody;
        internal Anchor Anchor;
        internal Fixture(bool scrambled = false, bool pirateOffshore = false, Action<Fixture> beforeBegin = null)
        {
            FixedStart.CancelStart();
            Plugin.Instance = new();
            GameState.currentlyLoading = false;
            Time.frameCount = 0;
            Time.realtimeSinceStartup = 0;
            StarterCargo.ThrowArm = false;
            StarterMooring.Cancel();
            StarterBoatRepair.Cancel();
            StarterCargo.Disarm();
            StartingValues.Cancel();
            LeopardStarterRig.Cancel();
            PlayerGold.currency = new[] { 10, 20, 30, 40 };
            GameState.newGameRegion = PortRegion.Emerald;
            MouseLook.cursorEnabled = true;
            GameState.inCursorMenu = true;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.Confined;
            MouseButtonPointer.instance = On<MouseButtonPointer>(At(0));
            MouseButtonPointer.instance.controllerPointer = At(0);
            World = At(0);
            OldBoat = At(60);
            OldOwned = At(70);
            OldPort = On<Port>(At(80));
            GameState.lastBoat = OldBoat;
            GameState.lastOwnedBoat = OldOwned;
            GameState.lastVisitedPort = OldPort;
            var menuRoot = At(0);
            Menu = On<StartMenu>(menuRoot);
            Menu.Observer = At(1, 2, 3);
            On<PlayerControllerMirror>(Menu.Observer).enabled = false;
            Menu.Controller = At(4, 5, 6).gameObject;
            Menu.Controller.Add<CharacterController>().enabled = false;
            Menu.Controller.Add<OVRPlayerController>().enabled = false;
            Menu.currentRegion = 1;
            Menu.animsPlaying = 0;
            Menu.chooseIslandUI.transform.localPosition = new(3, 4, 5);
            FixedStart.NativeStart = At(500);
            Starters = Enumerable.Range(0, 3).Select(i => On<PurchasableBoat>(At(100 + i))).ToArray();
            Menu.startingBoats = Starters;
            Menu.Observer.SetParent(World, true);
            var boat = At(10);
            boat.SetParent(World, true);
            var save = On<SaveableObject>(boat);
            var body = On<Rigidbody>(boat);
            body.velocity = new(1, 2, 3);
            body.angularVelocity = new(3, 2, 1);
            body.isKinematic = false;
            var purchase = On<PurchasableBoat>(boat);
            On<BoatDamage>(boat);
            On<BoatLocalItems>(boat);
            var ropes = On<BoatMooringRopes>(boat);
            var anchorPoint = At(13);
            anchorPoint.SetParent(World, true);
            Anchor = On<Anchor>(anchorPoint);
            AnchorBody = On<Rigidbody>(anchorPoint);
            AnchorBody.drag = .7f;
            AnchorBody.velocity = new(2, 3, 4);
            AnchorBody.angularVelocity = new(4, 3, 2);
            Anchor.Length = .4f;
            ropes.anchor = Anchor;
            var joint = On<ConfigurableJoint>(anchorPoint);
            joint.connectedBody = body;
            joint.connectedAnchor = new(3, 0, 0);
            ropes.Controller.joint = joint;
            OldCleat = On<GPButtonDockMooring>(At(15));
            OldCleat.transform.SetParent(World, true);
            var front = On<PickupableBoatMooringRope>(At(11));
            front.transform.SetParent(boat, true);
            front.MoorTo(OldCleat);
            var back = On<PickupableBoatMooringRope>(At(12));
            back.transform.SetParent(boat, true);
            ropes.ropes = new[] { front, back };
            var island = On<IslandHorizon>(At(1000));
            var site = new PortChoice
            {
                Island = island,
                HiddenKey = pirateOffshore ? "pirate" : null
            };
            var plan = new StartBerthPlan
            {
                Destination = site,
                Position = new(1000, 1, 0),
                ShorePosition = new(1002, 5, 0),
                Rotation = Quaternion.identity,
                FrontRope = front,
                BackRope = back
            };
            if (pirateOffshore)
                plan.Mode = StartBerthMode.OffshoreHeld;
            Selected = new ResolvedStart
            {
                Boat = purchase,
                Saveable = save,
                Body = body,
                Ropes = ropes,
                Berth = plan,
                Destination = site,
                Region = 2,
                Port = null,
                StarterSet = new()
                {
                    starterBoat = OldBoat
                },
                FrontMooring = On<GPButtonDockMooring>(At(1001)),
                BackMooring = On<GPButtonDockMooring>(At(999))
            };
            Recovery.ports = new()
            {
                new()
                {
                    parentPort = OldPort,
                    gameObject = new GameObject(),
                    boatPos = At(81),
                    mooringFront = At(82),
                    mooringBack = At(83)
                }
            };
            beforeBegin?.Invoke(this);
            Routine = FixedStart.BeginFixture(Menu, Selected, scrambled);
        }

        internal bool Tick()
        {
            Time.frameCount++;
            Time.realtimeSinceStartup += .1f;
            return Routine.MoveNext();
        }

        internal void Until(Func<bool> condition)
        {
            for (int n = 0; n < 100 && !condition(); n++)
            {
                if (!Tick())
                    break;
            }

            Check(condition(), "fixture did not reach requested phase");
        }

        internal void Finish(int limit = 100)
        {
            int n = 0;
            while (Tick() && n++ < limit)
            {
            }

            Check(n < limit, "iterator failed to finish");
        }

        // Native selection and side systems are back to their original state.
        internal void SelectionRestored()
        {
            Check(!Selected.Boat.isPurchased(), "rollback granted boat");
            Check(ReferenceEquals(Menu.startingBoats, Starters), "starter array not restored");
            Check(Selected.StarterSet.starterBoat == OldBoat, "starter set not restored");
            Check(GameState.lastBoat == OldBoat && GameState.lastOwnedBoat == OldOwned && GameState.lastVisitedPort == OldPort, "native selection fields not restored");
            Check(!StarterCargo.Armed && !StartingValues.Armed && !StarterMooring.Armed && !StarterBoatRepair.Armed && !LeopardStarterRig.Armed, "startup work remains armed");
        }

        internal void HullRestored()
        {
            Check(Selected.Body.position.Equals(new Vector3(10, 0, 0)) && !Selected.Body.isKinematic, "hull not restored");
            Check(Selected.Body.velocity.Equals(new Vector3(1, 2, 3)) && Selected.Body.angularVelocity.Equals(new Vector3(3, 2, 1)), "hull velocities not restored");
            Check(Selected.Ropes.ropes[0].Mooring == OldCleat, "old mooring not restored");
        }

        // Cancellation only: the menu, player actors and cursor return to the snapshot.
        internal void MenuRestored()
        {
            Check(PlayerGold.currency.SequenceEqual(new[] { 10, 20, 30, 40 }) && GameState.newGameRegion == PortRegion.Emerald, "currency/region not restored");
            Check(Menu.Observer.position.Equals(new Vector3(1, 2, 3)) && Menu.Controller.transform.position.Equals(new Vector3(4, 5, 6)), "player actors not restored");
            Check(Menu.currentRegion == 1 && Menu.animsPlaying == 0 && Menu.Reopened == 0 && Menu.chooseIslandUI.activeSelf && Menu.chooseIslandUI.transform.localPosition.Equals(new Vector3(3, 4, 5)), "menu pose/visibility/counter not restored without competing fade");
            Check(!Menu.Controller.GetComponent<CharacterController>().enabled && !Menu.Controller.GetComponent<OVRPlayerController>().enabled && !Menu.Observer.GetComponent<PlayerControllerMirror>().enabled, "controls not restored");
            Check(MouseLook.cursorEnabled && GameState.inCursorMenu && Cursor.visible && Cursor.lockState == CursorLockMode.Confined && MouseButtonPointer.instance.controllerPointer.gameObject.activeSelf, "cursor/pointer state not restored");
            Check(Menu.logo.activeSelf && Menu.Observer.parent == World, "logo/observer parent not restored");
        }

        internal void Restored(bool boat = true)
        {
            SelectionRestored();
            MenuRestored();
            if (boat)
                HullRestored();
        }

        // A selected start never returns to the menu after a placement problem.
        internal void StayedOutOfMenu()
        {
            Check(Menu.currentRegion == 2 && PlayerGold.currency[2] == 999 && !Menu.chooseIslandUI.activeSelf && Menu.Reopened == 0, "placement problem returned the player to the menu");
            Check(!FixedStart.StartInFlight && !FixedStart.TryGetStartBlock(out _), "finished start retained a start block");
        }

        // A truly unavailable target cannot be claimed as a successful different start.
        internal void Incomplete()
        {
            StayedOutOfMenu();
            Check(Menu.Native == null && !Selected.Boat.purchased && Starters.All(b => !b.purchased), "hard failure granted a different starter or ran the native sequence");
            Check(Plugin.Instance.Errors.Any(e => e.Contains("incomplete") || e.Contains("No regional start")), "hard failure lacked an explicit incomplete result");
        }

        // The placed boat was granted despite a logged problem.
        internal void GrantedAtBerth(string warning)
        {
            StayedOutOfMenu();
            Check(Selected.Boat.purchased && Selected.Body.position.x == 1000 && !Selected.Body.isKinematic, "placed boat was not granted at its berth with holds released");
            Check(Plugin.Instance.Warnings.Any(w => w.Contains(warning)), "problem was not logged: " + warning);
        }
    }

    static int Main()
    {
        var cases = new Action[]
        {
            BestEffortMooredUsesNativeLines,
            ShipyardMooringAndHoldFallback,
            KiciaOrdinaryShoreFallback,
            NativeYardCapabilityAndOwnership,
            NativeYardMarkerFallsBack,
            ShipyardReleasePoseAndLanding,
            ShipyardCancellationRestores,
            OrdinaryPortRelocatesToRecoveryBerth,
            OrdinaryPortPreservesNativeBerth,
            OrdinaryPreparationFailureIsIncomplete,
            OrdinaryBerthAdjustmentFailuresStillStart,
            ShipyardPlanAvailabilityAndShoreFallback,
            FortStartupUsesItsShipyard,
            AestrinPrefersLoadedDrydock,
            PirateInitiallySetAnchorGrants,
            PirateInitiallySetAnchorCancellationRestores,
            PirateTargetFailureRetainsSelection,
            OrdinaryInitiallySetAnchorGrants,
            PirateHeldCancellationStillRestores,
            PirateDestroyedAnchorWarnsAndContinues,
            StartGuardThroughGrant,
            NoRestartLatchAfterFailure,
            ScrambledWaitsThenContinuesOnce,
            ScrambledTargetFailureIsIncomplete,
            ScrambledCancellationNeverContinues,
            MenuHideOwnership,
            CancelBeforeOwnership,
            PurchaseUiLostWarnsAndContinues,
            OwnershipScope,
            RecoverableSetupFailuresKeepSelection,
            PreGrantNativeExceptionKeepsStart,
            LoadPrefixCancelsBeforeMutation,
            ForeignLoadDoesNotReceiveSnapshot,
            ResolveFailureIsIncomplete,
            NoFactoryKeepsPlacement,
            SuccessfulNativeGrantOwnsBoat,
            RollbackErrorStillRestoresPlayer,
            SceneryWaitGrantsNothing,
            SceneryDeadlineStillPrepares
        };
        foreach (var test in cases)
        {
            try
            {
                FixedStart.ResetStartGuardFixture();
                test();
                Console.WriteLine("PASS " + test.Method.Name);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL " + test.Method.Name + ": " + ex.Message);
                return 1;
            }
        }

        Console.WriteLine($"PASS {assertions} assertions against complete compiled production FixedStart.Placement.cs. Boundary doubles replace native factory, geometry, Unity objects and side systems. No Unity/physics/gameplay claim.");
        return 0;
    }

    static void BestEffortMooredUsesNativeLines()
    {
        var f = new Fixture(beforeBegin: x => x.Selected.Berth.Mode = StartBerthMode.Moored);
        var target = f.Selected.Berth.Position;
        f.Finish();
        Check(f.Selected.Body.position.Equals(target) && f.Selected.Ropes.ropes.All(r => r.IsMoored()) && StarterMooring.Armed && PirateStationKeeper.Active == null, "best-effort rope-reachable berth did not use native lines at unchanged pose");
    }

    static void ShipyardMooringAndHoldFallback()
    {
        foreach (var failedLine in new[] { -1, 0, 1 })
        {
            var f = new Fixture(beforeBegin: x =>
            {
                SetupShipyard(x);
                x.Selected.Berth.FrontRope = x.Selected.Ropes.ropes[0];
                x.Selected.Berth.BackRope = x.Selected.Ropes.ropes[1];
                if (failedLine >= 0)
                    x.Selected.Ropes.ropes[failedLine].ThrowMoor = true;
            });
            var target = f.Selected.Berth.Position;
            f.Finish();
            Check(f.Selected.Boat.purchased && f.Selected.Body.position.Equals(target) && StarterCargo.Armed, "shipyard mooring result blocked the start, supplies or moved the approved pose");
            if (failedLine < 0)
                Check(f.Selected.Berth.Mode == StartBerthMode.Moored && StarterMooring.Armed && PirateStationKeeper.Active == null && f.Selected.Ropes.ropes.All(r => r.IsMoored()), "successful shipyard mooring kept station hold or skipped native slack");
            else
                Check(f.Selected.Berth.Mode == StartBerthMode.ShipyardHeld && !StarterMooring.Armed && PirateStationKeeper.Active != null && f.Selected.Ropes.ropes.All(r => !r.IsMoored()), "failed shipyard connection left partial mooring or lost held fallback");
        }
    }

    static void KiciaOrdinaryShoreFallback()
    {
        var f = new Fixture(beforeBegin: SetupShipyard);
        var site = f.Selected.Destination;
        site.Index = 22;
        site.IdentityName = "port L 22 Lagoon Bay";
        var recovery = On<RecoveryPort>(At(1080, 3, 25));
        recovery.transform.SetParent(site.Island.transform, true);
        recovery.boatPos = At(1080, -1, 15);
        site.Recovery = recovery;
        Check(ShipyardStart.TryPlan(site, out var plan) && Plugin.Instance.LastShoreSeed.Equals(recovery.transform.position) && plan.Position.Equals(site.Yard.shipReleasePosition.position), "Kicia native geometry fallback lost ordinary player root or moved boat");
        site.Island.transform.position += new Vector3(500, 0, -200);
        Check(plan.ShorePosition.Equals(recovery.transform.position + new Vector3(2, 0, 0)), "Kicia ordinary shore ignored world movement");
        site.SiteIdentity = "site:provider|kicia";
        var documents = Resources.FindObjectsOfTypeAll<ShipyardDocuments>().Single(d => d.shipyard == site.Yard);
        Check(ShipyardStart.TryPlan(site, out plan) && Plugin.Instance.LastShoreSeed.Equals(documents.transform.position), "native Kicia shore policy changed registered provider yard");
        site.SiteIdentity = null;
        site.Recovery = null;
        Check(ShipyardStart.TryPlan(site, out plan) && Plugin.Instance.LastShoreSeed.Equals(documents.transform.position), "missing ordinary recovery vetoed yard fallback");
        FixedStart.CancelStart();
        f.Finish();
    }

    static void NativeYardCapabilityAndOwnership()
    {
        foreach (var row in new[]
        {
            (0, 1, "port A0 (Gold Rock)", "shipyard Al'Ankh"),
            (9, 9, "port E 9 (Dragon cliffs)", "shipyard E DragonCliffs"),
            (15, 15, "port M 15 Fort", "shipyard Aestrin"),
            (22, 27, "port L 22 Lagoon Bay", "shipyard Lagoon")
        })
        {
            var island = On<IslandHorizon>(At(1000));
            island.islandIndex = row.Item2;
            Refs.islands[island.islandIndex] = island.transform;
            var site = new PortChoice
            {
                Index = row.Item1,
                IdentityName = row.Item3,
                Island = island,
                NeedsPlannedBerth = false
            };
            Resources.Objects = Array.Empty<Component>();
            Check(ShipyardStart.HasNativeYard(site) && ShipyardStart.Available(site), "unloaded native scenery hid native shipyard capability");
            Check(!ShipyardStart.TryPlan(site, out _), "unloaded yard fabricated release pose");
            var scenery = On<IslandSceneryScene>(At(1000));
            scenery.parentIslandIndex = row.Item2;
            var yard = On<Shipyard>(At(1050, 3));
            yard.name = row.Item4;
            yard.transform.SetParent(scenery.transform, true);
            yard.shipReleasePosition = At(1070, 2);
            yard.shipReleasePosition.SetParent(yard.transform, true);
            Resources.Objects = new Component[]
            {
                yard
            };
            Check(yard.GetComponentInParent<IslandHorizon>() == null && StartDestinations.BelongsToIsland(yard, island), "native additive scenery ownership required IslandHorizon ancestry");
            Check(ShipyardStart.PreferredYard(site, new[] { yard }) == yard && ShipyardStart.TryPlan(site, out var plan) && plan.Position.Equals(yard.shipReleasePosition.position), "late-loaded yard not resolved through its native owner");
            Refs.islands[island.islandIndex] = At(9999);
            Check(!StartDestinations.BelongsToIsland(yard, island), "stale island with same index accepted scenery owner");
            Refs.islands[island.islandIndex] = island.transform;
            scenery.parentIslandIndex++;
            Check(!StartDestinations.BelongsToIsland(yard, island) && ShipyardStart.PreferredYard(site, new[] { yard }) == null, "foreign additive scenery was selected");
            scenery.parentIslandIndex--;
            site.SiteIdentity = "site:provider|yard";
            Check(!ShipyardStart.HasNativeYard(site) && !ShipyardStart.Available(site), "native index bypassed registered site's discovered capability");
            site.Yard = yard;
            Check(ShipyardStart.Available(site) && ShipyardStart.PreferredYard(site, new[] { yard }) == yard, "actual API-selected yard lost capability");
            site.SiteIdentity = null;
            site.IdentityName = "different port";
            site.Yard = null;
            Check(!ShipyardStart.HasNativeYard(site) && !ShipyardStart.Available(site), "native numeric index alone admitted another port");
        }
    }

    static void NativeYardMarkerFallsBack()
    {
        var f = new Fixture();
        FixedStart.CancelStart();
        var s = f.Selected;
        s.UseShipyardBerth = true;
        s.Destination.NeedsPlannedBerth = false;
        s.Destination.Index = 9;
        s.Destination.IdentityName = "port E 9 (Dragon cliffs)";
        s.Port = On<Port>(At(1000));
        s.Recovery = new RecoveryPort
        {
            gameObject = new GameObject(),
            parentPort = s.Port,
            boatPos = At(1050, 1),
            mooringFront = At(1050, 3),
            mooringBack = At(1040, 3)
        };
        On<GPButtonDockMooring>(s.Recovery.mooringFront);
        On<GPButtonDockMooring>(s.Recovery.mooringBack);
        s.Berth = null;
        Resources.Objects = Array.Empty<Component>();
        Check(FixedStart.ResolveRelocationForCheck(s, out var reason), "missing advertised native yard rejected existing ordinary recovery: " + reason);
        Check(!s.UseShipyardBerth && s.Recovery != null && s.FrontMooring != null && s.BackMooring != null, "missing native yard lost ordinary fallback components");
        Check(Plugin.Instance.Warnings.Any(w => w.Contains("ordinary berth")), "missing native yard fallback lacked diagnostic");
        var yard = On<Shipyard>(At(1050, 4));
        yard.name = "shipyard E DragonCliffs";
        yard.transform.SetParent(s.Destination.Island.transform, true);
        yard.shipReleasePosition = At(1055, 1);
        s.Destination.Yard = yard;
        Resources.Objects = new Component[]
        {
            yard
        };
        s.UseShipyardBerth = true;
        Check(FixedStart.ResolveRelocationForCheck(s, out reason) && s.Berth.Mode == StartBerthMode.ShipyardHeld && s.Recovery == null, "successful yard plan kept fallback recovery frame");
    }

    static void SetupShipyard(Fixture f)
    {
        var site = f.Selected.Destination;
        site.NeedsPlannedBerth = false;
        var root = At(1020, 6, 5);
        root.SetParent(site.Island.transform, true);
        var yard = On<Shipyard>(root);
        site.Yard = yard;
        var counter = At(1030, 6, 4);
        counter.SetParent(site.Island.transform, true);
        var documents = On<ShipyardDocuments>(counter);
        documents.shipyard = yard;
        var unrelated = On<ShipyardDocuments>(At(9000, 3, 4));
        unrelated.shipyard = On<Shipyard>(At(9001, 3, 4));
        Resources.Objects = new Component[]
        {
            unrelated,
            documents
        };
        yard.shipReleasePosition = At(1040, 2, 12);
        yard.shipReleasePosition.SetParent(root, true);
        yard.shipReleasePosition.rotation = new Quaternion(0, .70710677f, 0, .70710677f);
        Check(ShipyardStart.TryPlan(site, out var plan), "live shipyard marker did not produce plan");
        Check(Plugin.Instance.LastShoreSeed.Equals(counter.position), "shipyard shore seed lost native owner reference to sibling documents");
        f.Selected.Berth = plan;
        f.Selected.UseShipyardBerth = true;
    }

    static void ShipyardReleasePoseAndLanding()
    {
        var f = new Fixture(beforeBegin: SetupShipyard);
        var marker = f.Selected.Destination.Yard.shipReleasePosition;
        f.Selected.Destination.Island.transform.position += new Vector3(500, 0, -700);
        var target = marker.position;
        f.Finish();
        Check(FixedStart.RequestedYard && f.Selected.Boat.purchased && f.Selected.Body.position.Equals(target), "shipyard preference snapshot or moving-frame native release pose lost");
        Check(f.Selected.Body.rotation.Equals(marker.rotation), "shipyard release rotation altered");
        Check(f.Menu.NativeTarget.position.Equals(f.Selected.Berth.ShorePosition) && f.Menu.NativeTarget.position.y == 6 && f.Menu.NativeTarget.position.x != target.x, "player was put aboard instead of on dry shore");
        Check(StarterCargo.Armed && PirateStationKeeper.Active != null && !StarterMooring.Armed && !f.Anchor.IsSet(), "shipyard start failed supply/hold setup or used mooring/anchor path");
        Check(f.Selected.Destination.NeedsPlannedBerth == false, "shipyard start mutated catalog recovery identity");
    }

    static void ShipyardCancellationRestores()
    {
        var f = new Fixture(beforeBegin: SetupShipyard);
        f.Until(() => f.Menu.Native != null);
        var held = PirateStationKeeper.Active;
        FixedStart.CancelStart();
        f.Finish();
        f.Restored();
        Check(held.Removed, "cancelled shipyard start kept temporary hold");
    }

    // An ordinary port keeps its complete native recovery berth and marker.
    static void SetupOrdinaryPort(Fixture f)
    {
        var s = f.Selected;
        s.Destination.NeedsPlannedBerth = false;
        s.Berth = null;
        s.Port = On<Port>(At(1000));
        s.Recovery = new RecoveryPort
        {
            gameObject = At(200, 5, 1).gameObject,
            parentPort = s.Port,
            boatPos = At(203, 2, 1)
        };
    }

    static void OrdinaryPortRelocatesToRecoveryBerth()
    {
        foreach (var yardFallback in new[] { false, true })
        {
            var f = new Fixture(beforeBegin: x =>
            {
                if (yardFallback)
                {
                    SetupShipyard(x);
                    x.Selected.Destination.Yard.shipReleasePosition = null;
                }
                SetupOrdinaryPort(x);
            });
            var recovery = f.Selected.Recovery;
            f.Finish();
            Check(f.Selected.Boat.purchased && f.Selected.Body.position.Equals(new Vector3(204, 2, 2)) && f.Starters.All(b => !b.purchased), "ordinary port did not grant the selected boat at its adjusted recovery berth");
            Check(f.Selected.Berth.Mode == StartBerthMode.Moored && f.Selected.Ropes.ropes[0].Mooring == f.Selected.FrontMooring && f.Selected.Ropes.ropes[1].Mooring == f.Selected.BackMooring && StarterMooring.Armed, "ordinary port did not moor with the chosen ropes and arm slack");
            Check(f.Selected.Recovery == recovery && f.Menu.NativeTarget.position.Equals(recovery.transform.position) && PirateStationKeeper.Active == null && Plugin.Instance.LastBerth.Equals(recovery.boatPos.position), "ordinary port lost its recovery marker or probe reference, or kept a hold");
            Check(Plugin.Instance.Warnings.Any(w => w.Contains("ordinary starting berth")) == yardFallback, "ordinary berth diagnostic did not match the shipyard fallback");
        }
    }

    static void OrdinaryPortPreservesNativeBerth()
    {
        foreach (var cancel in new[] { false, true })
        {
            var f = new Fixture(beforeBegin: x =>
            {
                SetupOrdinaryPort(x);
                x.Anchor.OnLoad(true, .4f);
                x.Selected.NativeBerth = new NativeBerth.Plan
                {
                    ShorePosition = new(15, 5, 0),
                    FrontRope = x.Selected.Ropes.ropes[0],
                    BackRope = x.Selected.Ropes.ropes[1]
                };
            });
            var anchorCalls = f.Anchor.Calls.Count;
            if (cancel)
            {
                f.Until(() => f.Menu.Native != null);
                FixedStart.CancelStart();
                f.Finish();
                f.Restored();
            }
            else
            {
                f.Finish();
                Check(f.Selected.Boat.purchased && f.Starters.All(b => !b.purchased) && f.Selected.Berth.Mode == StartBerthMode.Preserved, "preserved native berth was not granted");
                Check(f.Menu.NativeTarget.position.Equals(new Vector3(15, 5, 0)) && StarterCargo.Armed && StarterBoatRepair.Armed && Plugin.Instance.Warnings.Count == 0, "preserved native berth lost its landing or startup setup");
            }
            Check(f.Selected.Body.position.Equals(new Vector3(10, 0, 0)) && f.Selected.Body.velocity.Equals(new Vector3(1, 2, 3)), "preserved native berth moved the hull");
            Check(f.Selected.Ropes.ropes[0].Mooring == f.OldCleat && f.Selected.Ropes.ropes[1].Mooring == null && !StarterMooring.Armed, "preserved native berth changed its dock lines or armed slack");
            Check(f.Anchor.IsSet() && f.Anchor.Length == .4f && f.Anchor.Calls.Count == anchorCalls, "preserved native berth changed its anchor");
        }
    }

    static void OrdinaryBerthAdjustmentFailuresStillStart()
    {
        BerthClearance.Throw = true;
        FixedStart.ThrowLargeBoatBerth = true;
        var f = new Fixture(beforeBegin: SetupOrdinaryPort);
        f.Finish();
        Check(f.Selected.Boat.purchased && f.Selected.Body.position.Equals(new Vector3(203, 2, 1)) && StarterMooring.Armed, "failed berth adjustments did not start at the native berth");
        Check(Plugin.Instance.Warnings.Any(w => w.Contains("Crab Beach berth clearance was skipped")) && Plugin.Instance.Warnings.Any(w => w.Contains("Large-boat berth distance was skipped")), "failed berth adjustments were not logged");
    }

    static void OrdinaryPreparationFailureIsIncomplete()
    {
        var f = new Fixture(beforeBegin: x =>
        {
            SetupOrdinaryPort(x);
            x.Selected.Recovery.boatPos = null;
        });
        f.Finish();
        f.Incomplete();
        Check(f.Selected.Body.position.Equals(new Vector3(10, 0, 0)) && Plugin.Instance.Errors.Any(e => e.Contains("could not be placed")), "ordinary port preparation failure moved the boat or was not logged");
    }

    static void ShipyardPlanAvailabilityAndShoreFallback()
    {
        var f = new Fixture(beforeBegin: SetupShipyard);
        var site = f.Selected.Destination;
        var yard = site.Yard;
        Check(ShipyardStart.Available(site), "live yard unavailable");
        yard.enabled = false;
        Check(!ShipyardStart.Available(site), "disabled yard selected");
        yard.enabled = true;
        var marker = yard.shipReleasePosition;
        yard.shipReleasePosition = null;
        Check(!ShipyardStart.TryPlan(site, out _), "missing native marker synthesized shipyard pose");
        yard.shipReleasePosition = marker;
        Plugin.Instance.ShoreFound = false;
        var counter = Resources.FindObjectsOfTypeAll<ShipyardDocuments>().Single(d => d.shipyard == yard).transform;
        Check(ShipyardStart.TryPlan(site, out var plan) && Plugin.Instance.Warnings.Any(w => w.Contains("Shipyard shore placement was not verified")) && plan.ShorePosition.Equals(counter.position), "shore probe warning vetoed shipyard start or changed documents reference");
        Resources.Objects = Array.Empty<Component>();
        Check(ShipyardStart.TryPlan(site, out plan) && plan.ShorePosition.Equals(yard.transform.position), "missing documents lost permissive yard-reference fallback");
        FixedStart.CancelStart();
        f.Finish();
    }

    static void FortStartupUsesItsShipyard()
    {
        var f = new Fixture(beforeBegin: SetupShipyard);
        var site = f.Selected.Destination;
        var yard = site.Yard;
        var marker = yard.shipReleasePosition;
        site.SiteIdentity = "site:oldchronian|fort-juniper";
        Check(ShipyardStart.Available(site), "Fort shipyard option was bypassed for ordinary Dock4");
        var counter = Resources.FindObjectsOfTypeAll<ShipyardDocuments>().Single(d => d.shipyard == yard).transform;
        // A tempting ordinary/Dock1 plan must not replace the selected yard's authored marker.
        StartBerthPlanner.FortPlan = new StartBerthPlan
        {
            Destination = site,
            Mode = StartBerthMode.OffshoreHeld,
            Position = new(1045, 1, 30),
            ShorePosition = new(1040, 3, 25)
        };
        var dockShore = new Vector3(1040, 3, 8);
        StartBerthPlanner.FortShore = dockShore;
        StartBerthPlan plan = null;
        Check(f.Selected.UseShipyardBerth && ShipyardStart.TryPlan(site, out plan, f.Selected.Body, f.Selected.Ropes) && plan.Mode == StartBerthMode.ShipyardHeld && plan.Position.Equals(marker.position) && plan.Rotation.Equals(marker.rotation), "Fort shipyard replaced its authored release pose with a dock plan");
        Check(Plugin.Instance.LastShoreSeed.Equals(dockShore), "Fort shipyard shore ignored its close dock landing");
        f.Selected.Berth = plan;
        site.Island.transform.position += new Vector3(500, 0, -200);
        Check(plan.Position.Equals(marker.position) && plan.ShorePosition.Equals(dockShore + new Vector3(502, 0, -200)), "Fort shipyard release or close dock shore lost moving-world frame");
        StartBerthPlanner.FortPlan = null;
        StartBerthPlanner.FortShore = null;
        Check(ShipyardStart.TryPlan(site, out var missingMarkers) && Plugin.Instance.LastShoreSeed.Equals(counter.position), "missing Fort dock markers vetoed start or lost native documents fallback");
        Check(site.Yard == yard && yard.enabled && yard.shipReleasePosition == marker, "Fort startup policy changed its real yard or services");
        site.SiteIdentity = "site:other-provider|fort-juniper";
        Check(ShipyardStart.Available(site) && ShipyardStart.TryPlan(site, out var other) && other.Mode == StartBerthMode.ShipyardHeld && other.Position.Equals(marker.position), "Fort exception affected a different provider's yard");
        site.SiteIdentity = null;
        Check(ShipyardStart.TryPlan(site, out var ordinary) && ordinary.Position.Equals(marker.position), "ordinary native yard release pose changed");
        FixedStart.CancelStart();
        f.Finish();
    }

    static void AestrinPrefersLoadedDrydock()
    {
        var f = new Fixture(beforeBegin: SetupShipyard);
        var site = f.Selected.Destination;
        var normal = site.Yard;
        normal.name = "shipyard Aestrin";
        site.Index = 15;
        site.IdentityName = "port M 15 Fort";
        var normalDocuments = Resources.FindObjectsOfTypeAll<ShipyardDocuments>().Single(d => d.shipyard == normal);
        var root = At(1300, 0, 30);
        root.name = "drydock(Clone)";
        root.SetParent(site.Island.transform, true);
        var dock = On<Shipyard>(At(1300, 3, 30));
        dock.name = "shipyard Aestrin long";
        dock.transform.SetParent(root, true);
        dock.shipReleasePosition = At(1320, 2, 35);
        dock.shipReleasePosition.SetParent(dock.transform, true);
        var documents = On<ShipyardDocuments>(At(1305, 4, 32));
        documents.transform.SetParent(root, true);
        documents.shipyard = dock;
        var foreignRoot = At(9000);
        On<IslandHorizon>(foreignRoot);
        var foreign = On<Shipyard>(At(9000));
        foreign.name = dock.name;
        foreign.transform.SetParent(foreignRoot, true);
        foreign.shipReleasePosition = At(9001);
        Resources.Objects = new Component[]
        {
            foreign,
            normal,
            normalDocuments,
            dock,
            documents
        };
        Check(ShipyardStart.PreferredYard(site, new[] { foreign, normal, dock }) == dock && ShipyardStart.PreferredYard(site, new[] { dock, normal, foreign }) == dock, "Aestrin drydock preference depended on discovery order");
        StartBerthPlan plan = null;
        Check(ShipyardStart.Available(site) && ShipyardStart.TryPlan(site, out plan) && plan.Position.Equals(dock.shipReleasePosition.position) && Plugin.Instance.LastShoreSeed.Equals(documents.transform.position), "Aestrin boat or shore used normal yard instead of loaded drydock");
        // The catalog can still point at the normal yard when the optional yard loads later.
        Check(site.Yard == normal && plan.Mode == StartBerthMode.ShipyardHeld, "late drydock resolution mutated catalog identity or lost the hold");
        site.Island.transform.position += new Vector3(500, 0, -200);
        Check(plan.Position.Equals(dock.shipReleasePosition.position) && plan.ShorePosition.Equals(documents.transform.position + new Vector3(2, 0, 0)), "drydock berth or shore ignored world movement");
        dock.enabled = false;
        Check(ShipyardStart.TryPlan(site, out plan) && plan.Position.Equals(normal.shipReleasePosition.position) && Plugin.Instance.LastShoreSeed.Equals(normalDocuments.transform.position), "disabled optional drydock did not use normal yard and shore");
        dock.enabled = true;
        var marker = dock.shipReleasePosition;
        dock.shipReleasePosition = null;
        Check(ShipyardStart.TryPlan(site, out plan) && plan.Position.Equals(normal.shipReleasePosition.position), "drydock without release marker prevented normal shipyard start");
        dock.shipReleasePosition = marker;
        Resources.Objects = new Component[]
        {
            normal,
            normalDocuments
        };
        site.Yard = dock;
        Check(ShipyardStart.TryPlan(site, out plan) && plan.Position.Equals(normal.shipReleasePosition.position) && Plugin.Instance.LastShoreSeed.Equals(normalDocuments.transform.position), "absent optional drydock kept stale yard instead of normal shore");
        Resources.Objects = new Component[]
        {
            normal,
            normalDocuments,
            dock,
            documents
        };
        var normalMarker = normal.shipReleasePosition;
        normal.shipReleasePosition = null;
        dock.name = "provider shipyard";
        site.Yard = normal;
        Check(ShipyardStart.PreferredYard(site, new[] { normal, dock }) == normal && !ShipyardStart.TryPlan(site, out plan), "unrelated same-island provider yard replaced missing native release marker");
        normal.shipReleasePosition = normalMarker;
        dock.name = "shipyard Aestrin long";
        site.Yard = normal;
        site.Index = 67;
        site.IdentityName = "port 67 Bottleneck";
        Check(ShipyardStart.PreferredYard(site, new[] { dock, normal }) == normal && ShipyardStart.TryPlan(site, out plan) && plan.Position.Equals(normal.shipReleasePosition.position), "Aestrin-specific preference altered generic mod yard");
        site.Index = 15;
        site.IdentityName = "port M 15 Fort";
        site.SiteIdentity = "site:provider|custom";
        Check(ShipyardStart.PreferredYard(site, new[] { dock, normal }) == normal, "native Aestrin preference overrode registered API site's selected yard");
        FixedStart.CancelStart();
        f.Finish();
    }

    static void PirateInitiallySetAnchorGrants()
    {
        var f = new Fixture(pirateOffshore: true);
        f.AnchorBody.position = new(13, -90, 500);
        f.Anchor.OnLoad(true, .4f);
        f.Finish();
        Check(f.Selected.Boat.purchased && !f.Selected.Body.isKinematic, "initially set Pirate anchor prevented native grant");
        Check(!f.Anchor.IsSet() && f.Anchor.Length == 0 && f.AnchorBody.position.Equals(new Vector3(1003, 1, 0)), "deployed anchor was not retracted at live joint endpoint");
        Check(f.Anchor.Calls.Count(c => c == "set") == 1 && f.Anchor.Calls.Contains("release"), "Pirate attempted to secure anchor after releasing original");
        Check(!StarterMooring.Armed && PirateStationKeeper.Active != null && GameState.lastOwnedBoat == f.Selected.Saveable.transform, "offshore grant lost hold/ownership or armed dock ropes");
        var held = PirateStationKeeper.Active;
        FixedStart.CancelStart();
        Check(held.Removed && f.Selected.Boat.purchased, "postgrant cleanup lost owned boat or leaked hold");
    }

    static void InitiallySetAnchorRestored(Fixture f)
    {
        Check(f.Anchor.IsSet() && f.Anchor.Length == .4f && f.AnchorBody.isKinematic && f.AnchorBody.drag == 50, "original set state/line/body properties not restored");
        Check(f.AnchorBody.position.Equals(new Vector3(13, -90, 500)) && f.AnchorBody.transform.parent == f.World && f.AnchorBody.rotation.Equals(Quaternion.identity), "original deployed anchor pose/parent not restored");
        Check(f.Selected.Ropes.ropes[0].Mooring == f.OldCleat && f.Selected.Ropes.ropes[1].Mooring == null, "original moorings lost on initially set anchor rollback");
        Check(f.Anchor.Calls.Last().Equals("set"), "rollback did not use native set restoration after pose/length");
    }

    static void PirateInitiallySetAnchorCancellationRestores()
    {
        var f = new Fixture(pirateOffshore: true);
        f.AnchorBody.position = new(13, -90, 500);
        f.Anchor.OnLoad(true, .4f);
        f.Until(() => f.Menu.Native != null);
        var held = PirateStationKeeper.Active;
        FixedStart.CancelStart();
        f.Finish();
        f.Restored();
        Check(PirateStationKeeper.Active == null && held.Removed, "cancellation leaked offshore hold");
        InitiallySetAnchorRestored(f);
    }

    static void PirateTargetFailureRetainsSelection()
    {
        var f = new Fixture(pirateOffshore: true);
        Plugin.Instance.FailTarget = true;
        f.Finish();
        f.Incomplete();
        Check(f.Selected.Body.position.x == 1000, "hard target failure replaced the selected berth");
    }

    static void OrdinaryInitiallySetAnchorGrants()
    {
        var f = new Fixture();
        f.Anchor.OnLoad(true, .4f);
        f.Finish();
        Check(f.Selected.Boat.purchased && f.Selected.Body.position.x == 1000 && !f.Anchor.IsSet(), "deployed anchor changed the selected starter instead of releasing for placement");
    }

    static void PirateHeldCancellationStillRestores()
    {
        var f = new Fixture(pirateOffshore: true);
        f.Until(() => f.Menu.Native != null);
        var held = PirateStationKeeper.Active;
        FixedStart.CancelStart();
        f.Finish();
        f.Restored();
        Check(held.Removed && PirateStationKeeper.Active == null, "cancelled offshore hold survived");
    }

    static void PirateDestroyedAnchorWarnsAndContinues()
    {
        var f = new Fixture(pirateOffshore: true);
        f.Until(() => f.Menu.Native != null);
        f.Anchor.Destroyed = true;
        f.AnchorBody.Destroyed = true;
        f.Finish();
        f.GrantedAtBerth("native anchor disappeared");
        Check(PirateStationKeeper.Active != null, "lost anchor removed the offshore hold");
    }

    static void StartGuardThroughGrant()
    {
        var f = new Fixture();
        Check(FixedStart.StartInFlight && FixedStart.TryGetStartBlock(out _), "snapshot-to-first-tick gap allowed another start");
        f.Until(() => f.Menu.Native != null);
        Check(!f.Selected.Boat.purchased && FixedStart.TryGetStartBlock(out _), "pregrant start was not blocked");
        f.Until(() => f.Selected.Boat.purchased);
        Check(FixedStart.StartInFlight && FixedStart.TryGetStartBlock(out _), "native ownership grant released the guard before iterator completion");
        f.Finish();
        Check(!FixedStart.StartInFlight && !FixedStart.TryGetStartBlock(out _), "completed start retained the guard");
    }

    static void NoRestartLatchAfterFailure()
    {
        foreach (var scrambled in new[] { false, true })
        {
            ScrambledBoundary.Reset();
            var f = new Fixture(scrambled: scrambled);
            Plugin.Instance.FailTarget = true;
            f.Finish();
            Check(!FixedStart.TryGetStartBlock(out var reason), "failed start left a start block: " + reason);
            FixedStart.CancelStart();
            Check(!FixedStart.TryGetStartBlock(out _), "cancellation after a failed start left a start block");
            Check(FixedStart.BeginFixture(f.Menu, f.Selected, scrambled) != null, "a later start was refused after a failed start");
            FixedStart.CancelStart();
        }
    }

    static void MenuHideOwnership()
    {
        FixedStart.CancelStart();
        var ordinary = On<StartMenu>(At(0));
        Check(FixedStart.HidePrefixFixture(ordinary), "ordinary menu hide intercepted");
        var f = new Fixture();
        Check(!f.Menu.chooseIslandUI.activeSelf && !f.Menu.NativeHidePending && f.Menu.animsPlaying == 1, "start menu hide scheduled a competing hide fade");
        f.Tick();
        FixedStart.CancelStart();
        f.Finish();
        f.Restored();
        f.Menu.FinishNativeHide();
        Check(f.Menu.chooseIslandUI.activeSelf && f.Menu.animsPlaying == 0 && f.Menu.chooseIslandUI.transform.localPosition.Equals(new Vector3(3, 4, 5)), "late native hide overwrote restored menu");
        FixedStart.PendingSnapshotFixture(f.Menu);
        Check(FixedStart.HidePrefixFixture(ordinary), "foreign menu hide intercepted");
        PlayerGold.currency[2] = 999;
        f.Menu.animsPlaying = 1;
        Check(!FixedStart.HidePrefixFixture(f.Menu), "owned pending snapshot did not hide menu");
        FixedStart.CancelStart();
        f.Restored();
    }

    static void CancelBeforeOwnership()
    {
        var f = new Fixture();
        f.Until(() => f.Menu.Native != null);
        Check(!f.Selected.Boat.purchased, "factory construction granted boat");
        Check(f.AnchorBody.position.Equals(new Vector3(1003, 1, 0)), "moored anchor did not follow hull displacement");
        FixedStart.CancelStart();
        f.Restored();
        var foreign = At(9000);
        GameState.lastOwnedBoat = foreign;
        PlayerGold.currency[0] = 1234;
        f.Finish();
        Check(f.Menu.Native.Moves == 1 && f.Menu.Native.Disposed, "cancelled native iterator advanced/granted or was not disposed");
        Check(GameState.lastOwnedBoat == foreign && PlayerGold.currency[0] == 1234, "stale finally overwrote newer state");
    }

    static void PreGrantNativeExceptionKeepsStart()
    {
        var f = new Fixture();
        f.Until(() => f.Menu.Native != null);
        Check(!f.Menu.logo.activeSelf, "native boundary did not hide logo before grant");
        f.Menu.ThrowBeforeGrant = true;
        bool threw = false;
        try
        {
            f.Tick();
        }
        catch (InvalidOperationException ex)
        {
            threw = ex.Message == "Injected pre-purchase exception";
        }

        Check(threw && f.Menu.Native.Moves == 2 && f.Menu.Native.Disposed, "pre-purchase exception did not execute/dispose native boundary");
        f.StayedOutOfMenu();
        Check(f.Menu.Controller.GetComponent<CharacterController>().enabled && !f.Menu.logo.activeSelf, "native exception restored menu controls");
        Check(f.Selected.Body.position.x == 1000 && Plugin.Instance.Warnings.Any(w => w.Contains("stopped before it finished")), "native exception rolled back the boat, kept a hold or was not logged");
    }

    static void PurchaseUiLostWarnsAndContinues()
    {
        var f = new Fixture();
        f.Until(() => f.Menu.Native != null);
        f.Selected.Boat.purchaseUI.Destroyed = true;
        f.Finish();
        Check(f.Menu.Native.Moves == 3 && f.Menu.Native.Disposed, "missing purchase UI stopped the native sequence");
        f.GrantedAtBerth("purchase prompt disappeared");
        Check(f.Selected.Saveable.extraSetting, "missing UI grant did not write the native saved ownership flag");
    }

    static void OwnershipScope()
    {
        var f = new Fixture();
        f.Until(() => f.Menu.Native != null);
        var other = On<PurchasableBoat>(At(2000));
        On<SaveableObject>(other.transform);
        other.purchaseUI = null;
        other.LoadAsPurchased();
        Check(!other.purchased, "unrelated boat received missing-UI grant");
        f.Selected.Boat.purchaseUI = null;
        f.Finish();
        Check(f.Selected.Boat.purchased, "unrelated load call consumed selected grant");
        f = new Fixture();
        f.Until(() => f.Menu.Native != null);
        f.Selected.Boat.purchaseUI = null;
        FixedStart.CancelStart();
        f.Selected.Boat.LoadAsPurchased();
        Check(!f.Selected.Boat.purchased, "cancelled start retained grant authority");
        f.Finish();
        f = new Fixture();
        f.Until(() => f.Menu.Native != null);
        f.Selected.Boat.purchaseUI = null;
        GameState.currentlyLoading = true;
        f.Selected.Boat.LoadAsPurchased();
        Check(!f.Selected.Boat.purchased, "saved-game load received a new-game grant");
        GameState.currentlyLoading = false;
        FixedStart.CancelStart();
        f.Finish();
        f = new Fixture();
        f.Until(() => f.Menu.Native != null);
        f.Selected.Boat.purchaseUI = null;
        f.Menu.startingBoats = f.Starters;
        f.Selected.Boat.LoadAsPurchased();
        Check(!f.Selected.Boat.purchased, "superseded selection retained grant authority");
        FixedStart.CancelStart();
        f.Finish();
    }

    static void RecoverableSetupFailuresKeepSelection()
    {
        foreach (var which in new[] { 0, 1, 2, 3, 4, 5 })
        {
            var f = new Fixture(pirateOffshore: which == 5, beforeBegin: x =>
            {
                if (which < 2)
                    x.Selected.Ropes.ropes[which].ThrowMoor = true;
                else if (which == 2)
                    x.Selected.Ropes.ThrowUnmoor = true;
                else if (which == 3)
                    StarterCargo.ThrowArm = true;
                else if (which == 4)
                    x.Selected.Ropes.ropes[0].mooredSpring = null;
                else
                    x.Selected.Ropes.Controller.ThrowReset = true;
            });
            f.Finish();
            Check(f.Selected.Boat.purchased && f.Selected.Body.position.x == 1000 && f.Menu.NativeTarget.position.Equals(f.Selected.Berth.ShorePosition), "optional setup failure changed boat or landing");
            Check(f.Starters.All(b => !b.purchased) && StartingValues.Armed && f.Selected.Options != null, "optional setup failure lost selected settings or granted regional starter");
            Check(Plugin.Instance.Warnings.Count > 0, "optional setup failure was not reported");
        }
    }

    static void LoadPrefixCancelsBeforeMutation()
    {
        var f = new Fixture();
        f.Until(() => f.Menu.Native != null);
        FixedStart.LoadPrefixFixture();
        f.Restored();
        GameState.currentlyLoading = true;
        var saved = At(12345);
        GameState.lastBoat = saved;
        GameState.lastOwnedBoat = saved;
        PlayerGold.currency = new[] { 700, 800, 900, 1000 };
        f.Menu.Observer.position = new(888, 4, 9);
        f.Finish();
        Check(GameState.lastOwnedBoat == saved && GameState.lastBoat == saved && PlayerGold.currency[0] == 700 && f.Menu.Observer.position.x == 888, "cancelled callback mutated incoming save");
        Check(!f.Selected.Boat.purchased && f.Menu.Native.Moves == 1 && f.Menu.Native.Disposed, "saved-game transition allowed stale grant");
        GameState.currentlyLoading = false;
    }

    static void ForeignLoadDoesNotReceiveSnapshot()
    {
        var f = new Fixture();
        f.Until(() => f.Menu.Native != null);
        GameState.currentlyLoading = true;
        var saved = At(333);
        GameState.lastBoat = saved;
        GameState.lastOwnedBoat = saved;
        PlayerGold.currency[0] = 777;
        f.Menu.Observer.position = new(555, 0, 0);
        f.Finish();
        Check(GameState.lastOwnedBoat == saved && GameState.lastBoat == saved && PlayerGold.currency[0] == 777 && f.Menu.Observer.position.x == 555, "foreign load received old snapshot");
        Check(!f.Selected.Boat.purchased && f.Menu.Native.Disposed, "foreign load lifecycle did not release ownership");
        GameState.currentlyLoading = false;
    }

    static void ResolveFailureIsIncomplete()
    {
        FixedStart.ResolveFails = true;
        var f = new Fixture();
        f.Finish();
        f.Incomplete();
        Check(Plugin.Instance.Errors.Any(e => e.Contains("Injected resolve failure")), "planning failure reason was not logged");
    }

    static void NoFactoryKeepsPlacement()
    {
        var f = new Fixture();
        f.Menu.NullFactory = true;
        f.Finish();
        f.StayedOutOfMenu();
        Check(Plugin.Instance.Errors.Any(e => e.Contains("native start sequence is unavailable")) && f.Selected.Body.position.x == 1000, "missing native factory was not logged or unwound the placement");
    }

    static void SuccessfulNativeGrantOwnsBoat()
    {
        var f = new Fixture();
        var registry = Recovery.ports;
        f.Finish();
        Check(f.Selected.Boat.purchased && f.Menu.Native.Disposed, "native grant did not complete");
        Check(GameState.lastOwnedBoat == f.Selected.Saveable.transform && GameState.lastVisitedPort == null, "Pirate identity/ownership wrong");
        Check(!f.Selected.Body.isKinematic, "native handoff left a frozen hull");
        Check(ReferenceEquals(Recovery.ports, registry) && Recovery.ports.Count == 1, "selected start changed native recovery registry");
        Check(Plugin.Instance.Warnings.Count == 0 && Plugin.Instance.Errors.Count == 0, "clean start logged a problem");
        var position = f.Selected.Body.position;
        FixedStart.CancelStart();
        Check(f.Selected.Boat.purchased && f.Selected.Body.position.Equals(position) && f.Menu.Reopened == 0, "post-grant cancel rolled back owned boat");
    }

    static void RollbackErrorStillRestoresPlayer()
    {
        var f = new Fixture();
        f.Until(() => f.Menu.Native != null);
        f.Selected.Ropes.ThrowUnmoor = true;
        FixedStart.CancelStart();
        f.Restored(false);
        f.Finish();
        Check(Plugin.Instance.Errors.Count > 0 && f.Menu.Native.Disposed, "rollback error did not surface/cleanup");
    }

    static void SceneryWaitGrantsNothing()
    {
        var f = new Fixture();
        f.Selected.Destination.Scenery = false;
        for (int n = 0; n < 10; n++)
            f.Tick();
        Check(f.Menu.Native == null && !f.Selected.Boat.purchased && f.Selected.Body.position.x == 10, "scenery wait mutated/granted boat");
        FixedStart.CancelStart();
        f.Restored();
        f.Finish();
    }

    static void SceneryDeadlineStillPrepares()
    {
        var f = new Fixture();
        f.Selected.Destination.Scenery = false;
        f.Finish(400);
        Check(Time.realtimeSinceStartup >= 30, "fixture did not pass the scenery deadline");
        f.GrantedAtBerth("did not report ready");
    }

    static void ScrambledWaitsThenContinuesOnce()
    {
        ScrambledBoundary.Reset();
        var f = new Fixture(scrambled: true);
        Check(FixedStart.PendingConsumedFixture() && ScrambledBoundary.FactoryCalls == 0, "Scrambled start dispatch did not transfer pending exactly once");
        f.Selected.Destination.Island.transform.position = new(4000, 0, 0);
        f.Selected.Destination.Scenery = false;
        for (int n = 0; n < 10; n++)
            f.Tick();
        Check(f.Menu.Observer.position.x == 4000 && ScrambledBoundary.FactoryCalls == 0 && !f.Selected.Boat.purchased, "Scrambled continuation ran before scenery preparation at moved island");
        f.Selected.Destination.Scenery = true;
        f.Finish();
        Check(ScrambledBoundary.PrefixCalls == 2 && ScrambledBoundary.FactoryCalls == 1 && f.Selected.Boat.purchased, "Scrambled continuation recursed or skipped native purchase");
        Check(ScrambledBoundary.Target.position.Equals(f.Selected.Berth.ShorePosition) && ScrambledBoundary.Observer == f.Menu.Observer && ScrambledBoundary.Controller == f.Menu.Controller, "Scrambled continuation captured stale target or different actors");
        Check(!FixedStart.TryGetStartBlock(out _), "successful Scrambled continuation retained inflight guard");
    }

    static void ScrambledTargetFailureIsIncomplete()
    {
        ScrambledBoundary.Reset();
        var f = new Fixture(scrambled: true);
        Plugin.Instance.FailTarget = true;
        f.Finish();
        Check(ScrambledBoundary.FactoryCalls == 0, "hard target failure ran the regional Scrambled start");
        f.Incomplete();
    }

    static void ScrambledCancellationNeverContinues()
    {
        ScrambledBoundary.Reset();
        var f = new Fixture(scrambled: true);
        f.Selected.Destination.Scenery = false;
        f.Tick();
        FixedStart.CancelStart();
        f.Finish();
        Check(ScrambledBoundary.FactoryCalls == 0, "cancelled preparation resumed Scrambled purchase");
        Check(!FixedStart.StartInFlight && !FixedStart.TryGetStartBlock(out _), "cancelled Scrambled start left a start block");
        f.Restored();
    }
}
