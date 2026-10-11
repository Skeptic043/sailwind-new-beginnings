using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using HarmonyLib;

namespace NewBeginnings
{
    internal static class StartDestinations
    {
        internal static bool Loaded(Component item) => item != null && item.gameObject.scene.IsValid() && item.gameObject.scene.isLoaded;

        internal static bool ValidPort(Port port) => Loaded(port) && port.enabled && Port.ports != null &&
            port.portIndex >= 0 && port.portIndex < Port.ports.Length && Port.ports[port.portIndex] == port &&
            (int)port.region >= 0 && (int)port.region <= 2 &&
            Resources.FindObjectsOfTypeAll<Port>().Count(item => Loaded(item) && item.enabled && item.portIndex == port.portIndex) == 1;

        private static bool OptionalCurrent(Component value, IslandHorizon island) =>
            ReferenceEquals(value, null) || Loaded(value) && value.GetComponentInParent<IslandHorizon>() == island;

        internal static bool IsCurrent(PortChoice site) => site != null && Loaded(site.Island) &&
            (site.Port == null ? site.HiddenKey == "pirate" && site.Island.islandIndex == 32 &&
                site.Island.name == "island 32 A (hideout)" : ValidPort(site.Port) &&
                site.Port.GetComponentInParent<IslandHorizon>() == site.Island && (int)site.Port.region == site.Region) &&
            Loaded(site.DockRoot) && site.DockRoot.GetComponentInParent<IslandHorizon>() == site.Island &&
            OptionalCurrent(site.Marker, site.Island) && OptionalCurrent(site.ShoreMarker, site.Island) &&
            OptionalCurrent(site.SceneryRoot, site.Island) && OptionalCurrent(site.PreferredFront, site.Island) &&
            OptionalCurrent(site.PreferredBack, site.Island) && OptionalCurrent(site.Yard, site.Island) &&
            (site.Yard == null || site.Yard.enabled) &&
            (site.HiddenKey != "saffron" || site.Marker != null);

        internal static void Complete(SelectionCatalog catalog)
        {
            var yards = Resources.FindObjectsOfTypeAll<Shipyard>().Where(item => Loaded(item) && item.enabled).ToArray();
            foreach (var choice in catalog.Ports)
            {
                choice.Island = choice.Port.GetComponentInParent<IslandHorizon>();
                choice.DockRoot = choice.Island != null ? choice.Island.transform : null;
                choice.Region = (int)choice.Port.region;
                choice.Key = "port:" + choice.IdentityName;
                choice.IsModded = !SelectionCatalog.IsKnownPort(choice.Port);
                if (choice.IsModded && !choice.Pool.HasValue)
                    choice.Pool = choice.Region == 0 ? PortPool.AlAnkh : choice.Region == 1 ? PortPool.Emerald : PortPool.Aestrin;
                choice.Yard = yards.FirstOrDefault(yard => yard.GetComponentInParent<IslandHorizon>() == choice.Island &&
                    !(yard.name == "FORT shipyard" && Ancestor(yard.transform, "JUNIPER(Clone)") != null) &&
                    Port.ports.Count(port => port != null && port.GetComponentInParent<IslandHorizon>() == choice.Island) == 1);
                choice.HasShipyard = choice.Yard != null;
                if (choice.Index == 33 && choice.IdentityName == "port A 33 (flower)")
                {
                    choice.HiddenKey = "saffron";
                    choice.Recovery = null;
                    var markers = choice.Port.GetComponentsInChildren<RecoveryPort>(true)
                        .Where(item => item.parentPort == choice.Port && item.boatPos != null)
                        .Select(item => item.boatPos).Distinct().ToArray();
                    choice.Marker = markers.Length == 1 ? markers[0] : null;
                    choice.DisplayName = "Saffron Island";
                }
            }
            foreach (var island in Resources.FindObjectsOfTypeAll<IslandHorizon>().Where(item => Loaded(item) &&
                item.islandIndex == 32 && item.name == "island 32 A (hideout)"))
                catalog.Ports.Add(new PortChoice { Key = "vanilla:pirate-hideout", SiteIdentity = "vanilla:pirate-hideout",
                    Island = island, DockRoot = island.transform, Region = 0, Pool = PortPool.SmallIslands,
                    HiddenKey = "pirate", DisplayName = "Pirate Island" });

            var chronos = catalog.Ports.FirstOrDefault(item => item.Index == 21 && item.IdentityName == "port 21 M chronos");
            if (chronos != null)
            {
                var forts = yards.Where(yard => yard.name == "FORT shipyard" &&
                    yard.GetComponentInParent<IslandHorizon>() == chronos.Island)
                    .Select(yard => new { Yard = yard, Root = Ancestor(yard.transform, "JUNIPER(Clone)") })
                    .Where(item => item.Root != null).ToArray();
                if (forts.Length == 1)
                    catalog.Ports.Add(new PortChoice { Port = chronos.Port, Island = chronos.Island,
                        DockRoot = forts[0].Root, Region = chronos.Region, IsModded = true, HasShipyard = true,
                        Yard = forts[0].Yard,
                        Key = "site:oldchronian|fort-juniper", SiteIdentity = "site:oldchronian|fort-juniper",
                        DisplayName = "Fort Juniper (Chronos)" });
            }
            foreach (var registration in StartPorts.Snapshot())
            {
                var options = registration.Options;
                var island = registration.Port.GetComponentInParent<IslandHorizon>();
                var saffron = registration.Port.portIndex == 33 && registration.Port.name == "port A 33 (flower)";
                var yard = options.Shipyard != null ? options.Shipyard : yards.FirstOrDefault(item => item.GetComponentInParent<IslandHorizon>() == island &&
                        (options.DockRoot == null || item.transform.IsChildOf(options.DockRoot)));
                catalog.Ports.Add(new PortChoice { Port = registration.Port, Island = island,
                    DockRoot = options.DockRoot != null ? options.DockRoot : island.transform,
                    Marker = options.BerthMarker, ShoreMarker = options.ShoreMarker, SceneryRoot = options.SceneryRoot,
                    PreferredFront = options.FrontMooring, PreferredBack = options.BackMooring,
                    Region = (int)registration.Port.region, IsModded = !saffron,
                    HiddenKey = saffron ? "saffron" : null, Pool = saffron ? (PortPool?)PortPool.SmallIslands : null,
                    HasShipyard = yard != null, Yard = yard,
                    Key = registration.Key, SiteIdentity = registration.Key,
                    DisplayName = string.IsNullOrWhiteSpace(options.DisplayName) ? registration.Port.GetPortName() : options.DisplayName });
            }
            // A conflicting built-in/API identity must not select whichever was enumerated first.
            var duplicateKeys = new HashSet<string>(catalog.Ports.GroupBy(item => item.Key).Where(group => group.Count() != 1).Select(group => group.Key));
            catalog.Ports.RemoveAll(item => duplicateKeys.Contains(item.Key) || item.Island == null ||
                item.HiddenKey == "saffron" && item.Marker == null);
        }

        private static Transform Ancestor(Transform value, string name)
        {
            for (; value != null; value = value.parent) if (value.name == name) return value;
            return null;
        }

        internal static bool SceneryReady(PortChoice site)
        {
            if (!IsCurrent(site) || !site.Island.SceneLoaded()) return false;
            if (site.SceneryRoot != null) return Loaded(site.SceneryRoot) && site.SceneryRoot.gameObject.activeInHierarchy;
            // CustomIslandAPI uses an explicit additive scene path, not islandIndex
            // as a build index. Its SceneLoaded flag is raised before load completes.
            var manager = AccessTools.TypeByName("CustomIslandAPI.IslandManager");
            var paths = manager != null ? AccessTools.Field(manager, "sceneIndexes")?.GetValue(null) as IDictionary : null;
            if (paths != null && paths.Contains(site.Island.islandIndex))
            {
                var path = paths[site.Island.islandIndex] as string;
                if (string.IsNullOrEmpty(path)) return false;
                var custom = SceneManager.GetSceneByPath(path);
                return custom.IsValid() && custom.isLoaded;
            }
            var native = SceneManager.GetSceneByBuildIndex(site.Island.islandIndex);
            if (native.IsValid() && native.isLoaded) return true;
            return false;
        }
    }
}
