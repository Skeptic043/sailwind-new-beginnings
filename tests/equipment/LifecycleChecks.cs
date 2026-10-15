using System;
using System.Linq;
using System.Collections.Generic;
using NewBeginnings;
using UnityEngine;

static class LifecycleChecks
{
    static GameObject Item(int index, string name, Type type, bool big = false, float scale = 1)
    {
        var go = new GameObject(name);
        go.AddComponent(type);
        go.GetComponent<ShipItem>().big = big;
        go.transform.localScale = new Vector3(scale, scale, scale);
        go.AddComponent<SaveablePrefab>().prefabIndex = index;
        return go;
    }

    static ResolvedStart EmptyStart()
    {
        EquipmentChecks.Reset();
        var directory = new GameObject("directory").AddComponent<PrefabsDirectory>();
        PrefabsDirectory.instance = directory;
        directory.directory = new GameObject[390];
        return new ResolvedStart
        {
            StarterSet = new GameObject("starter").AddComponent<StarterSet>(),
            Options = new StartingOptionsSettings()
        };
    }

    internal static void Run(Action<bool, string> check)
    {
        RosterSurvivesNativeScaleReset(check);
        TradeClonesAreMissionless(check);
    }

    static void RosterSurvivesNativeScaleReset(Action<bool, string> check)
    {
        var start = EmptyStart();
        var directory = PrefabsDirectory.instance;
        var carrier = Item(104, "104 crate of fishing hooks", typeof(ShipItemCrate), true);
        directory.directory[104] = carrier;
        var mesh = new Mesh();
        carrier.AddComponent<MeshFilter>().sharedMesh = mesh;
        var ui = new GameObject("ui").AddComponent<CrateInventoryUI>();
        ui.containerMeshes = new[] { mesh };
        ui.buttons = new object[30];
        var packable = new List<ShipItem>();
        var loose = new List<ShipItem>();
        for (var index = 0; index < 47; index++)
            packable.Add(Item(40, "ordinary " + index, typeof(ShipItemBottle)).GetComponent<ShipItem>());
        for (var index = 0; index < 15; index++)
            packable.Add(Item(116, "116 map A", typeof(ShipItemFoldable), scale: .95f).GetComponent<ShipItem>());
        foreach (var scale in new[] { .3f, .4f })
            loose.Add(Item(830, "unsupported authored scaled tool", typeof(ShipItem), scale: scale).GetComponent<ShipItem>());
        for (var index = 0; index < 34; index++)
            loose.Add(Item(1, "large supply " + index, typeof(ShipItem), big: true).GetComponent<ShipItem>());
        foreach (var item in packable.Concat(loose))
            item.transform.SetParent(start.StarterSet.transform, true);
        check(AdditionalEquipment.Append(start, packable.Concat(loose)), "scale-reset roster append failed");
        var items = EquipmentChecks.Children(start).ToArray();
        var carriers = items.Where(AdditionalEquipment.IsCarrier).ToArray();
        check(carriers.Length == (packable.Count + 11) / 12, "carrier reservation ignored supported map or included unsupported scale");
        EquipmentChecks.NativeInitialize(start);
        foreach (var item in items)
            item.transform.localScale = Vector3.one;
        check(AdditionalEquipment.TryPack(start, _ => { }, out var reason), "native scale-reset packing stalled: " + reason);
        check(packable.All(item => EquipmentChecks.InventoryContains(item, carriers)), "planned item omitted after native scale reset");
        check(loose.All(item => !AdditionalEquipment.IsPacked(item)), "native scale reset broadened frozen roster");
    }

    static bool SimulateMissionDependentLoad(GameObject clone)
    {
        var good = clone.GetComponent<Good>();
        if (good.GetMissionIndex() > -1)
        {
            var missions = new string[1];
            _ = missions[good.GetMissionIndex()].Length;
        }

        if (clone.GetComponent<ShipItemCrate>() != null)
            clone.AddComponent<CrateInventory>();
        return true;
    }

    static void TradeClonesAreMissionless(Action<bool, string> check)
    {
        foreach (var definition in new[]
        {
            (1, "1 crate salmon (E)", typeof(ShipItemCrate), 12f),
            (10, "10 barrel water", typeof(ShipItemBottle), 1f),
            (11, "11 barrel rum", typeof(ShipItemBottle), 2f),
            (213, "213 (43) crate oranges", typeof(ShipItemCrate), 24f)
        }

        )
        {
            var start = EmptyStart();
            var prefab = Item(definition.Item1, definition.Item2, definition.Item3, big: true);
            prefab.AddComponent<Good>();
            prefab.GetComponent<ShipItem>().amount = definition.Item4;
            PrefabsDirectory.instance.directory[definition.Item1] = prefab;
            start.Options.EquipmentQuantities["native:" + definition.Item1 + ":" + definition.Item2] = 1;
            check(AdditionalEquipment.Append(start, Array.Empty<ShipItem>()), "trade clone append failed");
            var clone = start.StarterSet.transform.children.Single().gameObject;
            check(clone.GetComponent<Good>().GetMissionIndex() == -1, "clone retained unassigned mission zero");
            check(prefab.GetComponent<Good>().GetMissionIndex() == 0, "asset mission index mutated");
            check(SimulateMissionDependentLoad(clone), "missionless load branch failed");
            check(clone.GetComponent<ShipItem>().amount == definition.Item4, "filled container quantity changed");
            if (definition.Item3 == typeof(ShipItemCrate))
                check(clone.GetComponent<CrateInventory>() != null, "missionless crate load did not initialize inventory double");
        }
    }
}
