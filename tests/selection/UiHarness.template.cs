using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using NewBeginnings;
using MenuUi = UiHarness;

class GameObject
{
    internal bool active;
    internal bool activeSelf => active;

    internal void SetActive(bool value) => active = value;
}

class Parent
{
    internal GameObject gameObject = new();
}

class Vector3
{
    internal static readonly Vector3 one = new();
    public static Vector3 operator *(Vector3 value, float scale) => value;
}

class Mathf
{
    internal static float Min(float a, float b) => Math.Min(a, b);
}

class Transform
{
    internal Parent parent = new();
    internal GameObject gameObject = new()
    {
        active = true
    };
    internal Vector3 localScale;
}

class TextMesh
{
    internal string text;
    internal int color;
    internal GameObject gameObject = new()
    {
        active = true
    };
    internal Transform transform = new();
}

class MenuUiButton
{
    internal Transform transform = new();
    internal bool unclickable;
    internal TextMesh text = new();
    internal string lookText, description;
}

// PRODUCTION_REPEAT
class UiHarness
{
    // Real panel navigation/draft lifetime; only visual refresh, scene objects and equipment discovery are doubled.
    class PanelHarness
    {
        internal UiHarness owner;
        internal bool IsOpen;
        internal StartingOptionsSettings draft;
        internal GameObject gameObject = new(), contentRoot = new(), moneyPage = new(), reputationPage = new(), equipmentPage = new(), spawnPage = new();
        internal int tab;
        internal readonly string[] amounts = new string[4];
        MenuUiButton moneyTab = new(), reputationTab = new(), equipmentTab = new(), spawnTab = new();
        TextMesh message = new();
        EquipmentEntry[] equipment = new EquipmentEntry[0];
        internal int vanillaEquipmentCount, page, focusedCurrency = -1, focusedEquipment = -1;
        internal bool selectAll;
        EquipmentButtonRepeat equipmentRepeat = new();
        void RefreshMoney()
        {
        }

        void RefreshReputation()
        {
        }

        void RefreshEquipment()
        {
        }
    // PRODUCTION_PANEL_MEMBERS
    }

    SelectionSettings settings = new();
    SelectionCatalog catalog = new();
    IndividualExclusions individualExclusions = IndividualExclusions.Load("", "");
    bool continueAvailable, editing, editingBoats, confirmWasActive, backWasActive, titleWasActive;
    const int Ink = 1, MutedInk = 2, EditorRowsPerPage = 4;
    int editorPage, saves;
    IndividualExclusions editorDraft;
    Plugin reloadSource;
    Action changed;
    GameObject editorRoot = new();
    Transform confirm = new(), back = new();
    TextMesh titleTemplate = new();
    PanelHarness startingOptions = new();
    readonly List<GameObject> mainControls = new();
    bool catalogUnavailable, built = true;
    void RefreshCatalog()
    {
        RefreshDisplay();
        if (editing)
            RefreshEditor();
    }

    TextMesh portValue = new(), boatValue = new(), status = new();
    GameObject portValuePanel = new();
    TextMesh editorTitle = new(), editorSummary = new();
    MenuUiButton editorIslands = new(), editorBoats = new(), editorPrevious = new(), editorNext = new();
    MenuUiButton[] editorRows = Enumerable.Range(0, 4).Select(_ => new MenuUiButton()).ToArray();
    MenuUiButton portPrevious = new(), portNext = new(), boatPrevious = new(), boatNext = new(), shipyardPortsOnly = new(), useShipyardBerth = new(), secretIslands = new(), includeModPorts = new(), randomPort = new(), randomBoat = new(), portExclusions = new(), boatExclusions = new();
    MenuUiButton[] portPools = Enumerable.Range(0, PoolOrder.Length).Select(_ => new MenuUiButton()).ToArray(), boatSizes = Enumerable.Range(0, 3).Select(_ => new MenuUiButton()).ToArray();
    static PortPool[] PoolOrder =
    {
        PortPool.AlAnkh,
        PortPool.Emerald,
        PortPool.Aestrin,
        PortPool.FireFishLagoon,
        PortPool.SmallIslands
    };
    static BoatSize[] SizeOrder =
    {
        BoatSize.Small,
        BoatSize.Medium,
        BoatSize.Large
    };
    static string[] PoolLabels =
    {
        "Al'Ankh",
        "Emerald",
        "Aestrin",
        "FFL",
        "Small Islands"
    };
    static TextMesh ButtonText(MenuUiButton b) => b?.text;
    static void SetButtonText(MenuUiButton b, string value) => b.text.text = value;
    void ApplyContinueState()
    {
    }

    void HideNativeRegionChoice()
    {
    }

    UiHarness()
    {
        changed = () => saves++;
        startingOptions.owner = this;
        mainControls.Add(portValuePanel);
        foreach (var b in new[] { portPrevious, portNext, boatPrevious, boatNext, randomPort, randomBoat, includeModPorts }.Concat(portPools).Concat(boatSizes))
            mainControls.Add(b.transform.parent.gameObject);
        foreach (var control in mainControls)
            control.SetActive(true);
        editorIslands.transform.parent.gameObject.SetActive(true);
        editorBoats.transform.parent.gameObject.SetActive(true);
    }

    // PRODUCTION_MEMBERS
    internal static void Run(Action<bool, string> check)
    {
        SubmenuNavigation(check);
        ConfigureBeforeRandom(check);
        var ordered = new UiHarness();
        ordered.catalog.Ports.AddRange(new[] { new PortChoice { Index = 101, Key = "mod:region", DisplayName = "A mod island", Pool = PortPool.AlAnkh, IsModded = true }, new PortChoice { Index = 21, Key = "native:chronos", IdentityName = "port 21 M chronos", DisplayName = "Chronos", Pool = PortPool.Chronos }, new PortChoice { Index = 102, Key = "mod:site", DisplayName = "Fort Juniper", IsModded = true }, new PortChoice { Index = 9, Key = "native:emerald", DisplayName = "Dragon Cliffs", Pool = PortPool.Emerald }, new PortChoice { Index = 1, Key = "native:zulu", DisplayName = "Zulu", Pool = PortPool.AlAnkh }, new PortChoice { Index = 103, Key = "mod:second", DisplayName = "Z mod island", Pool = PortPool.AlAnkh, IsModded = true }, new PortChoice { Index = 32, Key = "native:pirate", DisplayName = "Pirate Island", Pool = PortPool.SmallIslands, HiddenKey = "pirate" }, new PortChoice { Index = 2, Key = "native:alpha", DisplayName = "Alpha", Pool = PortPool.AlAnkh }, new PortChoice { Index = 40, Key = "native:unclassified", DisplayName = "Unclassified" } });
        ordered.catalog.SortPorts();
        ordered.settings.PortPools.Add(PortPool.Chronos);
        var expected = new[]
        {
            "native:alpha",
            "native:zulu",
            "native:emerald",
            "native:chronos",
            "native:unclassified",
            "mod:region",
            "mod:second",
            "mod:site"
        };
        ordered.settings.DestinationKey = expected[0];
        ordered.settings.PortIndex = 2;
        foreach (var key in expected.Skip(1).Concat(expected.Take(1)))
        {
            ordered.CyclePort(1);
            check(ordered.settings.DestinationKey == key, "manual cycle must keep native geography/name groups before all mod sites: " + key);
        }

        ordered.CyclePort(-1);
        check(ordered.settings.DestinationKey == "mod:site", "manual previous must wrap to last mod site");
        check(ordered.EditorPorts().Select(p => p.Key).SequenceEqual(expected.Where(k => k != "native:unclassified")), "exclusions must keep native then mod ordering and preserve manual-only filtering");
        ordered.settings.EnablePirateIsland = true;
        check(ordered.EditorPorts().Select(p => p.Key).SequenceEqual(new[] { "native:alpha", "native:zulu", "native:emerald", "native:pirate", "native:chronos", "mod:region", "mod:second", "mod:site" }), "enabling a native spoiler must retain native geography before all mod sites");
        foreach (bool random in new[] { false, true })
            foreach (bool preset in new[] { false, true })
                foreach (bool boat in new[] { false, true })
                    foreach (int page in new[] { 0, 1, 2, 3 })
                    {
                        var ui = new UiHarness();
                        ui.settings.RandomPort = random;
                        ui.settings.ShipyardPortsOnly = preset;
                        ui.settings.UseShipyardBerth = preset;
                        ui.settings.RandomBoat = boat;
                        ui.editing = page == 2;
                        ui.startingOptions.IsOpen = page != 0;
                        ui.startingOptions.tab = page == 3 ? 0 : 3;
                        var filters = page == 1;
                        var main = page == 0;
                        ui.RefreshDisplay();
                        check(ui.shipyardPortsOnly.transform.parent.gameObject.active == filters && !ui.shipyardPortsOnly.unclickable, "shipyard filter must remain editable before enabling random selection");
                        check(ui.useShipyardBerth.transform.parent.gameObject.active == filters && !ui.useShipyardBerth.unclickable, "shipyard berth toggle must stay independent of random/preset selection");
                        check(ui.useShipyardBerth.text.text == (preset ? "[x] Use shipyard berth" : "[ ] Use shipyard berth"), "shipyard berth display must reflect the saved setting");
                        check(ui.secretIslands.transform.parent.gameObject.active == filters && !ui.secretIslands.unclickable, "secret toggle must only show on Spawn and remain independent of random modes");
                        check(ui.portValuePanel.active == main && ui.portPrevious.transform.parent.gameObject.active == main && ui.portNext.transform.parent.gameObject.active == main, "full manual port row visibility mismatch");
                        check(!ui.portExclusions.unclickable && !ui.boatExclusions.unclickable, "both exclusion editors must remain enabled regardless of random modes or shipyard filter");
                        check(ui.portPools.All(b => b.unclickable == !random), "region filters disabled by preset");
                        check(ui.portExclusions.transform.parent.gameObject.active == filters && ui.boatExclusions.transform.parent.gameObject.active == filters, "both editor entries must remain present on Spawn only");
                        if (random)
                        {
                            ui.TogglePortPool(PortPool.FireFishLagoon);
                            check(ui.settings.PortPools.Contains(PortPool.FireFishLagoon), "preset prevented FFL toggle");
                        }

                        ui.editing = false;
                        ui.startingOptions.IsOpen = false;
                        ui.RefreshDisplay();
                        check(!ui.shipyardPortsOnly.transform.parent.gameObject.active && ui.portValuePanel.active, "main-page reopen visibility mismatch");
                    }

        var manual = new UiHarness();
        manual.catalog.Ports.Add(new PortChoice { Index = 2, Key = "native:2", DisplayName = "Neverdin" });
        manual.catalog.Ports.Add(new PortChoice { Index = 9, Key = "native:9", DisplayName = "Dragon Cliffs" });
        manual.settings.PortIndex = 2;
        manual.settings.ShipyardPortsOnly = true;
        manual.RefreshDisplay();
        check(!manual.portPrevious.unclickable && !manual.portNext.unclickable && manual.portValue.text == "Neverdin", "manual arrows unavailable with preset stored");
        manual.CyclePort(1);
        check(manual.settings.PortIndex == 9 && manual.portValue.text == "Dragon Cliffs", "manual next failed");
        manual.settings.RandomPort = true;
        manual.RefreshDisplay();
        check(manual.portPrevious.unclickable && manual.portNext.unclickable && manual.portValuePanel.active, "random mode should keep disabled manual row visible");
        manual.CyclePort(-1);
        check(manual.settings.PortIndex == 9, "random mode changed remembered manual port");
        manual.editing = true;
        manual.RefreshDisplay();
        manual.editing = false;
        manual.RefreshDisplay();
        check(manual.portValuePanel.active && !manual.shipyardPortsOnly.transform.parent.gameObject.active, "editor return lost controls");
        manual.settings.RandomPort = false;
        manual.RefreshDisplay();
        manual.CyclePort(-1);
        check(manual.settings.PortIndex == 2 && !manual.portPrevious.unclickable && !manual.portNext.unclickable && !manual.shipyardPortsOnly.unclickable, "manual mode not restored after random toggle");
        var sites = new UiHarness();
        sites.catalog.Ports.Add(new PortChoice { Index = 21, Key = "native:21", IdentityName = "port 21 M chronos", DisplayName = "Chronos" });
        sites.catalog.Ports.Add(new PortChoice { Index = 21, Key = "provider:fort", SiteIdentity = "provider:fort", DisplayName = "Fort Juniper", IsModded = true });
        sites.catalog.Ports.Add(new PortChoice { Index = 33, Key = "native:saffron", DisplayName = "Saffron", HiddenKey = "saffron" });
        sites.settings.PortIndex = 21;
        sites.settings.PortPools.Add(PortPool.Chronos);
        sites.RefreshDisplay();
        check(sites.portValue.text == "Chronos", "legacy port index did not retain ordinary Chronos");
        sites.CyclePort(1);
        check(sites.settings.DestinationKey == "provider:fort" && sites.settings.PortIndex == 21 && sites.portValue.text == "Fort Juniper", "same-port site lost distinct identity");
        sites.CyclePort(1);
        check(sites.settings.DestinationKey == "native:21", "manual cycle exposed filtered hidden destination");
        sites.settings.DestinationKey = "provider:absent";
        sites.RefreshDisplay();
        check(sites.portValue.text == "Unavailable" && sites.settings.DestinationKey == "provider:absent", "display silently changed missing provider selection");
        sites.catalog.Ports.RemoveAt(1);
        sites.CyclePort(1);
        check(sites.settings.DestinationKey == "native:21", "could not explicitly replace unavailable provider with sole visible location");
        sites.catalog.Ports.Add(new PortChoice { Key = "mod:x", IsModded = true });
        sites.settings.RandomPort = true;
        sites.settings.PortPools.Remove(PortPool.Chronos);
        sites.RefreshDisplay();
        check(sites.includeModPorts.transform.parent.gameObject.active && !sites.includeModPorts.unclickable, "mod checkbox absent with detected mod location");
        sites.ToggleModPorts();
        foreach (var pool in PoolOrder)
            sites.TogglePortPool(pool);
        // Toggle removed initially checked pools and added initially unchecked ones.
        foreach (var pool in sites.settings.PortPools.ToArray())
            sites.TogglePortPool(pool);
        check(sites.settings.IncludeModPorts && sites.settings.PortPools.Count == 0, "UI refused mod-only pool");
        sites.ToggleModPorts();
        check(sites.settings.IncludeModPorts && sites.includeModPorts.unclickable, "UI disabled last enabled pool");
        sites.TogglePortPool(PortPool.AlAnkh);
        sites.ToggleModPorts();
        check(!sites.settings.IncludeModPorts, "mod pool could not be disabled after enabling region");
        sites.editing = true;
        sites.RefreshDisplay();
        check(!sites.includeModPorts.transform.parent.gameObject.active, "mod checkbox visible over editor");
        sites.catalog.Ports.RemoveAll(p => p.IsModded);
        sites.editing = false;
        sites.RefreshDisplay();
        check(!sites.includeModPorts.transform.parent.gameObject.active, "mod checkbox stayed visible with no detected mods");
        var secrets = new UiHarness();
        secrets.settings.RandomPort = true;
        secrets.catalog.Ports.Add(new PortChoice { Key = "mod:site", IsModded = true });
        secrets.settings.PortPools.Clear();
        secrets.settings.PortPools.Add(PortPool.AlAnkh);
        secrets.settings.EnableSaffronIsland = true;
        secrets.RefreshDisplay();
        check(secrets.secretIslands.text.text == "[~] Secret islands" && !secrets.portPools[0].unclickable, "partial secret selection must be visible and permit deselecting the last geographic pool");
        secrets.TogglePortPool(PortPool.AlAnkh);
        check(secrets.settings.PortPools.Count == 0 && secrets.settings.AnySecretIslandsEnabled, "main pool toggle must allow secret-only starts");
        secrets.settings.IncludeModPorts = true;
        secrets.RefreshDisplay();
        secrets.ToggleModPorts();
        check(!secrets.settings.IncludeModPorts, "mod checkbox must allow deselection while a secret start remains enabled");
        secrets.ToggleSecretIslands();
        check(secrets.settings.SecretIslandsEnabled && secrets.secretIslands.text.text == "[x] Secret islands", "clicking a mixed secret group must enable all three");
        secrets.ToggleSecretIslands();
        check(!secrets.settings.AnySecretIslandsEnabled && secrets.secretIslands.text.text == "[ ] Secret islands", "clicking the checked secret group must turn off all three");
        var guarded = new UiHarness();
        guarded.catalogUnavailable = true;
        FixedStart.BlockStart = true;
        guarded.RefreshDisplay();
        check(!guarded.continueAvailable && guarded.status.text == "Starting blocked for fixture" && !guarded.CanStart(out var blocked) && blocked == "Starting blocked for fixture", "start guard did not block Continue and direct CanStart before native fallback");
        guarded.ShowCatalogUnavailable();
        check(!guarded.continueAvailable && guarded.status.text == "Starting blocked for fixture" && !guarded.CanStart(out _), "catalog error fallback bypassed persistent start guard");
        FixedStart.BlockStart = false;
        check(guarded.CanStart(out _), "released guard prevented ordinary native fallback");
        Console.WriteLine("PASS extracted production UI state matrix; GameObject activation and pointer parents modeled, no rendered-menu claim.");
    }

    private static void ConfigureBeforeRandom(Action<bool, string> check)
    {
        var plugin = Plugin.Instance;
        var ui = new UiHarness();
        ui.settings = plugin.Settings;
        ui.individualExclusions = plugin.Exclusions;
        ui.settings.RandomPort = false;
        ui.settings.RandomBoat = false;
        ui.settings.ShipyardPortsOnly = false;
        ui.settings.PortPools.Clear();
        ui.settings.PortPools.Add(PortPool.AlAnkh);
        ui.individualExclusions.ReplaceWith(IndividualExclusions.Load("", ""));
        ui.changed = () =>
        {
            ui.saves++;
            plugin.MenuSave();
        };
        var port = new PortChoice
        {
            Index = 2,
            Key = "native:2",
            IdentityName = "port2",
            DisplayName = "Shipyard",
            Pool = PortPool.AlAnkh,
            HasShipyard = true
        };
        var boat = new BoatChoice
        {
            Index = 10,
            IdentityName = "boat10",
            DisplayName = "Boat",
            Size = BoatSize.Small
        };
        var alternative = new BoatChoice
        {
            Index = 11,
            IdentityName = "boat11",
            DisplayName = "Other boat",
            Size = BoatSize.Small
        };
        ui.catalog.Ports.Add(port);
        ui.catalog.Boats.Add(boat);
        ui.catalog.Boats.Add(alternative);
        ui.settings.DestinationKey = port.Key;
        ui.settings.PortIndex = port.Index;
        ui.settings.BoatSceneIndex = boat.Index;
        ui.startingOptions.Open();
        ui.startingOptions.ShowTab(3);
        ui.ToggleShipyardPortsOnly();
        check(ui.settings.ShipyardPortsOnly && ui.saves == 1 && !ui.settings.RandomPort, "shipyard filter must save when Random Port is off without enabling it");
        ui.OpenEditor(false);
        ui.ToggleEditorRow(0);
        ui.ShowEditorTab(true);
        ui.ToggleEditorRow(0);
        ui.CloseEditor(true);
        check(ui.saves == 2 && ui.individualExclusions.IsPortExcluded(port) && ui.individualExclusions.IsBoatExcluded(boat) && ui.settings.ShipyardPortsOnly && !ui.settings.RandomPort && !ui.settings.RandomBoat, "both exclusion tabs must save in manual mode with shipyard-only on without changing those flags");
        plugin.ReloadConfigFile();
        check(ui.settings.ShipyardPortsOnly && !ui.settings.RandomPort && !ui.settings.RandomBoat && ui.individualExclusions.IsPortExcluded(port) && ui.individualExclusions.IsBoatExcluded(boat), "manual-mode spawn configuration must survive config reload");
        check(ui.catalog.TryChoose(ui.settings, ui.individualExclusions, new Random(1), out var chosen, out _) && chosen.Port == port && chosen.Boat == boat, "saved random exclusions must not alter the manual start");
        ui.settings.RandomPort = true;
        ui.settings.RandomBoat = true;
        check(ui.catalog.TryChoose(ui.settings, ui.individualExclusions, new Random(1), out chosen, out _) && chosen.Port == port && chosen.Boat == alternative, "enabling Random later must apply saved boat exclusions and retain shipyard-only island exclusion policy");
        ui.ToggleShipyardPortsOnly();
        check(!ui.catalog.TryChoose(ui.settings, ui.individualExclusions, new Random(1), out _, out _) && ui.individualExclusions.IsPortExcluded(port), "disabling shipyard-only later must apply the saved island exclusion");
        ui.startingOptions.Close(false);
    }

    private static void SubmenuNavigation(Action<bool, string> check)
    {
        var plugin = Plugin.Instance;
        plugin.StartOptions.CurrencyMultiplier = 1f;
        plugin.StartOptions.StartingReputation = 0;
        plugin.StartOptions.EquipmentQuantities.Clear();
        var ui = new UiHarness();
        ui.settings = plugin.Settings;
        ui.individualExclusions = plugin.Exclusions;
        ui.settings.RandomPort = true;
        ui.settings.RandomBoat = true;
        ui.settings.ShipyardPortsOnly = false;
        ui.individualExclusions.ReplaceWith(IndividualExclusions.Load("", ""));
        ui.changed = () =>
        {
            ui.saves++;
            plugin.MenuSave();
        };
        plugin.SettingsReloaded += ui.OnSettingsReloaded;
        var port = new PortChoice
        {
            Index = 2,
            Key = "native:2",
            IdentityName = "port2",
            DisplayName = "Native port",
            Pool = PortPool.AlAnkh
        };
        var boat = new BoatChoice
        {
            Index = 10,
            IdentityName = "boat10",
            DisplayName = "Native boat",
            Size = BoatSize.Small
        };
        ui.catalog.Ports.Add(port);
        ui.catalog.Boats.Add(boat);
        ui.RefreshDisplay();
        var panel = ui.startingOptions;
        ui.OpenEditor(false);
        check(!ui.editing, "exclusions must open from the Starting options Spawn tab only");
        panel.Open();
        panel.Open();
        check(panel.IsOpen && panel.contentRoot.active && ui.mainControls.All(c => !c.active) && !ui.confirm.gameObject.active && !ui.back.gameObject.active && !ui.titleTemplate.gameObject.active, "Starting options must hide main controls and native actions");
        panel.draft.CurrencyMultiplier = 7f;
        panel.amounts[0] = "123";
        panel.draft.StartingReputation = 4;
        panel.draft.EquipmentQuantities["fixture-item"] = 2;
        var draft = panel.draft;
        panel.ShowTab(3);
        check(panel.SpawnTabOpen && !panel.moneyPage.active && !panel.reputationPage.active && !panel.equipmentPage.active && panel.spawnPage.active, "fourth tab must show Spawn alone");
        check(!ui.CanStart(out _), "native start must remain blocked while Starting options is open");
        ui.settings.UseShipyardBerth = !ui.settings.UseShipyardBerth;
        ui.Changed();
        check(ui.saves == 1 && ReferenceEquals(panel.draft, draft) && panel.amounts[0] == "123" && plugin.StartOptions.CurrencyMultiplier == 1f, "immediate selection save must not reset or commit Starting options draft");
        ui.OpenEditor(false);
        ui.ToggleEditorRow(0);
        check(ui.editorRoot.active && !panel.contentRoot.active && panel.IsOpen && panel.gameObject.active && ui.editorDraft.IsPortExcluded(port) && !ui.individualExclusions.IsPortExcluded(port), "nested editor must hide only panel content and keep exclusion draft separate");
        ui.RefreshDisplay();
        check(ui.mainControls.All(c => !c.active) && !ui.portExclusions.transform.parent.gameObject.active, "refresh must not reveal hidden main/spawn targets");
        ui.CloseEditor(false);
        check(panel.contentRoot.active && panel.SpawnTabOpen && !ui.editorRoot.active && !ui.individualExclusions.IsPortExcluded(port) && ui.saves == 1 && !ui.confirm.gameObject.active, "Back must discard exclusion draft and return to the Spawn tab");
        ui.OpenEditor(false);
        ui.ToggleEditorRow(0);
        ui.CloseEditor(true);
        check(ui.individualExclusions.IsPortExcluded(port) && ui.saves == 2 && panel.contentRoot.active && panel.SpawnTabOpen && !ui.confirm.gameObject.active, "Done must save exclusions and return to the Spawn tab");
        check(ReferenceEquals(panel.draft, draft) && draft.CurrencyMultiplier == 7f && draft.StartingReputation == 4 && draft.EquipmentQuantities["fixture-item"] == 2 && panel.amounts[0] == "123" && plugin.StartOptions.CurrencyMultiplier == 1f, "both exclusion returns must preserve the pending Money, Reputation and Equipment draft");
        foreach (var tab in new[] { 0, 1, 2, 3 })
        {
            panel.ShowTab(tab);
            check(ReferenceEquals(panel.draft, draft) && !ui.confirm.gameObject.active && ui.portExclusions.transform.parent.gameObject.active == (tab == 3), "tab changes must preserve draft and hide other tab pointer targets");
        }

        panel.Close(true);
        check(!panel.IsOpen && panel.draft == null && plugin.StartOptions.CurrencyMultiplier == 7f && plugin.StartOptions.CurrencyOverrides[0] == 123 && plugin.StartOptions.StartingReputation == 4 && plugin.StartOptions.EquipmentQuantities["fixture-item"] == 2, "parent Done must commit the retained draft exactly when finishing Starting options");
        check(ui.mainControls.Where(c => c != ui.includeModPorts.transform.parent.gameObject).All(c => c.active) && ui.confirm.gameObject.active && ui.back.gameObject.active && ui.titleTemplate.gameObject.active, "parent Done must restore main and native actions");
        panel.Open();
        panel.ShowTab(3);
        ui.settings.RandomPort = false;
        ui.settings.UseShipyardBerth = true;
        ui.OnSettingsReloaded();
        check(panel.contentRoot.active && panel.SpawnTabOpen && !ui.shipyardPortsOnly.unclickable && !ui.useShipyardBerth.unclickable && ui.useShipyardBerth.text.text == "[x] Use shipyard berth" && !ui.confirm.gameObject.active, "configuration reload must update Spawn options in place without exposing native actions");
        panel.draft.CurrencyMultiplier = 9f;
        ui.settings.RandomPort = true;
        ui.OpenEditor(false);
        ui.ToggleEditorRow(0);
        var reloads = plugin.ReloadEvents;
        plugin.Config[new BepInEx.Configuration.ConfigDefinition("Starting Options", "CurrencyMultiplier")].BoxedValue = 8f;
        check(plugin.ReloadEvents == reloads + 1 && ui.editing && !panel.contentRoot.active && panel.IsOpen && panel.draft.CurrencyMultiplier == 8f && ui.editorDraft.IsPortExcluded(port) && !ui.confirm.gameObject.active, "real Configuration Manager-style setting event must replace both saved drafts while retaining nested editor navigation");
        ui.settings.RandomPort = false;
        ui.OnSettingsReloaded();
        check(ui.editing && !ui.editingBoats && ui.editorTitle.text == "Random Island Exclusions", "disabling Random Port externally must retain the current exclusion editor");
        ui.settings.RandomBoat = false;
        ui.OnSettingsReloaded();
        check(ui.editing && !panel.contentRoot.active && panel.SpawnTabOpen && ui.editorRoot.active && !ui.confirm.gameObject.active && ui.saves == 2, "disabling both random modes externally must keep the editor open without saving");
        ui.ShowEditorTab(true);
        check(ui.editingBoats && ui.editorTitle.text == "Random Boat Exclusions", "both editor tabs must work while Random is off");
        ui.CloseEditor(false);
        ui.settings.RandomPort = true;
        ui.settings.RandomBoat = true;
        panel.draft.CurrencyMultiplier = 11f;
        ui.OpenEditor(true);
        ui.ToggleEditorRow(0);
        ui.OnDisable();
        check(!panel.IsOpen && panel.draft == null && !ui.editing && !panel.gameObject.active && !ui.editorRoot.active && !ui.individualExclusions.IsBoatExcluded(boat) && ui.saves == 2 && plugin.StartOptions.CurrencyMultiplier == 8f, "menu disable must discard parent and exclusion drafts and close both");
        panel.Open();
        panel.ShowTab(3);
        ui.OpenEditor(true);
        panel.OnDisable();
        check(!panel.IsOpen && !ui.editing && !ui.editorRoot.active && ui.confirm.gameObject.active, "panel disable while nested must close child and restore native actions");
        ui.confirm.gameObject.SetActive(false);
        ui.back.gameObject.SetActive(false);
        ui.titleTemplate.gameObject.SetActive(false);
        panel.Open();
        panel.ShowTab(3);
        ui.OpenEditor(false);
        ui.CloseEditor(false);
        panel.Close(false);
        check(!ui.confirm.gameObject.active && !ui.back.gameObject.active && !ui.titleTemplate.gameObject.active, "submenu return must preserve originally hidden native controls");
        plugin.SettingsReloaded -= ui.OnSettingsReloaded;
    }
}
