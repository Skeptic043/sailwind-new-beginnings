using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using NewBeginnings;

internal static class SecretIslandChecks
{
    internal static void Run(string path, Action<bool, string> check)
    {
        var p = Plugin.Start(path);
        check(!p.Settings.SecretIslandsEnabled && !p.Settings.AnySecretIslandsEnabled, "secret group defaults enabled");
        p.Settings.SetSecretIslands(true);
        p.MenuSave();
        p = Plugin.Start(path);
        check(p.Settings.SecretIslandsEnabled, "group choice did not persist through three existing keys");
        p.Settings.PortPools.Clear();
        p.MenuSave();
        p = Plugin.Start(path);
        check(p.Settings.PortPools.SetEquals(new[] { PortPool.AlAnkh }) && p.Settings.SecretIslandsEnabled,
            "secret-only selection did not restore a geographic random source");
        p.Config[new ConfigDefinition("Spoilers", "EnablePirateIsland")].BoxedValue = false;
        check(!p.Settings.SecretIslandsEnabled && p.Settings.AnySecretIslandsEnabled && p.Settings.EnableSaffronIsland, "external individual choice lost mixed state");
        p.MenuSave();
        p = Plugin.Start(path);
        check(!p.Settings.EnablePirateIsland && p.Settings.EnableSaffronIsland && p.Settings.EnableChronos, "mixed secret keys overwritten on reload");
        p.Settings.PortPools.Clear();
        p.Settings.PortPools.Add(PortPool.AlAnkh);
        p.Settings.EnableChronos = false;
        p.Settings.EnableSaffronIsland = false;
        p.Settings.EnablePirateIsland = true;
        check(!p.Settings.SetPortPool(PortPool.AlAnkh, false), "spoiler filter counted as a random source");
        p.Settings.PortPools.Clear();
        p.MenuSave();
        p = Plugin.Start(path);
        check(p.Settings.PortPools.SetEquals(new[] { PortPool.AlAnkh }) && p.Settings.EnablePirateIsland,
            "legacy Pirate-only configuration did not restore geography");
        p.Settings.SetSecretIslands(false);
        p.MenuSave();
        p = Plugin.Start(path);
        check(!p.Settings.AnySecretIslandsEnabled && p.Settings.PortPools.SetEquals(new[] { PortPool.AlAnkh }), "disabling final secret group lost existing empty-pool fallback");
        var legacy = SelectionOverrides.Parse("", "21|port 21 M chronos|Chronos");
        check(legacy.Errors.Count == 0 && legacy.TryPortPool(21, "port 21 M chronos", out var legacyPool) &&
            legacyPool == PortPool.SmallIslands, "exact legacy Chronos override was not normalized to Small Islands");
        foreach (var invalid in new[] { "22|port 21 M chronos|Chronos", "21|another port|Chronos" })
        {
            var rejected = SelectionOverrides.Parse("", invalid);
            check(rejected.Errors.Count == 1 && !rejected.TryPortPool(21, "port 21 M chronos", out _),
                "unrelated legacy Chronos override was accepted: " + invalid);
        }
        var c = new SelectionCatalog();
        // Geographic membership must not reveal any spoiler location.
        var chronos = new PortChoice
        {
            Index = 21,
            IdentityName = "port 21 M chronos",
            Key = "chronos",
            Pool = PortPool.SmallIslands
        };
        var pirate = new PortChoice
        {
            Index = -1,
            IdentityName = "pirate",
            Key = "pirate",
            Hidden = HiddenSite.Pirate,
            Pool = PortPool.SmallIslands,
            HasShipyard = true
        };
        var saffron = new PortChoice
        {
            Index = 33,
            IdentityName = "saffron",
            Key = "saffron",
            Hidden = HiddenSite.Saffron,
            Pool = PortPool.SmallIslands
        };
        var fort = new PortChoice
        {
            Index = 21,
            IdentityName = "fort",
            Key = "fort",
            SiteIdentity = "fort",
            IsModded = true
        };
        c.Ports.AddRange(new[] { chronos, pirate, saffron, fort });
        c.Boats.Add(new BoatChoice { Index = 10, IdentityName = "boat", Size = BoatSize.Small });
        var s = new SelectionSettings
        {
            BoatSceneIndex = 10
        };
        var e = new IndividualExclusions();
        check(c.VisiblePorts(s).SequenceEqual(new[] { fort }), "secret gate used geographic pool or hid Fort");
        foreach (var port in new[] { chronos, pirate, saffron })
        {
            s.DestinationKey = port.Key;
            check(!c.TryChoose(s, e, new Random(1), out _, out _), "disabled secret allowed manual selection");
        }

        s.RandomPort = true;
        s.IncludeModPorts = true;
        s.PortPools.Clear();
        check(c.TryChoose(s, e, new Random(1), out var chosen, out _) && chosen.Port == fort, "mod pool bypassed secret gate");
        s.IncludeModPorts = false;
        s.EnablePirateIsland = true;
        check(!c.TryChoose(s, e, new Random(1), out chosen, out _), "Pirate-only random selection bypassed Small Islands");
        s.PortPools.Add(PortPool.SmallIslands);
        check(c.TryChoose(s, e, new Random(1), out chosen, out _) && chosen.Port == pirate,
            "enabled Pirate was not eligible inside Small Islands");
        s.SetSecretIslands(true);
        var seen = new HashSet<PortChoice>();
        for (var seed = 0; seed < 90; seed++)
        {
            check(c.TryChoose(s, e, new Random(seed), out chosen, out _) && new[] { chronos, pirate, saffron }.Contains(chosen.Port), "group failed independent random eligibility");
            seen.Add(chosen.Port);
        }

        check(seen.SetEquals(new[] { chronos, pirate, saffron }), "actual random choice stopped sampling all enabled secrets");
        s.RandomPort = false;
        foreach (var port in seen)
        {
            s.DestinationKey = port.Key;
            check(c.TryChoose(s, e, new Random(1), out chosen, out _) && chosen.Port == port, "enabled secret unavailable manually");
        }

        s.RandomPort = true;
        e.SetPortExcluded(chronos, true);
        e.SetPortExcluded(saffron, true);
        check(c.TryChoose(s, e, new Random(1), out chosen, out _) && chosen.Port == pirate, "secret group bypassed individual exclusions");
        e.SetPortExcluded(pirate, true);
        check(!c.TryChoose(s, e, new Random(1), out _, out _), "all excluded secrets remained selectable");
        s.ShipyardPortsOnly = true;
        check(c.TryChoose(s, e, new Random(1), out chosen, out _) && chosen.Port == pirate, "secret group changed existing shipyard filter semantics");
        var viable = FixedStart.Viable;
        try
        {
            FixedStart.Viable = (port, boat) => false;
            check(!c.TryChoose(s, e, new Random(1), out _, out _), "secret group bypassed actual pair viability");
        }
        finally
        {
            FixedStart.Viable = viable;
        }

        LastPortSourceRule(check);
    }

    // Every combination of geographic pools, mod ports and the three spoiler
    // filters: only geographic pools and mod ports count as random sources.
    private static void LastPortSourceRule(Action<bool, string> check)
    {
        var geographic = new[] { PortPool.AlAnkh, PortPool.Emerald, PortPool.Aestrin, PortPool.FireFishLagoon, PortPool.SmallIslands };
        var empty = new SelectionSettings();
        empty.PortPools.Clear();
        empty.SetSecretIslands(true);
        check(empty.SecretIslandsEnabled && empty.PortPools.SetEquals(new[] { PortPool.AlAnkh }),
            "grouped spoiler toggle left random selection without a source");
        string poolFailure = null, modFailure = null, secretFailure = null;
        for (var mask = 0; mask < 1 << 9; mask++)
        {
            Func<SelectionSettings> make = () =>
            {
                var s = new SelectionSettings { IncludeModPorts = (mask & 32) != 0, EnablePirateIsland = (mask & 64) != 0,
                    EnableSaffronIsland = (mask & 128) != 0, EnableChronos = (mask & 256) != 0 };
                s.PortPools.Clear();
                for (var i = 0; i < geographic.Length; i++)
                    if ((mask & (1 << i)) != 0) s.PortPools.Add(geographic[i]);
                return s;
            };
            var sources = 0;
            for (var bit = 0; bit < 6; bit++) sources += (mask >> bit) & 1;
            var state = make();
            if (state.HasRandomPortSource != sources > 0) poolFailure = poolFailure ?? "source count " + mask;
            foreach (var pool in geographic)
            {
                var s = make();
                var lastSource = s.PortPools.Contains(pool) && sources == 1;
                if (s.CanUncheckPortPool(pool) == lastSource || s.SetPortPool(pool, false) == lastSource ||
                    s.PortPools.Contains(pool) != lastSource)
                    poolFailure = poolFailure ?? pool + " in state " + mask;
            }
            if (state.CanUncheckModPorts == (state.IncludeModPorts && sources == 1))
                modFailure = modFailure ?? "state " + mask;
            var secrets = make();
            secrets.SetSecretIslands(false);
            var expectedPools = state.PortPools.Count + (sources == 0 ? 1 : 0);
            if (!secrets.HasRandomPortSource || secrets.AnySecretIslandsEnabled ||
                secrets.PortPools.Count != expectedPools)
                secretFailure = secretFailure ?? "state " + mask;
        }
        check(poolFailure == null, "geographic pool toggle broke the last-source rule: " + poolFailure);
        check(modFailure == null, "mod ports toggle broke the last-source rule: " + modFailure);
        check(secretFailure == null, "disabling secret islands left no random port source or restored geography needlessly: " + secretFailure);
    }
}
