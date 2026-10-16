using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NewBeginnings
{
    internal static class PortOverlayChecks
    {
        private static int checks;
        private static readonly List<Component> Objects = new List<Component>();

        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception("FAIL " + label);
            ++checks;
        }

        private static T On<T>(string name, Transform parent = null) where T : Component, new()
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent);
            var component = typeof(T) == typeof(Transform)
                ? (T)(Component)gameObject.transform
                : gameObject.Add<T>();
            Objects.Add(gameObject.transform);
            if (!ReferenceEquals(component, gameObject.transform)) Objects.Add(component);
            return component;
        }

        private static (IslandHorizon Island, Port Port, PortChoice Choice) PortFixture(
            int index, string identity, string display = null, bool known = true, bool recovery = true)
        {
            Objects.Clear();
            Refs.islands = new Transform[100];
            var island = On<IslandHorizon>("island " + index);
            island.islandIndex = index;
            Refs.islands[index] = island.transform;
            var port = On<Port>(identity, island.transform);
            port.portIndex = index;
            port.region = PortRegion.Aestrin;
            port.DisplayName = display ?? identity;
            port.Known = known;
            Port.ports = new Port[Math.Max(40, index + 1)];
            Port.ports[index] = port;
            RecoveryPort berth = null;
            if (recovery)
            {
                berth = On<RecoveryPort>("recovery", port.transform);
                berth.parentPort = port;
                berth.boatPos = On<Transform>("boat position", berth.transform);
            }
            Resources.Objects = Objects.ToArray();
            return (island, port, new PortChoice { Port = port, Recovery = berth,
                Pool = PortPool.Aestrin, DisplayName = port.DisplayName });
        }

        private static T Child<T>(string name, IslandHorizon island, Transform parent = null) where T : Component, new()
        {
            var result = On<T>(name, parent ?? island.transform);
            Resources.Objects = Objects.ToArray();
            return result;
        }

        private static PortChoice Complete(PortChoice choice)
        {
            var catalog = new SelectionCatalog();
            catalog.Ports.Add(choice);
            StartDestinations.Complete(catalog);
            Check(catalog.Ports.Count(item => ReferenceEquals(item.Port, choice.Port) && item.SiteIdentity == null) == 1,
                "native port produced more than one automatic destination");
            return catalog.Ports.Single(item => ReferenceEquals(item.Port, choice.Port) && item.SiteIdentity == null);
        }

        private static void NoRegistrationBaseline()
        {
            var fixture = PortFixture(16, "port M 16 Sunspire", "Sunspire");
            var automaticYard = Child<Shipyard>("automatic yard", fixture.Island);
            var choice = Complete(fixture.Choice);
            Check(choice.Key == "port:port M 16 Sunspire" && choice.SiteIdentity == null && choice.LegacySiteKey == null,
                "automatic native identity changed without registration");
            Check(!choice.MatchesKey(null) && !choice.MatchesIdentity(null),
                "missing saved identity matched an absent legacy alias");
            Check(choice.DisplayName == "Sunspire" && ReferenceEquals(choice.DockRoot, fixture.Island.transform) &&
                ReferenceEquals(choice.Recovery, fixture.Choice.Recovery), "automatic values changed without registration");
            Check(ReferenceEquals(choice.Yard, automaticYard) && choice.HasShipyard,
                "automatic mod-port shipyard discovery changed without registration");
            Check(!choice.HasAuthoredBerth && !choice.HasAuthoredYardScope && !choice.NeedsPlannedBerth,
                "ordinary native berth entered authored planning without registration");
        }

        private static void AuthoritativeOverlayAndConflicts()
        {
            var fixture = PortFixture(12, "port E 12 New Port", "New Port");
            var dock = Child<Transform>("author dock", fixture.Island);
            var marker = Child<Transform>("author berth", fixture.Island, dock);
            var shore = Child<Transform>("author shore", fixture.Island, dock);
            var scenery = Child<Transform>("author scenery", fixture.Island);
            var front = Child<GPButtonDockMooring>("front", fixture.Island, dock);
            var back = Child<GPButtonDockMooring>("back", fixture.Island, dock);
            var yard = Child<Shipyard>("author yard", fixture.Island, dock);
            var options = new StartPortOptions
            {
                DisplayName = "  Better New Port  ", DockRoot = dock, BerthMarker = marker, ShoreMarker = shore,
                SceneryRoot = scenery, FrontMooring = front, BackMooring = back, Shipyard = yard
            };
            Check(StartPorts.Register("author", "new-port", fixture.Port, options, out var reason), reason);
            var revision = StartPorts.Revision;
            Check(StartPorts.Register("author", "new-port", fixture.Port, options, out reason) &&
                StartPorts.Revision == revision, "identical registration was not idempotent");
            options.DisplayName = "mutated after acceptance";
            Check(!StartPorts.Register("author", "new-port-2", fixture.Port, new StartPortOptions(), out reason) &&
                reason.Contains("native port"), "second declaration for one live Port was accepted or unclear");
            var choice = Complete(fixture.Choice);
            Check(choice.Key == "port:port E 12 New Port" && choice.LegacySiteKey == "site:author|new-port" &&
                choice.SiteIdentity == null, "registration replaced canonical native identity");
            Check(choice.DisplayName == "Better New Port" && ReferenceEquals(choice.DockRoot, dock) &&
                ReferenceEquals(choice.Marker, marker) && ReferenceEquals(choice.ShoreMarker, shore) &&
                ReferenceEquals(choice.SceneryRoot, scenery), "explicit registration values did not overlay automatic values");
            Check(ReferenceEquals(choice.PreferredFront, front) && ReferenceEquals(choice.PreferredBack, back) &&
                ReferenceEquals(choice.Yard, yard) && choice.HasShipyard, "author moorings or shipyard did not win");
            Check(choice.HasAuthoredBerth && choice.HasAuthoredYardScope && choice.NeedsPlannedBerth,
                "explicit author berth intent did not enter planned placement");
        }

        private static void LifecycleAndValidation()
        {
            var fixture = PortFixture(13, "port E 13 Sage Hills");
            Check(!StartPorts.Register("author", "bad-mooring", fixture.Port,
                new StartPortOptions { FrontMooring = Child<GPButtonDockMooring>("front", fixture.Island) }, out var reason) &&
                reason.Contains("two distinct"), "one-sided mooring declaration was accepted");
            Check(StartPorts.Register("author", "sage", fixture.Port, new StartPortOptions(), out reason), reason);
            fixture.Port.Destroyed = true;
            var replacement = PortFixture(13, "port E 13 Sage Hills");
            Check(StartPorts.Register("author", "sage", replacement.Port, new StartPortOptions(), out reason),
                "recreated Port could not replace stale registration: " + reason);
            var registrations = StartPorts.Snapshot();
            Check(registrations.Length == 1 && ReferenceEquals(registrations[0].Port, replacement.Port),
                "stale registration survived recreated Port lifecycle");
            var other = Child<Port>("other port", replacement.Island);
            other.portIndex = 14;
            other.region = PortRegion.Aestrin;
            Array.Resize(ref Port.ports, 40);
            Port.ports[14] = other;
            Resources.Objects = Objects.ToArray();
            Check(!StartPorts.Register("author", "sage", other, new StartPortOptions(), out reason) &&
                reason.Contains("site identity"), "one site identity was rebound to another live Port");
        }

        private static void TriggerBoundariesAndYardScope()
        {
            var display = PortFixture(17, "port M 17 Mount Malefic", "Mount Malefic");
            Check(StartPorts.Register("author", "display", display.Port,
                new StartPortOptions { DisplayName = "Renamed", SceneryRoot = Child<Transform>("scenery", display.Island) }, out var reason), reason);
            var choice = Complete(display.Choice);
            Check(choice.DisplayName == "Renamed" && choice.SceneryRoot != null && !choice.HasAuthoredBerth && !choice.NeedsPlannedBerth,
                "display/scenery-only registration relocated an ordinary native berth");

            var shipyardOnly = PortFixture(19, "port M 19 (Eastwind)");
            var yard = Child<Shipyard>("explicit yard", shipyardOnly.Island);
            Check(StartPorts.Register("author", "yard", shipyardOnly.Port,
                new StartPortOptions { Shipyard = yard }, out reason), reason);
            choice = Complete(shipyardOnly.Choice);
            Check(ReferenceEquals(choice.Yard, yard) && choice.HasAuthoredYardScope && !choice.HasAuthoredBerth && !choice.NeedsPlannedBerth,
                "shipyard-only registration changed ordinary berth placement");

            var scoped = PortFixture(26, "port M 26 (Monastery)");
            var dock = Child<Transform>("dock A", scoped.Island);
            var otherDock = Child<Transform>("dock B", scoped.Island);
            var inside = Child<Shipyard>("yard A", scoped.Island, dock);
            Child<Shipyard>("yard B", scoped.Island, otherDock);
            Check(StartPorts.Register("author", "dock", scoped.Port,
                new StartPortOptions { DockRoot = dock }, out reason), reason);
            choice = Complete(scoped.Choice);
            Check(ReferenceEquals(choice.Yard, inside) && choice.HasAuthoredBerth && choice.HasAuthoredYardScope,
                "author dock root did not scope automatic shipyard discovery");

            var markerOnly = PortFixture(27, "port M 27 (Valley)");
            Check(StartPorts.Register("author", "marker", markerOnly.Port,
                new StartPortOptions { BerthMarker = Child<Transform>("marker", markerOnly.Island) }, out reason), reason);
            Check(Complete(markerOnly.Choice).NeedsPlannedBerth,
                "author berth marker alone did not enter planned placement");

            var shoreOnly = PortFixture(28, "port M 28 (Cave)");
            Check(StartPorts.Register("author", "shore", shoreOnly.Port,
                new StartPortOptions { ShoreMarker = Child<Transform>("shore", shoreOnly.Island) }, out reason), reason);
            Check(Complete(shoreOnly.Choice).NeedsPlannedBerth,
                "author shore marker alone did not enter planned placement");

            var mooringsOnly = PortFixture(29, "port E 29 (jungle)");
            Check(StartPorts.Register("author", "moorings", mooringsOnly.Port, new StartPortOptions
            {
                FrontMooring = Child<GPButtonDockMooring>("front", mooringsOnly.Island),
                BackMooring = Child<GPButtonDockMooring>("back", mooringsOnly.Island)
            }, out reason), reason);
            Check(Complete(mooringsOnly.Choice).NeedsPlannedBerth,
                "author mooring pair alone did not enter planned placement");
        }

        private static void SaffronAndChronosRemainBuiltIn()
        {
            var saffron = PortFixture(33, "port A 33 (flower)", "Flower Island", recovery: false);
            saffron.Choice.Pool = PortPool.SmallIslands;
            var markerRecovery = Child<RecoveryPort>("saffron recovery", saffron.Island, saffron.Port.transform);
            markerRecovery.parentPort = saffron.Port;
            markerRecovery.boatPos = Child<Transform>("saffron marker", saffron.Island, markerRecovery.transform);
            Check(StartPorts.Register("author", "saffron", saffron.Port,
                new StartPortOptions { DisplayName = "Garden", SceneryRoot = Child<Transform>("garden scenery", saffron.Island) }, out var reason), reason);
            var saffronChoice = Complete(saffron.Choice);
            Check(saffronChoice.Hidden == HiddenSite.Saffron && saffronChoice.Pool == PortPool.SmallIslands &&
                ReferenceEquals(saffronChoice.Marker, markerRecovery.boatPos) && saffronChoice.DisplayName == "Garden",
                "Saffron registration replaced rather than enriched its built-in destination");

            var chronos = PortFixture(21, "port 21 M chronos", "Chronos");
            var fortRoot = Child<Transform>("JUNIPER(Clone)", chronos.Island);
            var fortYard = Child<Shipyard>("FORT shipyard", chronos.Island, fortRoot);
            fortYard.FortRoot = fortRoot;
            Check(StartPorts.Register("oldchronian", "fort-juniper", chronos.Port,
                new StartPortOptions { DisplayName = "New Chronos", DockRoot = fortRoot }, out reason), reason);
            var catalog = new SelectionCatalog();
            catalog.Ports.Add(chronos.Choice);
            StartDestinations.Complete(catalog);
            Check(catalog.Ports.Count == 2, "Chronos registration duplicated or removed the Fort destination");
            var ordinary = catalog.Ports.Single(item => item.SiteIdentity == null);
            var fort = catalog.Ports.Single(item => item.SiteIdentity == KnownSites.FortJuniperKey);
            Check(ordinary.DisplayName == "New Chronos" && ordinary.LegacySiteKey == null &&
                ReferenceEquals(ordinary.Yard, fortYard) && ReferenceEquals(ordinary.DockRoot, fortRoot) &&
                fort.DisplayName == "Fort Juniper" && fort.LegacySiteKey == null,
                "Chronos registration leaked into Fort Juniper identity");
            Check(catalog.Rejections.Any(item => item.Contains(KnownSites.FortJuniperKey)),
                "built-in collision did not report the unusable legacy alias");
        }

        private static int Main()
        {
            try
            {
                NoRegistrationBaseline();
                AuthoritativeOverlayAndConflicts();
                LifecycleAndValidation();
                TriggerBoundariesAndYardScope();
                SaffronAndChronosRemainBuiltIn();
                Console.WriteLine("PASS " + checks + " actual-source port registration overlay checks. No Unity runtime acceptance.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.Message);
                Console.Error.WriteLine(exception.StackTrace);
                return 1;
            }
        }
    }
}
