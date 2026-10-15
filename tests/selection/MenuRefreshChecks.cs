using System;
using NewBeginnings;

namespace MenuRefreshBoundary
{
    static class MenuRefreshChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            SelectionCatalog.Calls = 0;
            Time.realtimeSinceStartup = 100;
            StartPorts.Revision = 3;
            var menu = new MenuUi();
            menu.Open();
            menu.Tick();
            check(SelectionCatalog.Calls == 1 && menu.displays == 1, "opening must discover once without repeating during first Update");
            Time.realtimeSinceStartup = 101.99f;
            menu.Tick();
            check(SelectionCatalog.Calls == 1, "opening reset did not hold two-second refresh interval");
            Time.realtimeSinceStartup = 102;
            menu.Tick();
            check(SelectionCatalog.Calls == 2, "late unregistered provider discovery stopped");
            StartPorts.Revision++;
            menu.Tick();
            check(SelectionCatalog.Calls == 3, "provider registration revision did not refresh immediately");
            menu.Tick();
            check(SelectionCatalog.Calls == 3, "revision refresh repeated");
            Time.realtimeSinceStartup = 110;
            menu.Open();
            menu.Tick();
            check(SelectionCatalog.Calls == 4, "fresh reopen did not discover exactly once");
            SelectionCatalog.Fail = true;
            Time.realtimeSinceStartup = 120;
            menu.RefreshCatalog();
            menu.Tick();
            check(SelectionCatalog.Calls == 5 && menu.catalogUnavailable, "failed discovery spun during same frame");
            SelectionCatalog.Fail = false;
            SelectionCatalog.Empty = true;
            Time.realtimeSinceStartup = 122;
            menu.Tick();
            menu.Tick();
            check(SelectionCatalog.Calls == 6, "empty catalog spun during same frame");
            SelectionCatalog.Empty = false;
            Time.realtimeSinceStartup = 124;
            menu.Tick();
            check(SelectionCatalog.Calls == 7 && !menu.catalogUnavailable, "late available provider did not recover after empty catalog");
            var port = new Port
            {
                portIndex = 1
            };
            Port.ports = new Port[3];
            Port.ports[1] = port;
            Resources.ScenePorts = new[] { port };
            Resources.Calls = 0;
            var snapshot = Resources.FindObjectsOfTypeAll<Port>();
            check(StartDestinations.ValidPort(port, snapshot) && StartDestinations.ValidPort(port, snapshot) && Resources.Calls == 1, "per-discovery port snapshot triggered another global scan");
            Resources.ScenePorts = new[]
            {
                port,
                new Port
                {
                    portIndex = 1
                }
            };
            check(!StartDestinations.ValidPort(port) && Resources.Calls == 2, "standalone validation missed newly added duplicate identity");
            check(!StartDestinations.ValidPort(port, Resources.FindObjectsOfTypeAll<Port>()), "fresh discovery snapshot allowed duplicate port identity");
            Resources.ScenePorts[1].enabled = false;
            check(StartDestinations.ValidPort(port), "disabled duplicate incorrectly excluded registered port");
            Resources.ScenePorts[1].enabled = true;
            Resources.ScenePorts[1].scene.isLoaded = false;
            check(StartDestinations.ValidPort(port), "unloaded duplicate incorrectly excluded registered port");
            Console.WriteLine("PASS source-extracted menu refresh and port identity checks; operation counts, no live frame-time measurement.");
        }
    }
}

internal static class AvailabilityChecks
{
    sealed class CountingRandom : Random
    {
        internal int Calls;
        public override int Next(int maxValue)
        {
            Calls++;
            return 0;
        }
    }

    internal static void Run(Action<bool, string> check)
    {
        var c = new SelectionCatalog();
        c.Ports.Add(new PortChoice { Index = 1, Key = "port:1", Pool = PortPool.AlAnkh });
        c.Ports.Add(new PortChoice { Index = 2, Key = "mod:2", IsModded = true });
        c.Boats.Add(new BoatChoice { Index = 10, Size = BoatSize.Small });
        c.Boats.Add(new BoatChoice { Index = 20, Size = BoatSize.Large });
        var s = new SelectionSettings
        {
            RandomBoat = true,
            RandomPort = true,
            IncludeModPorts = true,
            PortIndex = 1,
            BoatSceneIndex = 10
        };
        foreach (bool randomPort in new[] { false, true })
            foreach (bool randomBoat in new[] { false, true })
                foreach (bool includeMod in new[] { false, true })
                    foreach (int mask in new[] { 0, 1, 2, 5, 15 })
                    {
                        s.RandomPort = randomPort;
                        s.RandomBoat = randomBoat;
                        s.IncludeModPorts = includeMod;
                        int calls = 0;
                        FixedStart.Viable = (p, b) =>
                        {
                            calls++;
                            return (mask & (1 << ((p.Index - 1) * 2 + (b.Index == 10 ? 0 : 1)))) != 0;
                        };
                        var rng = new CountingRandom();
                        var all = c.TryChoose(s, null, rng, out var chosen, out var reason);
                        var allCalls = calls;
                        calls = 0;
                        var any = c.TryChoose(s, null, new CountingRandom(), out _, out var anyReason, availabilityOnly: true);
                        check(any == all && anyReason == reason, "availability and exhaustive choice disagree on filters or reason");
                        check(calls <= allCalls, "availability checked more pairs than selection");
                    }

        s.RandomPort = s.RandomBoat = s.IncludeModPorts = true;
        int visits = 0;
        FixedStart.Viable = (p, b) =>
        {
            visits++;
            return true;
        };
        var sample = new CountingRandom();
        c.TryChoose(s, null, sample, out var last, out _);
        check(visits == 4 && sample.Calls == 4 && last.Port.Index == 2 && last.Boat.Index == 20, "default actual choice did not process complete reservoir");
        visits = 0;
        sample = new CountingRandom();
        c.TryChoose(s, null, sample, out _, out _, availabilityOnly: true);
        check(visits == 1 && sample.Calls == 0, "availability failed to stop at first usable pair");
        FixedStart.Viable = (p, b) => true;
    }
}
