using System;
using System.Linq;
using System.Reflection;
using NewBeginnings;
using UnityEngine;

static class KemyPackingChecks
{
    static GameObject Tool(int id, string name, bool big, float scale)
    {
        var go = new GameObject(name);
        var item = go.AddComponent<ShipItem>();
        item.name = name;
        item.category = TransactionCategory.toolsAndSupplies;
        item.big = big;
        go.transform.localScale = new Vector3(scale, scale, scale);
        go.AddComponent<SaveablePrefab>().prefabIndex = id;
        go.AddComponent<Rigidbody>();
        go.AddComponent<Collider>();
        go.AddComponent<MeshFilter>().sharedMesh = new Mesh();
        go.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            new Material
            {
                shader = new Shader()
            }
        };
        PrefabsDirectory.instance.directory[id] = go;
        return go;
    }

    internal static void Run(Action<bool, string> check)
    {
        EquipmentChecks.Reset();
        var dir = new GameObject("dir").AddComponent<PrefabsDirectory>();
        PrefabsDirectory.instance = dir;
        dir.directory = new GameObject[1000];
        BepInEx.Bootstrap.Chainloader.PluginInfos["com.larsonlogistics.sailwind.navsuite"] = new()
        {
            Instance = new KemyNavTools.TestPlugin(),
            Metadata = new()
            {
                GUID = "com.larsonlogistics.sailwind.navsuite",
                Name = "Navigation Suite"
            }
        };
        var compass = Tool(831, "BearingCompass", false, .3f);
        var incl = Tool(830, "Inclinometer", false, .4f);
        var binnacle = Tool(832, "Binnacle", true, 1);
        KemyNavTools.PreloadDirectoryPatch.compassPrefab = compass;
        KemyNavTools.PreloadDirectoryPatch.inclinometerPrefab = incl;
        KemyNavTools.PreloadDirectoryPatch.binnaclePrefab = binnacle;
        var entries = ModEquipment.Discover(dir);
        check(entries.Length == 3 && entries.Count(e => e.AllowScaledPacking) == 2, "exact Kemy pair metadata incorrect");
        check(!entries.Single(e => e.PrefabName == "Binnacle").AllowScaledPacking, "binnacle metadata allows packing");
        var donor = new GameObject("104 crate of fishing hooks");
        donor.AddComponent<ShipItemCrate>().big = true;
        donor.AddComponent<SaveablePrefab>().prefabIndex = 104;
        var mesh = new Mesh();
        donor.AddComponent<MeshFilter>().sharedMesh = mesh;
        dir.directory[104] = donor;
        var ui = new GameObject("ui").AddComponent<CrateInventoryUI>();
        ui.containerMeshes = new[] { mesh };
        ui.buttons = new object[30];
        var start = new ResolvedStart
        {
            StarterSet = new GameObject("starter").AddComponent<StarterSet>(),
            Options = new StartingOptionsSettings()
        };
        start.Options.EquipmentQuantities[entries.Single(e => e.PrefabName == "BearingCompass").Key] = 12;
        start.Options.EquipmentQuantities[entries.Single(e => e.PrefabName == "Inclinometer").Key] = 1;
        start.Options.EquipmentQuantities[entries.Single(e => e.PrefabName == "Binnacle").Key] = 2;
        var map = new GameObject("116 map A").AddComponent<ShipItemFoldable>();
        map.gameObject.AddComponent<SaveablePrefab>().prefabIndex = 116;
        map.transform.localScale = new Vector3(.95f, .95f, .95f);
        map.transform.SetParent(start.StarterSet.transform, true);
        var generic = new GameObject("generic scaled tool").AddComponent<ShipItem>();
        generic.gameObject.AddComponent<SaveablePrefab>();
        generic.transform.localScale = new Vector3(.5f, .5f, .5f);
        generic.transform.SetParent(start.StarterSet.transform, true);
        check(AdditionalEquipment.Append(start, new[] { map, generic }), "Kemy packing append failed");
        var items = start.StarterSet.transform.children.Select(t => t.gameObject.GetComponent<ShipItem>()).ToArray();
        var carriers = items.Where(AdditionalEquipment.IsCarrier).ToArray();
        var tools = items.Where(i => i.GetComponent<SaveablePrefab>().prefabIndex == 830 || i.GetComponent<SaveablePrefab>().prefabIndex == 831).ToArray();
        check(carriers.Length == 2 && tools.Length == 13, "13 authored scaled tools did not reserve two12-slot carriers");
        var one = tools[0];
        check(!AdditionalEquipment.TryPack(start, _ => { }, out _), "packed Kemy before native ownership");
        int id = 1;
        foreach (var item in items)
        {
            item.gameObject.SetActive(true);
            item.sold = true;
            item.GetComponent<SaveablePrefab>().instanceId = id++;
            if (item is ShipItemCrate)
                item.gameObject.AddComponent<CrateInventory>();
            item.transform.localScale = Vector3.one;
        }

        check(AdditionalEquipment.TryPack(start, _ => { }, out var reason), "Kemy initialized packing failed: " + reason);
        check(tools.All(AdditionalEquipment.IsPacked), "Kemy pair not packed");
        check(items.Where(i => i.GetComponent<SaveablePrefab>().prefabIndex == 832).All(i => !AdditionalEquipment.IsPacked(i)), "binnacle packed");
        check(AdditionalEquipment.IsPacked(map) && !AdditionalEquipment.IsPacked(generic), "supported native map or unsupported generic scaled roster incorrect");
        check(carriers.Select(c => c.GetComponent<CrateInventory>().containedItems.Count).SequenceEqual(new[] { 12, 2 }), "Kemy and supported native map overflow capacity wrong");
        check(tools.All(i => EquipmentChecks.InventoryContains(i, carriers)), "Kemy native crate relationships missing");
        check(AdditionalEquipment.TryPack(start, _ =>
        {
            throw new Exception("duplicate insertion");
        }, out _), "Kemy repeat packing not idempotent");
        var crate = carriers.Single(c => c.GetComponent<CrateInventory>().containedItems.Contains(one));
        crate.GetComponent<CrateInventory>().WithdrawItem(one);
        check(one.GetComponent<SaveablePrefab>().currentCrateId == 0 && one.transform.localScale.x == 1, "native Kemy withdrawal contract changed");
        AdditionalEquipment.Reset();
        var failing = new ResolvedStart
        {
            StarterSet = new GameObject("failing starter").AddComponent<StarterSet>(),
            Options = new StartingOptionsSettings()
        };
        failing.Options.EquipmentQuantities[entries.Single(e => e.PrefabName == "BearingCompass").Key] = 2;
        UnityEngine.Object.failCloneAt = UnityEngine.Object.cloneCalls + 2;
        check(!AdditionalEquipment.Append(failing, Array.Empty<ShipItem>()) && failing.StarterSet.transform.children.Count == 0, "partial Kemy clone failure not rolled back");
        UnityEngine.Object.failCloneAt = 0;
        check(!AdditionalEquipment.Append(failing, Array.Empty<ShipItem>()) && AdditionalEquipment.TryPack(failing, _ => throw new Exception("packed after failure"), out _), "failed start did not keep its finished failure state");
        AdditionalEquipment.Reset();
        var next = new ResolvedStart
        {
            StarterSet = new GameObject("next starter").AddComponent<StarterSet>(),
            Options = new StartingOptionsSettings()
        };
        next.Options.EquipmentQuantities[entries.Single(e => e.PrefabName == "BearingCompass").Key] = 1;
        check(AdditionalEquipment.Append(next, Array.Empty<ShipItem>()), "Kemy rollback prevented subsequent start");
        EquipmentChecks.NativeInitialize(next);
        check(AdditionalEquipment.TryPack(next, _ => { }, out _), "Kemy subsequent start failed packing");
        var nextTools = EquipmentChecks.Children(next).Where(AdditionalEquipment.IsExtra).ToArray();
        check(nextTools.Length == 1 && EquipmentChecks.InventoryContains(nextTools[0], EquipmentChecks.Children(next).Where(AdditionalEquipment.IsCarrier)), "Kemy rollback left incorrect clone ownership");
        check(compass.transform.localScale.x == .3f && incl.transform.localScale.x == .4f && binnacle.GetComponent<ShipItem>().big, "Kemy asset templates mutated");
    }
}
