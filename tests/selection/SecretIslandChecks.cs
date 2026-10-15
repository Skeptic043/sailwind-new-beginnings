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
        foreach (var pool in new[] { PortPool.AlAnkh, PortPool.Emerald, PortPool.Aestrin })
            check(p.Settings.SetPortPool(pool, false), "secret selection blocked last geographic pool removal");
        p.MenuSave();
        p = Plugin.Start(path);
        check(p.Settings.PortPools.SetEquals(new[] { PortPool.Chronos }) && p.Settings.SecretIslandsEnabled, "secret-only selection restored a geographic pool");
        p.Config[new ConfigDefinition("Spoilers", "EnablePirateIsland")].BoxedValue = false;
        check(!p.Settings.SecretIslandsEnabled && p.Settings.AnySecretIslandsEnabled && p.Settings.EnableSaffronIsland, "external individual choice lost mixed state");
        p.MenuSave();
        p = Plugin.Start(path);
        check(!p.Settings.EnablePirateIsland && p.Settings.EnableSaffronIsland && p.Settings.PortPools.Contains(PortPool.Chronos), "mixed secret keys overwritten on reload");
        p.Settings.PortPools.Clear();
        p.Settings.PortPools.Add(PortPool.AlAnkh);
        p.Settings.EnableSaffronIsland = false;
        p.Settings.EnablePirateIsland = true;
        check(p.Settings.SetPortPool(PortPool.AlAnkh, false), "Pirate-only selection blocked removal of last geographic checkbox");
        p.MenuSave();
        p = Plugin.Start(path);
        check(p.Settings.PortPools.Count == 0 && p.Settings.EnablePirateIsland, "legacy Pirate-only configuration restored geography");
        p.Settings.SetSecretIslands(false);
        p.MenuSave();
        p = Plugin.Start(path);
        check(!p.Settings.AnySecretIslandsEnabled && p.Settings.PortPools.SetEquals(new[] { PortPool.AlAnkh }), "disabling final secret group lost existing empty-pool fallback");
        var c = new SelectionCatalog();
        // A geographic override must not reveal the real Chronos, nor hide an unrelated port.
        var chronos = new PortChoice
        {
            Index = 21,
            IdentityName = "port 21 M chronos",
            Key = "chronos",
            Pool = PortPool.AlAnkh
        };
        var pirate = new PortChoice
        {
            Index = -1,
            IdentityName = "pirate",
            Key = "pirate",
            HiddenKey = "pirate",
            Pool = PortPool.SmallIslands,
            HasShipyard = true
        };
        var saffron = new PortChoice
        {
            Index = 33,
            IdentityName = "saffron",
            Key = "saffron",
            HiddenKey = "saffron",
            Pool = PortPool.SmallIslands,
            IsModded = true
        };
        var fort = new PortChoice
        {
            Index = 21,
            IdentityName = "fort",
            Key = "fort",
            SiteIdentity = "fort",
            IsModded = true
        };
        var other = new PortChoice
        {
            Index = 45,
            IdentityName = "other",
            Key = "other",
            Pool = PortPool.Chronos
        };
        c.Ports.AddRange(new[] { chronos, pirate, saffron, fort, other });
        c.Boats.Add(new BoatChoice { Index = 10, IdentityName = "boat", Size = BoatSize.Small });
        var s = new SelectionSettings
        {
            BoatSceneIndex = 10
        };
        var e = new IndividualExclusions();
        check(c.VisiblePorts(s).SequenceEqual(new[] { fort, other }), "secret gate used mutable pool or hid Fort");
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
        check(c.TryChoose(s, e, new Random(1), out chosen, out _) && chosen.Port == pirate, "Pirate-only random selection required geographic checkbox");
        s.SetSecretIslands(true);
        c.Ports.Remove(other); // Unrelated geographic override remains governed by its configured pool.
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
    }
}
