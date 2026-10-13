using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    // GoPointer resolves the button on the hit collider, which is the native
    // "bg+trigger" child. This component is attached there, not to the visual root.
    internal sealed class MenuUiButton : GoPointerButton
    {
        private static readonly FieldInfo OutlineField = AccessTools.Field(typeof(GoPointerButton), "outline");
        internal Action Clicked;
        internal Action<RaycastHit> Hit;

        public override void Start()
        {
            // The source button may already have its runtime outline when cloned.
            // GoPointerButton.UpdateColor reads its private outline field, so reuse
            // that component and initialize this instance's field explicitly.
            var existing = GetComponent("Outline");
            if (existing == null) base.Start();
            else OutlineField?.SetValue(this, existing);
        }

        public override void OnActivateHit(RaycastHit hit)
        {
            if (!unclickable) Hit?.Invoke(hit);
        }

        public override void OnActivate()
        {
            if (!unclickable) Clicked?.Invoke();
        }
    }

    internal sealed class MenuUi : MonoBehaviour
    {
        private static readonly FieldInfo ChoiceUiField = AccessTools.Field(typeof(StartMenu), "chooseIslandUI");
        private StartMenu menu;
        private SelectionSettings settings;
        private Func<SelectionOverrides> overrides;
        private IndividualExclusions individualExclusions;
        private IndividualExclusions editorDraft;
        private Plugin reloadSource;
        private Action changed;
        private SelectionCatalog catalog;
        private GameObject buttonTemplate;
        private TextMesh titleTemplate;
        private Transform panel;
        private Transform confirm;
        private Transform back;
        private StartMenuButton nativeConfirm;
        private bool continueAvailable = true;
        private bool catalogUnavailable;
        private bool catalogFaultLogged;
        private TextMesh portValue;
        private GameObject portValuePanel;
        private TextMesh boatValue;
        private TextMesh status;
        private MenuUiButton randomPort;
        private MenuUiButton shipyardPortsOnly;
        private MenuUiButton useShipyardBerth;
        private MenuUiButton includeModPorts;
        private MenuUiButton randomBoat;
        private MenuUiButton portPrevious;
        private MenuUiButton portNext;
        private MenuUiButton boatPrevious;
        private MenuUiButton boatNext;
        private MenuUiButton[] portPools;
        private MenuUiButton[] boatSizes;
        private MenuUiButton portExclusions;
        private MenuUiButton boatExclusions;
        private readonly List<GameObject> mainControls = new List<GameObject>();
        private GameObject spawnRoot;
        private bool spawnOptionsOpen;
        private GameObject editorRoot;
        private TextMesh editorTitle;
        private TextMesh editorSummary;
        private MenuUiButton editorIslands;
        private MenuUiButton editorBoats;
        private MenuUiButton[] editorRows;
        private MenuUiButton editorPrevious;
        private MenuUiButton editorNext;
        private StartingOptionsUi startingOptions;
        private bool EditingAny => spawnOptionsOpen || editing || startingOptions != null && startingOptions.IsOpen;
        private bool editing;
        private bool editingBoats;
        private bool confirmWasActive;
        private bool backWasActive;
        private bool titleWasActive;
        private int editorPage;
        private bool built;
        private bool seasLayout;
        private float nextDiscoveryRefresh;
        private int registrationRevision = -1;
        internal const float StartingOptionsOffsetY = .35f;
        private const float VerticalOffset = 1.15f;
        private const float SeasMenuOffsetX = 1.12f;
        private const float StandaloneMenuOffsetX = .18f;
        private const float SeasContentWidth = .90f;
        private const int EditorRowsPerPage = 4;

        // Chronos is a spoiler pool, set only from the Configuration Manager spoiler drawer.
        private static readonly PortPool[] PoolOrder =
            { PortPool.AlAnkh, PortPool.Emerald, PortPool.Aestrin,
              PortPool.FireFishLagoon, PortPool.SmallIslands };
        private static readonly BoatSize[] SizeOrder =
            { BoatSize.Small, BoatSize.Medium, BoatSize.Large };
        private static readonly string[] PoolLabels =
            { "Al'Ankh", "Emerald", "Aestrin", "FFL", "Small Islands" };
        private static readonly Color Ink = new Color(.06f, .035f, .02f, 1f);
        private static readonly Color MutedInk = new Color(.30f, .23f, .19f, 1f);
        private static readonly Color WarningInk = new Color(.30f, .055f, .025f, 1f);

        // Call from a StartMenu.Awake postfix ordered after Scrambled Seas' UI
        // postfix. RefreshCatalog may also be called when the menu is reopened.
        internal static MenuUi Attach(StartMenu startMenu, SelectionSettings current,
            Func<SelectionOverrides> currentOverrides, IndividualExclusions randomExclusions,
            Action onChanged)
        {
            var choice = startMenu != null ? ChoiceUiField?.GetValue(startMenu) as GameObject : null;
            if (choice == null || current == null)
                return null;
            // StartMenu.Start hides this panel with a 0.2-second fade, after
            // our Awake postfix has populated it. Conceal the whole panel now
            // so its controls cannot flash during the initial main-menu fade.
            // EnableIslandMenu later reactivates it for a chosen new game.
            choice.SetActive(false);
            var ui = choice.GetComponent<MenuUi>() ?? choice.AddComponent<MenuUi>();
            ui.Initialize(startMenu, current, currentOverrides, randomExclusions, onChanged);
            return ui;
        }

        private void Initialize(StartMenu startMenu, SelectionSettings current,
            Func<SelectionOverrides> currentOverrides, IndividualExclusions randomExclusions,
            Action onChanged)
        {
            menu = startMenu;
            settings = current;
            overrides = currentOverrides;
            individualExclusions = randomExclusions;
            changed = onChanged;
            seasLayout = ScrambledSeasIntegration.IsInstalled;
            if (!built)
            {
                var choiceRoot = (ChoiceUiField.GetValue(menu) as GameObject).transform;
                var confirm = choiceRoot.Find("button confirm");
                var title = choiceRoot.Find("text title");
                var confirmButton = confirm != null
                    ? confirm.GetComponentInChildren<StartMenuButton>(true) : null;
                if (confirm == null || title == null ||
                    confirmButton == null ||
                    title.GetComponent<TextMesh>() == null)
                    throw new InvalidOperationException("Sailwind's starting menu button/text templates were not found.");
                buttonTemplate = confirm.gameObject;
                nativeConfirm = confirmButton;
                titleTemplate = title.GetComponent<TextMesh>();
                UseNativeButtonFont(titleTemplate, confirm.Find("text")?.GetComponent<TextMesh>());
                this.confirm = confirm;
                back = choiceRoot.Find("button back");
                panel = choiceRoot.Find("bg");
                Build(choiceRoot);
                foreach (Transform child in choiceRoot)
                    if (child.name.StartsWith("New Beginnings ", StringComparison.Ordinal))
                        mainControls.Add(child.gameObject);
                BuildSpawnOptions(choiceRoot);
                BuildEditor(choiceRoot);
                startingOptions = StartingOptionsUi.Create(this, choiceRoot);
                built = true;
            }
            HideNativeRegionChoice();
            RefreshCatalog();
        }

        private void OnEnable()
        {
            var plugin = Plugin.Instance;
            if (plugin != null)
            {
                // Pick up hand edits to the file; Configuration Manager changes
                // arrive through SettingsReloaded while the menu is shown.
                plugin.ReloadConfigFile();
                plugin.SettingsReloaded -= OnSettingsReloaded;
                plugin.SettingsReloaded += OnSettingsReloaded;
                reloadSource = plugin;
            }
            if (built && settings != null)
            {
                HideNativeRegionChoice();
                RefreshCatalog();
            }
        }

        private void OnDisable()
        {
            if (reloadSource != null) reloadSource.SettingsReloaded -= OnSettingsReloaded;
            reloadSource = null;
            if (editing) CloseEditor(false);
            CloseSpawnOptions();
            startingOptions?.Close(false);
        }

        // Settings changed outside the menu: an open panel reloads from the saved values and discards its pending edits.
        private void OnSettingsReloaded()
        {
            if (!built || settings == null) return;
            if (editing && individualExclusions != null) editorDraft = individualExclusions.Copy();
            startingOptions?.ApplyExternalChange();
            RefreshCatalog();
        }

        private void Update()
        {
            if (!built) return;
            // Optional providers can initialize after the menu opens. Keep the
            // catalog live only while this panel is active, without saving settings.
            if (registrationRevision != StartPorts.Revision || Time.realtimeSinceStartup >= nextDiscoveryRefresh)
            {
                RefreshCatalog();
            }
            HideNativeRegionChoice();
            ApplyContinueState();
            if (EditingAny)
            {
                if (confirm != null) Hide(confirm);
                if (back != null) Hide(back);
            }
        }

        // Catalog discovery is deliberately refreshed at each menu opening:
        // Port/Recovery registration and boat purchase UIs may not exist in Awake.
        internal void RefreshCatalog()
        {
            if (!built || settings == null) return;
            try
            {
                catalog = SelectionCatalog.Discover(overrides?.Invoke());
                if (catalog.Ports.Count == 0 || catalog.Boats.Count == 0)
                {
                    var reason = catalog.Rejections.FirstOrDefault() ??
                        "No playable ports or boats were available.";
                    ShowCatalogUnavailable();
                    // The first refresh runs from StartMenu.Awake while this
                    // panel is hidden and before ports register. Only an empty
                    // catalog on a shown menu is worth a warning.
                    if (!gameObject.activeInHierarchy)
                    {
                        Plugin.Instance?.DebugLog("New Beginnings catalog not ready at menu creation; it refreshes when the start menu opens. " + reason);
                    }
                    else if (!catalogFaultLogged)
                    {
                        catalogFaultLogged = true;
                        Plugin.Instance?.Warn("New Beginnings catalog has no usable start; Sailwind's normal start remains available. " + reason);
                    }
                    return;
                }
                RefreshDisplay();
                if (editing) RefreshEditor();
                catalogUnavailable = false;
            }
            catch (Exception exception)
            {
                ShowCatalogUnavailable();
                if (catalogFaultLogged) return;
                catalogFaultLogged = true;
                Plugin.Instance?.Error("New Beginnings catalog is unavailable; Sailwind's normal start remains available.", exception);
            }
            finally
            {
                // Opening the panel or changing settings already refreshed it.
                // Do not repeat discovery during the same opening animation.
                registrationRevision = StartPorts.Revision;
                nextDiscoveryRefresh = Time.realtimeSinceStartup + 2f;
            }
        }

        internal bool CatalogUnavailable => catalogUnavailable;

        internal void ShowCatalogUnavailable()
        {
            catalog = null;
            catalogUnavailable = true;
            continueAvailable = !FixedStart.TryGetStartBlock(out var blocked);
            try
            {
                if (portValue != null) portValue.text = "Unavailable";
                if (boatValue != null) boatValue.text = "Unavailable";
                if (status != null) status.text = continueAvailable ?
                    "New Beginnings unavailable; Sailwind start" : Trim(blocked, 58);
                ApplyContinueState();
            }
            catch (Exception exception)
            {
                // Keep the native button path available even if a changed menu
                // no longer accepts one of these optional display updates.
                Plugin.Instance?.Warn("New Beginnings fallback display could not be updated: " +
                    exception.Message);
            }
        }

        internal bool CanStart(out string reason)
        {
            reason = null;
            if (FixedStart.TryGetStartBlock(out reason)) return false;
            if (EditingAny)
            {
                reason = "Finish editing your starting options before continuing.";
                return false;
            }
            if (Plugin.Instance?.StartOptions != null &&
                (!Plugin.Instance.StartOptions.TryValidate(out reason) ||
                 !AdditionalEquipment.TryValidateSelection(Plugin.Instance.StartOptions.EquipmentQuantities.Keys, out reason))) return false;
            RefreshCatalog();
            if (catalogUnavailable) return true;
            if (catalog != null && catalog.TryChoose(settings, individualExclusions,
                    new System.Random(1), out _, out reason)) return true;
            if (catalog == null) reason = "The start catalog is not ready.";
            return false;
        }

        internal static MenuUi ActiveFor(StartMenu startMenu)
        {
            var choice = startMenu != null
                ? ChoiceUiField?.GetValue(startMenu) as GameObject : null;
            if (choice == null || !choice.activeInHierarchy) return null;
            var ui = choice.GetComponent<MenuUi>();
            return ui != null && ui.built && ui.isActiveAndEnabled ? ui : null;
        }

        internal void ShowBlockedStart(string reason)
        {
            if (status != null) status.text = Trim(reason, 58);
            continueAvailable = false;
            ApplyContinueState();
        }

        private void ApplyContinueState()
        {
            if (nativeConfirm != null)
            {
                nativeConfirm.unclickable = !continueAvailable;
                nativeConfirm.lookText = continueAvailable ? "Continue" : "No valid start";
            }
            var text = confirm != null ? confirm.Find("text")?.GetComponent<TextMesh>() : null;
            if (text != null) text.color = continueAvailable ? Ink : MutedInk;
        }

        private void HideNativeRegionChoice()
        {
            var choice = menu != null ? ChoiceUiField?.GetValue(menu) as GameObject : null;
            if (choice == null) return;
            var root = choice.transform;
            Hide(root.Find("button left"));
            Hide(root.Find("button right"));
            Hide(root.Find("start deco Al'Ankh"));
            Hide(root.Find("start deco Emerald"));
            Hide(root.Find("start deco Medi"));
            var regionNameScroll = root.Find("bg (1)");
            var hasSaveNameInput = SaveSlotsPlusIntegration.TryLayoutNameInput(regionNameScroll, !EditingAny);
            if (!hasSaveNameInput)
                Hide(regionNameScroll);
            var actionOffset = hasSaveNameInput ? .12f : 0f;
            // The parchment mesh is rotated: its local Y is screen horizontal,
            // local Z is screen vertical, and local X is depth. The wider,
            // shorter sheet backs the selectors, leaving native actions below.
            if (panel != null)
            {
                var optionsOpen = startingOptions != null && startingOptions.IsOpen;
                // Reassert the backing on every refresh, including first open
                // after other menu initialization has changed its transform.
                panel.localPosition = new Vector3(MenuX(0f),
                    (optionsOpen ? StartingOptionsOffsetY : .30f) + VerticalOffset, panel.localPosition.z);
                panel.localScale = seasLayout
                    ? new Vector3(1.32f, 2.50f, optionsOpen ? 3.4f : 2.75f)
                    : new Vector3(1.32f, 3.45f, optionsOpen ? 3.4f : 2.75f);
            }
            // Keep the native button and StartMenuButton intact: it still calls
            // StartNewGame, whose prefix chooses the selected/random port pair.
            if (confirm != null)
            {
                confirm.localPosition = new Vector3(0f,
                    .10f - actionOffset,
                    confirm.localPosition.z);
                var text = confirm.Find("text")?.GetComponent<TextMesh>();
                if (text != null)
                {
                    text.text = "Continue";
                    text.color = continueAvailable ? Ink : MutedInk;
                }
            }
            if (back != null)
            {
                back.localPosition = new Vector3(0f,
                    -.23f - actionOffset,
                    back.localPosition.z);
                foreach (var text in back.GetComponentsInChildren<TextMesh>(true))
                    text.color = Ink;
            }
            if (titleTemplate != null)
            {
                titleTemplate.text = "New Beginnings";
                var position = titleTemplate.transform.localPosition;
                titleTemplate.transform.localPosition = new Vector3(
                    MenuX(0f),
                    .76f + VerticalOffset, position.z);
                titleTemplate.transform.localScale = Vector3.one * .017f;
                titleTemplate.color = Ink;
            }
        }

        private static void Hide(Transform target)
        {
            if (target != null && target.gameObject.activeSelf) target.gameObject.SetActive(false);
        }

        private static void UseNativeButtonFont(TextMesh text, TextMesh nativeText)
        {
            if (nativeText == null)
                throw new InvalidOperationException("Sailwind's native button font was not found.");
            // The installed title template uses italic IMMORTAL; native menu
            // buttons use its bold face. Copy the actual font/style and atlas
            // together so cloned headings and values match the game's controls.
            text.font = nativeText.font;
            text.fontStyle = nativeText.fontStyle;
            text.fontSize = nativeText.fontSize;
            text.characterSize = nativeText.characterSize;
            text.GetComponent<Renderer>().sharedMaterial = nativeText.GetComponent<Renderer>().sharedMaterial;
        }

        private void Build(Transform root)
        {
            MakeLabel(root, "Port heading", "Starting Port", -1.05f, .57f, .012f);
            MakeLabel(root, "Boat heading", "Starting Boat", 1.05f, .57f, .012f);
            randomPort = MakeButton(root, "Random port", -1.05f, .38f, .92f, .58f,
                () => { settings.RandomPort = !settings.RandomPort; Changed(); });
            randomBoat = MakeButton(root, "Random boat", 1.05f, .38f, .92f, .58f,
                () => { settings.RandomBoat = !settings.RandomBoat; Changed(); });

            portPrevious = MakeButton(root, "Previous port", -1.83f, .19f, .28f, .52f,
                () => CyclePort(-1));
            portValue = MakeValuePanel(root, "Selected port", -1.05f, .19f, out portValuePanel);
            portNext = MakeButton(root, "Next port", -.27f, .19f, .28f, .52f,
                () => CyclePort(1));
            boatPrevious = MakeButton(root, "Previous boat", .27f, .19f, .28f, .52f,
                () => CycleBoat(-1));
            boatValue = MakeValuePanel(root, "Selected boat", 1.05f, .19f, out _);
            boatNext = MakeButton(root, "Next boat", 1.83f, .19f, .28f, .52f,
                () => CycleBoat(1));

            var spawn = MakeButton(root, "Spawn options", -1.05f, -.08f, 1.65f, .52f,
                OpenSpawnOptions);
            SetButtonText(spawn, "Spawn options...");
            spawn.description = "Port pools, boat sizes, shipyard settings and individual exclusions.";
            var options = MakeButton(root, "Starting options", 1.05f, -.08f, 1.65f, .52f,
                () => startingOptions?.Open());
            SetButtonText(options, "Starting options...");
            options.description = "Starting money, reputation and equipment.";
            // Shown above the title, clear of the lower controls.
            status = MakeLabel(root, "Selection status", "", 0f, .88f, .009f);
            status.color = WarningInk;
        }

        private void BuildSpawnOptions(Transform root)
        {
            spawnRoot = new GameObject("New Beginnings spawn options");
            spawnRoot.transform.SetParent(root, false);
            spawnRoot.SetActive(false);
            var page = spawnRoot.transform;
            MakeLabel(page, "Spawn options title", "Spawn Options", 0f, .78f, .016f);
            MakeLabel(page, "Port pools heading", "Random Port Pools", -1.05f, .33f, .012f);
            MakeLabel(page, "Boat sizes heading", "Random Boat Sizes", 1.05f, .33f, .012f);
            shipyardPortsOnly = MakeButton(page, "Shipyard ports only", -1.05f, -.53f, 1.55f, .44f,
                () => { if (settings.RandomPort) { settings.ShipyardPortsOnly = !settings.ShipyardPortsOnly; Changed(); } });
            shipyardPortsOnly.description = "Detected shipyards in the enabled starting pools. Ignores individual island exclusions without changing them. Spoiler location options still apply.";
            useShipyardBerth = MakeButton(page, "Use shipyard berth", 1.05f, -.53f, 1.55f, .44f,
                () => { settings.UseShipyardBerth = !settings.UseShipyardBerth; Changed(); });
            useShipyardBerth.description = "Start at the selected shipyard's berth when one is available.";
            includeModPorts = MakeButton(page, "Include mod ports", -.41f, -.16f, .60f, .44f,
                ToggleModPorts);
            includeModPorts.description = "Include detected mod locations regardless of region. Select a geographic pool before turning off the last enabled pool.";
            var poolX = new[] { -1.69f, -1.05f, -.41f, -1.77f, -1.13f };
            var poolY = new[] { 0f, 0f, 0f, -.16f, -.16f };
            var poolWidth = new[] { .60f, .60f, .60f, .44f, .74f };
            portPools = new MenuUiButton[PoolOrder.Length];
            for (var i = 0; i < portPools.Length; ++i)
            {
                var index = i;
                portPools[i] = MakeButton(page, "Port pool " + PoolLabels[i],
                    poolX[i], poolY[i], poolWidth[i], .44f,
                    () => TogglePortPool(PoolOrder[index]));
            }
            var sizeX = new[] { .45f, 1.05f, 1.65f };
            boatSizes = new MenuUiButton[SizeOrder.Length];
            for (var i = 0; i < boatSizes.Length; ++i)
            {
                var index = i;
                boatSizes[i] = MakeButton(page, "Boat size " + SizeOrder[i],
                    sizeX[i], 0f, .55f, .44f,
                    () => ToggleBoatSize(SizeOrder[index]));
            }
            portExclusions = MakeButton(page, "Island exclusions", -1.05f, -.34f,
                1.55f, .44f, () => OpenEditor(false));
            boatExclusions = MakeButton(page, "Boat exclusions", 1.05f, -.34f,
                1.55f, .44f, () => OpenEditor(true));
            portExclusions.description = "Choose which islands can be selected by Random Port.";
            boatExclusions.description = "Choose which boats can be selected by Random Boat.";
            var done = MakeButton(page, "Back from spawn options", 0f, -.76f, .82f, .64f,
                CloseSpawnOptions);
            SetButtonText(done, "Back");
        }

        private void OpenSpawnOptions()
        {
            if (EditingAny || spawnRoot == null) return;
            spawnOptionsOpen = true;
            ShowSubmenu(true);
            spawnRoot.SetActive(true);
            RefreshDisplay();
        }

        private void CloseSpawnOptions()
        {
            if (!spawnOptionsOpen) return;
            if (editing) CloseEditor(false);
            spawnOptionsOpen = false;
            spawnRoot.SetActive(false);
            ShowSubmenu(false);
        }

        private void BuildEditor(Transform root)
        {
            editorRoot = new GameObject("New Beginnings exclusion editor");
            editorRoot.transform.SetParent(root, false);
            editorRoot.SetActive(false);
            var page = editorRoot.transform;
            editorTitle = MakeLabel(page, "Exclusion editor title", "Random Start Exclusions",
                0f, .78f, .016f);
            editorIslands = MakeButton(page, "Islands tab", -.55f, .54f, .82f, .48f,
                () => ShowEditorTab(false));
            editorBoats = MakeButton(page, "Boats tab", .55f, .54f, .82f, .48f,
                () => ShowEditorTab(true));
            editorSummary = MakeLabel(page, "Exclusion count", "", 0f, .35f, .010f);
            editorRows = new MenuUiButton[EditorRowsPerPage];
            for (var i = 0; i < editorRows.Length; ++i)
            {
                var row = i;
                editorRows[i] = MakeButton(page, "Exclusion row " + (i + 1),
                    0f, .18f - .16f * i, 3.25f, .43f, () => ToggleEditorRow(row));
            }
            editorPrevious = MakeButton(page, "Previous exclusions page", -1.18f,
                -.51f, .45f, .45f, () => ChangeEditorPage(-1));
            editorNext = MakeButton(page, "Next exclusions page", 1.18f,
                -.51f, .45f, .45f, () => ChangeEditorPage(1));
            var cancel = MakeButton(page, "Back from exclusions", -.55f, -.76f,
                .82f, .64f, () => CloseEditor(false));
            SetButtonText(cancel, "Back");
            cancel.description = "Discard changes and return to Spawn options.";
            var done = MakeButton(page, "Done editing exclusions", .55f, -.76f,
                .82f, .64f, () => CloseEditor(true));
            SetButtonText(done, "Done");
            done.description = "Save individual island and boat exclusions.";
        }

        internal void ShowStartingOptions(bool visible) => ShowSubmenu(visible);

        private void ShowSubmenu(bool visible)
        {
            if (visible)
            {
                confirmWasActive = confirm != null && confirm.gameObject.activeSelf;
                backWasActive = back != null && back.gameObject.activeSelf;
                titleWasActive = titleTemplate != null && titleTemplate.gameObject.activeSelf;
            }
            foreach (var control in mainControls) control.SetActive(!visible);
            if (confirm != null) confirm.gameObject.SetActive(!visible && confirmWasActive);
            if (back != null) back.gameObject.SetActive(!visible && backWasActive);
            if (titleTemplate != null) titleTemplate.gameObject.SetActive(!visible && titleWasActive);
            HideNativeRegionChoice();
            if (!visible) RefreshDisplay();
        }

        private bool CanEditExclusions(bool boats) => settings != null &&
            (boats ? settings.RandomBoat : settings.RandomPort && !settings.ShipyardPortsOnly);

        private void OpenEditor(bool boats)
        {
            if (!spawnOptionsOpen || editing || !CanEditExclusions(boats) ||
                individualExclusions == null || editorRoot == null) return;
            RefreshCatalog();
            // Edit a draft: Done applies it, any other close discards it.
            editorDraft = individualExclusions.Copy();
            editing = true;
            editingBoats = boats;
            editorPage = 0;
            spawnRoot.SetActive(false);
            // Set both tabs from the current random modes before activation,
            // including the first opening and every reopening of this page.
            RefreshEditor();
            editorRoot.SetActive(true);
            HideNativeRegionChoice();
        }

        private void CloseEditor(bool save)
        {
            if (!editing) return;
            editing = false;
            if (save)
            {
                individualExclusions?.ReplaceWith(editorDraft);
            }
            editorDraft = null;
            editorRoot.SetActive(false);
            spawnRoot.SetActive(spawnOptionsOpen);
            HideNativeRegionChoice();
            RefreshDisplay();
            if (save) changed?.Invoke();
        }

        private void ShowEditorTab(bool boats)
        {
            if (!editing || !CanEditExclusions(boats) || editingBoats == boats) return;
            editingBoats = boats;
            editorPage = 0;
            RefreshEditor();
        }

        private void ChangeEditorPage(int direction)
        {
            if (!editing) return;
            editorPage += direction;
            RefreshEditor();
        }

        private PortChoice[] EditorPorts() => catalog == null ? new PortChoice[0] :
            catalog.VisiblePorts(settings).Where(item => item.Pool.HasValue || item.IsModded).ToArray();

        private BoatChoice[] EditorBoats() => catalog == null ? new BoatChoice[0] :
            catalog.Boats.Where(item => item.Size.HasValue).ToArray();

        private void ToggleEditorRow(int row)
        {
            if (!editing || !CanEditExclusions(editingBoats) || editorDraft == null) return;
            var index = editorPage * EditorRowsPerPage + row;
            if (editingBoats)
            {
                var boats = EditorBoats();
                if (index >= boats.Length) return;
                var boat = boats[index];
                editorDraft.SetBoatExcluded(boat,
                    !editorDraft.IsBoatExcluded(boat));
            }
            else
            {
                var ports = EditorPorts();
                if (index >= ports.Length) return;
                var port = ports[index];
                editorDraft.SetPortExcluded(port,
                    !editorDraft.IsPortExcluded(port));
            }
            RefreshEditor();
        }

        private void RefreshEditor()
        {
            if (!editing || catalog == null || editorDraft == null) return;
            if (!CanEditExclusions(editingBoats))
            {
                if (!CanEditExclusions(!editingBoats))
                {
                    CloseEditor(false);
                    return;
                }
                editingBoats = !editingBoats;
                editorPage = 0;
            }
            var ports = editingBoats ? null : EditorPorts();
            var boats = editingBoats ? EditorBoats() : null;
            var count = editingBoats ? boats.Length : ports.Length;
            var excluded = editingBoats
                ? boats.Count(editorDraft.IsBoatExcluded)
                : ports.Count(editorDraft.IsPortExcluded);
            var pages = Math.Max(1, (count + EditorRowsPerPage - 1) / EditorRowsPerPage);
            editorPage = (editorPage % pages + pages) % pages;
            editorTitle.text = editingBoats ? "Random Boat Exclusions" : "Random Island Exclusions";
            var showTabs = CanEditExclusions(false) && CanEditExclusions(true);
            SetButtonVisible(editorIslands, showTabs);
            SetButtonVisible(editorBoats, showTabs);
            SetEnabled(editorIslands, showTabs && editingBoats);
            SetEnabled(editorBoats, showTabs && !editingBoats);
            SetButtonText(editorIslands, editingBoats ? "Islands" : "[Islands]");
            SetButtonText(editorBoats, editingBoats ? "[Boats]" : "Boats");
            editorSummary.text = (count - excluded) + " of " + count + " " +
                (editingBoats ? "boats" : "islands") + " allowed - page " +
                (editorPage + 1) + "/" + pages;
            for (var row = 0; row < EditorRowsPerPage; ++row)
            {
                var button = editorRows[row];
                var index = editorPage * EditorRowsPerPage + row;
                var visible = index < count || count == 0 && row == 0;
                button.transform.parent.gameObject.SetActive(visible);
                if (!visible) continue;
                if (count == 0)
                {
                    SetButtonText(button, "No random-eligible " +
                        (editingBoats ? "boats" : "islands") + " available");
                    SetEnabled(button, false);
                    continue;
                }
                var name = editingBoats ? boats[index].DisplayName : ports[index].DisplayName;
                var isExcluded = editingBoats
                    ? editorDraft.IsBoatExcluded(boats[index])
                    : editorDraft.IsPortExcluded(ports[index]);
                var label = (isExcluded ? "[ ] " : "[x] ") + Trim(name, 34);
                SetButtonText(button, label);
                var text = ButtonText(button);
                text.transform.localScale = Vector3.one *
                    (.012f * Mathf.Min(1f, 34f / Math.Max(34, label.Length)));
                text.color = isExcluded ? MutedInk : Ink;
                button.unclickable = false;
                button.lookText = (isExcluded ? "Include " : "Exclude ") + name +
                    " in random starts";
                button.description = editingBoats ? boats[index].OriginGroup : "Random island selection";
            }
            SetButtonText(editorPrevious, "<");
            SetButtonText(editorNext, ">");
            SetEnabled(editorPrevious, pages > 1);
            SetEnabled(editorNext, pages > 1);
        }

        internal MenuUiButton MakeButton(Transform root, string name, float x, float y,
            float width, float height, Action onClick)
        {
            var visual = MakeButtonVisual(root, name, x, y, width, height, out var surface);
            var target = surface.gameObject.AddComponent<MenuUiButton>();
            target.Clicked = onClick;
            target.lookText = name;
            target.description = "New Beginnings";
            target.gameObject.name = "New Beginnings trigger " + name;
            visual.SetActive(true);
            SetButtonText(target, name);
            ButtonText(target).color = Ink;
            return target;
        }

        private TextMesh MakeValuePanel(Transform root, string name, float x, float y, out GameObject visual)
        {
            visual = MakeButtonVisual(root, name, x, y, 1.14f, .52f, out var surface);
            // Keep the native panel/text appearance, but no pointer target or
            // collider: these display fields must never intercept arrow clicks.
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                Destroy(collider);
            }
            var outline = surface.GetComponent("Outline") as Behaviour;
            if (outline != null)
            {
                outline.enabled = false;
                Destroy(outline);
            }
            var text = visual.GetComponentInChildren<TextMesh>(true);
            text.text = "";
            text.transform.localScale = Vector3.one * .012f;
            text.color = Ink;
            visual.SetActive(true);
            return text;
        }

        private GameObject MakeButtonVisual(Transform root, string name, float x, float y,
            float width, float height, out Transform surface)
        {
            var visual = Instantiate(buttonTemplate, root, false);
            visual.name = "New Beginnings " + name;
            visual.SetActive(false);
            visual.transform.localPosition = new Vector3(MenuX(x), y + VerticalOffset,
                buttonTemplate.transform.localPosition.z);
            visual.transform.localRotation = buttonTemplate.transform.localRotation;
            // Scale the button surface, not its root. A nonuniform root scale
            // squeezes the TextMesh glyphs and makes native lettering illegible.
            visual.transform.localScale = Vector3.one;
            var native = visual.GetComponentInChildren<StartMenuButton>(true);
            if (native == null) throw new InvalidOperationException("Cloned start button has no pointer target.");
            surface = native.transform;
            var surfaceScale = surface.localScale;
            surface.localScale = new Vector3(surfaceScale.x * width * (seasLayout ? SeasContentWidth : 1f),
                surfaceScale.y * height, surfaceScale.z);
            native.unclickable = true;
            native.enabled = false;
            Destroy(native);
            var labels = visual.GetComponentsInChildren<TextMesh>(true);
            if (labels.Length == 0) throw new InvalidOperationException("Cloned start button has no text.");
            for (var i = 1; i < labels.Length; ++i) labels[i].gameObject.SetActive(false);
            return visual;
        }

        internal TextMesh MakeLabel(Transform root, string name, string value,
            float x, float y, float scale)
        {
            var visual = Instantiate(titleTemplate.gameObject, root, false);
            visual.name = "New Beginnings " + name;
            visual.transform.localPosition = new Vector3(MenuX(x), y + VerticalOffset,
                titleTemplate.transform.localPosition.z);
            visual.transform.localRotation = titleTemplate.transform.localRotation;
            visual.transform.localScale = Vector3.one * scale;
            var text = visual.GetComponent<TextMesh>();
            text.text = value;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Ink;
            return text;
        }

        private float MenuX(float x) => seasLayout
            ? x * SeasContentWidth + SeasMenuOffsetX
            : x + StandaloneMenuOffsetX;

        internal static TextMesh ButtonText(MenuUiButton button)
        {
            return button != null ? button.transform.parent.GetComponentInChildren<TextMesh>(true) : null;
        }

        internal static void SetButtonText(MenuUiButton button, string value)
        {
            var text = ButtonText(button);
            if (text == null) return;
            text.text = value;
            // The port/size buttons are narrow. Keep the font proportions and
            // reduce long labels evenly instead of stretching or clipping them.
            var width = button.transform.localScale.x;
            var shrink = Mathf.Min(1f, 12f / Math.Max(12, value.Length));
            var size = .014f * shrink * Mathf.Min(1f, width / .72f);
            text.transform.localScale = Vector3.one * size;
        }

        internal static void SetEnabled(MenuUiButton button, bool enabled)
        {
            if (button == null) return;
            button.unclickable = !enabled;
            var text = ButtonText(button);
            if (text != null) text.color = enabled ? Ink : MutedInk;
        }

        private static void SetButtonVisible(MenuUiButton button, bool visible)
        {
            if (button != null) button.transform.parent.gameObject.SetActive(visible);
        }

        private void CyclePort(int direction)
        {
            if (settings.RandomPort || catalog == null) return;
            var ports = catalog.VisiblePorts(settings).ToList();
            if (ports.Count == 0) return;
            var current = ports.FindIndex(IsSelectedPort);
            if (current < 0) current = direction > 0 ? -1 : 0;
            var selected = ports[(current + direction + ports.Count) % ports.Count];
            settings.DestinationKey = selected.Key;
            settings.PortIndex = selected.Index;
            Changed();
        }

        private bool IsSelectedPort(PortChoice port) =>
            string.IsNullOrEmpty(settings.DestinationKey)
                ? port.SiteIdentity == null && port.Index == settings.PortIndex
                : string.Equals(port.Key, settings.DestinationKey, StringComparison.Ordinal);

        private void CycleBoat(int direction)
        {
            if (settings.RandomBoat || catalog == null || catalog.Boats.Count == 0) return;
            var current = catalog.Boats.FindIndex(item => item.Index == settings.BoatSceneIndex);
            if (current < 0) current = direction > 0 ? -1 : 0;
            settings.BoatSceneIndex = catalog.Boats[(current + direction + catalog.Boats.Count) % catalog.Boats.Count].Index;
            Changed();
        }

        private void TogglePortPool(PortPool pool)
        {
            if (!settings.RandomPort) return;
            if (settings.SetPortPool(pool, !settings.PortPools.Contains(pool))) Changed();
        }

        private void ToggleModPorts()
        {
            if (!settings.RandomPort || catalog == null || !catalog.Ports.Any(item => item.IsModded)) return;
            if (settings.IncludeModPorts && settings.PortPools.Count == 0) return;
            settings.IncludeModPorts = !settings.IncludeModPorts;
            Changed();
        }

        private void ToggleBoatSize(BoatSize size)
        {
            if (!settings.RandomBoat) return;
            if (settings.SetBoatSize(size, !settings.BoatSizes.Contains(size))) Changed();
        }

        private void Changed()
        {
            changed?.Invoke();
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (catalog == null) return;
            var visiblePorts = catalog.VisiblePorts(settings).ToList();
            var port = visiblePorts.FirstOrDefault(IsSelectedPort);
            var boat = catalog.Boats.FirstOrDefault(item => item.Index == settings.BoatSceneIndex);
            portValue.text = port != null ? Trim(port.DisplayName, 19) : "Unavailable";
            boatValue.text = boat != null ? Trim(boat.DisplayName, 19) : "No boat";
            portValue.color = settings.RandomPort ? MutedInk : Ink;
            boatValue.color = settings.RandomBoat ? MutedInk : Ink;
            portValuePanel.SetActive(!EditingAny);
            SetButtonVisible(portPrevious, !EditingAny);
            SetButtonVisible(portNext, !EditingAny);
            var showSpawnOptions = spawnOptionsOpen && !editing;
            SetButtonVisible(shipyardPortsOnly, showSpawnOptions);
            SetEnabled(shipyardPortsOnly, settings.RandomPort);
            SetButtonText(shipyardPortsOnly, (settings.ShipyardPortsOnly ? "[x] " : "[ ] ") + "Shipyard ports only");
            SetButtonVisible(useShipyardBerth, showSpawnOptions);
            SetEnabled(useShipyardBerth, true);
            SetButtonText(useShipyardBerth, (settings.UseShipyardBerth ? "[x] " : "[ ] ") + "Use shipyard berth");
            var hasModPorts = catalog.Ports.Any(item => item.IsModded);
            SetButtonVisible(includeModPorts, showSpawnOptions && hasModPorts);
            SetEnabled(includeModPorts, settings.RandomPort && hasModPorts &&
                (!settings.IncludeModPorts || settings.PortPools.Count > 0));
            SetButtonText(includeModPorts, (settings.IncludeModPorts ? "[x] " : "[ ] ") + "Mod ports");
            SetEnabled(portPrevious, !settings.RandomPort && (visiblePorts.Count > 1 || port == null && visiblePorts.Count > 0));
            SetEnabled(portNext, !settings.RandomPort && (visiblePorts.Count > 1 || port == null && visiblePorts.Count > 0));
            SetEnabled(boatPrevious, !settings.RandomBoat && catalog.Boats.Count > 1);
            SetEnabled(boatNext, !settings.RandomBoat && catalog.Boats.Count > 1);
            SetButtonText(portPrevious, "<");
            SetButtonText(portNext, ">");
            SetButtonText(boatPrevious, "<");
            SetButtonText(boatNext, ">");
            SetButtonText(randomPort, settings.RandomPort ? "Random Port: ON" : "Random Port: OFF");
            SetButtonText(randomBoat, settings.RandomBoat ? "Random Boat: ON" : "Random Boat: OFF");

            for (var i = 0; i < portPools.Length; ++i)
            {
                var checkedPool = settings.PortPools.Contains(PoolOrder[i]);
                SetButtonText(portPools[i], (checkedPool ? "[x] " : "[ ] ") + PoolLabels[i]);
                SetEnabled(portPools[i], settings.RandomPort &&
                    (!checkedPool || settings.PortPools.Count > 1 || settings.IncludeModPorts));
            }
            for (var i = 0; i < boatSizes.Length; ++i)
            {
                var checkedSize = settings.BoatSizes.Contains(SizeOrder[i]);
                SetButtonText(boatSizes[i], (checkedSize ? "[x] " : "[ ] ") + SizeOrder[i]);
                SetEnabled(boatSizes[i], settings.RandomBoat &&
                    (!checkedSize || settings.BoatSizes.Count > 1));
            }
            SetButtonText(portExclusions, "Island exclusions...");
            SetButtonText(boatExclusions, "Boat exclusions...");
            // RefreshCatalog can also run with the editor open. Keep these
            // controls hidden then, including their native pointer colliders.
            SetButtonVisible(portExclusions, showSpawnOptions && settings.RandomPort);
            SetButtonVisible(boatExclusions, showSpawnOptions && settings.RandomBoat);
            SetEnabled(portExclusions, CanEditExclusions(false) && individualExclusions != null);
            SetEnabled(boatExclusions, settings.RandomBoat && individualExclusions != null);

            continueAvailable = !FixedStart.TryGetStartBlock(out var reason) &&
                catalog.TryChoose(settings, individualExclusions, new System.Random(1), out _, out reason, availabilityOnly: true);
            ApplyContinueState();
            if (continueAvailable)
            {
                status.text = "";
            }
            else if (catalog.Rejections.Count > 0 && catalog.Ports.Count == 0 && catalog.Boats.Count == 0)
                status.text = Trim(catalog.Rejections[0], 58);
            else
                status.text = Trim(reason, 58);
        }

        private static string Trim(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= maximum ? value : value.Substring(0, maximum - 1) + "…";
        }
    }
}
