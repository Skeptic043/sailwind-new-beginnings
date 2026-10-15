using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NewBeginnings;
using UnityEngine;

static class ModChecks
{
    static Type GenericComponent, CustomItem;
    static void Reset()
    {
        EquipmentChecks.Reset();
        var go = new GameObject("directory");
        PrefabsDirectory.instance = go.AddComponent<PrefabsDirectory>();
        PrefabsDirectory.instance.directory = new GameObject[1000];
    }

    static void Provider(string id, object instance)
    {
        BepInEx.Bootstrap.Chainloader.PluginInfos[id] = new()
        {
            Instance = instance,
            Metadata = new()
            {
                GUID = id,
                Name = id
            }
        };
    }

    static void Visual(GameObject go)
    {
        go.AddComponent<MeshFilter>().sharedMesh = new Mesh();
        go.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            new Material
            {
                shader = new Shader()
            }
        };
    }

    static GameObject Tool(int id, string name, TransactionCategory category = TransactionCategory.toolsAndSupplies, Type itemType = null)
    {
        var go = new GameObject(name);
        var item = (ShipItem)go.AddComponent(itemType ?? typeof(ShipItem));
        item.name = name;
        item.category = category;
        var save = go.AddComponent<SaveablePrefab>();
        save.prefabIndex = id;
        go.AddComponent<Rigidbody>();
        go.AddComponent<Collider>();
        Visual(go);
        PrefabsDirectory.instance.directory[id] = go;
        return go;
    }

    static void GenericProvider()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("TestMod" + Guid.NewGuid()), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("main");
        GenericComponent = module.DefineType("TestMod.Tool", TypeAttributes.Public, typeof(MonoBehaviour)).CreateType();
        CustomItem = module.DefineType("TestMod.Instrument", TypeAttributes.Public, typeof(ShipItem)).CreateType();
        var plugin = module.DefineType("TestMod.Plugin", TypeAttributes.Public).CreateType();
        Provider("test.tools", Activator.CreateInstance(plugin));
    }

    internal static void Run(Action<bool, string> check)
    {
        SelectionMigration(check);
        GenericProviderIdentity(check);
        NavigationProvider(check);
        PropellerContract(check);
        WindIndicatorEligibility(check);
        RegisteredNativeTools(check);
        FurnitureExclusion(check);
        AmbiguousProviderIdentity(check);
        GenericScaleEligibility(check);
        CustomItemOwnership(check);
        VisibleGeometry(check);
        NavigationDisplayName(check);
    }

    static void SelectionMigration(Action<bool, string> check)
    {
        var ids = new HashSet<string>
        {
            "native:139:139 map F",
            "native:381:381 brining jar small",
            "native:71:71 fuel wood",
            "native:99:99 fishing hook",
            "native:104:104 crate of fishing hooks",
            "unchanged"
        };
        AdditionalEquipment.NormalizeSelection(ids);
        check(ids.SetEquals(new[] { "native:108:108 crate of firewood", "native:104:104 crate of fishing hooks", "unchanged" }), "retired native selection migration/deduplication");
        AdditionalEquipment.NormalizeSelection(ids);
        check(ids.Count == 3, "migration idempotence");
    }

    static void GenericProviderIdentity(Action<bool, string> check)
    {
        Reset();
        GenericProvider();
        var tool = Tool(800, "800 compass");
        tool.AddComponent(GenericComponent);
        var entries = ModEquipment.Discover(PrefabsDirectory.instance);
        check(entries.Length == 1 && entries[0].IsModded && entries[0].SourceId == "test.tools", "generic verified provenance discovery");
        var key = entries[0].Key;
        PrefabsDirectory.instance.directory[800] = null;
        PrefabsDirectory.instance.directory[850] = tool;
        tool.name = "850 compass";
        tool.GetComponent<SaveablePrefab>().prefabIndex = 850;
        entries = ModEquipment.Discover(PrefabsDirectory.instance);
        check(entries.Length == 1 && entries[0].Key == key && entries[0].Index == 850, "mod key changed after index shift");
        var start = new ResolvedStart
        {
            StarterSet = new GameObject("start").AddComponent<StarterSet>(),
            Options = new StartingOptionsSettings()
        };
        start.Options.EquipmentQuantities[key] = 1;
        AdditionalEquipment.Append(start, Array.Empty<ShipItem>());
        var spawned = start.StarterSet.transform.children.Single().gameObject;
        check(spawned.GetComponent<SaveablePrefab>().prefabIndex == 850 && spawned.GetComponent(GenericComponent) != null && !spawned.activeSelf, "mod spawning lost runtime index, companion component, or native initialization boundary");
        check(spawned.transform.localScale.x == 1, "mod spawning changed authored template scale");
        AdditionalEquipment.Reset();
        tool.transform.localScale = new Vector3(.4f, .4f, .4f);
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "scaled generic item offered");
        tool.transform.localScale = Vector3.one;
        tool.GetComponent<ShipItem>().big = true;
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "big generic item offered");
        tool.GetComponent<ShipItem>().big = false;
        var duplicate = Tool(860, "860 compass");
        duplicate.AddComponent(GenericComponent);
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "ambiguous duplicate mod key accepted");
        PrefabsDirectory.instance.directory[860] = null;
        tool.scene = new Scene
        {
            valid = true
        };
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "scene/shop clone offered");
        tool.scene = new Scene();
        tool.GetComponent<SaveablePrefab>().instanceId = 1;
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "registered owned item offered as template");
        tool.GetComponent<SaveablePrefab>().instanceId = 0;
        tool.GetComponent<ShipItem>().category = TransactionCategory.furniture;
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "generic furniture offered");
        tool.GetComponent<ShipItem>().category = TransactionCategory.toolsAndSupplies;
        tool.AddComponent<Good>();
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "trade good offered as tool");
        Reset();
        GenericProvider();
        tool = Tool(100, "100 native-like");
        tool.AddComponent(GenericComponent);
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "native range mislabeled as mod");
    }

    static void NavigationProvider(Action<bool, string> check)
    {
        Reset();
        Provider("com.larsonlogistics.sailwind.navsuite", new KemyNavTools.TestPlugin());
        var tool = Tool(830, "Inclinometer", TransactionCategory.retailFood);
        KemyNavTools.PreloadDirectoryPatch.inclinometerPrefab = tool;
        tool.transform.localScale = new Vector3(.4f, .4f, .4f);
        var entries = ModEquipment.Discover(PrefabsDirectory.instance);
        check(entries.Length == 1, "verified authored-scale Kemy item omitted");
        tool.transform.localScale = Vector3.one;
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 1, "verified small unit-scale inclinometer not offered");
        var other = Tool(840, "Imposter", TransactionCategory.retailFood);
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 1, "unverified category0 item accepted");
        tool.transform.localScale = new Vector3(.4f, .4f, .4f);
        var kemyKey = ModEquipment.Discover(PrefabsDirectory.instance).Single().Key;
        var kemyStart = new ResolvedStart
        {
            StarterSet = new GameObject("kemy start").AddComponent<StarterSet>(),
            Options = new StartingOptionsSettings()
        };
        kemyStart.Options.EquipmentQuantities[kemyKey] = 2;
        check(AdditionalEquipment.Append(kemyStart, Array.Empty<ShipItem>()), "scaled Kemy clones refused");
        var kemyClones = kemyStart.StarterSet.transform.children.Select(t => t.gameObject.GetComponent<ShipItem>()).ToArray();
        check(kemyClones.Length == 2 && kemyClones.All(i => i.transform.localScale.x == .4f && !i.gameObject.activeSelf), "scaled Kemy quantity or authored scale lost");
        check(AdditionalEquipment.TryPack(kemyStart, _ =>
        {
            throw new Exception("scaled Kemy packed");
        }, out _), "loose Kemy packing stalled");
        AdditionalEquipment.Reset();
        tool.GetComponent<ShipItem>().big = true;
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 1, "verified big Kemy omitted");
        tool.GetComponent<ShipItem>().big = false;
        tool.transform.localScale = new Vector3(float.NaN, 1, 1);
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "invalid Kemy scale admitted");
        tool.transform.localScale = Vector3.one;
    }

    static void PropellerContract(Action<bool, string> check)
    {
        Reset();
        Provider("DogEggz.JetPump", new JetPump.TestPlugin());
        var tool = Tool(604, "604 propeller", TransactionCategory.furniture);
        JetPump.PumpAssets.Pump = tool;
        JetPump.Plugin.Ready = true;
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "propeller without contract marker offered");
        var marker = new GameObject("JetPumpAssetContract_v13");
        marker.transform.SetParent(tool.transform, true);
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 1, "verified propeller not offered");
        JetPump.Plugin.Ready = false;
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "unready propeller offered");
    }

    static void WindIndicatorEligibility(Action<bool, string> check)
    {
        Reset();
        Provider("com.nandbrew.Windicators", new Windicators.TestPlugin());
        var tool = Tool(505, "505 weathervane A large", TransactionCategory.furniture);
        Windicators.AssetTools.itemPrefabs.Clear();
        Windicators.AssetTools.itemPrefabs[505] = tool;
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "known-provider weathervane offered");
        tool.name = "505 wind compass A";
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "display-name weathervane bypassed exclusion");
        tool.GetComponent<ShipItem>().name = "wind compass A";
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 1, "small wind compass not offered");
        var other = Tool(506, "506 table", TransactionCategory.furniture);
        Windicators.AssetTools.itemPrefabs[506] = other;
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 1, "provider furniture addition bypassed navigation eligibility");
        tool.GetComponent<ShipItem>().big = true;
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "known provider bypassed big gate");
        tool.GetComponent<ShipItem>().big = false;
        check(!AdditionalEquipment.TryValidateSelection(new[] { "mod:removed:tool:ShipItem" }, out _), "missing mod selection silently substituted");
        var native = new GameObject("80 compass A");
        native.AddComponent<ShipItemCompass>();
        native.AddComponent<SaveablePrefab>().prefabIndex = 80;
        PrefabsDirectory.instance.directory[80] = native;
        BepInEx.Bootstrap.Chainloader.PluginInfos["broken"] = new()
        {
            Instance = new object(),
            Metadata = null
        };
        check(AdditionalEquipment.GetCatalog().Any(e => !e.IsModded && e.Index == 80), "optional provider discovery failure hid vanilla equipment");
    }

    static void RegisteredNativeTools(Action<bool, string> check)
    {
        Reset();
        GenericProvider();
        var tool = Tool(800, "800 native component tool");
        var entries = ModEquipment.Discover(PrefabsDirectory.instance);
        check(entries.Length == 1 && entries[0].SourceId == "registered" && entries[0].SourceName == "Other mod items", "ownerless native-component tool not discovered");
        var key = entries[0].Key;
        PrefabsDirectory.instance.directory[800] = null;
        PrefabsDirectory.instance.directory[899] = tool;
        tool.name = "899 native component tool";
        tool.GetComponent<SaveablePrefab>().prefabIndex = 899;
        entries = ModEquipment.Discover(PrefabsDirectory.instance);
        check(entries.Single().Key == key, "ownerless stable identity changed after registry shift");
        var other = Tool(900, "900 native component tool");
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "duplicate ownerless identity offered");
        PrefabsDirectory.instance.directory[900] = null;
        tool.scene = new Scene
        {
            valid = true
        };
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "ownerless scene clone offered");
        tool.scene = new Scene();
        tool.GetComponent<SaveablePrefab>().currentCrateId = 42;
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "ownerless crated item offered");
        tool.GetComponent<SaveablePrefab>().currentCrateId = 0;
        foreach (var name in new[] { "large speaker", "SPEAKER_LARGE", "899 Speaker", "turbo wolfer", "Turbo-Woofer", "WEATHER VANE M small" })
        {
            tool.name = name;
            tool.GetComponent<ShipItem>().name = "tool";
            check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "explicit exclusion bypassed: " + name);
        }

        tool.name = "899 Small Speaker";
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 1, "small speaker excluded with large variant");
        tool.name = "tool";
        tool.GetComponent<ShipItem>().name = "large speaker";
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "explicit display-name exclusion bypassed");
        tool.GetComponent<ShipItem>().name = "tool";
        tool.GetComponent<ShipItem>().category = TransactionCategory.furniture;
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "ownerless furniture offered");
        tool.GetComponent<ShipItem>().category = TransactionCategory.toolsAndSupplies;
        tool.AddComponent(GenericComponent);
        Provider("test.ambiguous", BepInEx.Bootstrap.Chainloader.PluginInfos["test.tools"].Instance);
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "ambiguous provenance fell back to ownerless");
    }

    static void FurnitureExclusion(Action<bool, string> check)
    {
        Reset();
        Provider("com.kemy.kemyfurniture", new KemyFurniture.FurniturePlugin());
        var tool = Tool(850, "misclassified small furniture");
        KemyFurniture.FurniturePlugin.LoadedPrefabs = new[] { tool };
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "native-only Kemy furniture prefab offered");
        KemyFurniture.FurniturePlugin.LoadedPrefabs = Array.Empty<GameObject>();
        tool.AddComponent<KemyFurniture.FurnitureComponent>();
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "Kemy furniture component bypassed exclusion");
    }

    static void AmbiguousProviderIdentity(Action<bool, string> check)
    {
        Reset();
        Provider("com.larsonlogistics.sailwind.navsuite", new KemyNavTools.TestPlugin());
        Provider("com.nandbrew.Windicators", new Windicators.TestPlugin());
        var tool = Tool(830, "wind compass conflicting provider");
        KemyNavTools.PreloadDirectoryPatch.compassPrefab = tool;
        Windicators.AssetTools.itemPrefabs.Clear();
        Windicators.AssetTools.itemPrefabs[830] = tool;
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "two known provider identities fell back to generic");
    }

    static void GenericScaleEligibility(Action<bool, string> check)
    {
        Reset();
        GenericProvider();
        var tool = Tool(800, "800 crate predicate probe");
        foreach (var big in new[] { false, true })
            foreach (var scale in new[] { 0f, .4f, .9999f, 1f, 1.01f, float.NaN, float.PositiveInfinity })
            {
                tool.GetComponent<ShipItem>().big = big;
                tool.transform.localScale = new Vector3(scale, scale, scale);
                var eligible = !big && (tool.transform.localScale - Vector3.one).sqrMagnitude < .000001f;
                check((ModEquipment.Discover(PrefabsDirectory.instance).Length == 1) == eligible, "discovery and actual crate predicate differ");
            }
    }

    static void CustomItemOwnership(Action<bool, string> check)
    {
        Reset();
        GenericProvider();
        var tool = Tool(820, "820 custom instrument", itemType: CustomItem);
        var entries = ModEquipment.Discover(PrefabsDirectory.instance);
        check(entries.Length == 1 && entries[0].SourceId == "test.tools" && entries[0].ItemType == CustomItem.FullName, "custom ShipItem with exact provider omitted");
        var key = entries[0].Key;
        var start = new ResolvedStart
        {
            StarterSet = new GameObject("start").AddComponent<StarterSet>(),
            Options = new StartingOptionsSettings()
        };
        start.Options.EquipmentQuantities[key] = 1;
        AdditionalEquipment.Append(start, Array.Empty<ShipItem>());
        var spawned = start.StarterSet.transform.children.Single().gameObject;
        check(spawned.GetComponent<ShipItem>().GetType() == CustomItem && !spawned.activeSelf, "custom item clone changed type or activated prematurely");
        AdditionalEquipment.Reset();
        tool.GetComponent<ShipItem>().category = TransactionCategory.furniture;
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "custom item bypassed generic category gate");
        tool.GetComponent<ShipItem>().category = TransactionCategory.toolsAndSupplies;
        var customOwner = BepInEx.Bootstrap.Chainloader.PluginInfos["test.tools"].Instance;
        BepInEx.Bootstrap.Chainloader.PluginInfos.Clear();
        Provider("other.plugin", new object());
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "custom root used ownerless fallback");
        Provider("test.tools", customOwner);
        Provider("ambiguous.custom", customOwner);
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "custom root accepted ambiguous provider");
    }

    static void VisibleGeometry(Action<bool, string> check)
    {
        Reset();
        GenericProvider();
        var tool = Tool(830, "830 visibility probe");
        var renderer = tool.GetComponent<MeshRenderer>();
        var filter = tool.GetComponent<MeshFilter>();
        renderer.sharedMaterials = new Material[]
        {
            null
        };
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "all-null material template offered");
        renderer.sharedMaterials = new[] { new Material() };
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "shaderless template offered");
        renderer.sharedMaterials = new[]
        {
            new Material
            {
                shader = new Shader()
            }
        };
        filter.sharedMesh = null;
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "all-null mesh template offered");
        var child = new GameObject("valid child");
        child.transform.SetParent(tool.transform, true);
        Visual(child);
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 1, "valid child visual rejected for empty root mesh");
        child.SetActive(false);
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "inactive child visual admitted invisible root");
        child.SetActive(true);
        child.GetComponent<MeshRenderer>().enabled = false;
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "disabled child renderer admitted invisible root");
        var skin = child.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = new Mesh();
        skin.sharedMaterials = new[]
        {
            new Material
            {
                shader = new Shader()
            }
        };
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 1, "valid skinned item omitted");
        tool.SetActive(false);
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 1, "inactive root rejected despite native activation and active child");
        renderer.sharedMaterials = new Material[]
        {
            null
        };
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "root null material permitted despite native Outline dereference");
        renderer.enabled = false;
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 0, "disabled root null material bypassed Outline gate");
        renderer.sharedMaterials = Array.Empty<Material>();
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 1, "empty root materials blocked valid child geometry");
        tool.components.Remove(renderer);
        check(ModEquipment.Discover(PrefabsDirectory.instance).Length == 1, "absent root renderer blocked valid child geometry");
    }

    static void NavigationDisplayName(Action<bool, string> check)
    {
        Reset();
        Provider("com.larsonlogistics.sailwind.navsuite", new KemyNavTools.TestPlugin());
        var tool = Tool(831, "BearingCompass");
        tool.transform.localScale = new Vector3(.3f, .3f, .3f);
        KemyNavTools.PreloadDirectoryPatch.compassPrefab = tool;
        var entries = ModEquipment.Discover(PrefabsDirectory.instance);
        check(entries.Single().DisplayName == "Bearing Compass", "Bearing Compass label not cleaned");
        check(entries.Single().Key == "mod:com.larsonlogistics.sailwind.navsuite:BearingCompass:ShipItem", "Bearing Compass label changed stable selection key");
    }
}
