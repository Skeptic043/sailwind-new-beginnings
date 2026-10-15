using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

// Production chooser, plan construction and survey run against reused managed
// math doubles. Physical proof stages return separately controlled outcomes.
static class PirateCandidateChecks
{
    enum BoatSize
    {
        Small,
        Medium,
        Large
    }

    class IslandFrame
    {
        internal Transform transform;
        internal T[] GetComponentsInChildren<T>(bool inactive) => transform.GetComponentsInChildren<T>(inactive);
    }

    class Shipyard
    {
        internal Transform shipReleasePosition;
    }

    class PortChoice
    {
        internal RecoveryPort Recovery;
        internal int Index;
        internal string HiddenKey = "pirate", DisplayName = "Pirate Island";
        internal Transform DockRoot = new(), Marker, ShoreMarker;
        internal IslandFrame Island;
        internal PortChoice()
        {
            Island = new IslandFrame
            {
                transform = DockRoot
            };
        }
    }

    class RecoveryPort
    {
        internal Transform transform, boatPos, mooringFront, mooringBack;
    }

    class RoutePort
    {
        internal int portIndex;
        internal string name;
    }

    class RouteIsland
    {
        internal int islandIndex;
        internal string name;
    }

    class Hull
    {
        internal PickupableBoatMooringRope[] Ropes;
        internal Vector3[] RopePositions;
        internal float Radius = 2, HalfSegment;
        internal Vector3 Center = Vector3.zero;
    }

    class GPButtonDockMooring
    {
        static int next;
        readonly int id = ++next;
        internal int GetInstanceID() => id;
        internal Transform transform = new();
        internal object spring = new();
        internal string name;
        internal bool enabled = true, Occupied;
    }

    static class StartDestinations
    {
        internal static bool Loaded(GPButtonDockMooring c) => c != null;
        internal static bool Loaded(RecoveryPort c) => c != null;
    }

    static bool ValidCleat(PortChoice p, GPButtonDockMooring c, Rigidbody body, BoatMooringRopes ropes) => c != null && c.enabled && !c.Occupied && c.spring != null;
    class PickupableBoatMooringRope
    {
        public static float maxLength = 100f;
    }

    static readonly System.Reflection.FieldInfo RopeMaximum = typeof(PickupableBoatMooringRope).GetField("maxLength");
    class ShoreProbe
    {
        internal Transform Front, Back;
        internal Vector3 Shore = new(2, 4, 1), Seed, Berth;
        internal bool Fails;
        internal readonly List<string> Warnings = new();
        internal void Warn(string message) => Warnings.Add(message);
        internal bool TryFindStartShore(PortChoice site, Vector3 seed, Vector3 berth, out Vector3 shore, out Collider support, out string reason, Transform front = null, Transform back = null)
        {
            Front = front;
            Back = back;
            Seed = seed;
            Berth = berth;
            shore = Fails ? Vector3.zero : Shore;
            support = null;
            reason = Fails ? "injected dry shore failure" : null;
            return !Fails;
        }
    }

    static class Plugin
    {
        internal static ShoreProbe Instance = new();
    }

    class StartBerthPlan
    {
        internal PortChoice Destination;
        internal Transform Frame;
        internal Vector3 LocalPosition, LocalShorePosition;
        internal Quaternion LocalRotation;
        internal StartBerthMode Mode;

        internal string OffshoreDiagnostics;
        internal GPButtonDockMooring Front, Back;
        internal PickupableBoatMooringRope FrontRope, BackRope;
        internal Quaternion Rotation => Frame.rotation * LocalRotation;
        internal Vector3 Position => Frame.TransformPoint(LocalPosition);
        internal Vector3 ShorePosition => Frame.TransformPoint(LocalShorePosition);
    }

    static int checks, clearCalls, departureCalls;
    static Func<int, bool> clear, departure;
    static readonly List<Vector3> positions = new();
    static bool Clear(Rigidbody body, Hull hull, Vector3 p, Quaternion q, Survey survey, out string reason)
    {
        clearCalls++;
        positions.Add(p);
        reason = "hull boundary";
        return survey.Query() && clear(clearCalls);
    }

    static bool DepartureClear(Rigidbody body, Hull hull, Vector3 p, Quaternion q, Survey survey, out string reason)
    {
        departureCalls++;
        reason = "departure boundary";
        return survey.Query() && departure(clearCalls);
    }

    static bool dockSupported = true, deckAvailable = true;
    static float deckHeight = 1.5f;
    static Func<int, bool> ropes = _ => true, water = _ => true;
    static int ropeCalls, waterCalls;
    static bool DockContinuous(GPButtonDockMooring a, GPButtonDockMooring b, Survey survey, out string reason)
    {
        reason = "injected missing dock support";
        return survey.Query() && dockSupported;
    }

    // Boundaries for the physical rope, deck and water queries.
    static readonly PickupableBoatMooringRope FrontLine = new(), BackLine = new();
    static bool RopePair(Hull hull, Vector3 p, Quaternion q, GPButtonDockMooring front, GPButtonDockMooring back, out PickupableBoatMooringRope frontRope, out PickupableBoatMooringRope backRope)
    {
        if (hull.Ropes != null)
        {
            ropeCalls++;
            return NativeRopePair(hull, p, q, front, back, out frontRope, out backRope);
        }

        var fits = ropes(++ropeCalls);
        frontRope = fits ? FrontLine : null;
        backRope = fits ? BackLine : null;
        return fits;
    }

    static bool DeckSupport(Vector3 point, Survey survey, out Vector3 deck)
    {
        deck = new(point.x, deckHeight, point.z);
        return deckAvailable && survey.Query();
    }

    static bool OpenWater(Vector3 point, float height, Hull hull, Survey survey) => water(++waterCalls);
    static Hull measuredHull;
    static float measuredHeight;
    static bool TryHull(Rigidbody body, BoatMooringRopes ropes, out Hull hull, out string reason)
    {
        hull = measuredHull;
        reason = null;
        return hull != null;
    }

    static bool TryHeight(Rigidbody body, out float height, out string reason)
    {
        height = measuredHeight;
        reason = null;
        return true;
    }

    // CANDIDATE_PRODUCTION
    static void Reset()
    {
        clearCalls = departureCalls = ropeCalls = waterCalls = 0;
        positions.Clear();
        clear = departure = ropes = water = _ => true;
        dockSupported = deckAvailable = true;
        Plugin.Instance = new();
    }

    static void Check(bool pass, string label)
    {
        checks++;
        if (!pass)
            throw new Exception(label);
    }

    static StartBerthPlan Choose(PortChoice site, StartBerthPlan landing, Survey survey = null)
    {
        Check(TryFixedOffshoreHold(site, new Rigidbody(), new Hull(), 2, PirateOffshoreCenter, Vector3.right, survey ?? new Survey(true), landing, out var p, out var reason) && reason == null, "fixed offshore chooser rejected: " + reason);
        return p;
    }

    static bool Near(Vector3 a, Vector3 b) => (a - b).magnitude < .001f;
    // The production dock survey runs with controlled stage outcomes. A survey
    // that accepts no candidate still returns the closest usable plan.
    static bool RunSurvey(PortChoice site, GPButtonDockMooring[] cleats, out StartBerthPlan plan, out string reason, Hull hull = null) => TrySurveyDock(site, new Rigidbody(), new BoatMooringRopes(), hull ?? new Hull { Radius = 2, HalfSegment = 8 }, 2, cleats, new Survey(), out plan, out reason);
    static void BestEffortChecks()
    {
        int before = checks;
        var site = new PortChoice
        {
            HiddenKey = "provider:test",
            DisplayName = "Test harbour"
        };
        site.Island.transform = new Transform
        {
            position = new Vector3(10, 0, -50)
        };
        var a = new GPButtonDockMooring();
        a.transform.position = new(0, 1, 0);
        var b = new GPButtonDockMooring();
        b.transform.position = new(20, 1, 0);
        var pair = new[] { a, b };
        var deck = new Vector3(10, 1.5f, 0);
        // Hull clear from the second pose, departure clear from the third, no dry shore anywhere.
        Reset();
        clear = n => n >= 2;
        departure = n => n >= 3;
        Plugin.Instance.Fails = true;
        Check(RunSurvey(site, pair, out var plan, out var reason) && reason == null, "survey without an accepted pose failed instead of returning its best effort: " + reason);
        Check(plan.Mode == StartBerthMode.Moored && Near(plan.Position, positions[2]) && plan.Front == a && plan.Back == b && plan.FrontRope == FrontLine && plan.BackRope == BackLine, "best effort did not keep the first pose that passed the most stages");
        Check(plan.OffshoreDiagnostics.Contains("passed rope reach, hull clearance and departure clearance. The player lands on the dock between its cleats.") && Near(plan.ShorePosition, deck), "dry-shore-only failure did not land the player on the dock deck between its cleats");
        Check(plan.OffshoreDiagnostics.StartsWith("Best-effort moored") && plan.OffshoreDiagnostics.Contains("departure clearance") && plan.OffshoreDiagnostics.Contains("Survey: cleats=2") && plan.OffshoreDiagnostics.Contains("DryShore=58"), "best-effort warning lost its survey report: " + plan.OffshoreDiagnostics);
        // Hull clearance passes from the second pose, departure never: stage two wins over stage one.
        Reset();
        clear = n => n >= 2;
        departure = _ => false;
        Check(RunSurvey(site, pair, out plan, out reason) && plan.Mode == StartBerthMode.Moored && Near(plan.Position, positions[1]) && plan.OffshoreDiagnostics.Contains("passed rope reach and hull clearance"), "hull-clear pose did not outrank rope-only poses");
        Check(plan.ShorePosition == Plugin.Instance.Shore && plan.OffshoreDiagnostics.Contains("passed rope reach and hull clearance.") && !plan.OffshoreDiagnostics.Contains("lands on the dock"), "best effort skipped an available dry landing");
        Reset();
        clear = n => n >= 2;
        departure = _ => false;
        Plugin.Instance.Fails = true;
        Check(RunSurvey(site, pair, out plan, out reason) && plan.OffshoreDiagnostics.Contains("The player lands on the dock between its cleats.") && Near(plan.ShorePosition, deck), "unverified best-effort landing did not fall back to the dock deck");
        // Ropes never reach: hold the boat offshore beside the closest pair, preferring the seaward side.
        Reset();
        ropes = _ => false;
        Check(RunSurvey(site, pair, out plan, out reason) && plan.Mode == StartBerthMode.OffshoreHeld && plan.Front == a && plan.Back == b, "unreachable ropes did not produce an offshore hold beside the closest pair");
        Check(Near(plan.Position, new Vector3(10, 2, 4)) && Near(plan.ShorePosition, deck) && plan.OffshoreDiagnostics.Contains("the player lands on that dock") && clearCalls == 0, "offshore hold was not beside the dock on its seaward side");
        Check(plan.OffshoreDiagnostics.StartsWith("Best-effort offshore") && plan.OffshoreDiagnostics.Contains("RopeReach=60"), "offshore warning lost its survey report: " + plan.OffshoreDiagnostics);
        Reset();
        ropes = _ => false;
        water = n => n != 1;
        Check(RunSurvey(site, pair, out plan, out reason) && Near(plan.Position, new Vector3(10, 2, -4)), "offshore hold ignored measured water on the other side");
        // Separate posts: one real rope attempt at the existing fallback pose.
        var nativeHull = new Hull
        {
            Radius = 2,
            HalfSegment = 8,
            Ropes = new[] { FrontLine, BackLine },
            RopePositions = new[] { new Vector3(0, 0, 8), new Vector3(0, 0, -8) }
        };
        Reset();
        dockSupported = false;
        Check(RunSurvey(site, pair, out plan, out reason, nativeHull) && plan.Mode == StartBerthMode.Moored && plan.Front == a && ropeCalls == 1 && plan.OffshoreDiagnostics.Contains("DockSupport=1") && plan.OffshoreDiagnostics.StartsWith("Best-effort moored start: continuous dock support was not verified"), "unsupported quay skipped bounded native rope attempt or claimed physical verification");
        var fallbackPose = plan.Position;
        Reset();
        dockSupported = false;
        PickupableBoatMooringRope.maxLength = 1;
        Check(RunSurvey(site, pair, out plan, out reason, nativeHull) && plan.Mode == StartBerthMode.OffshoreHeld && ropeCalls == 1 && Near(plan.Position, fallbackPose) && plan.OffshoreDiagnostics.Contains("closest-pair rope test also failed"), "failed closest-pair ropes moved pose, cancelled or misreported untested reach");
        PickupableBoatMooringRope.maxLength = 100;
        // A single cleat, a marker without cleats, and no usable reference at all.
        Reset();
        deckAvailable = false;
        Check(RunSurvey(site, new[] { a }, out plan, out reason) && plan.Mode == StartBerthMode.OffshoreHeld && plan.Back == null && Near(plan.ShorePosition, a.transform.position), "single cleat produced no hold landing at the cleat");
        var marked = new PortChoice
        {
            HiddenKey = "provider:marked",
            DisplayName = "Marked"
        };
        marked.Marker = new Transform
        {
            position = new Vector3(50, 0, 50),
            rotation = Quaternion.LookRotation(new Vector3(1, 0, 0), Vector3.up)
        };
        Reset();
        Check(RunSurvey(marked, new GPButtonDockMooring[0], out plan, out reason) && plan.Mode == StartBerthMode.OffshoreHeld && Near(plan.Position, new Vector3(50, 2, 50)) && plan.ShorePosition == Plugin.Instance.Shore, "marker-only location produced no offshore hold at its marker");
        Reset();
        Check(!RunSurvey(site, new GPButtonDockMooring[0], out plan, out reason) && plan == null && reason.Contains("No dock cleat or starting marker"), "location with no cleat and no marker was not reported as unusable");
        Console.WriteLine("PASS " + (checks - before) + " production dock survey best-effort checks (stage preference, dock landing, offshore hold, unusable input) with controlled physical stages. No live geometry claim.");
    }

    static void FortOuterChecks()
    {
        var before = checks;
        foreach (var turn in new[] { Quaternion.identity, new Quaternion(0, .70710677f, 0, .70710677f) })
            foreach (var hull in new[]
            {
                new Hull
                {
                    Radius = 2.5f,
                    HalfSegment = 5.535f,
                    Center = new(0, 2.1f, -.8f)
                },
                new Hull
                {
                    Radius = 3,
                    HalfSegment = 12,
                    Center = new(0, .21f, -.9f)
                },
                new Hull
                {
                    Radius = 4.40086f,
                    HalfSegment = 9.97875f,
                    Center = new(0, 2.4f, -.67037f)
                }
            })
            {
                Reset();
                var site = new PortChoice
                {
                    HiddenKey = null,
                    DisplayName = "Fort Juniper"
                };
                site.DockRoot.position = new(1000, 0, -500);
                site.DockRoot.rotation = turn;
                var dock4 = new Transform
                {
                    name = "FORT_dock4"
                };
                var west = new GPButtonDockMooring
                {
                    name = "FORT_dock_mooring_4S5"
                };
                var east = new GPButtonDockMooring
                {
                    name = "FORT_dock_mooring_4S1"
                };
                west.transform.parent = east.transform.parent = dock4;
                west.transform.position = site.DockRoot.TransformPoint(new(440.82f, 2.66f, 51.02f));
                east.transform.position = site.DockRoot.TransformPoint(new(448.47f, 2.62f, 42.38f));
                // Dock5 duplicates Dock4 leaf names. Only the selected actual pier may win.
                var inner = new GPButtonDockMooring
                {
                    name = "FORT_dock_mooring_4S1"
                };
                inner.transform.parent = new Transform
                {
                    name = "FORT_dock5"
                };
                site.DockRoot.Components = new object[]
                {
                    inner,
                    east,
                    west
                };
                var along = new Vector3(-7.65f, 0, 8.64f).normalized;
                var outward = new Vector3(-8.64f, 0, -7.65f).normalized;
                var mid = new Vector3(444.645f, 2.64f, 46.7f);
                var center = site.DockRoot.TransformPoint(mid + outward * (hull.Radius + 2));
                var heading = turn * Quaternion.LookRotation(along, Vector3.up);
                var expected = center - heading * hull.Center;
                expected.y = -.2f;
                Check(TryFortOuter(site, new Rigidbody(), new BoatMooringRopes(), hull, -.2f, out var moored, out _), "Fort fixed outer plan rejected");
                Check(moored.Mode == StartBerthMode.Moored && moored.Front == west && moored.Back == east && moored.FrontRope == FrontLine && moored.BackRope == BackLine, "Fort used inner pair or lost native ropes");
                Check(Near(moored.Position, expected) && Near(moored.Rotation * Vector3.forward, turn * along), "Fort measured hull origin or departure heading changed");
                Check(Near(Plugin.Instance.Seed, site.DockRoot.TransformPoint(mid - outward * 2)), "Fort landing seed left Dock4 centerline");
                ropes = _ => false;
                Check(TryFortOuter(site, new Rigidbody(), new BoatMooringRopes(), hull, -.2f, out var held, out _) && held.Mode == StartBerthMode.OffshoreHeld && Near(held.Position, moored.Position), "rope failure relocated Fort starter or vetoed start");
                Check(clearCalls == 0 && departureCalls == 0 && waterCalls == 0, "Fort outer placement performed alternate clearance search");
                west.Occupied = true;
                ropes = _ => true;
                Check(TryFortOuter(site, new Rigidbody(), new BoatMooringRopes(), hull, -.2f, out held, out _) && held.Mode == StartBerthMode.OffshoreHeld && Near(held.Position, expected), "occupied outer cleat relocated or vetoed Fort starter");
                west.Occupied = false;
                west.enabled = false;
                Check(TryFortOuter(site, new Rigidbody(), new BoatMooringRopes(), hull, -.2f, out held, out _) && held.Mode == StartBerthMode.OffshoreHeld && Near(held.Position, expected) && held.Front == west, "disabled Dock4 cleat abandoned the selected pose instead of holding it");
                Plugin.Instance.Fails = true;
                deckAvailable = false;
                Check(TryFortOuter(site, new Rigidbody(), new BoatMooringRopes(), hull, -.2f, out held, out _) && Near(held.ShorePosition, Plugin.Instance.Seed), "unverified Fort shore blocked finite dock reference");
                var oldPosition = held.Position;
                site.DockRoot.position += new Vector3(100, 0, -200);
                Check(Near(held.Position, oldPosition + new Vector3(100, 0, -200)), "prepared Fort pose ignored moving world frame");
                site.DockRoot.rotation = Quaternion.identity;
                Check(Near(held.Position, site.DockRoot.TransformPoint(held.LocalPosition)), "prepared Fort pose lost rotated live frame");
                site.DockRoot.Components = new object[]
                {
                    inner
                };
                Check(!TryFortOuter(site, new Rigidbody(), new BoatMooringRopes(), hull, -.2f, out _, out _) && Plugin.Instance.Warnings.Any(w => w.Contains("ordinary dock placement")), "missing actual outer marker claimed fixed success");
                Reset();
                Check(RunSurvey(site, new[] { inner }, out var fallback, out _) && fallback != null, "missing outer markers prevented existing fallback");
            }

        Console.WriteLine("PASS " + (checks - before) + " source-extracted Fort outer-edge pose, live-frame, mooring/hold and permissive fallback checks. Native rope/physics stages remain boundaries.");
    }

    static void ShipyardSecuringChecks()
    {
        var before = checks;
        PickupableBoatMooringRope.maxLength = 900;
        foreach (var turn in new[] { Quaternion.identity, new Quaternion(0, .70710677f, 0, .70710677f) })
        {
            Reset();
            var site = new PortChoice
            {
                HiddenKey = null
            };
            site.DockRoot.position = new(1000, 0, -500);
            site.DockRoot.rotation = turn;
            var left = new GPButtonDockMooring
            {
                name = "FORT_dock_mooring_1L6"
            };
            var right = new GPButtonDockMooring
            {
                name = "FORT_dock_mooring_1R6"
            };
            var innerLeft = new GPButtonDockMooring
            {
                name = "FORT_dock_mooring_1L5"
            };
            var innerRight = new GPButtonDockMooring
            {
                name = "FORT_dock_mooring_1R5"
            };
            left.transform.position = site.DockRoot.TransformPoint(new(472.83699f, 2.889f, 77.733f));
            right.transform.position = site.DockRoot.TransformPoint(new(487.846f, 2.89f, 77.730f));
            innerLeft.transform.position = site.DockRoot.TransformPoint(new(478.83999f, 2.826f, 77.733f));
            innerRight.transform.position = site.DockRoot.TransformPoint(new(481.83699f, 2.879f, 77.730f));
            site.DockRoot.Components = new object[]
            {
                innerRight,
                innerLeft,
                right,
                left
            };
            var release = site.DockRoot.TransformPoint(new(480.4f, 5, 86));
            var heading = turn * Quaternion.LookRotation(new(-1, 0, 0), Vector3.up);
            Check(TryFortShipyardShore(site, release, out var shore) && Near(shore, site.DockRoot.TransformPoint(new(480.32734f, 2.8895f, 75.73155f))), "Fort shore left its measured north dock face after world movement");
            var backLeft = new PickupableBoatMooringRope();
            var backRight = new PickupableBoatMooringRope();
            var frontLeft = new PickupableBoatMooringRope();
            var frontRight = new PickupableBoatMooringRope();
            measuredHull = new Hull
            {
                Center = new(0, .21f, -.9f),
                Ropes = new[] { backLeft, backRight, frontLeft, frontRight },
                RopePositions = new[]
                {
                    new Vector3(-1.7512006f, 1.4586001f, -10.017702f),
                    new Vector3(1.7435000f, 1.4553000f, -10.033102f),
                    new Vector3(-1.9778000f, 2.0218000f, 9.8659015f),
                    new Vector3(1.9778006f, 1.9778001f, 9.898901f)
                }
            };
            foreach (var reverse in new[] { false, true })
            {
                if (reverse)
                    site.DockRoot.Components = site.DockRoot.Components.Reverse().ToArray();
                var plan = Make(site, release, heading, shore);
                plan.Mode = StartBerthMode.ShipyardHeld;
                PrepareShipyardMoorings(plan, new Rigidbody(), new BoatMooringRopes());
                Check(plan.Front == left && plan.Back == right && plan.FrontRope == frontLeft && plan.BackRope == backLeft, "Fort Jong chose crossed/central cleats instead of shortest bow/stern pair");
                Check(Near(plan.Position, release) && Near(plan.ShorePosition, shore) && Near(plan.Rotation * Vector3.forward, heading * Vector3.forward) && plan.Mode == StartBerthMode.ShipyardHeld, "shipyard securing changed approved boat/shore pose or claimed native connection");
            }

            foreach (var unavailable in new[] { 0, 1, 2 })
            {
                var plan = Make(site, release, heading, shore);
                plan.Mode = StartBerthMode.ShipyardHeld;
                site.DockRoot.Components = new object[]
                {
                    left,
                    right
                };
                left.Occupied = unavailable == 0;
                right.enabled = unavailable != 1;
                PickupableBoatMooringRope.maxLength = unavailable == 2 ? 1 : 900;
                PrepareShipyardMoorings(plan, new Rigidbody(), new BoatMooringRopes());
                Check(plan.FrontRope == null && plan.BackRope == null && Near(plan.Position, release) && plan.Mode == StartBerthMode.ShipyardHeld, "unavailable cleat/rope changed shipyard pose or lost hold fallback");
            }

            PickupableBoatMooringRope.maxLength = 900;
            site.DockRoot.Components = new object[]
            {
                left
            };
            Check(!TryFortShipyardShore(site, release, out _), "missing Fort marker fabricated dock landing");
        }

        PickupableBoatMooringRope.maxLength = 100;
        Console.WriteLine("PASS " + (checks - before) + " actual-source shipyard pairing with native Jong endpoints, enumeration reversal and rotated world; Fort shore and held fallback checks.");
    }

    static void NativeYardChecks()
    {
        int before = checks;
        var sites = new[]
        {
            (Index: 0, Front: "dock_spring_attachment (5)", Back: "dock_spring_attachment (4)", A: new Vector3(809.46093f, 3, -200.14648f), B: new Vector3(816.51232f, 2.876f, -209.84643f), Release: new Vector3(819.57455f, 1.98f, -197.02579f), Forward: new Vector3(.5697875f, 0, -.8217921f)),
            (Index: 9, Front: "dock_mooring E (16)", Back: "dock_mooring E (15)", A: new Vector3(-142.677f, 1.83f, -514.622f), B: new Vector3(-146.79f, 1.83f, -508.44f), Release: new Vector3(-154.97812f, .59609f, -510.60534f), Forward: new Vector3(-.5297697f, 0, .8481415f)),
            (Index: 22, Front: "dock_mooring E (10)", Back: "dock_mooring E (15)", A: new Vector3(-35.02f, 1.142f, 129.866f), B: new Vector3(4.006f, 1.14f, 115.437f), Release: new Vector3(-13.69758f, 3.57f, 127.19542f), Forward: new Vector3(-.9297986f, 0, .3680686f))
        };
        foreach (var row in sites)
            foreach (var turn in new[] { Quaternion.identity, new Quaternion(0, .70710677f, 0, .70710677f) })
            {
                Vector3? firstSeed = null;
                foreach (var hull in new[]
                {
                    new Hull
                    {
                        Radius = 1.42f,
                        HalfSegment = 4.38f,
                        Center = new(0, .3f, -.12f)
                    },
                    new Hull
                    {
                        Radius = 2.5f,
                        HalfSegment = 5.535f,
                        Center = new(0, 2.1f, -.8f)
                    },
                    new Hull
                    {
                        Radius = 3,
                        HalfSegment = 12,
                        Center = new(0, .21f, -.9f)
                    },
                    new Hull
                    {
                        Radius = 2.6f,
                        HalfSegment = 6.59f,
                        Center = new(0, .6f, -.4f)
                    },
                    new Hull
                    {
                        Radius = 4.40086f,
                        HalfSegment = 9.97875f,
                        Center = new(0, .9951f, -.67037f)
                    }
                })
                {
                    Reset();
                    measuredHull = hull;
                    measuredHeight = -.2f;
                    var site = new PortChoice
                    {
                        Index = row.Index,
                        HiddenKey = null
                    };
                    site.DockRoot.position = new(1000, 0, -500);
                    site.DockRoot.rotation = turn;
                    var front = new GPButtonDockMooring
                    {
                        name = row.Front
                    };
                    var back = new GPButtonDockMooring
                    {
                        name = row.Back
                    };
                    front.transform.position = site.DockRoot.TransformPoint(row.A);
                    back.transform.position = site.DockRoot.TransformPoint(row.B);
                    site.DockRoot.Components = new object[]
                    {
                        back,
                        front
                    };
                    if (row.Index == 22)
                    {
                        site.Recovery = new RecoveryPort
                        {
                            transform = new Transform
                            {
                                position = site.DockRoot.TransformPoint(new(-12.75f, 2.70657f, 142.45f))
                            },
                            mooringFront = new Transform(),
                            mooringBack = new Transform()
                        };
                    }

                    var yard = new Shipyard
                    {
                        shipReleasePosition = new Transform
                        {
                            position = site.DockRoot.TransformPoint(row.Release),
                            rotation = turn * Quaternion.LookRotation(row.Forward, Vector3.up)
                        }
                    };
                    Check(TryNativeShipyard(site, yard, new Rigidbody(), new BoatMooringRopes(), out var plan, out var reason) && plan.Mode == StartBerthMode.ShipyardHeld, "native yard rejected actual dock: " + reason);
                    var midpoint = (row.A + row.B) * .5f;
                    var outward = Vector3.Cross(Vector3.up, (row.A - row.B).normalized);
                    outward.y = 0;
                    outward = outward.normalized;
                    var landing = midpoint - outward * 2;
                    if (row.Index == 9)
                        landing -= (row.A - row.B).normalized * .5f;
                    if (row.Index == 22)
                        landing = new(-12.75f, 2.70657f, 142.45f);
                    var expectedSeed = site.DockRoot.TransformPoint(landing);
                    Check(Near(Plugin.Instance.Seed, expectedSeed) && Near(Plugin.Instance.Berth, expectedSeed), "yard landing left dock or depends on hull distance");
                    Check(!firstSeed.HasValue || Near(firstSeed.Value, Plugin.Instance.Seed), "boat size changed the player/supplies landing");
                    firstSeed = Plugin.Instance.Seed;
                    Check(NativeYardMeasuredBox.Contains(row.Index, site.DockRoot.InverseTransformPoint(plan.Position)), "native yard root outside measured service trigger");
                    var center = site.DockRoot.InverseTransformPoint(plan.Position + plan.Rotation * hull.Center);
                    var distance = Vector3.Dot(center - midpoint, outward);
                    if (row.Index == 22)
                    {
                        var expectedCenter = hull.Radius < 3 ? new Vector3(-13.697577f, 0, 127.195416f) : hull.Radius == 3 ? new Vector3(-13.659231f, 0, 127.299129f) : new Vector3(-13.173435f, 0, 128.613058f);
                        Check(MathF.Abs(center.x - expectedCenter.x) < .001f && MathF.Abs(center.z - expectedCenter.z) < .001f, "Kicia ordinary landing changed accepted shipyard hull position");
                        Check(plan.Front == front && plan.Back == back && Plugin.Instance.Front == site.Recovery.mooringFront && Plugin.Instance.Back == site.Recovery.mooringBack, "Kicia ordinary shore probe changed boat cleats or kept yard exclusions");
                    }

                    Check(distance - hull.Radius >= 1.999f, "wide native hull overlaps its yard pier margin");
                    if (row.Index == 0)
                        Check(MathF.Abs(distance - hull.Radius - 2) < .001f, "Gold Rock starter stayed away from the player dock instead of keeping a two-metre hull-side gap");
                    Check(Near(plan.Rotation * Vector3.forward, turn * row.Forward.normalized) && MathF.Abs(plan.Position.y - measuredHeight) < .001f, "native yard heading or buoyancy height changed");
                    Check(clearCalls == 0 && departureCalls == 0 && ropeCalls == 0, "native yard added a clearance/mooring gate");
                    if (row.Index == 0 && hull.Radius == 2.6f)
                    {
                        // Actual Santa Maria rest endpoints from the retained exploration bundle
                        // snapshot (pirate-live-followup/boat.json), compared with both native piers.
                        hull.Ropes = Enumerable.Range(0, 4).Select(_ => new PickupableBoatMooringRope()).ToArray();
                        hull.RopePositions = new[]
                        {
                            new Vector3(1.7006004f, 4.3032f, -5.3669014f),
                            new Vector3(-1.7006004f, 4.3032f, -5.3669014f),
                            new Vector3(1.8676761f, 3.3774002f, 7.1875002f),
                            new Vector3(-1.8676758f, 3.3774002f, 7.1875002f)
                        };
                        var farFront = new GPButtonDockMooring
                        {
                            name = "dock_spring_attachment (10)"
                        };
                        farFront.transform.position = site.DockRoot.TransformPoint(new(831.575416f, 2.876f, -199.271581f));
                        var farBack = new GPButtonDockMooring
                        {
                            name = "dock_spring_attachment (9)"
                        };
                        farBack.transform.position = site.DockRoot.TransformPoint(new(824.579058f, 3, -189.531156f));
                        PickupableBoatMooringRope.maxLength = 900;
                        foreach (var reverse in new[] { false, true })
                        {
                            site.DockRoot.Components = reverse ? new object[]
                            {
                                farBack,
                                front,
                                farFront,
                                back
                            }

                            : new object[]
                            {
                                back,
                                farFront,
                                front,
                                farBack
                            };
                            PrepareShipyardMoorings(plan, new Rigidbody(), new BoatMooringRopes());
                            Check(plan.Front == back && plan.Back == front && plan.FrontRope == hull.Ropes[2] && plan.BackRope == hull.Ropes[0], "Gold Rock's closer pose did not naturally choose the player-dock cleats and visible Santa Maria endpoints");
                        }

                        PickupableBoatMooringRope.maxLength = 100;
                    }

                    var old = plan.Position;
                    site.DockRoot.position += new Vector3(200, 0, 300);
                    Check(Near(plan.Position, old + new Vector3(200, 0, 300)), "native yard plan ignores moving world");
                    site.DockRoot.position -= new Vector3(200, 0, 300);
                    Plugin.Instance.Fails = true;
                    deckAvailable = false;
                    Plugin.Instance.Warnings.Clear();
                    Check(TryNativeShipyard(site, yard, new Rigidbody(), new BoatMooringRopes(), out plan, out _) && Near(plan.ShorePosition, expectedSeed) && Plugin.Instance.Warnings.Any(w => w.Contains("shipyard landing was not verified")), "shore warning vetoed chosen yard or moved to root/NPC");
                    if (row.Index == 22)
                    {
                        var recovery = site.Recovery;
                        site.Recovery = null;
                        Check(TryNativeShipyard(site, yard, new Rigidbody(), new BoatMooringRopes(), out var fallback, out _) && Near(fallback.Position, plan.Position), "missing ordinary recovery vetoed Kicia start or moved boat");
                        site.Recovery = recovery;
                    }

                    site.ShoreMarker = new Transform
                    {
                        position = new Vector3(1050, 3, -510)
                    };
                    Check(TryNativeShipyard(site, yard, new Rigidbody(), new BoatMooringRopes(), out plan, out _) && Near(plan.ShorePosition, site.ShoreMarker.position), "explicit author shore marker lost priority");
                }
            }

        Console.WriteLine("PASS " + (checks - before) + " actual-source native yard placement checks, 3 yards x 5 hulls x 2 world orientations. Physics/native height are boundaries.");
    }

    static void SirenSongChecks()
    {
        var before = checks;
        var route = new SirenRoute
        {
            Port = new RoutePort
            {
                portIndex = 18,
                name = "port M 18 Siren Song"
            },
            Island = new RouteIsland
            {
                islandIndex = 21,
                name = "island 21 M (Siren Song)"
            },
            Recovery = new object ()
        };
        Check(route.UsesCustomBerth && route.IsSirenSong, "native Siren recovery bypassed custom route");
        // Boat identity is deliberately absent from the classification: selected Cog
        // and every other starter use the same override, including native-berth candidates.
        route.SiteIdentity = "site:author|siren";
        Check(!route.IsSirenSong && route.UsesCustomBerth, "native Siren override hijacked author-registered berth");
        route.SiteIdentity = null;
        foreach (var changed in new[] { 0, 1, 2, 3 })
        {
            var oldPort = route.Port;
            var oldIsland = route.Island;
            route.Port = new RoutePort
            {
                portIndex = changed == 0 ? 17 : 18,
                name = changed == 1 ? "Other Siren Song" : "port M 18 Siren Song"
            };
            route.Island = new RouteIsland
            {
                islandIndex = changed == 2 ? 20 : 21,
                name = changed == 3 ? "Other Siren Song" : "island 21 M (Siren Song)"
            };
            Check(!route.IsSirenSong && !route.UsesCustomBerth, "index/name alias triggered Siren override");
            route.Port = oldPort;
            route.Island = oldIsland;
        }

        foreach (var turn in new[] { Quaternion.identity, new Quaternion(0, .70710677f, 0, .70710677f) })
            foreach (var hull in new[]
            {
                new Hull
                {
                    Radius = 1.42f,
                    HalfSegment = 4.38f,
                    Center = new(0, 1.35f, -.12f)
                }, // Dhow
                new Hull
                {
                    Radius = 2.4f,
                    HalfSegment = 8.86f,
                    Center = new(0, 1.27f, .5f)
                }, // Native medium junk
                new Hull
                {
                    Radius = 2.14f,
                    HalfSegment = 3.415f,
                    Center = new(0, 1.44f, -1.2f)
                }, // Cog
                new Hull
                {
                    Radius = 3,
                    HalfSegment = 12,
                    Center = new(0, .21f, -.9f)
                }, // Jong
                new Hull
                {
                    Radius = 2.6f,
                    HalfSegment = 6.59f,
                    Center = new(0, .6f, -.4f)
                }
            })
            { // Santa Maria
                Reset();
                PickupableBoatMooringRope.maxLength = 900;
                hull.Ropes = new[]
                {
                    new PickupableBoatMooringRope(),
                    new PickupableBoatMooringRope(),
                    new PickupableBoatMooringRope(),
                    new PickupableBoatMooringRope()
                };
                // Geometry reach boundary: fixtures use proportional bow/stern endpoints.
                // Actual hull dimensions above are measured; native per-boat endpoint binding
                // remains a live check, unlike the separate actual-Jong endpoint regression.
                var half = hull.HalfSegment + hull.Radius;
                hull.RopePositions = new[]
                {
                    new Vector3(-hull.Radius * .7f, 1, half * .75f),
                    new Vector3(hull.Radius * .7f, 1, half * .75f),
                    new Vector3(-hull.Radius * .7f, 1, -half * .75f),
                    new Vector3(hull.Radius * .7f, 1, -half * .75f)
                };
                var site = new PortChoice
                {
                    HiddenKey = null
                };
                site.DockRoot.position = new(1000, 0, -500);
                site.DockRoot.rotation = turn;
                var a = new GPButtonDockMooring
                {
                    name = "dock_mooring M (5)"
                };
                var b = new GPButtonDockMooring
                {
                    name = "dock_mooring M (4)"
                };
                a.transform.parent = b.transform.parent = site.DockRoot;
                // Exact retained native positions in the recovery frame (scene.json).
                var localA = new Vector3(2.927867f, .281022f, 44.8779f);
                var localB = new Vector3(-5.085697f, .281022f, 58.83474f);
                a.transform.position = site.DockRoot.TransformPoint(localA);
                b.transform.position = site.DockRoot.TransformPoint(localB);
                site.Recovery = new RecoveryPort
                {
                    boatPos = new Transform
                    {
                        position = site.DockRoot.TransformPoint(Vector3.zero)
                    }
                };
                site.DockRoot.Components = new object[]
                {
                    b,
                    a
                };
                Check(TrySirenSong(site, new Rigidbody(), new BoatMooringRopes(), hull, -.2f, out var plan, out var reason) && reason == null && plan.Mode == StartBerthMode.Moored, "Siren starter failed fixed moored plan: " + reason);
                var center = site.DockRoot.InverseTransformPoint(plan.Position + plan.Rotation * hull.Center);
                var midpoint = (localA + localB) * .5f;
                var line = (localA - localB).normalized;
                var normal = Vector3.Cross(Vector3.up, line);
                if (Vector3.Dot(Vector3.zero - localA, normal) < 0)
                    normal = normal * -1;
                Check(MathF.Abs(Vector3.Dot(center - midpoint, normal) - hull.Radius - 2) < .001f && MathF.Abs(Vector3.Dot(center - midpoint, line)) < .001f, "hull side lost two-metre end-dock gap or centering");
                Check(MathF.Abs(Vector3.Dot(plan.Rotation * Vector3.forward, turn * line)) > .999f && MathF.Abs(plan.Position.y + .2f) < .001f, "Siren heading or native-height boundary changed");
                Check((plan.Front == a && plan.Back == b || plan.Front == b && plan.Back == a) && plan.FrontRope != null && plan.BackRope != null, "Siren lost exact end-cleat/native rope pairing");
                Check(Near(Plugin.Instance.Seed, site.DockRoot.TransformPoint(midpoint - normal * 2)) && Plugin.Instance.Front == plan.Front.transform, "player/supplies probe left end-of-dock seed");
                Check(clearCalls == 0 && departureCalls == 0, "Siren added clearance/departure veto");
                var old = plan.Position;
                var oldCenter = plan.Position + plan.Rotation * hull.Center;
                var oldShore = plan.ShorePosition;
                site.DockRoot.position += new Vector3(200, 0, 300);
                Check(Near(plan.Position, old + new Vector3(200, 0, 300)) && Near(plan.ShorePosition, oldShore + new Vector3(200, 0, 300)), "Siren boat/player targets ignored live frame translation");
                site.DockRoot.position -= new Vector3(200, 0, 300);
                var localPosition = plan.LocalPosition;
                var localShore = plan.LocalShorePosition;
                site.DockRoot.rotation = site.DockRoot.rotation * new Quaternion(0, .70710677f, 0, .70710677f);
                Check(Near(plan.Position, site.DockRoot.TransformPoint(localPosition)) && Near(plan.ShorePosition, site.DockRoot.TransformPoint(localShore)), "Siren targets ignored live frame rotation");
                site.DockRoot.rotation = turn;
                foreach (var unavailable in new[] { 0, 1, 2 })
                {
                    a.Occupied = unavailable == 0;
                    b.enabled = unavailable != 1;
                    PickupableBoatMooringRope.maxLength = unavailable == 2 ? 1 : 900;
                    Plugin.Instance.Fails = true;
                    deckAvailable = false;
                    Check(TrySirenSong(site, new Rigidbody(), new BoatMooringRopes(), hull, -.2f, out var fallback, out _) && fallback.Mode == StartBerthMode.OffshoreHeld && Near(fallback.Position + fallback.Rotation * hull.Center, oldCenter) && fallback.OffshoreDiagnostics.Contains("dry landing was not verified"), "unavailable cleat/reach/shore vetoed or relocated Siren starter");
                }
            }

        PickupableBoatMooringRope.maxLength = 100;
        Console.WriteLine("PASS " + (checks - before) + " actual-source Siren end-dock classification, five measured hull sizes, two frames, native-rope reach and permissive securing/shore fallback checks. Physics and native buoyancy are boundaries.");
    }

    static int Main()
    {
        SirenSongChecks();
        ShipyardSecuringChecks();
        NativeYardChecks();
        FortOuterChecks();
        var fixedBefore = checks;
        var site = new PortChoice();
        var front = new GPButtonDockMooring();
        var back = new GPButtonDockMooring();
        var landing = Make(site, new Vector3(1, 0, 1), Quaternion.identity, new Vector3(3, 4, 2));
        landing.Front = front;
        landing.Back = back;
        var fixedPose = new Vector3(60.7f, 2, -77.8f);
        Check(new BoatSize? [] { BoatSize.Medium, BoatSize.Large, null }.All(size => PirateRequiresOffshore(site, size)) && !PirateRequiresOffshore(site, BoatSize.Small) && !PirateRequiresOffshore(new PortChoice { HiddenKey = "saffron" }, BoatSize.Large), "mandatory offshore size/site policy changed");
        Reset();
        var plan = Choose(site, landing);
        Check(plan.Mode == StartBerthMode.OffshoreHeld && plan.OffshoreDiagnostics == null && Near(plan.Position, fixedPose) && clearCalls == 1 && departureCalls == 1, "verified fixed hold moved, warned or kept surveying");
        Check(Near(plan.Frame.rotation * plan.LocalRotation * Vector3.forward, new Vector3(1, 0, 0)), "fixed hold bow does not face island east");
        Check(plan.ShorePosition == landing.ShorePosition && plan.Front == front && plan.Back == back, "offshore result lost exact landing/cleat references");
        Reset();
        departure = _ => false;
        plan = Choose(site, landing);
        Check(Near(plan.Position, fixedPose) && plan.OffshoreDiagnostics.Contains("departure boundary") && !plan.OffshoreDiagnostics.Contains("hull clearance unverified"), "blocked departure moved or cancelled the hold");
        Reset();
        clear = _ => false;
        plan = Choose(site, landing);
        Check(plan.Mode == StartBerthMode.OffshoreHeld && Near(plan.Position, fixedPose) && plan.OffshoreDiagnostics.Contains("hull clearance unverified") && plan.OffshoreDiagnostics.Contains("hull boundary") && departureCalls == 0 && plan.Position != landing.Position, "failed hull check cancelled, moved to dock or still surveyed departure");
        Reset();
        var budget = new Survey(true);
        for (int i = 0; i < 48; i++)
            budget.Candidate();
        plan = Choose(site, landing, budget);
        Check(clearCalls == 0 && Near(plan.Position, fixedPose) && plan.OffshoreDiagnostics.Contains("exhausted"), "exhausted shared budget was reset or prevented the fixed hold");
        var moved = new PortChoice();
        moved.DockRoot.position = new Vector3(1000, 0, -500);
        moved.DockRoot.rotation = Quaternion.LookRotation(new Vector3(1, 0, 0), Vector3.up);
        Reset();
        var movedLanding = Make(moved, new Vector3(1000, 0, -500), Quaternion.identity, new Vector3(1000, 4, -500));
        Check(TryFixedOffshoreHold(moved, new Rigidbody(), new Hull { Center = new Vector3(0, 0, 3) }, 2, PirateOffshoreCenter, Vector3.right, new Survey(true), movedLanding, out plan, out var movedReason) && movedReason == null, "offset hull hold rejected");
        var bow = plan.Frame.rotation * plan.LocalRotation * Vector3.forward;
        Check(Near(plan.Position + bow * 3, new Vector3(1000 - 77.8f, 2, -500 - 60.7f)) && Near(bow, new Vector3(0, 0, -1)), "offset hull centre or moved and rotated island frame was not honoured");
        var tilted = new PortChoice();
        tilted.DockRoot.rotation = Quaternion.LookRotation(new Vector3(0, 0, 1), new Vector3(1, 0, 0));
        Reset();
        Check(!TryFixedOffshoreHold(tilted, new Rigidbody(), new Hull(), 2, PirateOffshoreCenter, Vector3.right, new Survey(true), landing, out plan, out var tiltReason) && plan == null && tiltReason.Contains("not level"), "tilted island frame was accepted");
        Reset();
        Check(!TryFixedOffshoreHold(site, new Rigidbody(), new Hull(), float.NaN, PirateOffshoreCenter, Vector3.right, new Survey(true), landing, out plan, out var failure) && plan == null && failure.Contains("not finite"), "nonfinite hold was accepted");
        var saffron = new PortChoice
        {
            HiddenKey = "saffron",
            DisplayName = "Saffron Island"
        };
        var saffronLanding = Make(saffron, new Vector3(200, 2, 170), Quaternion.identity, new Vector3(201, 3, 171));
        Reset();
        Check(TryFixedOffshoreHold(saffron, new Rigidbody(), new Hull(), 2, SaffronOffshoreCenter, Vector3.forward, new Survey(true), saffronLanding, out plan, out var saffronReason) && saffronReason == null && plan.Mode == StartBerthMode.OffshoreHeld && plan.Front == null && Near(plan.Position, new Vector3(250f, 2, 169.9f)) && Near(plan.Frame.rotation * plan.LocalRotation * Vector3.forward, new Vector3(0, 0, 1)) && plan.ShorePosition == saffronLanding.ShorePosition, "Saffron hold lost its pose, bow, landing or claimed dock references");
        Reset();
        clear = _ => false;
        Check(TryFixedOffshoreHold(saffron, new Rigidbody(), new Hull(), 2, SaffronOffshoreCenter, Vector3.forward, new Survey(true), saffronLanding, out plan, out saffronReason) && plan.OffshoreDiagnostics.StartsWith("Saffron Island offshore hold") && plan.OffshoreDiagnostics.Contains("hull clearance unverified"), "Saffron hold cancelled or reported another location");
        Check(PiratePlanMessage(StartBerthMode.OffshoreHeld, 12).Contains("OffshoreHeld") && PiratePlanMessage(StartBerthMode.Moored, 12).Length < 100, "mode diagnostic ambiguous or excessive");
        front.transform.position = new(0, 1, 0);
        back.transform.position = new(4, 1, 0);
        Reset();
        Check(TryPirateOffshore(site, new Rigidbody(), new BoatMooringRopes(), new Hull(), 2, new[] { front, back }, new Survey(true), out plan, out failure) && plan.Mode == StartBerthMode.OffshoreHeld && Near(plan.Position, fixedPose) && plan.ShorePosition == Plugin.Instance.Shore && plan.Front.transform == Plugin.Instance.Front && plan.Back.transform == Plugin.Instance.Back, "offshore entry lost actual validated landing references or returned dock plan");
        Reset();
        clear = _ => false;
        Check(TryPirateOffshore(site, new Rigidbody(), new BoatMooringRopes(), new Hull(), 2, new[] { front, back }, new Survey(true), out plan, out failure) && plan.Mode == StartBerthMode.OffshoreHeld && Near(plan.Position, fixedPose), "offshore entry returned a hull-at-dock fallback when geometry failed");
        Reset();
        Plugin.Instance.Fails = true;
        Check(TryPirateOffshore(site, new Rigidbody(), new BoatMooringRopes(), new Hull(), 2, new[] { front, back }, new Survey(true), out plan, out failure) && plan.Mode == StartBerthMode.OffshoreHeld && Near(plan.Position, fixedPose) && Near(plan.ShorePosition, new Vector3(2, 1.5f, 0)) && plan.OffshoreDiagnostics.StartsWith("Best-effort landing") && plan.OffshoreDiagnostics.Contains("injected dry shore failure"), "Pirate offshore start without a verified landing failed or did not land on the dock deck");
        // A small boat surveys Pirate's dock first. Without a fully verified
        // moored pose it uses the fixed hold, unless only the dry shore failed.
        Reset();
        departure = _ => false;
        Check(RunSurvey(site, new[] { front, back }, out plan, out failure) && plan.Mode == StartBerthMode.OffshoreHeld && Near(plan.Position, fixedPose) && plan.Front == front && plan.Back == back && plan.OffshoreDiagnostics.StartsWith("No clear, reachable moored berth") && plan.OffshoreDiagnostics.Contains("Departure=") && plan.OffshoreDiagnostics.Contains("offshore hold"), "Pirate dock survey without a verified berth did not use the fixed hold or lost its survey report: " + plan?.OffshoreDiagnostics);
        Reset();
        Plugin.Instance.Fails = true;
        Check(RunSurvey(site, new[] { front, back }, out plan, out failure) && plan.Mode == StartBerthMode.Moored && plan.OffshoreDiagnostics.StartsWith("Best-effort moored") && plan.OffshoreDiagnostics.Contains("passed rope reach, hull clearance and departure clearance. The player lands on the dock between its cleats."), "Pirate berth that failed only its dry shore did not keep the best-effort moored start");
        Console.WriteLine("PASS " + (checks - fixedBefore) + " actual Pirate and Saffron fixed offshore hold pose/diagnostic/budget and Pirate dock-survey fallback checks with controlled physics stages and managed math. No live clearance/stationkeeping proof.");
        BestEffortChecks();
        return 0;
    }
}
