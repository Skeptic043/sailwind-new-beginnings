using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    internal static class ModEquipment
    {
        // The inspected stable and beta native directories have 390 entries.
        // Recheck this boundary when Sailwind adds native prefabs. Registered
        // tools without detectable plugin provenance use a separate identity.
        private const int NativeDirectoryLength = 390;
        private const string NavigationProvider = "com.larsonlogistics.sailwind.navsuite";
        private const string FurnitureProvider = "com.kemy.kemyfurniture";
        private static readonly Provider Unattributed = new Provider
        { Id = "registered", Name = "Other mod items" };
        private static readonly FieldInfo CurrentCrate = AccessTools.Field(typeof(SaveablePrefab), "currentCrateId");
        private sealed class Provider
        {
            internal string Id;
            internal string Name;
            internal Assembly Assembly;
            internal string Directory;
        }

        internal static string CanonicalName(string name)
        {
            var index = 0;
            while (index < name.Length && char.IsDigit(name[index])) index++;
            return index > 0 && index < name.Length && char.IsWhiteSpace(name[index]) ?
                name.Substring(index).TrimStart() : name;
        }

        internal static EquipmentEntry[] Discover(PrefabsDirectory directory)
        {
            try { return DiscoverCore(directory); }
            catch (Exception exception)
            {
                // Provider reflection is optional. Its failure must never hide
                // the vanilla catalogue or stop an ordinary new game.
                Plugin.Instance?.DebugLog("Mod equipment discovery unavailable: " + exception.Message);
                return Array.Empty<EquipmentEntry>();
            }
        }

        private static EquipmentEntry[] DiscoverCore(PrefabsDirectory directory)
        {
            if (directory?.directory == null) return Array.Empty<EquipmentEntry>();
            var providers = Chainloader.PluginInfos.Values.Where(info => info.Instance != null)
                .Select(info => new Provider
                {
                    Id = info.Metadata.GUID,
                    Name = info.Metadata.Name,
                    Assembly = info.Instance.GetType().Assembly,
                    Directory = info.Instance.GetType().Assembly.IsDynamic ? null :
                        Path.GetDirectoryName(info.Instance.GetType().Assembly.Location)
                }).ToArray();
            if (providers.Length == 0) return Array.Empty<EquipmentEntry>();
            var found = new List<EquipmentEntry>();
            for (var index = NativeDirectoryLength; index < directory.directory.Length; index++)
            {
                var prefab = directory.directory[index];
                if (!IsTemplate(prefab, index, out var item) || !HasVisibleMesh(prefab) ||
                    ExcludedName(prefab.name) || ExcludedName(item.name) || HasConsumableComponent(prefab)) continue;
                var owners = Owners(prefab, providers).Distinct().ToArray();
                if (owners.Any(provider => provider.Id == FurnitureProvider) || IsFurniture(prefab, providers)) continue;
                var itemAssembly = item.GetType().Assembly;
                if (itemAssembly != typeof(ShipItem).Assembly &&
                    (item.category != TransactionCategory.toolsAndSupplies ||
                     owners.Length != 1 || owners[0].Assembly != itemAssembly)) continue;
                Provider owner = null;
                var knownEquipment = false;
                foreach (var provider in providers)
                {
                    bool known;
                    try { known = KnownEquipment(provider, prefab); }
                    catch (Exception) { continue; }
                    if (!known) continue;
                    if (owner != null) { owner = null; break; }
                    owner = provider;
                    knownEquipment = true;
                }
                if (!knownEquipment)
                {
                    if (item.category != TransactionCategory.toolsAndSupplies || owners.Length > 1) continue;
                    owner = owners.Length == 1 ? owners[0] : Unattributed;
                }
                if (owner == null || owner.Id == FurnitureProvider) continue;
                // Exact Kemy tool references may use authored scale. The compass
                // and inclinometer opt into native crate scale/withdrawal; the
                // native-big binnacle stays loose. Other mods keep the original gate.
                var looseNavigation = knownEquipment && owner.Id == NavigationProvider &&
                    itemAssembly == typeof(ShipItem).Assembly && owners.Length <= 1 &&
                    (owners.Length == 0 || owners[0] == owner) && ValidScale(prefab.transform.localScale);
                if (!FitsCrate(item) && !looseNavigation) continue;
                var scaledPacking = looseNavigation && !item.big &&
                    (MatchesField(owner.Assembly.GetType("KemyNavTools.PreloadDirectoryPatch", false), "compassPrefab", prefab) ||
                     MatchesField(owner.Assembly.GetType("KemyNavTools.PreloadDirectoryPatch", false), "inclinometerPrefab", prefab));
                var title = string.IsNullOrWhiteSpace(item.name) ? CanonicalName(prefab.name) : item.name;
                // The authored variant name distinguishes same-name gauges,
                // compasses and telltales without exposing a mutable save index.
                var variant = CanonicalName(prefab.name);
                var bearingCompassLabel = owner.Id == NavigationProvider && variant == "BearingCompass";
                if (bearingCompassLabel) title = "Bearing Compass";
                var radioLabel = owner.Id == "local.sailwind.radio" &&
                    (string.Equals(title, "Radio", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(title, "Small Speaker", StringComparison.OrdinalIgnoreCase));
                if (radioLabel)
                    title = string.Equals(title, "Radio", StringComparison.OrdinalIgnoreCase) ? "Radio" : "Small Speaker";
                var redundantVariant = radioLabel || bearingCompassLabel ||
                    (string.Equals(title, "Propeller Controller", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(variant, "Pump Controller", StringComparison.OrdinalIgnoreCase)) ||
                    (string.Equals(title, "Celestial Atlas", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(variant, "star atlas", StringComparison.OrdinalIgnoreCase));
                if (!redundantVariant && !string.Equals(title, variant, StringComparison.OrdinalIgnoreCase))
                    title += " (" + variant + ")";
                found.Add(new EquipmentEntry(index, prefab.name, item.GetType().FullName,
                    title, owner.Id, owner.Name, scaledPacking));
            }
            return found.GroupBy(entry => entry.Key, StringComparer.Ordinal)
                .Where(group => group.Count() == 1).Select(group => group.Single())
                .OrderBy(entry => entry.SourceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static bool HasVisibleMesh(GameObject prefab)
        {
            // Native GoPointerButton creates a root Outline, whose render pass
            // dereferences every root material slot even without a root mesh.
            var rootRenderer = prefab.GetComponent<Renderer>();
            if (rootRenderer != null && rootRenderer.sharedMaterials.Any(material => material == null)) return false;
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled) continue;
                // StarterSet activates the root. Authored inactive descendants
                // remain inactive and cannot supply the object's visible mesh.
                var active = true;
                for (var current = renderer.transform; current != null && current != prefab.transform; current = current.parent)
                    if (!current.gameObject.activeSelf) { active = false; break; }
                if (!active) continue;
                var skinned = renderer as SkinnedMeshRenderer;
                var mesh = skinned != null ? skinned.sharedMesh :
                    renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
                if (mesh != null && renderer.sharedMaterials.Any(material => material != null && material.shader != null)) return true;
            }
            return false;
        }

        private static bool FitsCrate(ShipItem item)
        {
            // Keep aligned with AdditionalEquipment.CanPack: native withdrawal
            // resets the root scale to one, so scaled items cannot be offered.
            return !item.big && (item.transform.localScale - Vector3.one).sqrMagnitude < 0.000001f;
        }

        internal static bool ValidScale(Vector3 scale) => ValidScaleAxis(scale.x) &&
            ValidScaleAxis(scale.y) && ValidScaleAxis(scale.z);

        private static bool ValidScaleAxis(float value) => value > 0f &&
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool ExcludedName(string name)
        {
            var compact = new string(CanonicalName(name ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            return compact == "speaker" || compact.Contains("weathervane") || compact.Contains("largespeaker") ||
                compact.Contains("speakerlarge") || compact.Contains("turbowolfer") || compact.Contains("turbowoofer");
        }

        private static bool IsFurniture(GameObject prefab, Provider[] providers)
        {
            if (prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(component => component != null &&
                (component.GetType().Namespace ?? "").StartsWith("KemyFurniture", StringComparison.Ordinal))) return true;
            foreach (var provider in providers.Where(value => value.Id == FurnitureProvider))
            {
                try
                {
                    // Furniture can use entirely native components. Its public
                    // loaded-asset list still proves exact prefab membership.
                    var type = provider.Assembly.GetType("KemyFurniture.FurniturePlugin", false);
                    var property = type?.GetProperty("LoadedPrefabs", BindingFlags.Public | BindingFlags.Static);
                    var prefabs = property?.GetValue(null, null) as GameObject[];
                    if (prefabs != null && prefabs.Any(value => ReferenceEquals(value, prefab))) return true;
                }
                catch (Exception) { }
            }
            return false;
        }

        private static bool IsTemplate(GameObject prefab, int index, out ShipItem item)
        {
            item = null;
            // Registered asset templates only. A shop instance can carry state
            // and live controllers even when its native sold flag is still false.
            if (prefab == null || prefab.scene.IsValid()) return false;
            item = prefab.GetComponent<ShipItem>();
            var saveable = prefab.GetComponent<SaveablePrefab>();
            return item != null &&
                !item.sold && saveable != null && saveable.prefabIndex == index && saveable.instanceId == 0 &&
                (CurrentCrate == null || (int)CurrentCrate.GetValue(saveable) == 0) &&
                prefab.GetComponent<Rigidbody>() != null && prefab.GetComponent<Collider>() != null;
        }

        private static bool HasConsumableComponent(GameObject prefab)
        {
            return prefab.GetComponents<Component>().Any(component => component != null &&
                (component is Good || component is ShipItemBottle || component is ShipItemFood ||
                 component is ShipItemCrate || component.GetType().Name == "FoodState" ||
                 component.GetType().Name == "ShipItemBed" || component.GetType().Name == "ShipItemStove" ||
                 component.GetType().Name == "ShipItemLampHook" ||
                 component.GetType().Name == "CookableFood" || component.GetType().Name == "ShipItemSoup" ||
                 component.GetType().Name == "ShipItemKettle" || component.GetType().Name == "ShipItemTea" ||
                 component.GetType().Name == "ShipItemTobacco" || component.GetType().Name == "ShipItemSalt" ||
                 component.GetType().Name == "ShipItemElixir" || component.GetType().Name == "ShipItemRandomElixir"));
        }

        private static IEnumerable<Provider> Owners(GameObject prefab, Provider[] providers)
        {
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null) continue;
                var assembly = component.GetType().Assembly;
                if (assembly == typeof(ShipItem).Assembly) continue;
                var owners = providers.Where(provider => provider.Assembly == assembly).ToArray();
                if (owners.Length == 0)
                {
                    var location = assembly.IsDynamic ? null : Path.GetDirectoryName(assembly.Location);
                    if (string.IsNullOrEmpty(location)) continue;
                    owners = providers.Where(provider => string.Equals(provider.Directory, location,
                        StringComparison.OrdinalIgnoreCase)).ToArray();
                }
                // Preserve ambiguous ownership so callers cannot mistake it
                // for an ownerless native-component template.
                foreach (var owner in owners) yield return owner;
            }
        }

        private static bool KnownEquipment(Provider provider, GameObject prefab)
        {
            if (provider.Id == NavigationProvider)
            {
                var type = provider.Assembly.GetType("KemyNavTools.PreloadDirectoryPatch", false);
                return MatchesField(type, "inclinometerPrefab", prefab) ||
                    MatchesField(type, "compassPrefab", prefab) || MatchesField(type, "binnaclePrefab", prefab);
            }
            if (provider.Id == "com.nandbrew.Windicators")
            {
                var name = CanonicalName(prefab.name);
                if (!new[] { "telltale", "wind compass", "anemometer", "barometer" }
                    .Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return false;
                var type = provider.Assembly.GetType("Windicators.AssetTools", false);
                var field = type == null ? null : AccessTools.Field(type, "itemPrefabs");
                var items = field != null && field.IsStatic ? field.GetValue(null) as IDictionary : null;
                return items != null && items.Values.Cast<object>().Any(value => ReferenceEquals(value, prefab));
            }
            if (provider.Id == "DogEggz.JetPump")
            {
                var plugin = provider.Assembly.GetType("JetPump.Plugin", false);
                var ready = plugin == null ? null : AccessTools.Field(plugin, "Ready");
                if (ready == null || ready.FieldType != typeof(bool) || !(bool)ready.GetValue(null) ||
                    prefab.transform.Find("JetPumpAssetContract_v13") == null) return false;
                var type = provider.Assembly.GetType("JetPump.PumpAssets", false);
                return MatchesField(type, "Pump", prefab) || MatchesField(type, "Controller", prefab) ||
                    MatchesField(type, "HugePump", prefab);
            }
            return false;
        }

        private static bool MatchesField(Type type, string name, GameObject prefab)
        {
            var field = type == null ? null : AccessTools.Field(type, name);
            return field != null && field.IsStatic && field.FieldType == typeof(GameObject) &&
                ReferenceEquals(field.GetValue(null), prefab);
        }
    }
}
