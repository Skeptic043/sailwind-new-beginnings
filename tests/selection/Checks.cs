// Behavioral checks for live configuration reload, using BepInEx 5.4.23.5's
// real ConfigFile. Configuration Manager changes a setting by assigning
// ConfigEntryBase.BoxedValue; Cm() below does exactly that.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using NewBeginnings;

namespace NewBeginnings
{
    // Test half of Plugin. The other half (saves, guard, handler, reloads) is
    // extracted verbatim from src/Plugin.cs into Production.generated.cs.
    internal sealed partial class Plugin
    {
        internal static Plugin Instance;
        internal ConfigFile Config;
        private SelectionConfig selection;
        private StartingOptionsConfig startingOptions;
        internal readonly List<string> Warnings = new List<string>();
        internal readonly List<string> Errors = new List<string>();
        internal int ReloadEvents;
        internal SelectionSettings Settings => selection.Settings;
        internal SelectionOverrides Overrides => selection.Overrides;
        internal StartingOptionsSettings StartOptions => startingOptions.Settings;
        internal IndividualExclusions Exclusions => selection.IndividualExclusions;

        internal void Warn(string message) => Warnings.Add(message);
        internal void Error(string message, Exception exception) => Errors.Add(message + " " + exception);
        internal void MenuSave() => SaveSelection();
        // Mirrors Plugin.Awake: BaseUnityPlugin's ConfigFile (saveOnInit false),
        // both configs, then the subscription last.
        internal static Plugin Start(string path)
        {
            var plugin = new Plugin();
            Instance = plugin;
            plugin.Config = new ConfigFile(path, false);
            plugin.selection = new SelectionConfig(plugin.Config);
            plugin.startingOptions = new StartingOptionsConfig(plugin.Config);
            plugin.Config.SettingChanged += plugin.OnConfigSettingChanged;
            plugin.SettingsReloaded += () => plugin.ReloadEvents++;
            return plugin;
        }
    }
}

internal static class Checks
{
    private static int checks;
    private static string tempRoot;
    private static void Check(bool ok, string label)
    {
        if (!ok)
            throw new Exception("FAIL " + label);
        checks++;
    }

    private static int Main(string[] args)
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "nb-config-reload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var tests = new Action[]
            {
                StartupRoundtrip,
                ConfigurationManagerChangeApplies,
                MenuSaveKeepsExternalChange,
                OwnSaveDoesNotReloadHalfway,
                InvalidValuesMatchStartup,
                HandEditedFileReload,
                ExclusionEditorReload,
                StartingOptionsPanelReload,
                DestinationAndModPoolReload,
                DestinationSelection,
                RegistrationAliasContinuity,
                ShipyardBerthSetting,
                NativeYardPoolEligibility,
                ChronosPoolReload,
                SpoilerDrawer,
                () => SecretIslandChecks.Run(NewPath("secret-group"), Check),
            };
            // --only Name runs one group, so a negative control can show its targeted failure.
            var only = Array.IndexOf(args, "--only")is var at && at >= 0 ? args[at + 1] : null;
            foreach (var test in tests)
                if (only == null || test.Method.Name == only)
                    test();
            if (only == null)
                UiHarness.Run(Check);
            if (only == null)
                MenuRefreshBoundary.MenuRefreshChecks.Run(Check);
            if (only == null)
                AvailabilityChecks.Run(Check);
            if (only == null)
                Lifetime.DestinationChecks.Run(Check);
            if (only == null)
                BoatNamesChecks.Run(Check);
            if (checks == 0)
                throw new Exception("FAIL no checks ran");
            Console.WriteLine("PASS " + checks + " config-reload checks with BepInEx 5.4.23.5 ConfigFile. " + "Menu panels are doubles around reload members extracted from production. No Unity, " + "Configuration Manager UI or in-game acceptance.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(exception.StackTrace);
            return 1;
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, true);
            }
            catch
            {
            }
        }
    }

    private static void ChronosPoolReload()
    {
        var path = NewPath("chronos");
        var p = Plugin.Start(path);
        Check(!p.Settings.EnableChronos && p.Settings.PortPools.SetEquals(new[] { PortPool.AlAnkh, PortPool.Emerald, PortPool.Aestrin }), "Chronos unexpectedly changed default regions");
        Cm(p, "Random Port Pools", "Chronos", true);
        Check(p.Settings.EnableChronos && !p.Settings.PortPools.Contains(PortPool.SmallIslands), "live Chronos config change altered geographic pools");
        foreach (var key in new[] { "AlAnkh", "EmeraldArchipelago", "Aestrin", "FireFishLagoon", "SmallIslands" })
            Cm(p, "Random Port Pools", key, false);
        p.MenuSave();
        p = Plugin.Start(path);
        Check(p.Settings.PortPools.SetEquals(new[] { PortPool.AlAnkh }) && p.Settings.EnableChronos,
            "Chronos-only configuration did not restore a geographic pool");
        var catalog = new SelectionCatalog();
        var ordinary = new PortChoice
        {
            Index = 21,
            Key = "port:chronos",
            IdentityName = "port 21 M chronos",
            Pool = PortPool.SmallIslands
        };
        var fort = new PortChoice
        {
            Index = 21,
            Key = "site:fort",
            SiteIdentity = "site:fort",
            IdentityName = "site:fort",
            IsModded = true
        };
        catalog.Ports.AddRange(new[] { ordinary, fort });
        catalog.Boats.Add(new BoatChoice { Index = p.Settings.BoatSceneIndex, Size = BoatSize.Small });
        p.Settings.RandomPort = true;
        p.Settings.PortPools.Clear();
        p.Settings.PortPools.Add(PortPool.SmallIslands);
        Check(catalog.TryChoose(p.Settings, p.Exclusions, new Random(1), out var selected, out _) && selected.Port == ordinary, "Chronos pool selected Fort with mod ports disabled");
        p.Settings.PortPools.Clear();
        Check(!catalog.TryChoose(p.Settings, p.Exclusions, new Random(1), out selected, out _),
            "Chronos bypassed the Small Islands requirement");
        p.Settings.IncludeModPorts = true;
        p.Settings.EnableChronos = false;
        Check(catalog.TryChoose(p.Settings, p.Exclusions, new Random(1), out selected, out _) && selected.Port == fort, "mod-only pool admitted ordinary Chronos");
        p.Settings.RandomPort = false;
        p.Settings.IncludeModPorts = false;
        p.Settings.DestinationKey = ordinary.Key;
        Check(!catalog.TryChoose(p.Settings, p.Exclusions, new Random(1), out selected, out _), "disabled Chronos remained available manually");
    }

    private static void SpoilerDrawer()
    {
        var plugin = Plugin.Start(NewPath("spoiler-drawer"));
        SpoilerDrawerChecks.Run(plugin, Check);
    }

    private static void ShipyardBerthSetting()
    {
        var path = NewPath("shipyard-berth");
        var p = Plugin.Start(path);
        Check(!p.Settings.UseShipyardBerth && !(bool)Get(p, "Selection", "UseShipyardBerth"), "shipyard berth must default off");
        Cm(p, "Selection", "UseShipyardBerth", true);
        Check(p.Settings.UseShipyardBerth && !p.Settings.RandomPort && !p.Settings.ShipyardPortsOnly, "CM berth toggle must apply independently of random and shipyard-only filters");
        var port = new PortChoice
        {
            Index = 3,
            Key = "native:3",
            IdentityName = "native-port",
            DisplayName = "Native Port",
            Pool = PortPool.AlAnkh,
            HasShipyard = true
        };
        var boat = new BoatChoice
        {
            Index = 20,
            IdentityName = "native-boat",
            DisplayName = "Native Boat",
            Size = BoatSize.Medium
        };
        var catalog = new SelectionCatalog();
        catalog.Ports.Add(port);
        catalog.Boats.Add(boat);
        p.Settings.PortIndex = 3;
        p.Settings.BoatSceneIndex = 20;
        p.MenuSave();
        var oldAvailability = ShipyardStart.CanUse;
        var oldViable = FixedStart.Viable;
        try
        {
            FixedStart.Viable = (a, b) => true;
            ShipyardStart.CanUse = site => ReferenceEquals(site, port);
            Check(catalog.TryChoose(p.Settings, p.Exclusions, new Random(1), out var selected, out _) && selected.UseShipyardBerth && ReferenceEquals(selected.Port, port), "manual selection must capture enabled available berth without changing destination");
            Cm(p, "Selection", "UseShipyardBerth", false);
            Check(selected.UseShipyardBerth && catalog.TryChoose(p.Settings, p.Exclusions, new Random(1), out var ordinary, out _) && !ordinary.UseShipyardBerth, "selection must capture berth option without changing an already selected pair");
            p.Settings.UseShipyardBerth = true;
            p.MenuSave();
            Check((bool)Get(p, "Selection", "UseShipyardBerth") && Plugin.Start(path).Settings.UseShipyardBerth, "menu save must persist berth option for reopening");
            foreach (var random in new[] { false, true })
                foreach (var preset in new[] { false, true })
                {
                    p.Settings.RandomPort = random;
                    p.Settings.ShipyardPortsOnly = preset;
                    Check(catalog.TryChoose(p.Settings, p.Exclusions, new Random(1), out var pair, out _) && pair.UseShipyardBerth, "available berth option must work independently of random/preset selection");
                }

            ShipyardStart.CanUse = site => false;
            Check(catalog.TryChoose(p.Settings, p.Exclusions, new Random(1), out var fallback, out _) && !fallback.UseShipyardBerth && ReferenceEquals(fallback.Port, port) && ReferenceEquals(fallback.Boat, boat), "unavailable berth must keep the chosen port and boat with ordinary placement");
            Check(catalog.TryChoose(p.Settings, p.Exclusions, new Random(1), out var available, out _, availabilityOnly: true) && !available.UseShipyardBerth, "availability query must use the same berth snapshot rule");
        }
        finally
        {
            ShipyardStart.CanUse = oldAvailability;
            FixedStart.Viable = oldViable;
        }
    }

    private static void NativeYardPoolEligibility()
    {
        var catalog = new SelectionCatalog();
        var dc = new PortChoice
        {
            Index = 9,
            Key = "port:dragon",
            IdentityName = "port E 9 (Dragon cliffs)",
            Pool = PortPool.Emerald
        };
        var kb = new PortChoice
        {
            Index = 22,
            Key = "port:kicia",
            IdentityName = "port L 22 Lagoon Bay",
            Pool = PortPool.FireFishLagoon
        };
        dc.HasShipyard = ShipyardStart.HasNativeYard(dc);
        kb.HasShipyard = ShipyardStart.HasNativeYard(kb);
        catalog.Ports.Add(dc);
        catalog.Ports.Add(kb);
        catalog.Boats.Add(new BoatChoice { Index = 70, IdentityName = "Jong", Size = BoatSize.Large });
        var settings = new SelectionSettings
        {
            RandomPort = true,
            ShipyardPortsOnly = true,
            UseShipyardBerth = true,
            BoatSceneIndex = 70
        };
        var old = ShipyardStart.CanUse;
        ShipyardStart.CanUse = ShipyardStart.HasNativeYard;
        try
        {
            foreach (var port in new[] { dc, kb })
            {
                settings.PortPools.Clear();
                settings.PortPools.Add(port.Pool.Value);
                Check(catalog.TryChoose(settings, new IndividualExclusions(), new Random(1), out var pair, out var reason) && ReferenceEquals(pair.Port, port) && pair.UseShipyardBerth, "single-region native shipyard selection failed: " + reason);
            }
        }
        finally
        {
            ShipyardStart.CanUse = old;
        }
    }

    private static string NewPath(string name) => Path.Combine(tempRoot, name + "-" + Guid.NewGuid().ToString("N") + ".cfg");
    private static ConfigEntryBase Entry(Plugin plugin, string section, string key)
    {
        var definition = new ConfigDefinition(section, key);
        if (!plugin.Config.ContainsKey(definition))
            throw new Exception("harness: no entry " + section + "/" + key);
        return plugin.Config[definition];
    }

    private static object Get(Plugin plugin, string section, string key) => Entry(plugin, section, key).BoxedValue;
    private static void Cm(Plugin plugin, string section, string key, object value) => Entry(plugin, section, key).BoxedValue = value;
    private static string DumpSelection(SelectionSettings s, IndividualExclusions e, SelectionOverrides o) => string.Join("|", new object[] { s.PortIndex, s.RandomPort, s.ShipyardPortsOnly, s.BoatSceneIndex, s.RandomBoat, string.Join(",", s.PortPools.OrderBy(x => x)), string.Join(",", s.BoatSizes.OrderBy(x => x)), e.SavePorts(), e.SaveBoats(), string.Join(";", o.Errors) });
    private static string DumpOptions(StartingOptionsSettings o) => string.Join("|", new object[] { o.CurrencyMultiplier.ToString("R", CultureInfo.InvariantCulture), o.StartingReputation, string.Join(",", o.CurrencyOverrides.Select(v => v?.ToString(CultureInfo.InvariantCulture) ?? "-")), string.Join(",", o.FactionReputation), o.StandardSupplies, StartingOptionsConfig.WriteEquipment(o) });
    private static string Dump(Plugin p) => DumpSelection(p.Settings, p.Exclusions, p.Overrides) + " || " + DumpOptions(p.StartOptions);
    private static PortChoice Port(string name) => new PortChoice
    {
        IdentityName = name
    };
    private static BoatChoice Boat(string name) => new BoatChoice
    {
        IdentityName = name
    };
    private static string SetLine(string text, string section, string key, string value)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var inSection = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("["))
                inSection = lines[i].Trim() == "[" + section + "]";
            else if (inSection && lines[i].StartsWith(key + " = "))
            {
                lines[i] = key + " = " + value;
                return string.Join("\r\n", lines);
            }
        }

        throw new Exception("fixture line not found: " + section + "/" + key);
    }

    private static readonly string[][] Fixtures =
    {
        null,
        new[]
        {
            "[Selection]",
            "LastSelectedPort = 7",
            "RandomPort = true",
            "ShipyardPortsOnly = true",
            "LastSelectedBoat = 20",
            "RandomBoat = true",
            "[Random Port Pools]",
            "FireFishLagoon = true",
            "EmeraldArchipelago = false",
            "[Random Boat Sizes]",
            "Large = false",
            "[Individual Random Exclusions]",
            "Ports = Neverdin;Dragon%20Cliffs",
            "Boats = BOAT%20dhow%20small%20(10)",
            "[Starting Options]",
            "CurrencyMultiplier = 2.5",
            "StartingFactionReputation = 4",
            "StandardSupplies = false",
            "AdditionalEquipment = native%3A71%3A71%20fuel%20wood=2;mod%3Ax=1",
            "[Starting Currency Overrides]",
            "AlAnkh = 100",
            "Gold = 0",
            "[Starting Reputation Overrides]",
            "Emerald = 6",
            "[Compatibility]",
            "BoatSizeOverrides = 212|BOAT CUTTER (212)(Clone)|Large",
        },
        new[]
        {
            "[Random Port Pools]",
            "AlAnkh = false",
            "EmeraldArchipelago = false",
            "Aestrin = false",
            "[Random Boat Sizes]",
            "Small = false",
            "Medium = false",
            "Large = false",
            "[Compatibility]",
            "BoatSizeOverrides = bad",
            "PortPoolOverrides = 1|x|Nope;1|x|Nope",
            "[Starting Options]",
            "CurrencyMultiplier = 500",
            "StartingFactionReputation = 11",
            "AdditionalEquipment = a=-1;b%ZZ=1;c=2;d=wat",
            "[Starting Currency Overrides]",
            "Gold = abc",
            "Emerald = -5",
            "[Starting Reputation Overrides]",
            "Aestrin = -1",
        },
    };
    private static void StartupRoundtrip()
    {
        for (var index = 0; index < Fixtures.Length; index++)
        {
            var path = NewPath("startup");
            if (Fixtures[index] != null)
                File.WriteAllText(path, string.Join("\r\n", Fixtures[index]) + "\r\n");
            var plugin = Plugin.Start(path);
            if (index == 1)
            {
                Check(plugin.Settings.PortIndex == 7 && plugin.Settings.RandomPort && plugin.Settings.ShipyardPortsOnly, "valid startup selection was not loaded");
                Check(plugin.StartOptions.CurrencyMultiplier == 2.5f && plugin.StartOptions.StartingReputation == 4 && !plugin.StartOptions.StandardSupplies, "valid starting options were not loaded");
            }

            if (index == 2)
                Check(plugin.Warnings.Count >= 8, "invalid startup fixture produced no meaningful validation");
            plugin.Settings.PortIndex = 33;
            plugin.Settings.SetPortPool(PortPool.SmallIslands, true);
            plugin.MenuSave();
            plugin.StartOptions.StartingReputation = 3;
            plugin.SaveStartingOptions();
            var expected = Dump(plugin);
            var restarted = Plugin.Start(path);
            Check(Dump(restarted) == expected, "saved startup settings failed roundtrip for fixture " + index);
            Check(restarted.Settings.PortIndex == 33 && restarted.Settings.PortPools.Contains(PortPool.SmallIslands) && restarted.StartOptions.StartingReputation == 3, "explicit menu edits were lost on restart");
            Check(plugin.ReloadEvents == 0, "own startup/menu saves raised a reload");
        }
    }

    private static void DestinationAndModPoolReload()
    {
        var path = NewPath("destinations");
        File.WriteAllText(path, "[Selection]\r\nLastSelectedPort = 21\r\n");
        var p = Plugin.Start(path);
        Check(p.Settings.PortIndex == 21 && p.Settings.DestinationKey == "", "legacy index migration changed selection");
        Check(!p.Settings.IncludeModPorts && !p.Settings.EnablePirateIsland && !p.Settings.EnableSaffronIsland, "new pools or hidden locations were enabled by default");
        Cm(p, "Random Port Pools", "IncludeModPorts", true);
        foreach (var key in new[] { "AlAnkh", "EmeraldArchipelago", "Aestrin", "FireFishLagoon", "SmallIslands" })
            Cm(p, "Random Port Pools", key, false);
        Check(p.Settings.IncludeModPorts && p.Settings.PortPools.Count == 0, "live mod-only selection restored vanilla pool");
        Cm(p, "Selection", "LastSelectedDestination", "provider:absent:fort");
        Cm(p, "Spoilers", "EnablePirateIsland", true);
        Cm(p, "Spoilers", "EnableSaffronIsland", true);
        var events = p.ReloadEvents;
        p.Settings.RandomBoat = true;
        p.MenuSave();
        Check(p.ReloadEvents == events, "own save reloaded new selection fields partway");
        Check((string)Get(p, "Selection", "LastSelectedDestination") == "provider:absent:fort", "unrelated menu save lost missing provider key");
        p = Plugin.Start(path);
        Check(p.Settings.IncludeModPorts && p.Settings.PortPools.Count == 0, "restart lost mod-only pool");
        Check(p.Settings.DestinationKey == "provider:absent:fort" && p.Settings.PortIndex == 21, "restart changed missing provider selection");
        Check(p.Settings.EnablePirateIsland && p.Settings.EnableSaffronIsland, "spoiler gates did not persist");
        Cm(p, "Random Port Pools", "IncludeModPorts", false);
        Check(p.Settings.PortPools.SetEquals(new[] { PortPool.AlAnkh }) && p.Settings.AnySecretIslandsEnabled,
            "secret-only config did not restore geography");
        var ordinary = Port("Chronos");
        var site = Port("site:provider:fort");
        p.Exclusions.SetPortExcluded(site, true);
        p.MenuSave();
        p = Plugin.Start(path);
        Check(p.Exclusions.IsPortExcluded(site) && !p.Exclusions.IsPortExcluded(ordinary), "site exclusion leaked to native port");
    }

    private static void DestinationSelection()
    {
        var c = new SelectionCatalog();
        var ordinary = new PortChoice
        {
            Index = 21,
            Key = "native:21",
            IdentityName = "port 21 M chronos",
            DisplayName = "Chronos"
        };
        var fort = new PortChoice
        {
            Index = 21,
            Key = "mod:fort",
            SiteIdentity = "mod:fort",
            IdentityName = "site:mod:fort",
            IsModded = true,
            HasShipyard = true
        };
        var bottleneck = new PortChoice
        {
            Index = 67,
            Key = "native:67",
            IdentityName = "Bottleneck",
            Pool = PortPool.Emerald,
            IsModded = true
        };
        var pirate = new PortChoice
        {
            Index = -1,
            Key = "vanilla:pirate",
            IdentityName = "site:pirate",
            Pool = PortPool.SmallIslands,
            Hidden = HiddenSite.Pirate,
            HasShipyard = true
        };
        var saffron = new PortChoice
        {
            Index = 33,
            Key = "native:33",
            IdentityName = "Saffron",
            Pool = PortPool.SmallIslands,
            Hidden = HiddenSite.Saffron
        };
        var neverdin = new PortChoice
        {
            Index = 2,
            Key = "native:2",
            IdentityName = "Neverdin",
            Pool = PortPool.AlAnkh
        };
        // Put the fort first to verify legacy index migration does not select a site.
        c.Ports.AddRange(new[] { fort, ordinary, bottleneck, pirate, saffron, neverdin });
        c.Boats.Add(new BoatChoice { Index = 10, IdentityName = "boat", Size = BoatSize.Small });
        var s = new SelectionSettings
        {
            PortIndex = 21,
            BoatSceneIndex = 10
        };
        s.EnableChronos = true;
        var e = new IndividualExclusions();
        StartPair chosen;
        string reason;
        Check(c.TryChoose(s, e, new Random(1), out chosen, out reason) && chosen.Port == ordinary, "legacy index selected fort");
        s.DestinationKey = fort.Key;
        Check(c.TryChoose(s, e, new Random(1), out chosen, out reason) && chosen.Port == fort, "stable key failed manual fort");
        s.DestinationKey = "absent:provider";
        Check(!c.TryChoose(s, e, new Random(1), out chosen, out reason) && s.DestinationKey == "absent:provider", "missing provider silently substituted");
        foreach (var hidden in new[] { pirate, saffron })
        {
            s.DestinationKey = hidden.Key;
            Check(!c.TryChoose(s, e, new Random(1), out chosen, out reason), "hidden default allowed manual selection");
        }

        s.RandomPort = true;
        s.IncludeModPorts = true;
        s.PortPools.Clear();
        s.EnableChronos = false;
        var seen = new HashSet<PortChoice>();
        for (var seed = 0; seed < 50; seed++)
        {
            Check(c.TryChoose(s, e, new Random(seed), out chosen, out reason) && chosen.Port.IsModded, "mod-only selection admitted geographic location or failed");
            seen.Add(chosen.Port);
        }

        Check(seen.SetEquals(new[] { fort, bottleneck }), "mod-only selection missed Fort or Emerald Bottleneck");
        s.ShipyardPortsOnly = true;
        e.SetPortExcluded(fort, true);
        Check(c.TryChoose(s, e, new Random(1), out chosen, out reason) && chosen.Port == fort, "shipyard filter lost fort or applied ordinary exclusion");
        s.IncludeModPorts = false;
        s.PortPools.Add(PortPool.SmallIslands);
        Check(!c.TryChoose(s, e, new Random(1), out chosen, out reason), "shipyard filter bypassed spoiler gate");
        s.EnablePirateIsland = true;
        Check(c.TryChoose(s, e, new Random(1), out chosen, out reason) && chosen.Port == pirate, "opted-in hidden shipyard did not become eligible");
        s.ShipyardPortsOnly = false;
        s.EnablePirateIsland = false;
        s.EnableSaffronIsland = true;
        Check(c.TryChoose(s, e, new Random(1), out chosen, out reason) && chosen.Port == saffron, "Saffron did not join Small Islands after opt-in");
        s.PortPools.Clear();
        s.PortPools.Add(PortPool.AlAnkh);
        var geographicAndSecret = new HashSet<PortChoice>();
        for (var seed = 0; seed < 20; seed++)
        {
            Check(c.TryChoose(s, e, new Random(seed), out chosen, out reason), "secret plus geographic selection failed");
            geographicAndSecret.Add(chosen.Port);
        }

        Check(geographicAndSecret.SetEquals(new[] { neverdin }), "Saffron bypassed Small Islands after opt-in");
        s.PortPools.Clear();
        s.PortPools.Add(PortPool.SmallIslands);
        Check(c.TryChoose(s, e, new Random(1), out chosen, out reason) && chosen.Port == saffron,
            "Saffron was not eligible when Small Islands was checked");
        s.EnableSaffronIsland = false;
        s.IncludeModPorts = true;
        s.PortPools.Clear();
        s.ShipyardPortsOnly = false;
        e.SetPortExcluded(fort, false);
        FixedStart.Viable = (port, boat) => port == bottleneck;
        Check(c.TryChoose(s, e, new Random(1), out chosen, out reason) && chosen.Port == bottleneck, "mod switch bypassed placement filter");
        FixedStart.Viable = (port, boat) => true;
        c.Ports.RemoveAll(port => port.IsModded);
        Check(!c.TryChoose(s, e, new Random(1), out chosen, out reason) && s.PortPools.Count == 0, "missing mod pool silently restored geography");
    }

    private static void RegistrationAliasContinuity()
    {
        var port = new PortChoice
        {
            Index = 21,
            Key = "port:port 21 M chronos",
            IdentityName = "port 21 M chronos",
            LegacySiteKey = "site:author|chronos",
            DisplayName = "Author's Chronos"
        };
        var catalog = new SelectionCatalog();
        catalog.Ports.Add(port);
        catalog.Boats.Add(new BoatChoice { Index = 10, IdentityName = "boat", Size = BoatSize.Small });
        var settings = new SelectionSettings
        {
            DestinationKey = port.LegacySiteKey,
            PortIndex = 21,
            BoatSceneIndex = 10,
            EnableChronos = true
        };
        Check(catalog.TryChoose(settings, new IndividualExclusions(), new Random(1), out var chosen, out _) &&
            ReferenceEquals(chosen.Port, port), "legacy provider key did not resolve to canonical native destination");
        Check(settings.DestinationKey == port.Key, "resolved legacy provider key was not normalized for later saves");

        var exclusions = IndividualExclusions.Load(port.LegacySiteKey, "");
        Check(exclusions.IsPortExcluded(port), "legacy provider exclusion did not resolve to canonical native destination");
        Check(exclusions.SetPortExcluded(port, false) && !exclusions.IsPortExcluded(port) && exclusions.SavePorts() == "",
            "clearing a legacy provider exclusion did not remove its saved alias");
        Check(exclusions.SetPortExcluded(port, true) && exclusions.SavePorts() == Uri.EscapeDataString(port.IdentityName),
            "new exclusion persisted the provider alias instead of canonical native identity");

        const string formerFortKey = "site:oldchronian|fort-juniper";
        var ordinaryChronos = new PortChoice
        {
            Index = 21,
            Key = "port:port 21 M chronos",
            IdentityName = "port 21 M chronos",
            DisplayName = "Chronos"
        };
        var fort = new PortChoice
        {
            Index = 21,
            Key = formerFortKey,
            IdentityName = formerFortKey,
            SiteIdentity = formerFortKey,
            DisplayName = "Fort Juniper",
            IsModded = true
        };
        catalog = new SelectionCatalog();
        catalog.Ports.AddRange(new[] { ordinaryChronos, fort });
        catalog.Boats.Add(new BoatChoice { Index = 10, IdentityName = "boat", Size = BoatSize.Small });
        settings = new SelectionSettings { DestinationKey = formerFortKey, PortIndex = 21, BoatSceneIndex = 10 };
        Check(catalog.TryChoose(settings, new IndividualExclusions(), new Random(1), out chosen, out _) &&
            ReferenceEquals(chosen.Port, fort), "former Fort registration key selected ordinary Chronos");
        exclusions = IndividualExclusions.Load(formerFortKey, "");
        Check(exclusions.IsPortExcluded(fort) && !exclusions.IsPortExcluded(ordinaryChronos),
            "former Fort registration exclusion migrated to ordinary Chronos");
    }

    private static void ConfigurationManagerChangeApplies()
    {
        var p = Plugin.Start(NewPath("cm"));
        var settings = p.Settings;
        var exclusions = p.Exclusions;
        var options = p.StartOptions;
        Func<SelectionOverrides> menuOverrides = () => p.Overrides; // how MenuUi now holds them
        Cm(p, "Selection", "RandomPort", true);
        Check(settings.RandomPort && p.ReloadEvents == 1, "CM RandomPort change not applied to the shared settings object");
        Cm(p, "Selection", "LastSelectedBoat", 42);
        Check(settings.BoatSceneIndex == 42, "CM boat index not applied");
        Cm(p, "Random Port Pools", "SmallIslands", true);
        Check(settings.PortPools.Contains(PortPool.SmallIslands), "CM pool not applied");
        Cm(p, "Individual Random Exclusions", "Ports", "Neverdin;Dragon%20Cliffs");
        Check(exclusions.IsPortExcluded(Port("neverdin")) && exclusions.IsPortExcluded(Port("Dragon Cliffs")), "CM exclusion not applied in place");
        Cm(p, "Starting Options", "CurrencyMultiplier", 2.5f);
        Check(options.CurrencyMultiplier == 2.5f, "CM multiplier not applied");
        Cm(p, "Starting Options", "AdditionalEquipment", "native%3A71%3A71%20fuel%20wood=2");
        Check(options.EquipmentQuantities.Count == 1 && options.EquipmentQuantities["native:108:108 crate of firewood"] == 2, "CM equipment not read through the startup normalization");
        Cm(p, "Starting Currency Overrides", "AlAnkh", "75");
        Cm(p, "Starting Reputation Overrides", "Emerald", 3);
        Check(options.CurrencyOverrides[0] == 75 && options.FactionReputation[1] == 3, "CM currency/reputation override not applied");
        Cm(p, "Starting Currency Overrides", "AlAnkh", "");
        Check(options.CurrencyOverrides[0] == null, "cleared CM currency override kept its old value");
        Cm(p, "Compatibility", "BoatSizeOverrides", "212|BOAT CUTTER (212)(Clone)|Large");
        Check(menuOverrides().TryBoatSize(212, "boat cutter (212)(clone)", out var size) && size == BoatSize.Large, "CM boat size override not visible to the menu");
        Check(ReferenceEquals(settings, p.Settings) && ReferenceEquals(exclusions, p.Exclusions) && ReferenceEquals(options, p.StartOptions), "reload replaced a shared settings object");
        Check(p.Warnings.Count == 0 && p.Errors.Count == 0, "valid CM changes produced warnings or errors");
    }

    private static void MenuSaveKeepsExternalChange()
    {
        var path = NewPath("revert");
        var p = Plugin.Start(path);
        Cm(p, "Selection", "RandomBoat", true);
        Cm(p, "Random Boat Sizes", "Medium", false);
        Cm(p, "Individual Random Exclusions", "Boats", "BOAT%20X");
        Cm(p, "Starting Options", "StandardSupplies", false);
        Cm(p, "Starting Options", "CurrencyMultiplier", 3f);
        // Unrelated edits made in the menu afterwards.
        p.Settings.PortIndex = 9;
        p.MenuSave();
        p.StartOptions.StartingReputation = 2;
        p.SaveStartingOptions();
        Check((bool)Get(p, "Selection", "RandomBoat") && !(bool)Get(p, "Random Boat Sizes", "Medium") && (string)Get(p, "Individual Random Exclusions", "Boats") == "BOAT%20X", "menu save reverted a CM selection change");
        Check(!(bool)Get(p, "Starting Options", "StandardSupplies") && (float)Get(p, "Starting Options", "CurrencyMultiplier") == 3f, "starting options save reverted a CM change");
        Check((int)Get(p, "Selection", "LastSelectedPort") == 9 && (int)Get(p, "Starting Options", "StartingFactionReputation") == 2, "menu edit not saved");
        var reopened = Plugin.Start(path);
        Check(reopened.Settings.RandomBoat && !reopened.Settings.BoatSizes.Contains(BoatSize.Medium) && reopened.Settings.PortIndex == 9 && !reopened.StartOptions.StandardSupplies && reopened.StartOptions.CurrencyMultiplier == 3f && reopened.StartOptions.StartingReputation == 2, "saved file lost CM or menu values");
    }

    // Without the guard, assigning AlAnkh=false first would reread a mix of old
    // and new entries and the remaining assignments would write that mix.
    private static void OwnSaveDoesNotReloadHalfway()
    {
        var p = Plugin.Start(NewPath("partial"));
        p.Settings.SetPortPool(PortPool.FireFishLagoon, true);
        p.Settings.SetPortPool(PortPool.AlAnkh, false);
        p.Settings.SetPortPool(PortPool.Emerald, false);
        p.Settings.SetPortPool(PortPool.Aestrin, false);
        p.MenuSave();
        Check(p.Settings.PortPools.SetEquals(new[] { PortPool.FireFishLagoon }) && !(bool)Get(p, "Random Port Pools", "AlAnkh") && !(bool)Get(p, "Random Port Pools", "EmeraldArchipelago") && !(bool)Get(p, "Random Port Pools", "Aestrin") && (bool)Get(p, "Random Port Pools", "FireFishLagoon"), "own selection save wrote a half-reloaded pool mix");
        Check(p.ReloadEvents == 0, "own selection save triggered a reload");
        p.StartOptions.CurrencyMultiplier = 5f;
        p.StartOptions.EquipmentQuantities["x"] = 4;
        p.SaveStartingOptions();
        Check((string)Get(p, "Starting Options", "AdditionalEquipment") == "x=4" && p.StartOptions.EquipmentQuantities["x"] == 4, "own starting options save lost equipment to a partial reload");
        Check(p.ReloadEvents == 0, "own starting options save triggered a reload");
    }

    private static void InvalidValuesMatchStartup()
    {
        var path = NewPath("invalid");
        var p = Plugin.Start(path);
        p.Warnings.Clear();
        Cm(p, "Random Port Pools", "AlAnkh", false);
        Cm(p, "Random Port Pools", "EmeraldArchipelago", false);
        Check(p.Warnings.Count == 0, "warning before every pool was disabled");
        Cm(p, "Random Port Pools", "Aestrin", false);
        Check(p.Settings.PortPools.SetEquals(new[] { PortPool.AlAnkh }) && p.Warnings.SequenceEqual(new[] { "Every random port pool was disabled; Al'Ankh was restored." }), "CM disabling every pool did not use the startup fallback/warning once");
        Cm(p, "Starting Options", "CurrencyMultiplier", 500f);
        Check(p.StartOptions.CurrencyMultiplier == 1f && p.Warnings.Count == 2 && p.Warnings[1] == "Invalid starting currency multiplier; using 1x.", "CM invalid multiplier fallback/warning");
        Cm(p, "Starting Currency Overrides", "Gold", "abc");
        Check(p.StartOptions.CurrencyOverrides[3] == null && p.Warnings.Count == 3 && p.Warnings[2] == "Ignored invalid starting currency override: Gold.", "CM invalid currency override");
        Cm(p, "Starting Options", "AdditionalEquipment", "a=-1");
        Check(p.StartOptions.EquipmentQuantities.Count == 0 && p.Warnings.Count == 4 && p.Warnings[3] == "Ignored invalid equipment quantity.", "CM invalid equipment quantity");
        Cm(p, "Compatibility", "PortPoolOverrides", "1|x|Nope");
        Check(p.Warnings.Count == 5 && p.Warnings[4] == "Invalid or duplicate port pool override: 1|x|Nope", "CM invalid override");
        Cm(p, "Selection", "RandomBoat", true);
        Cm(p, "Starting Reputation Overrides", "AlAnkh", 2);
        Check(p.Warnings.Count == 5, "unrelated CM changes repeated existing warnings: " + string.Join(" / ", p.Warnings));
        Check(p.Errors.Count == 0, "reload raised errors");
        // Startup from the file Configuration Manager saved: same values, same warnings.
        var copy = NewPath("invalid-copy");
        File.Copy(path, copy);
        var startup = Plugin.Start(copy);
        Check(Dump(startup) == Dump(p), "runtime reload differs from startup for the same entries\n runtime " + Dump(p) + "\n startup " + Dump(startup));
        Check(new HashSet<string>(startup.Warnings).SetEquals(p.Warnings), "runtime warnings differ from startup warnings");
        Plugin.Instance = p;
        Cm(p, "Starting Options", "CurrencyMultiplier", 2f);
        Check(p.StartOptions.CurrencyMultiplier == 2f && p.Warnings.Count == 5, "valid multiplier not applied quietly");
        Cm(p, "Starting Options", "CurrencyMultiplier", -1f);
        Check(p.Warnings.Count == 6 && p.Warnings[5] == "Invalid starting currency multiplier; using 1x.", "a reintroduced problem was not reported again");
        // A later menu save writes the same normalized pool the startup save would.
        p.MenuSave();
        Check((bool)Get(p, "Random Port Pools", "AlAnkh"), "menu save did not write the startup pool fallback");
    }

    private static void HandEditedFileReload()
    {
        var path = NewPath("hand");
        var p = Plugin.Start(path);
        p.Warnings.Clear();
        var text = File.ReadAllText(path);
        text = SetLine(text, "Random Port Pools", "AlAnkh", "false");
        text = SetLine(text, "Random Port Pools", "EmeraldArchipelago", "false");
        text = SetLine(text, "Random Port Pools", "Aestrin", "false");
        text = SetLine(text, "Random Port Pools", "FireFishLagoon", "true");
        text = SetLine(text, "Selection", "RandomPort", "true");
        text = SetLine(text, "Starting Options", "CurrencyMultiplier", "3");
        text = SetLine(text, "Individual Random Exclusions", "Ports", "Neverdin");
        File.WriteAllText(path, text);
        var bytes = File.ReadAllBytes(path);
        p.ReloadConfigFile();
        Check(p.Settings.RandomPort && p.Settings.PortPools.SetEquals(new[] { PortPool.FireFishLagoon }) && p.StartOptions.CurrencyMultiplier == 3f && p.Exclusions.IsPortExcluded(Port("Neverdin")), "hand-edited file values not applied");
        Check(p.Warnings.Count == 0, "file reload reread per entry and reported a transient state: " + string.Join(" / ", p.Warnings));
        Check(p.ReloadEvents == 1, "file reload did not reread exactly once (" + p.ReloadEvents + ")");
        Check(File.ReadAllBytes(path).SequenceEqual(bytes), "file reload rewrote the hand-edited file");
        Check(p.Config.SaveOnConfigSet, "SaveOnConfigSet was not restored");
        p.ReloadConfigFile();
        Check(File.ReadAllBytes(path).SequenceEqual(bytes) && p.Warnings.Count == 0, "unchanged file reload rewrote or warned");
        Cm(p, "Selection", "RandomBoat", true);
        Check(File.ReadAllText(path).Contains("RandomBoat = true"), "CM change after a file reload was not saved");
        File.WriteAllText(path, SetLine(File.ReadAllText(path), "Starting Options", "CurrencyMultiplier", "500"));
        p.ReloadConfigFile();
        Check(p.StartOptions.CurrencyMultiplier == 1f && p.Warnings.SequenceEqual(new[] { "Invalid starting currency multiplier; using 1x." }), "invalid hand edit fallback/warning");
        File.Delete(path);
        p.ReloadConfigFile();
        Check(p.Settings.RandomPort && p.Errors.Count == 0 && p.Warnings.Count == 1 && p.Config.SaveOnConfigSet, "missing file reload changed settings or failed");
    }

    // Rule: an external change while a panel is open reloads that panel from
    // the saved values and discards its pending edits. Panel reload members are
    // extracted verbatim into MenuUiDouble/StartingOptionsUiDouble.
    private static (Plugin, MenuUiDouble) Menu(string name)
    {
        var p = Plugin.Start(NewPath(name));
        var menu = new MenuUiDouble
        {
            settings = p.Settings,
            individualExclusions = p.Exclusions,
            startingOptions = new StartingOptionsUiDouble()
        };
        p.SettingsReloaded += menu.Reloaded; // MenuUi subscribes while shown
        return (p, menu);
    }

    private static void ExclusionEditorReload()
    {
        var(p, menu) = Menu("editor");
        Cm(p, "Individual Random Exclusions", "Ports", "A");
        menu.OpenEditor();
        menu.editorDraft.SetPortExcluded(Port("B"), true); // pending edit
        menu.editorDraft.SetPortExcluded(Port("A"), false); // pending edit
        Cm(p, "Individual Random Exclusions", "Ports", "A;C");
        Check(menu.editorDraft.SavePorts() == "A;C" && !ReferenceEquals(menu.editorDraft, p.Exclusions) && menu.Refreshes > 0, "open exclusion editor was not reloaded from the saved values: " + menu.editorDraft.SavePorts());
        menu.editorDraft.SetBoatExcluded(Boat("X"), true); // edit after the reload
        menu.Done();
        Check((string)Get(p, "Individual Random Exclusions", "Ports") == "A;C" && (string)Get(p, "Individual Random Exclusions", "Boats") == "X", "editor Done after an external change wrote stale exclusions: " + Get(p, "Individual Random Exclusions", "Ports"));
        menu.OpenEditor();
        menu.editorDraft.SetBoatExcluded(Boat("Z"), true);
        Cm(p, "Individual Random Exclusions", "Boats", "Y");
        Check(menu.editorDraft.SaveBoats() == "Y", "open editor kept a pending boat edit over the external change");
        menu.Back();
        p.Settings.PortIndex = 4;
        p.MenuSave();
        Check((string)Get(p, "Individual Random Exclusions", "Boats") == "Y" && p.Exclusions.SaveBoats() == "Y", "Back or a later save lost the external boat exclusion");
        var refreshes = menu.Refreshes;
        Cm(p, "Individual Random Exclusions", "Boats", "W"); // editor closed
        Check(menu.editorDraft == null && menu.Refreshes == refreshes + 1, "closed editor gained a draft on reload");
    }

    private static void StartingOptionsPanelReload()
    {
        var(p, menu) = Menu("options");
        var panel = menu.startingOptions;
        Cm(p, "Starting Options", "AdditionalEquipment", "x=2");
        panel.Open();
        panel.draft.CurrencyMultiplier = 9f;
        panel.draft.EquipmentQuantities["y"] = 3;
        panel.amounts[0] = "100";
        panel.amounts[3] = "7";
        Cm(p, "Starting Options", "CurrencyMultiplier", 4f);
        Check(panel.draft.CurrencyMultiplier == 4f && StartingOptionsConfig.WriteEquipment(panel.draft) == "x=2" && panel.amounts.SequenceEqual(new[] { "", "", "", "" }) && panel.ShowTabCalls == 1 && !ReferenceEquals(panel.draft, p.StartOptions), "open starting options panel was not reloaded from the saved values");
        Cm(p, "Starting Currency Overrides", "AlAnkh", "50");
        Cm(p, "Starting Options", "AdditionalEquipment", "x=5");
        Check(panel.amounts.SequenceEqual(new[] { "50", "", "", "" }) && panel.draft.CurrencyOverrides[0] == 50 && StartingOptionsConfig.WriteEquipment(panel.draft) == "x=5", "currency text or equipment not reloaded");
        panel.amounts[1] = "20"; // edit after the reload
        Check(panel.Done(), "panel Done rejected valid values");
        Check((float)Get(p, "Starting Options", "CurrencyMultiplier") == 4f && (string)Get(p, "Starting Options", "AdditionalEquipment") == "x=5" && (string)Get(p, "Starting Currency Overrides", "AlAnkh") == "50" && (string)Get(p, "Starting Currency Overrides", "Emerald") == "20" && (string)Get(p, "Starting Currency Overrides", "Gold") == "", "panel Done after an external change wrote stale values");
        panel.Open();
        panel.draft.StandardSupplies = false;
        panel.amounts[2] = "3";
        Cm(p, "Starting Options", "StartingFactionReputation", 6);
        Check(panel.draft.StandardSupplies && panel.draft.StartingReputation == 6 && panel.amounts[2] == "", "open panel kept pending edits over the external change");
        panel.Back();
        p.SaveStartingOptions();
        Check((int)Get(p, "Starting Options", "StartingFactionReputation") == 6 && (bool)Get(p, "Starting Options", "StandardSupplies") && (string)Get(p, "Starting Currency Overrides", "Aestrin") == "", "Back or a later save lost the external change");
        var calls = panel.ShowTabCalls;
        Cm(p, "Starting Options", "CurrencyMultiplier", 2f); // panel closed
        Check(panel.draft == null && panel.ShowTabCalls == calls, "closed panel reacted to a reload");
    }
}
