// Exercises production selection/append/packing state against explicit doubles.
// Does not simulate Unity physics, visual UI, native initialization, or saved games.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using NewBeginnings;

static class EquipmentChecks
{
    static int checks;
    internal static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
        checks++;
    }

    static EquipmentEntry[] Definitions
    {
        get
        {
            var definitionsField = typeof(AdditionalEquipment).GetField("Definitions", BindingFlags.Static | BindingFlags.NonPublic);
            return ((EquipmentEntry[])definitionsField.GetValue(null)).Where(entry => entry.Key != AdditionalEquipment.WallHooksKey).ToArray();
        }
    }

    internal static List<ShipItem> Children(ResolvedStart start) => start.StarterSet.transform.children.Select(t => t.gameObject.GetComponent<ShipItem>()).Where(i => i != null).ToList();
    static void Append(ResolvedStart start) => AdditionalEquipment.Append(start, Children(start));
    internal static void Reset()
    {
        AdditionalEquipment.Reset();
        GameState.currentlyLoading = false;
        BepInEx.Bootstrap.Chainloader.PluginInfos.Clear();
        Resources.all.Clear();
        Resources.crateUiDiscoveries = 0;
        PrefabsDirectory.instance = null;
        CrateInventory.failInsert = false;
        CrateInventory.failAfterInsert = false;
        CrateInventory.failOnInsertCall = 0;
        CrateInventory.insertCalls = 0;
        CrateInventory.inserted.Clear();
        UnityEngine.Object.cloneCalls = 0;
        UnityEngine.Object.failCloneAt = 0;
    }

    internal static ResolvedStart Setup(int buttons = 30, bool crateUi = true)
    {
        Reset();
        var directory = new GameObject("directory").AddComponent<PrefabsDirectory>();
        directory.directory = new GameObject[390];
        PrefabsDirectory.instance = directory;
        foreach (var entry in Definitions.Concat(new[] { new EquipmentEntry(79, "79 lamp hanger generic", "ShipItemLampHook", "") }))
        {
            var go = new GameObject(entry.PrefabName);
            var item = (ShipItem)go.AddComponent(typeof(ShipItem).Assembly.GetType(entry.ItemType));
            var save = go.AddComponent<SaveablePrefab>();
            save.prefabIndex = entry.Index;
            item.big = new[] { 1, 10, 11, 104, 108, 131, 213 }.Contains(entry.Index);
            item.amount = entry.Index == 104 ? 20 : entry.Index == 108 ? 12 : 0;
            directory.directory[entry.Index] = go;
        }

        var meshes = Enumerable.Range(0, 4).Select(_ => new Mesh()).ToArray();
        directory.directory[104].AddComponent<MeshFilter>().sharedMesh = meshes[0];
        if (crateUi)
        {
            var ui = new GameObject("ui").AddComponent<CrateInventoryUI>();
            ui.containerMeshes = meshes;
            ui.buttons = new object[buttons];
        }

        var set = new GameObject("starter").AddComponent<StarterSet>();
        var native = new GameObject("native cup");
        native.AddComponent<ShipItemBottle>();
        native.AddComponent<SaveablePrefab>();
        native.transform.SetParent(set.transform, true);
        return new ResolvedStart
        {
            StarterSet = set,
            Options = new StartingOptionsSettings()
        };
    }

    internal static void NativeInitialize(ResolvedStart start)
    {
        int id = 100;
        foreach (var item in Children(start))
        {
            item.gameObject.SetActive(true);
            item.sold = true;
            item.GetComponent<SaveablePrefab>().instanceId = id++;
            if (item is ShipItemCrate)
                item.gameObject.AddComponent<CrateInventory>();
        }
    }

    static int Main(string[] args)
    {
        try
        {
            Check(!args.Contains("--self-test-failure"), "intentional assertion failure");
            PackingPreservesInventory();
            NativeCapacityAndLooseFallback();
            OptionalCreationRollback();
            OpeningAndPickupReleaseOwnership();
            RemovedSuppliesStayExcluded();
            WallBundleChecks();
            CapacityChecks();
            EquipmentStateChecks.Run(Check);
            KemyPackingChecks.Run(Check);
            ModChecks.Run(Check);
            LifecycleChecks.Run(Check);
            Console.WriteLine($"PASS {checks} source-linked equipment checks.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: " + exception);
            return 1;
        }
    }

    static void PackingPreservesInventory()
    {
        var start = Setup();
        var native = Children(start).Single();
        foreach (var entry in Definitions)
            start.Options.EquipmentQuantities[entry.Key] = 1;
        Check(AdditionalEquipment.TryValidateSelection(start.Options.EquipmentQuantities.Keys, out _), "fixture catalogue selection rejected");
        Append(start);
        var items = Children(start);
        var extras = items.Where(AdditionalEquipment.IsExtra).ToArray();
        var carriers = items.Where(AdditionalEquipment.IsCarrier).ToArray();
        var expectedPacked = extras.Where(item => !item.big).Append(native).ToArray();
        Check(extras.Length == Definitions.Length, "selected quantities changed during append");
        Check(carriers.Length == (expectedPacked.Length + 11) / 12, "insufficient or excess overflow carriers");
        Check(extras.All(item => !item.gameObject.activeSelf), "extras activated before native initialization");
        Check(carriers.All(item => item.amount == 0 && !item.gameObject.activeSelf), "carrier contents or activation changed");
        Check(extras.Single(item => item.GetComponent<SaveablePrefab>().prefabIndex == 104).amount == 20 && extras.Single(item => item.GetComponent<SaveablePrefab>().prefabIndex == 108).amount == 12, "selected supply boxes lost native contents");
        Check(!AdditionalEquipment.IsExtra(native) && !native.sold, "native roster identity or ownership changed");
        Append(start);
        Check(Children(start).SequenceEqual(items), "repeated append created duplicates");
        Check(!AdditionalEquipment.TryPack(start, _ => { }, out _), "packing ran before native initialization");
        NativeInitialize(start);
        var prepared = new List<ShipItem>();
        var bodyBox = new GameObject("solid body box").AddComponent<Collider>();
        expectedPacked.First().GetItemRigidbody().subcolliders = new List<Collider> { bodyBox };
        Check(AdditionalEquipment.TryPack(start, prepared.Add, out var reason), "packing failed: " + reason);
        Check(bodyBox.isTrigger, "packed item kept a solid body box inside its crate");
        Check(prepared.Count == expectedPacked.Length, "eligible items were omitted or prepared twice");
        foreach (var item in expectedPacked)
            Check(InventoryContains(item, carriers), "native membership or saved crate relationship missing");
        Check(extras.Where(item => item.big).All(item => !AdditionalEquipment.IsPacked(item)), "large supply packed");
        Check(AdditionalEquipment.TryPack(start, prepared.Add, out _) && prepared.Count == expectedPacked.Length, "completed packing inserted items again");
        Check(expectedPacked.All(item => BodyFrozenAtCrate(item, carriers)), "packed body not frozen at its crate");
        foreach (var carrier in carriers)
            carrier.transform.position = carrier.transform.position + new Vector3(500f, 0f, 0f);
        AdditionalEquipment.MovePacked(_ => { });
        Check(expectedPacked.All(item => BodyFrozenAtCrate(item, carriers)), "packed body left behind when its crate moved");
        var withdrawn = expectedPacked.First();
        var crate = carriers.Single(carrier => carrier.GetComponent<CrateInventory>().containedItems.Contains(withdrawn));
        crate.GetComponent<CrateInventory>().WithdrawItem(withdrawn);
        Check(!InventoryContains(withdrawn, carriers) && withdrawn.GetComponent<SaveablePrefab>().currentCrateId == 0, "withdrawal retained native relationship");
        var contents = carriers.SelectMany(carrier => carrier.GetComponent<CrateInventory>().containedItems).ToArray();
        AdditionalEquipment.Reset();
        Check(items.All(item => !AdditionalEquipment.IsAdditional(item) && !AdditionalEquipment.IsPacked(item)), "reset retained startup ownership");
        Check(carriers.SelectMany(carrier => carrier.GetComponent<CrateInventory>().containedItems).SequenceEqual(contents), "reset changed native inventory");
    }

    static bool BodyFrozenAtCrate(ShipItem item, IEnumerable<ShipItem> carriers)
    {
        var crate = carriers.Single(carrier => carrier.GetComponent<CrateInventory>().containedItems.Contains(item));
        var body = item.GetItemRigidbody();
        return body.resets > 0 && (body.resetPosition - crate.transform.position).sqrMagnitude < 1e-6f;
    }

    internal static bool InventoryContains(ShipItem item, IEnumerable<ShipItem> carriers)
    {
        var owners = carriers.Where(carrier => carrier.GetComponent<CrateInventory>().containedItems.Contains(item)).ToArray();
        return owners.Length == 1 && owners[0].GetComponent<CrateInventory>().containedItems.Count(candidate => ReferenceEquals(item, candidate)) == 1 && item.GetComponent<SaveablePrefab>().currentCrateId == owners[0].GetComponent<SaveablePrefab>().instanceId;
    }

    static void NativeCapacityAndLooseFallback()
    {
        var start = Setup(8);
        foreach (var entry in Definitions)
            start.Options.EquipmentQuantities[entry.Key] = 1;
        Append(start);
        NativeInitialize(start);
        Check(AdditionalEquipment.TryPack(start, _ => { }, out _), "limited button packing failed");
        Check(Children(start).Where(AdditionalEquipment.IsCarrier).All(carrier => carrier.GetComponent<CrateInventory>().containedItems.Count <= 8), "button ceiling exceeded");
        start = Setup(crateUi: false);
        start.Options.EquipmentQuantities[Definitions.Single(entry => entry.Index == 40).Key] = 1;
        Append(start);
        NativeInitialize(start);
        Check(Children(start).Count == 2 && !Children(start).Any(AdditionalEquipment.IsCarrier), "missing capacity dropped items or created unusable carriers");
        Check(AdditionalEquipment.TryPack(start, _ => { }, out _), "loose fallback stalled");
    }

    static void OptionalCreationRollback()
    {
        var start = Setup();
        var entry = Definitions.Single(candidate => candidate.Index == 40);
        start.Options.EquipmentQuantities[entry.Key] = 1;
        PrefabsDirectory.instance.directory[entry.Index].name = "different item";
        Check(!AdditionalEquipment.TryValidateSelection(start.Options.EquipmentQuantities.Keys, out _), "spoofed native index accepted");
        Append(start);
        Check(Children(start).Count == 1, "unavailable selection spawned a replacement");
        Check(AdditionalEquipment.TryPack(start, _ => { }, out _), "unavailable extra blocked original supplies");
        start = Setup();
        foreach (var selected in Definitions.Take(5))
            start.Options.EquipmentQuantities[selected.Key] = 1;
        UnityEngine.Object.failCloneAt = 3;
        Append(start);
        Check(Children(start).Count == 1, "creation failure retained partial clones");
        Check(AdditionalEquipment.TryPack(start, _ => { }, out _), "optional rollback blocked native supplies");
    }

    static void OpeningAndPickupReleaseOwnership()
    {
        var start = Setup();
        var native = Children(start).Single();
        native.amount = 3;
        native.health = .75f;
        var food = new GameObject("native food").AddComponent<ShipItemFood>();
        food.gameObject.AddComponent<SaveablePrefab>();
        food.amount = 4;
        food.health = .6f;
        food.transform.SetParent(start.StarterSet.transform, true);
        Append(start);
        NativeInitialize(start);
        Check(AdditionalEquipment.TryPack(start, _ => { }, out _), "native supply packing failed");
        var carrier = Children(start).Single(AdditionalEquipment.IsCarrier);
        var contents = carrier.GetComponent<CrateInventory>().containedItems.ToArray();
        var unrelated = new GameObject("unrelated crate").AddComponent<CrateInventory>();
        Check(!HandoffHarness.Invoke(start, unrelated), "unrelated crate released startup ownership");
        Check(HandoffHarness.Invoke(start, carrier.GetComponent<CrateInventory>()), "generated crate opening retained startup ownership");
        Check(carrier.GetComponent<CrateInventory>().containedItems.SequenceEqual(contents), "handoff changed inventory contents");
        Check(native.amount == 3 && native.health == .75f && food.amount == 4 && food.health == .6f, "packing or handoff altered native state");
        start = Setup();
        Append(start);
        NativeInitialize(start);
        AdditionalEquipment.TryPack(start, _ => { }, out _);
        native = Children(start).First();
        Check(HandoffHarness.Pickup(start, native), "packed native item pickup retained startup ownership");
        Check(!AdditionalEquipment.IsPacked(native), "pickup retained packing ownership");
    }

    static void RemovedSuppliesStayExcluded()
    {
        var start = Setup();
        var native = Children(start).Single();
        var removedFood = new GameObject("removed food").AddComponent<ShipItemFood>();
        removedFood.gameObject.AddComponent<SaveablePrefab>();
        removedFood.transform.SetParent(start.StarterSet.transform, true);
        var removedMap = new GameObject("removed map").AddComponent<ShipItemFoldable>();
        removedMap.gameObject.AddComponent<SaveablePrefab>();
        removedMap.transform.SetParent(start.StarterSet.transform, true);
        var survivors = RosterHarness.Filter(start.StarterSet, new HashSet<ShipItem> { removedFood }, item => ReferenceEquals(item, removedMap));
        Check(survivors.SequenceEqual(new[] { native }), "scheduled removals entered surviving roster");
        start.Options.EquipmentQuantities[Definitions.Single(entry => entry.Index == 115).Key] = 1;
        AdditionalEquipment.Append(start, survivors);
        NativeInitialize(start);
        Check(AdditionalEquipment.TryPack(start, _ => { }, out _), "surviving roster packing failed");
        Check(!AdditionalEquipment.IsPacked(removedFood) && !AdditionalEquipment.IsPacked(removedMap) && AdditionalEquipment.IsPacked(native), "removed supply packed or survivor omitted");
        Check(AdditionalEquipment.IsPacked(Children(start).Single(AdditionalEquipment.IsExtra)), "optional map mistaken for removed map");
        start = Setup();
        AdditionalEquipment.Append(start, RosterHarness.Filter(start.StarterSet, Children(start).ToHashSet(), _ => false));
        Check(!Children(start).Any(AdditionalEquipment.IsCarrier) && AdditionalEquipment.TryPack(start, _ => { }, out _), "empty surviving roster created a carrier or stalled");
    }

    static void WallBundleChecks()
    {
        var start = Setup();
        start.Options.EquipmentQuantities[AdditionalEquipment.WallHooksKey] = 10;
        start.Options.EquipmentQuantities[Definitions.Single(e => e.Index == 52).Key] = 1;
        Check(AdditionalEquipment.Append(start, Children(start)), "ten hook bundles refused");
        var items = Children(start);
        var boxes = items.Where(i => AdditionalEquipment.IsExtra(i) && AdditionalEquipment.IsCarrier(i)).ToArray();
        Check(boxes.Length == 10 && boxes.All(i => i.amount == 0 && i.GetComponent<SaveablePrefab>().prefabIndex == 104), "dedicated boxes not empty native104");
        var hooks = items.Where(i => i is ShipItemLampHook).ToArray();
        Check(hooks.Length == 120, "ten boxes not 120 real wall hooks");
        Check(items.Where(i => !(i is ShipItemLampHook)).All(other => hooks.All(hook =>
            (hook.transform.position - other.transform.position).sqrMagnitude > 9f)), "wall hooks spawned within reach of other supplies");
        Check(!AdditionalEquipment.TryPack(start, _ => { }, out _), "hooks packed before native ownership");
        var prepared = new List<ShipItem>();
        NativeInitialize(start);
        Check(AdditionalEquipment.TryPack(start, prepared.Add, out _), "dedicated hooks failed packing");
        for (var index = 0; index < boxes.Length; index++)
            Check(boxes[index].GetComponent<CrateInventory>().containedItems.SequenceEqual(hooks.Skip(index * 12).Take(12)), "hook box order or contents changed");
        Check(prepared.Count == CrateInventory.inserted.Count && prepared.ToHashSet().SetEquals(CrateInventory.inserted), "prepared and inserted item identities differ");
        Check(Resources.crateUiDiscoveries == 1, "120 hooks rescanned the native crate UI: " + Resources.crateUiDiscoveries);
        Check(items.Where(i => i is ShipItemLampHook).All(i => InventoryContains(i, boxes)), "hook native crate IDs not retained");
        Check(items.Single(i => AdditionalEquipment.IsExtra(i) && i is ShipItemFood).GetComponent<SaveablePrefab>().currentCrateId != boxes[0].GetComponent<SaveablePrefab>().instanceId, "generic supplies mixed into wall hook box");
        Check(AdditionalEquipment.TryPack(start, _ =>
        {
            throw new Exception("repeat insert");
        }, out _), "repeated hook packing not idempotent");
        Check(HandoffHarness.Invoke(start, boxes[0].GetComponent<CrateInventory>()), "hook box opening did not release setup");
        Check(boxes.All(i => i.GetComponent<CrateInventory>().containedItems.Count == 12), "reset deleted persisted hooks");
        start = Setup(8);
        Check(!AdditionalEquipment.GetCatalog().Any(e => e.Key == AdditionalEquipment.WallHooksKey), "insufficient box capacity offered bundle");
        start = Setup();
        PrefabsDirectory.instance.directory[79].name = "imposter";
        Check(!AdditionalEquipment.TryValidateSelection(new[] { AdditionalEquipment.WallHooksKey }, out _), "missing exact wall hook dependency accepted");
        start = Setup();
        start.Options.EquipmentQuantities[AdditionalEquipment.WallHooksKey] = 1;
        UnityEngine.Object.failCloneAt = 5;
        Check(!AdditionalEquipment.Append(start, Children(start)) && Children(start).Count == 1, "partial hook bundle failed rollback");
        start = Setup();
        Check(AdditionalEquipment.Append(start, Children(start)), "hook rollback polluted next start");
        NativeInitialize(start);
        Check(AdditionalEquipment.TryPack(start, _ => { }, out _), "stale bundle map survived rollback/reset");
        var keys = new HashSet<string>
        {
            "native:130:130 lantern candle",
            "native:131:131 lantern candle crate"
        };
        AdditionalEquipment.NormalizeSelection(keys);
        Check(keys.SetEquals(new[] { "native:131:131 lantern candle crate" }), "candle migration failed deduplication");
    }

    static void CapacityChecks()
    {
        bool OffersHooks() => AdditionalEquipment.GetCatalog().Any(e => e.Key == AdditionalEquipment.WallHooksKey);
        var start = Setup();
        var ui = Resources.all.OfType<CrateInventoryUI>().Single();
        var filter = PrefabsDirectory.instance.directory[104].GetComponent<MeshFilter>();
        var mesh = filter.sharedMesh;
        Check(OffersHooks() && Resources.crateUiDiscoveries == 1, "initial crate UI capability not discovered");
        ui.buttons = new object[8];
        Check(!OffersHooks(), "replaced button array did not lower live capacity");
        ui.buttons = new object[30];
        Check(OffersHooks(), "restored button array did not restore capacity");
        ui.containerMeshes = new[] { new Mesh(), new Mesh(), new Mesh(), mesh };
        Check(!OffersHooks(), "replaced mesh array did not change native capacity");
        ui.containerMeshes[3] = new Mesh();
        ui.containerMeshes[0] = mesh;
        Check(OffersHooks(), "in-place mesh array edit did not update capacity");
        filter.sharedMesh = ui.containerMeshes[3];
        Check(!OffersHooks(), "crate mesh replacement did not select its live capacity");
        filter.sharedMesh = mesh;
        Check(Resources.crateUiDiscoveries == 1, "live field changes unnecessarily rescanned Resources");
        var replacement = new GameObject("replacement UI").AddComponent<CrateInventoryUI>();
        replacement.containerMeshes = new[] { mesh };
        replacement.buttons = new object[8];
        UnityEngine.Object.Destroy(ui.gameObject);
        Check(!OffersHooks() && Resources.crateUiDiscoveries == 2, "destroyed cached UI was not rediscovered");
        replacement.buttons = new object[30];
        Check(OffersHooks(), "replacement UI live capacity not observed");
        AdditionalEquipment.Reset();
        Check(OffersHooks() && Resources.crateUiDiscoveries == 3, "reset retained cached UI discovery");
        start = Setup(crateUi: false);
        Check(!OffersHooks(), "missing UI advertised hook capacity");
        ui = new GameObject("late UI").AddComponent<CrateInventoryUI>();
        ui.containerMeshes = new[] { PrefabsDirectory.instance.directory[104].GetComponent<MeshFilter>().sharedMesh };
        ui.buttons = new object[12];
        Check(OffersHooks() && Resources.crateUiDiscoveries == 2, "missing UI was permanently cached");
        start = Setup();
        ui = Resources.all.OfType<CrateInventoryUI>().Single();
        start.Options.EquipmentQuantities[AdditionalEquipment.WallHooksKey] = 1;
        Append(start);
        NativeInitialize(start);
        var prepared = 0;
        Check(!AdditionalEquipment.TryPack(start, _ =>
        {
            if (++prepared == 5)
                ui.buttons = new object[4];
        }, out var reason) && reason.StartsWith("native equipment packing failed:"), "mid-pack native capacity failure changed");
        Check(CrateInventory.inserted.Count == 5 && Children(start).Count(AdditionalEquipment.IsPacked) == 5, "mid-pack capacity change altered already inserted items");
        ui.buttons = new object[30];
        Check(!AdditionalEquipment.TryPack(start, _ => { }, out _) && CrateInventory.inserted.Count == 5, "uncertain insertion/capacity failure was retried");
    }
}
