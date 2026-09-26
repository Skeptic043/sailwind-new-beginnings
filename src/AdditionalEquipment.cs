using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    internal sealed class EquipmentEntry
    {
        internal readonly string Key;
        internal readonly string DisplayName;
        internal readonly int Index;
        internal readonly string PrefabName;
        internal readonly string ItemType;
        internal readonly bool IsModded;
        internal readonly bool AllowScaledPacking;
        internal readonly string SourceName = "";
        internal readonly string SourceId = "";

        internal EquipmentEntry(int index, string prefabName, string itemType, string displayName, string key = null)
        {
            Index = index;
            PrefabName = prefabName;
            ItemType = itemType;
            DisplayName = displayName;
            Key = key ?? "native:" + index + ":" + prefabName;
        }

        internal EquipmentEntry(int index, string prefabName, string itemType, string displayName,
            string sourceId, string sourceName, bool allowScaledPacking = false) : this(index, prefabName, itemType, displayName)
        {
            IsModded = true;
            AllowScaledPacking = allowScaledPacking;
            SourceId = sourceId;
            SourceName = sourceName;
            Key = "mod:" + Uri.EscapeDataString(sourceId) + ":" +
                Uri.EscapeDataString(ModEquipment.CanonicalName(prefabName)) + ":" + Uri.EscapeDataString(itemType);
        }
    }

    // Adds ordinary native prefabs to the regional startup roster. The native
    // starter coroutine initializes ownership; native crates own their contents.
    internal static class AdditionalEquipment
    {
        internal const string WallHooksKey = "bundle:wallhooks";
        private const int WallHooksPerBox = 12;
        private static readonly EquipmentEntry[] Definitions = {
            new EquipmentEntry(1, "1 crate salmon (E)", "ShipItemCrate", "Box of salmon"),
            new EquipmentEntry(10, "10 barrel water", "ShipItemBottle", "Barrel of water"),
            new EquipmentEntry(11, "11 barrel rum", "ShipItemBottle", "Barrel of rum"),
            new EquipmentEntry(52, "52 cheese", "ShipItemFood", "Single cheese"),
            new EquipmentEntry(40, "40 empty bottle", "ShipItemBottle", "Empty bottle"),
            new EquipmentEntry(70, "70 bucket", "ShipItemBottle", "Bucket"),
            new EquipmentEntry(108, "108 crate of firewood", "ShipItemCrate", "Crate of firewood"),
            new EquipmentEntry(80, "80 compass A", "ShipItemCompass", "Compass (Al'Ankh)"),
            new EquipmentEntry(81, "81 compass E", "ShipItemCompass", "Compass (Emerald)"),
            new EquipmentEntry(82, "82 compass M", "ShipItemCompass", "Compass (Aestrin)"),
            new EquipmentEntry(83, "83 chronometer A", "ShipItemCompass", "Chronocompass (Al'Ankh)"),
            new EquipmentEntry(84, "84 chronometer E", "ShipItemCompass", "Chronocompass (Emerald)"),
            new EquipmentEntry(85, "85 chronometer M", "ShipItemCompass", "Chronocompass (Aestrin)"),
            new EquipmentEntry(86, "86 sun compass A", "ShipItemCompass", "Sun compass"),
            new EquipmentEntry(90, "90 quadrant", "ShipItemQuadrant", "Quadrant"),
            new EquipmentEntry(92, "92 chip log M", "ShipItemChipLog", "Chip log (Aestrin)"),
            new EquipmentEntry(93, "93 chip log E", "ShipItemChipLog", "Chip log (Emerald)"),
            new EquipmentEntry(94, "94 broom", "ShipItemBroom", "Broom"),
            new EquipmentEntry(95, "95 fishing rod 1", "ShipItemFishingRod", "Fishing rod"),
            new EquipmentEntry(104, "104 crate of fishing hooks", "ShipItemCrate", "Box of fishing hooks"),
            new EquipmentEntry(104, "104 crate of fishing hooks", "ShipItemCrate", "Box of wall hooks", WallHooksKey),
            new EquipmentEntry(100, "100 mug wood", "ShipItemBottle", "Mug (wood)"),
            new EquipmentEntry(101, "101 mug clay", "ShipItemBottle", "Mug (clay)"),
            new EquipmentEntry(102, "102 mug metal", "ShipItemBottle", "Mug (metal)"),
            new EquipmentEntry(103, "103 mug metal gold", "ShipItemBottle", "Mug (gold)"),
            new EquipmentEntry(110, "110 lantern A", "ShipItemLight", "Lantern (Al'Ankh)"),
            new EquipmentEntry(111, "111 lantern E yellow", "ShipItemLight", "Lantern (yellow)"),
            new EquipmentEntry(112, "112 lantern E red", "ShipItemLight", "Lantern (red)"),
            new EquipmentEntry(113, "113 lantern E green", "ShipItemLight", "Lantern (green)"),
            new EquipmentEntry(114, "114 lantern M", "ShipItemLight", "Lantern (Aestrin)"),
            new EquipmentEntry(115, "115 map ocean", "ShipItemFoldable", "Ocean map"),
            new EquipmentEntry(116, "116 map A", "ShipItemFoldable", "Al'Ankh map"),
            new EquipmentEntry(117, "117 map E", "ShipItemFoldable", "Emerald Archipelago map"),
            new EquipmentEntry(118, "118 map M", "ShipItemFoldable", "Aestrin map"),
            new EquipmentEntry(119, "119 map L", "ShipItemFoldable", "Fire Fish Lagoon map"),
            new EquipmentEntry(131, "131 lantern candle crate", "ShipItemCrate", "Box of candles"),
            new EquipmentEntry(132, "132 lantern oil bottle", "ShipItemLanternFuel", "Lamp oil"),
            new EquipmentEntry(133, "133 lantern M big", "ShipItemLight", "Lantern (large Aestrin)"),
            new EquipmentEntry(134, "134 lantern E blu", "ShipItemLight", "Lantern (blue)"),
            new EquipmentEntry(156, "156 pot", "ShipItemSoup", "Cooking pot"),
            new EquipmentEntry(157, "157 pot big", "ShipItemSoup", "Large cooking pot"),
            new EquipmentEntry(159, "159 hammer", "ShipItemHammer", "Hammer"),
            new EquipmentEntry(160, "160 spyglass big", "ShipItemSpyglass", "Spyglass (large)"),
            new EquipmentEntry(161, "161 spyglass small", "ShipItemSpyglass", "Spyglass (small)"),
            new EquipmentEntry(162, "162 spyglass tiny", "ShipItemSpyglass", "Spyglass (tiny)"),
            new EquipmentEntry(165, "165 map mirage mountain", "ShipItemFoldable", "Mirage Mountain map"),
            new EquipmentEntry(166, "166 oakum", "ShipItemOakum", "Oakum"),
            new EquipmentEntry(167, "167 ink set", "ShipItemInkSet", "Charting kit"),
            new EquipmentEntry(168, "168 oar", "ShipItemOar", "Oar"),
            new EquipmentEntry(170, "170 clock A", "ShipItemClock", "Chronometer (Al'Ankh)"),
            new EquipmentEntry(171, "171 clock E", "ShipItemClock", "Chronometer (Emerald)"),
            new EquipmentEntry(172, "172 clock M", "ShipItemClock", "Chronometer (Aestrin)"),
            new EquipmentEntry(213, "213 (43) crate oranges", "ShipItemCrate", "Box of oranges"),
            new EquipmentEntry(370, "370 slicing knife A", "ShipItemKnife", "Knife (Al'Ankh)"),
            new EquipmentEntry(371, "371 slicing knife E", "ShipItemKnife", "Knife (Emerald)"),
            new EquipmentEntry(372, "372 slicing knife M", "ShipItemKnife", "Knife (Aestrin)"),
            new EquipmentEntry(382, "382 kettle A", "ShipItemKettle", "Kettle (Al'Ankh)"),
            new EquipmentEntry(383, "383 kettle E", "ShipItemKettle", "Kettle (Emerald)"),
            new EquipmentEntry(384, "384 kettle M", "ShipItemKettle", "Kettle (Aestrin)"),
        };
        private static readonly HashSet<ShipItem> Extras = new HashSet<ShipItem>();
        private static readonly HashSet<ShipItem> PackingCandidates = new HashSet<ShipItem>();
        private static readonly HashSet<ShipItem> ScaledPackingTools = new HashSet<ShipItem>();
        private static readonly List<ShipItem> Carriers = new List<ShipItem>();
        private static readonly Dictionary<ShipItem, ShipItem> Packed = new Dictionary<ShipItem, ShipItem>();
        private static readonly Dictionary<ShipItem, ShipItem> WallHooks = new Dictionary<ShipItem, ShipItem>();
        private static readonly HashSet<ShipItem> WallHookBoxes = new HashSet<ShipItem>();
        private static ResolvedStart owner;
        private static bool appended;
        private static bool appendSucceeded;
        private static bool packingComplete;
        private static string failure;

        private static readonly Type CrateType = typeof(ShipItem).Assembly.GetType("CrateInventory");
        private static readonly Type CrateUiType = typeof(ShipItem).Assembly.GetType("CrateInventoryUI");
        private static readonly MethodInfo Insert = CrateType == null ? null :
            AccessTools.Method(CrateType, "InsertItem", new[] { typeof(ShipItem) });
        private static readonly FieldInfo Contents = CrateType == null ? null : AccessTools.Field(CrateType, "containedItems");
        private static readonly FieldInfo CrateId = AccessTools.Field(typeof(SaveablePrefab), "currentCrateId");
        private static readonly FieldInfo UiMeshes = CrateUiType == null ? null : AccessTools.Field(CrateUiType, "containerMeshes");
        private static readonly FieldInfo UiButtons = CrateUiType == null ? null : AccessTools.Field(CrateUiType, "buttons");

        private static PrefabsDirectory Directory => PrefabsDirectory.instance ??
            Resources.FindObjectsOfTypeAll<PrefabsDirectory>().FirstOrDefault();

        internal static EquipmentEntry[] GetCatalog()
        {
            var directory = Directory;
            return Definitions.Where(entry => Resolve(directory, entry) != null)
                .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Concat(ModEquipment.Discover(directory)).ToArray();
        }

        private static GameObject Resolve(PrefabsDirectory directory, EquipmentEntry entry)
        {
            if (directory?.directory == null || entry.Index >= directory.directory.Length) return null;
            var prefab = directory.directory[entry.Index];
            if (prefab == null || prefab.name != entry.PrefabName) return null;
            var item = prefab.GetComponent<ShipItem>();
            var saveable = prefab.GetComponent<SaveablePrefab>();
            if (entry.Key == WallHooksKey && !CanCreateWallHookBox(directory, prefab)) return null;
            return item != null && item.GetType().FullName == entry.ItemType &&
                saveable != null && saveable.prefabIndex == entry.Index ? prefab : null;
        }

        private static GameObject WallHookPrefab(PrefabsDirectory directory) => Resolve(directory,
            new EquipmentEntry(79, "79 lamp hanger generic", "ShipItemLampHook", ""));

        private static bool CanCreateWallHookBox(PrefabsDirectory directory, GameObject crate)
        {
            var hooks = WallHookPrefab(directory);
            return hooks != null && CanPack(hooks.GetComponent<ShipItem>()) &&
                CarrierCapacity(crate) >= WallHooksPerBox;
        }

        private static void AppendWallHookBox(Transform staging, Vector3 origin, List<GameObject> created)
        {
            var directory = Directory;
            var box = Clone(Resolve(directory, new EquipmentEntry(104,
                "104 crate of fishing hooks", "ShipItemCrate", "")), staging, origin, created.Count);
            created.Add(box.gameObject);
            // Save index 104 remains an ordinary empty crate. Real native hook
            // objects and their crate IDs persist through the native save path.
            box.amount = 0f;
            Extras.Add(box);
            WallHookBoxes.Add(box);
            var hookPrefab = WallHookPrefab(directory);
            for (var index = 0; index < WallHooksPerBox; index++)
            {
                var hook = Clone(hookPrefab, staging, origin, created.Count);
                created.Add(hook.gameObject);
                Extras.Add(hook);
                WallHooks.Add(hook, box);
            }
        }

        internal static bool TryValidateSelection(IEnumerable<string> selected, out string reason)
        {
            reason = null;
            var keys = new HashSet<string>(selected ?? Array.Empty<string>(), StringComparer.Ordinal);
            NormalizeSelection(keys);
            if (keys.Count == 0) return true;
            var directory = Directory;
            var catalog = GetCatalog();
            foreach (var key in keys)
            {
                var entry = catalog.FirstOrDefault(candidate => candidate.Key == key);
                if (entry == null || Resolve(directory, entry) == null)
                {
                    reason = "Additional equipment is unavailable in this game version: " + (entry?.DisplayName ?? key) + ". Clear that selection before starting.";
                    return false;
                }
            }
            return true;
        }

        internal static void NormalizeSelection(HashSet<string> selected)
        {
            selected.Remove("native:139:139 map F");
            selected.Remove("native:381:381 brining jar small");
            if (selected.Remove("native:71:71 fuel wood")) selected.Add("native:108:108 crate of firewood");
            if (selected.Remove("native:99:99 fishing hook")) selected.Add("native:104:104 crate of fishing hooks");
            if (selected.Remove("native:130:130 lantern candle")) selected.Add("native:131:131 lantern candle crate");
        }

        internal static void Reset()
        {
            owner = null;
            appended = false;
            appendSucceeded = false;
            packingComplete = false;
            failure = null;
            Extras.Clear();
            PackingCandidates.Clear();
            ScaledPackingTools.Clear();
            Carriers.Clear();
            Packed.Clear();
            WallHooks.Clear();
            WallHookBoxes.Clear();
        }

        internal static bool IsExtra(ShipItem item) => !ReferenceEquals(item, null) && Extras.Contains(item);
        internal static bool IsCarrier(ShipItem item) => !ReferenceEquals(item, null) &&
            (Carriers.Contains(item) || WallHookBoxes.Contains(item));
        internal static bool IsAdditional(ShipItem item) => IsExtra(item) ||
            IsCarrier(item);
        internal static bool IsPacked(ShipItem item) => !ReferenceEquals(item, null) && Packed.ContainsKey(item);

        internal static bool Append(ResolvedStart selected, IEnumerable<ShipItem> nativeCandidates)
        {
            if (appended) return appendSucceeded;
            owner = selected;
            appended = true;
            PackingCandidates.UnionWith(nativeCandidates.Where(item => item != null));
            var options = selected.Options?.Copy() ?? new StartingOptionsSettings();
            options.NormalizeEquipment();
            var valid = options.TryValidate(out var selectionReason) &&
                TryValidateSelection(options.EquipmentQuantities.Keys, out selectionReason);
            if (!valid)
            {
                Plugin.Instance?.Warn(selectionReason + " The normal starter supplies will still be placed.");
                packingComplete = true;
                return false;
            }
            var directory = Directory;
            var choices = GetCatalog().Where(entry => options.EquipmentQuantities.ContainsKey(entry.Key)).ToArray();
            var created = new List<GameObject>();
            GameObject staging = null;
            try
            {
                // Inactive staging prevents Awake from running until the native
                // coroutine activates each direct child of StarterSet.
                staging = new GameObject("NewBeginnings equipment staging");
                staging.SetActive(false);
                var origin = selected.StarterSet.transform.position;
                var existing = selected.StarterSet.GetComponentsInChildren<ShipItem>(true).FirstOrDefault();
                if (existing != null) origin = existing.transform.position;
                foreach (var choice in choices)
                for (var quantity = 0; quantity < options.EquipmentQuantities[choice.Key]; quantity++)
                {
                    if (choice.Key == WallHooksKey)
                    {
                        AppendWallHookBox(staging.transform, origin, created);
                        continue;
                    }
                    var item = Clone(Resolve(directory, choice), staging.transform, origin, created.Count);
                    created.Add(item.gameObject);
                    Extras.Add(item);
                    if (choice.AllowScaledPacking) ScaledPackingTools.Add(item);
                    PackingCandidates.Add(item);
                }
                var carrierPrefab = Resolve(directory, new EquipmentEntry(104, "104 crate of fishing hooks", "ShipItemCrate", ""));
                var capacity = CarrierCapacity(carrierPrefab);
                // Decide the roster once, before native activation. Native
                // ItemRigidbody.LateUpdate resets loose root scales to one;
                // reclassifying those items later can exceed the carrier count
                // and accidentally pack scaled items outside explicit support.
                PackingCandidates.IntersectWith(PackingCandidates.Where(CanPack).ToArray());
                if (capacity > 0)
                {
                    var count = PackingCandidates.Count;
                    for (var index = 0; index < (count + (long)capacity - 1) / capacity; index++)
                    {
                        var item = Clone(carrierPrefab, staging.transform, origin, created.Count);
                        created.Add(item.gameObject);
                        item.amount = 0f;
                        Carriers.Add(item);
                    }
                }
                else if (PackingCandidates.Count > 0)
                    Plugin.Instance?.Warn("Native starter crates are unavailable; starter supplies will use dockside ground placement.");
                foreach (var gameObject in created)
                    gameObject.transform.SetParent(selected.StarterSet.transform, true);
                appendSucceeded = true;
                return true;
            }
            catch (Exception exception)
            {
                // All clones are still inactive, so no native ownership was
                // registered. Preserve the untouched native starter roster.
                foreach (var gameObject in created)
                {
                    gameObject.SetActive(false);
                    gameObject.transform.SetParent(staging == null ? null : staging.transform, true);
                    UnityEngine.Object.Destroy(gameObject);
                }
                Extras.Clear();
                WallHooks.Clear();
                WallHookBoxes.Clear();
                PackingCandidates.Clear();
                ScaledPackingTools.Clear();
                Carriers.Clear();
                packingComplete = true;
                Plugin.Instance?.Error("Starter crate and additional equipment initialization failed; normal supplies will use dockside ground placement.", exception);
                return false;
            }
            finally
            {
                if (staging != null) UnityEngine.Object.Destroy(staging);
            }
        }

        private static ShipItem Clone(GameObject prefab, Transform staging, Vector3 origin, int ordinal)
        {
            var clone = UnityEngine.Object.Instantiate(prefab, staging, false);
            clone.SetActive(false);
            clone.name = prefab.name;
            // Native trade templates serialize mission index zero. These new
            // player supplies have no delivery assignment; clear that state
            // before OnLoad reads PlayerMissions or initializes crate inventory.
            clone.GetComponent<Good>()?.RegisterAsMissionless();
            clone.transform.SetPositionAndRotation(origin + new Vector3(ordinal % 8 * 0.5f, 2f + ordinal / 8 * 0.5f, 1.5f), Quaternion.identity);
            return clone.GetComponent<ShipItem>();
        }

        private static int CarrierCapacity(GameObject prefab)
        {
            if (prefab == null || Insert == null || Contents == null || CrateId == null || UiMeshes == null || UiButtons == null) return 0;
            var ui = Resources.FindObjectsOfTypeAll(CrateUiType).FirstOrDefault();
            if (ui == null) return 0;
            var meshes = UiMeshes.GetValue(ui) as Mesh[];
            var buttons = UiButtons.GetValue(ui) as Array;
            var mesh = prefab.GetComponent<MeshFilter>()?.sharedMesh;
            if (meshes == null || buttons == null || mesh == null) return 0;
            var index = Array.IndexOf(meshes, mesh);
            var capacity = index == 0 ? 12 : index == 1 ? 20 : index == 2 ? 30 : index == 3 ? 8 : 0;
            return Math.Min(capacity, buttons.Length);
        }

        private static bool CanPack(ShipItem item)
        {
            // Native insertion/withdrawal owns inventory scale. Only the exact
            // selected Kemy compass/inclinometer clones opt into that lifecycle;
            // other authored scaled equipment remains loose.
            return item != null && !item.big &&
                ((item.transform.localScale - Vector3.one).sqrMagnitude < 0.000001f ||
                 (ScaledPackingTools.Contains(item) && ModEquipment.ValidScale(item.transform.localScale)));
        }

        internal static bool TryPack(ResolvedStart selected, Action<ShipItem> prepare, out string reason)
        {
            reason = failure;
            if (failure != null) return false;
            if (!ReferenceEquals(owner, selected) || packingComplete) return true;
            try
            {
                foreach (var pair in WallHooks)
                {
                    if (Packed.ContainsKey(pair.Key)) continue;
                    var item = pair.Key;
                    var crate = pair.Value;
                    if (item.held != null || crate.held != null)
                    { reason = "a wall-hook box was picked up before packing"; return false; }
                    var inventory = crate.GetComponent(CrateType);
                    if (!item.sold || !crate.sold || crate.GetComponent<SaveablePrefab>().instanceId <= 0 || inventory == null)
                    { reason = "waiting for native wall-hook box ownership"; return false; }
                    var contents = (IList)Contents.GetValue(inventory);
                    if (contents.Count >= CarrierCapacity(crate.gameObject))
                        throw new InvalidOperationException("wall-hook box has no free native inventory slot");
                    prepare(item);
                    item.transform.SetPositionAndRotation(crate.transform.position, crate.transform.rotation);
                    Insert.Invoke(inventory, new object[] { item });
                    Packed.Add(item, crate);
                }
                if (Carriers.Count == 0) { packingComplete = true; return true; }
                foreach (var item in PackingCandidates.Where(CanPack))
                {
                    if (Packed.ContainsKey(item)) continue;
                    if (item.held != null) { reason = "a starter item was picked up before packing"; return false; }
                    var crate = Carriers.FirstOrDefault(candidate => candidate != null && candidate.held == null &&
                        candidate.GetComponent(CrateType) != null &&
                        ((IList)Contents.GetValue(candidate.GetComponent(CrateType))).Count < CarrierCapacity(candidate.gameObject));
                    if (crate == null) { reason = "waiting for an available native equipment crate"; return false; }
                    if (!item.sold || !crate.sold || crate.GetComponent<SaveablePrefab>().instanceId <= 0)
                    { reason = "waiting for native starter-item ownership"; return false; }
                    prepare(item);
                    item.transform.SetPositionAndRotation(crate.transform.position, crate.transform.rotation);
                    Insert.Invoke(crate.GetComponent(CrateType), new object[] { item });
                    Packed.Add(item, crate);
                }
                packingComplete = true;
                return true;
            }
            catch (Exception exception)
            {
                // An insertion may have partly completed; never retry it or
                // create replacements without a known native relationship.
                failure = reason = "native equipment packing failed: " + exception.Message;
                return false;
            }
        }

        internal static bool TryConfirmPacked(ShipItem item, out string reason)
        {
            reason = null;
            if (!Packed.TryGetValue(item, out var crate)) return true;
            var inventory = crate == null ? null : crate.GetComponent(CrateType);
            var saveable = item == null ? null : item.GetComponent<SaveablePrefab>();
            var contents = inventory == null ? null : Contents.GetValue(inventory) as IList;
            if (item == null || crate == null || saveable == null || contents == null ||
                contents.Cast<object>().Count(entry => ReferenceEquals(entry, item)) != 1 ||
                (int)CrateId.GetValue(saveable) != crate.GetComponent<SaveablePrefab>().instanceId ||
                contents.Count > CarrierCapacity(crate.gameObject) || !item.sold)
            {
                reason = "a starter item lost its native crate relationship";
                return false;
            }
            return true;
        }

        internal static void MovePacked(Action<ShipItem> resetDistance)
        {
            foreach (var pair in Packed)
            {
                pair.Key.transform.SetPositionAndRotation(pair.Value.transform.position, pair.Value.transform.rotation);
                pair.Key.GetItemRigidbody().ForceRigidbodyToWalkCol();
                resetDistance(pair.Key);
            }
        }
    }
}
