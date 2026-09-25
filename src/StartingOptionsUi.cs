using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    // Uses the same world-space pointer targets as Sailwind's settings sliders.
    internal sealed class StartingOptionsSlider : MonoBehaviour
    {
        private static readonly FieldInfo UiCameraField = AccessTools.Field(typeof(MouseButtonPointer), "uiCam");
        internal MenuUiButton Button;
        internal Action<float> Changed;
        internal float Maximum;
        internal float Step;
        private bool dragging;
        private float value;
        private Camera dragCamera;
        private Transform thumb;
        private Material markerMaterial;
        private float trackWidth;

        internal void InitializeVisuals()
        {
            var surface = Button.transform;
            var collider = Button.GetComponent<BoxCollider>();
            trackWidth = Mathf.Abs(surface.localScale.x) * collider.size.x - .10f;
            markerMaterial = new Material(Button.GetComponent<Renderer>().sharedMaterial);
            var ink = new Color(.06f, .035f, .02f, 1f);
            markerMaterial.color = ink;
            markerMaterial.SetColor("_EmissionColor", ink);
            Marker("Track", trackWidth, .018f, .002f);
            thumb = Marker("Thumb", .055f, .09f, .003f);
            MenuUi.ButtonText(Button).text = "";
        }

        private Transform Marker(string name, float width, float height, float depth)
        {
            var surface = Button.transform;
            var marker = new GameObject(name);
            marker.layer = Button.gameObject.layer;
            marker.transform.SetParent(surface.parent, false);
            marker.transform.localPosition = surface.localPosition + new Vector3(0f, 0f, depth);
            marker.transform.localRotation = surface.localRotation;
            marker.transform.localScale = new Vector3(width, height, surface.localScale.z);
            marker.AddComponent<MeshFilter>().sharedMesh = Button.GetComponent<MeshFilter>().sharedMesh;
            marker.AddComponent<MeshRenderer>().sharedMaterial = markerMaterial;
            return marker.transform;
        }

        internal void Set(float next)
        {
            value = Mathf.Clamp(next, 0f, Maximum);
            if (thumb != null)
            {
                var position = thumb.localPosition;
                // The native visual root reverses X relative to the page.
                position.x = (.5f - value / Maximum) * trackWidth;
                thumb.localPosition = position;
            }
        }

        internal void Activate(RaycastHit hit)
        {
            SetPoint(hit.point);
            dragCamera = MouseButtonPointer.instance != null
                ? UiCameraField?.GetValue(MouseButtonPointer.instance) as Camera : null;
            dragging = dragCamera != null && Input.GetMouseButton(0);
        }

        internal void Nudge(int direction)
        {
            Apply(value + direction * Step);
        }

        private void Apply(float next)
        {
            next = Mathf.Clamp(Mathf.Round(next / Step) * Step, 0f, Maximum);
            Set(next);
            Changed?.Invoke(next);
        }

        private void SetPoint(Vector3 worldPoint)
        {
            var visual = Button.transform.parent;
            // Native button roots rotate 180 degrees around Y. Measure in the
            // page coordinates so increasing values follow the visible row.
            var localX = visual.parent.InverseTransformPoint(worldPoint).x - visual.localPosition.x;
            Apply((localX / trackWidth + .5f) * Maximum);
        }

        private void Update()
        {
            if (!dragging) return;
            if (!Input.GetMouseButton(0) || !Application.isFocused)
            {
                dragging = false;
                return;
            }
            if (dragCamera == null) { dragging = false; return; }
            var visual = Button.transform.parent;
            var ray = dragCamera.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(visual.forward, visual.position);
            if (plane.Raycast(ray, out var distance)) SetPoint(ray.GetPoint(distance));
        }

        private void OnDisable() { dragging = false; dragCamera = null; }
        private void OnDestroy() { if (markerMaterial != null) Destroy(markerMaterial); }
    }

    internal sealed class StartingOptionsUi : MonoBehaviour
    {
        private static readonly string[] FactionNames = { "Al'Ankh", "Emerald Archipelago", "Aestrin" };
        private const int EquipmentRows = 10;
        private MenuUi owner;
        private StartingOptionsSettings draft;
        private GameObject moneyPage;
        private GameObject reputationPage;
        private GameObject equipmentPage;
        private TextMesh message;
        private TextMesh multiplierLabel;
        private TextMesh equipmentSummary;
        private readonly MenuUiButton[] currencyFields = new MenuUiButton[4];
        private readonly string[] amounts = new string[4];
        private readonly TextMesh[] reputationLabels = new TextMesh[4];
        private readonly StartingOptionsSlider[] reputationSliders = new StartingOptionsSlider[4];
        private readonly MenuUiButton[] equipmentRows = new MenuUiButton[EquipmentRows];
        private StartingOptionsSlider multiplier;
        private MenuUiButton previous;
        private MenuUiButton next;
        private MenuUiButton clearEquipment;
        private MenuUiButton moneyTab;
        private MenuUiButton reputationTab;
        private MenuUiButton equipmentTab;
        private EquipmentEntry[] equipment = new EquipmentEntry[0];
        private int vanillaEquipmentCount;
        private int page;
        private int tab;
        private int focusedCurrency = -1;
        private bool selectAll;
        internal bool IsOpen { get; private set; }

        internal static StartingOptionsUi Create(MenuUi owner, Transform parent)
        {
            var root = new GameObject("New Beginnings Starting Options Editor");
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, MenuUi.StartingOptionsOffsetY, 0f);
            var ui = root.AddComponent<StartingOptionsUi>();
            ui.owner = owner;
            ui.Build();
            return ui;
        }

        private GameObject Page(string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(transform, false);
            return root;
        }

        private MenuUiButton Button(Transform root, string name, string label,
            float x, float y, float width, float height, Action action)
        {
            var button = owner.MakeButton(root, name, x, y, width, height,
                () => { Blur(); action(); });
            MenuUi.SetButtonText(button, label);
            return button;
        }

        private void Build()
        {
            owner.MakeLabel(transform, "Starting options title", "Starting Options", 0f, .76f, .016f);
            moneyTab = Button(transform, "Money tab", "Money", -1.18f, .55f, .90f, .47f, () => ShowTab(0));
            reputationTab = Button(transform, "Reputation tab", "Reputation", 0f, .55f, 1.08f, .47f, () => ShowTab(1));
            equipmentTab = Button(transform, "Equipment tab", "Equipment", 1.18f, .55f, 1.05f, .47f, () => ShowTab(2));
            message = owner.MakeLabel(transform, "Starting options message", "", 0f, -.78f, .009f);
            Button(transform, "Save starting options", "Done", 0f, -.97f, .82f, .64f, () => Close(true));

            moneyPage = Page("Money");
            multiplierLabel = owner.MakeLabel(moneyPage.transform, "Money multiplier value", "", 0f, .33f, .012f);
            multiplier = Slider(moneyPage.transform, "Starting money multiplier", 0f, .14f, 2.7f, 100f, .1f,
                value => { draft.CurrencyMultiplier = value; RefreshMoney(); });
            owner.MakeLabel(moneyPage.transform, "Money override explanation", "Custom amounts override multiplier setting", 0f, -.08f, .009f);
            for (var i = 0; i < 4; i++)
            {
                var index = i;
                var x = i % 2 == 0 ? -1.0f : 1.0f;
                var y = -.25f - .27f * (i / 2);
                owner.MakeLabel(moneyPage.transform, "Currency name " + i, PlayerGold.GetCurrencyName(i), x, y, .010f);
                currencyFields[i] = owner.MakeButton(moneyPage.transform, "Currency amount " + i,
                    x, y - .12f, 1.65f, .40f, () => Focus(index));
                currencyFields[i].description = "Enter a whole amount or leave blank. The multiplier affects only your starting region's currency. Zero is allowed.";
            }

            reputationPage = Page("Reputation");
            for (var i = 0; i < 4; i++)
            {
                var index = i;
                var y = .29f - .27f * i;
                reputationLabels[i] = owner.MakeLabel(reputationPage.transform, "Reputation value " + i,
                    "", -1.02f, y, .010f);
                reputationSliders[i] = Slider(reputationPage.transform, "Reputation slider " + i,
                    .80f, y, 1.58f, 10f, 1f, value =>
                    {
                        if (index == 0) draft.StartingReputation = Mathf.RoundToInt(value);
                        else draft.FactionReputation[index - 1] = Mathf.RoundToInt(value);
                        RefreshReputation();
                    });
            }
            owner.MakeLabel(reputationPage.transform, "Reputation explanation",
                "Regional levels override the starting region level", 0f, -.69f, .009f);

            equipmentPage = Page("Additional Equipment");
            equipmentSummary = owner.MakeLabel(equipmentPage.transform, "Additional equipment count", "", 0f, .33f, .010f);
            for (var i = 0; i < EquipmentRows; i++)
            {
                var row = i;
                equipmentRows[i] = Button(equipmentPage.transform, "Additional equipment row " + i, "",
                    i % 2 == 0 ? -1.01f : 1.01f, .13f - .17f * (i / 2), 1.91f, .43f, () => ToggleEquipment(row));
            }
            previous = Button(equipmentPage.transform, "Previous equipment page", "<", -1.45f, -.70f, .44f, .42f,
                () => { page--; RefreshEquipment(); });
            next = Button(equipmentPage.transform, "Next equipment page", ">", 1.45f, -.70f, .44f, .42f,
                () => { page++; RefreshEquipment(); });
            clearEquipment = Button(equipmentPage.transform, "Clear additional equipment", "Clear selection", 0f, -.70f, 1.35f, .42f,
                ClearEquipmentSelection);
        }

        private StartingOptionsSlider Slider(Transform parent, string name, float x, float y,
            float width, float maximum, float step, Action<float> change)
        {
            var button = Button(parent, name, "", x, y, width, .38f, () => { });
            var slider = button.gameObject.AddComponent<StartingOptionsSlider>();
            slider.Button = button;
            slider.Maximum = maximum;
            slider.Step = step;
            slider.Changed = change;
            slider.InitializeVisuals();
            button.Hit = slider.Activate;
            var decrease = Button(parent, name + " decrease", "-", x - width / 2f - .16f, y, .25f, .38f,
                () => slider.Nudge(-1));
            var increase = Button(parent, name + " increase", "+", x + width / 2f + .16f, y, .25f, .38f,
                () => slider.Nudge(1));
            MenuUi.ButtonText(decrease).transform.localScale = Vector3.one * .012f;
            MenuUi.ButtonText(increase).transform.localScale = Vector3.one * .012f;
            return slider;
        }

        internal void Open()
        {
            if (IsOpen || Plugin.Instance?.StartOptions == null) return;
            draft = Plugin.Instance.StartOptions.Copy();
            for (var i = 0; i < amounts.Length; i++)
                amounts[i] = draft.CurrencyOverrides[i]?.ToString(CultureInfo.InvariantCulture) ?? "";
            equipment = AdditionalEquipment.GetCatalog().OrderBy(entry => entry.IsModded).ToArray();
            vanillaEquipmentCount = equipment.Count(entry => !entry.IsModded);
            IsOpen = true;
            page = 0;
            focusedCurrency = -1;
            owner.ShowStartingOptions(true);
            gameObject.SetActive(true);
            ShowTab(0);
        }

        internal void Close(bool save)
        {
            if (!IsOpen) return;
            Blur();
            if (save)
            {
                if (!ReadAmounts())
                {
                    message.text = "Enter whole amounts between 0 and " + int.MaxValue + ".";
                    return;
                }
                if (!draft.TryValidate(out var reason))
                {
                    message.text = reason;
                    return;
                }
                Plugin.Instance.StartOptions.ReplaceWith(draft);
                Plugin.Instance.SaveStartingOptions();
            }
            IsOpen = false;
            draft = null;
            gameObject.SetActive(false);
            owner.ShowStartingOptions(false);
        }

        private void OnDisable()
        {
            focusedCurrency = -1;
            selectAll = false;
            if (IsOpen) Close(false);
        }

        private void ShowTab(int index)
        {
            Blur();
            tab = index;
            moneyPage.SetActive(tab == 0);
            reputationPage.SetActive(tab == 1);
            equipmentPage.SetActive(tab == 2);
            MenuUi.SetEnabled(moneyTab, tab != 0);
            MenuUi.SetEnabled(reputationTab, tab != 1);
            MenuUi.SetEnabled(equipmentTab, tab != 2);
            message.text = tab == 2 ? "Each checked item adds one extra to your normal loadout." : "";
            RefreshMoney();
            RefreshReputation();
            RefreshEquipment();
        }

        private void RefreshMoney()
        {
            multiplier.Set(draft.CurrencyMultiplier);
            multiplierLabel.text = "Starting money: " + draft.CurrencyMultiplier.ToString("0.0", CultureInfo.InvariantCulture) + "x";
            for (var i = 0; i < amounts.Length; i++)
            {
                var value = amounts[i].Length == 0 ? "No override" : amounts[i];
                if (focusedCurrency == i) value = (selectAll ? "[" + amounts[i] + "]" : amounts[i]) + "|";
                MenuUi.SetButtonText(currencyFields[i], value);
                MenuUi.ButtonText(currencyFields[i]).transform.localScale = Vector3.one * .012f;
            }
        }

        private void RefreshReputation()
        {
            for (var i = 0; i < reputationSliders.Length; i++)
            {
                var value = i == 0 ? draft.StartingReputation : draft.FactionReputation[i - 1];
                reputationSliders[i].Set(value);
                reputationLabels[i].text = (i == 0 ? "Starting region" : FactionNames[i - 1]) + "\n" +
                    (value == 0 ? "Default" : "Level " + value);
            }
        }

        private int VanillaPages => (vanillaEquipmentCount + EquipmentRows - 1) / EquipmentRows;
        private int ModPages => (equipment.Length - vanillaEquipmentCount + EquipmentRows - 1) / EquipmentRows;
        private int EquipmentPageCount => Math.Max(1, VanillaPages + ModPages);
        private int EquipmentPageStart => page < VanillaPages ? page * EquipmentRows :
            vanillaEquipmentCount + (page - VanillaPages) * EquipmentRows;
        private int EquipmentPageEnd => Math.Min(EquipmentPageStart + EquipmentRows,
            page < VanillaPages ? vanillaEquipmentCount : equipment.Length);

        private void RefreshEquipment()
        {
            var pages = EquipmentPageCount;
            page = (page % pages + pages) % pages;
            var modSection = page >= VanillaPages;
            equipmentSummary.text = (modSection ? "Mod items" : "Vanilla items") +
                " - page " + (page + 1) + "/" + pages + " - " +
                draft.AdditionalEquipment.Count + " selected total";
            for (var row = 0; row < EquipmentRows; row++)
            {
                var index = EquipmentPageStart + row;
                var button = equipmentRows[row];
                button.transform.parent.gameObject.SetActive(index < EquipmentPageEnd);
                if (index >= EquipmentPageEnd) continue;
                var entry = equipment[index];
                var selected = draft.AdditionalEquipment.Contains(entry.Key);
                var label = (selected ? "[x] " : "[ ] ") + entry.DisplayName;
                MenuUi.SetButtonText(button, label);
                MenuUi.ButtonText(button).transform.localScale = Vector3.one *
                    (.011f * Mathf.Min(1f, 26f / Math.Max(26, label.Length)));
                button.lookText = entry.DisplayName;
                button.description = (entry.IsModded ? entry.SourceName + ": " : "") +
                    (selected ? "Remove" : "Add") + " one extra " + entry.DisplayName;
            }
            MenuUi.SetEnabled(previous, pages > 1);
            MenuUi.SetEnabled(next, pages > 1);
            if (equipment.Length == 0) equipmentSummary.text = "No additional equipment is available yet.";
            var unavailable = UnavailableEquipmentKeys();
            MenuUi.SetButtonText(clearEquipment, unavailable.Length > 0 ? "Clear unavailable" : "Clear selection");
            clearEquipment.description = unavailable.Length > 0
                ? "Remove unavailable equipment selections and keep the rest."
                : "Remove all additional equipment selections.";
            if (tab == 2)
                message.text = unavailable.Length > 0
                    ? unavailable.Length + " selected items unavailable. Clear unavailable to remove them."
                    : "Each checked item adds one extra to your normal loadout.";
        }

        private string[] UnavailableEquipmentKeys() => draft.AdditionalEquipment
            .Except(equipment.Select(entry => entry.Key), StringComparer.Ordinal).ToArray();

        private void ClearEquipmentSelection()
        {
            var unavailable = UnavailableEquipmentKeys();
            if (unavailable.Length == 0) draft.AdditionalEquipment.Clear();
            else foreach (var key in unavailable) draft.AdditionalEquipment.Remove(key);
            RefreshEquipment();
        }

        private void ToggleEquipment(int row)
        {
            var index = EquipmentPageStart + row;
            if (index < 0 || index >= EquipmentPageEnd) return;
            var key = equipment[index].Key;
            if (!draft.AdditionalEquipment.Remove(key)) draft.AdditionalEquipment.Add(key);
            RefreshEquipment();
        }

        private void Focus(int index)
        {
            focusedCurrency = index;
            selectAll = true;
            RefreshMoney();
        }

        private void Blur()
        {
            focusedCurrency = -1;
            selectAll = false;
            if (draft != null) RefreshMoney();
        }

        private bool ReadAmounts()
        {
            for (var i = 0; i < amounts.Length; i++)
            {
                if (amounts[i].Length == 0) draft.CurrencyOverrides[i] = null;
                else if (int.TryParse(amounts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                    draft.CurrencyOverrides[i] = value;
                else return false;
            }
            return true;
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (!Application.isFocused) { Blur(); return; }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (focusedCurrency >= 0) Blur();
                return;
            }
            if (focusedCurrency < 0 || tab != 0) return;
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                Focus((focusedCurrency + (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 3 : 1)) % 4);
                return;
            }
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.A))
                selectAll = true;
            var value = amounts[focusedCurrency];
            foreach (var character in Input.inputString)
            {
                if (character == '\n' || character == '\r')
                {
                    amounts[focusedCurrency] = value;
                    Blur();
                    return;
                }
                if (character == '\b')
                {
                    value = selectAll || value.Length == 0 ? "" : value.Substring(0, value.Length - 1);
                    selectAll = false;
                }
                else if (character >= '0' && character <= '9')
                {
                    var proposed = (selectAll ? "" : value) + character;
                    if (int.TryParse(proposed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                        value = parsed.ToString(CultureInfo.InvariantCulture);
                    selectAll = false;
                }
            }
            if (Input.GetKeyDown(KeyCode.Delete)) { value = ""; selectAll = false; }
            amounts[focusedCurrency] = value;
            RefreshMoney();
        }
    }
}
