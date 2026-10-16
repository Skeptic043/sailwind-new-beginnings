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

        internal static bool ValidPort(Port port) => ValidPort(port, Resources.FindObjectsOfTypeAll<Port>());

        // Discovery shares one scene snapshot across its ports. Standalone
        // identity checks still enumerate fresh objects at their call site.
        internal static bool ValidPort(Port port, Port[] scenePorts) => Loaded(port) && port.enabled && Port.ports != null &&
            port.portIndex >= 0 && port.portIndex < Port.ports.Length && Port.ports[port.portIndex] == port &&
            (int)port.region >= 0 && (int)port.region <= 2 &&
            scenePorts.Count(item => Loaded(item) && item.enabled && item.portIndex == port.portIndex) == 1;

        private static bool OptionalCurrent(Component value, IslandHorizon island) =>
            ReferenceEquals(value, null) || Loaded(value) && value.GetComponentInParent<IslandHorizon>() == island;

        internal static bool BelongsToIsland(Component value, IslandHorizon island)
        {
            if (!Loaded(value) || !Loaded(island)) return false;
            var parent = value.GetComponentInParent<IslandHorizon>();
            if (parent != null) return parent == island;
            // Native additive scenery follows the registered island by index,
            // without parenting its objects beneath IslandHorizon.
            var scenery = value.GetComponentInParent<IslandSceneryScene>();
            var index = island.islandIndex;
            return scenery != null && scenery.parentIslandIndex == index && Refs.islands != null &&
                index >= 0 && index < Refs.islands.Length && Refs.islands[index] == island.transform;
        }

        internal static bool IsCurrent(PortChoice site) => site != null && Loaded(site.Island) &&
            (site.Port == null ? site.Hidden == HiddenSite.Pirate && KnownSites.IsPirateHideout(site.Island) : ValidPort(site.Port) &&
                site.Port.GetComponentInParent<IslandHorizon>() == site.Island && (int)site.Port.region == site.Region) &&
            Loaded(site.DockRoot) && site.DockRoot.GetComponentInParent<IslandHorizon>() == site.Island &&
            OptionalCurrent(site.Marker, site.Island) && OptionalCurrent(site.ShoreMarker, site.Island) &&
            OptionalCurrent(site.SceneryRoot, site.Island) && OptionalCurrent(site.PreferredFront, site.Island) &&
            OptionalCurrent(site.PreferredBack, site.Island) &&
            (ReferenceEquals(site.Yard, null) || BelongsToIsland(site.Yard, site.Island)) &&
            (site.Yard == null || site.Yard.enabled) &&
            (site.Hidden != HiddenSite.Saffron || site.Marker != null);

        internal static void Complete(SelectionCatalog catalog)
        {
            var yards = Resources.FindObjectsOfTypeAll<Shipyard>().Where(item => Loaded(item) && item.enabled).ToArray();
            var registrations = StartPorts.Snapshot();
            foreach (var choice in catalog.Ports)
            {
                choice.Island = choice.Port.GetComponentInParent<IslandHorizon>();
                choice.DockRoot = choice.Island != null ? choice.Island.transform : null;
                choice.Region = (int)choice.Port.region;
                choice.Key = "port:" + choice.IdentityName;
                choice.IsModded = !SelectionCatalog.IsKnownPort(choice.Port);
                if (choice.IsModded && !choice.Pool.HasValue)
                    choice.Pool = choice.Region == 0 ? PortPool.AlAnkh : choice.Region == 1 ? PortPool.Emerald : PortPool.Aestrin;
                if (KnownSites.IsSaffronPort(choice.Port))
                {
                    choice.Hidden = HiddenSite.Saffron;
                    choice.Recovery = null;
                    var markers = choice.Port.GetComponentsInChildren<RecoveryPort>(true)
                        .Where(item => item.parentPort == choice.Port && item.boatPos != null)
                        .Select(item => item.boatPos).Distinct().ToArray();
                    choice.Marker = markers.Length == 1 ? markers[0] : null;
                    choice.DisplayName = "Saffron Island";
                }
                var registration = registrations.FirstOrDefault(item => ReferenceEquals(item.Port, choice.Port));
                var options = registration?.Options;
                if (registration != null)
                {
                    choice.LegacySiteKey = registration.Key;
                    if (!string.IsNullOrWhiteSpace(options.DisplayName)) choice.DisplayName = options.DisplayName;
                    if (options.DockRoot != null) choice.DockRoot = options.DockRoot;
                    if (options.BerthMarker != null) choice.Marker = options.BerthMarker;
                    if (options.ShoreMarker != null) choice.ShoreMarker = options.ShoreMarker;
                    if (options.SceneryRoot != null) choice.SceneryRoot = options.SceneryRoot;
                    if (options.FrontMooring != null)
                    {
                        choice.PreferredFront = options.FrontMooring;
                        choice.PreferredBack = options.BackMooring;
                    }
                    choice.HasAuthoredBerth = options.DockRoot != null || options.BerthMarker != null ||
                        options.ShoreMarker != null || options.FrontMooring != null;
                    choice.HasAuthoredYardScope = options.DockRoot != null || options.Shipyard != null;
                }
                var yard = options?.Shipyard;
                if (yard == null)
                    yard = yards.FirstOrDefault(item => BelongsToIsland(item, choice.Island) &&
                        (options?.DockRoot == null
                            ? KnownSites.FortJuniperRoot(item) == null &&
                              Port.ports.Count(port => port != null && port.GetComponentInParent<IslandHorizon>() == choice.Island) == 1
                            : item.transform.IsChildOf(options.DockRoot)));
                choice.Yard = yard;
                if (!choice.HasAuthoredYardScope)
                    choice.Yard = ShipyardStart.PreferredYard(choice, yards);
                choice.HasShipyard = choice.Yard != null || !choice.HasAuthoredYardScope && ShipyardStart.HasNativeYard(choice);
            }
            foreach (var island in Resources.FindObjectsOfTypeAll<IslandHorizon>().Where(item => Loaded(item) &&
                KnownSites.IsPirateHideout(item)))
                catalog.Ports.Add(new PortChoice { Key = KnownSites.PirateHideoutKey, SiteIdentity = KnownSites.PirateHideoutKey,
                    Island = island, DockRoot = island.transform, Region = 0, Pool = PortPool.SmallIslands,
                    Hidden = HiddenSite.Pirate, DisplayName = "Pirate Island" });

            var chronos = catalog.Ports.FirstOrDefault(KnownSites.IsChronos);
            if (chronos != null)
            {
                var forts = yards.Where(yard => yard.GetComponentInParent<IslandHorizon>() == chronos.Island)
                    .Select(yard => new { Yard = yard, Root = KnownSites.FortJuniperRoot(yard) })
                    .Where(item => item.Root != null).ToArray();
                if (forts.Length == 1)
                    catalog.Ports.Add(new PortChoice { Port = chronos.Port, Island = chronos.Island,
                        DockRoot = forts[0].Root, Region = chronos.Region, IsModded = true, HasShipyard = true,
                        Yard = forts[0].Yard,
                        Key = KnownSites.FortJuniperKey, SiteIdentity = KnownSites.FortJuniperKey,
                        DisplayName = "Fort Juniper" });
            }
            // An API alias which duplicates a canonical built-in site must
            // select the built-in instead of also matching its Port.
            foreach (var choice in catalog.Ports.Where(item => item.LegacySiteKey != null).ToArray())
                if (catalog.Ports.Any(item => !ReferenceEquals(item, choice) &&
                    string.Equals(item.Key, choice.LegacySiteKey, StringComparison.Ordinal)))
                {
                    catalog.Rejections.Add("A registered port's legacy site identity conflicts with a built-in location: " +
                        choice.LegacySiteKey);
                    choice.LegacySiteKey = null;
                }
            // A conflicting built-in identity must not select whichever was enumerated first.
            var duplicateKeys = new HashSet<string>(catalog.Ports.GroupBy(item => item.Key).Where(group => group.Count() != 1).Select(group => group.Key));
            catalog.Ports.RemoveAll(item => duplicateKeys.Contains(item.Key) || item.Island == null ||
                item.Hidden == HiddenSite.Saffron && item.Marker == null);
        }

        private static PortChoice readySite;
        private static int readyFrame = -1;
        private static bool readyResult;

        // Shore and supply surface probes ask this hundreds of times per frame.
        // Scene loading and object destruction only complete between frames.
        internal static bool SceneryReady(PortChoice site)
        {
            var frame = Time.frameCount;
            if (ReferenceEquals(site, readySite) && frame == readyFrame) return readyResult;
            readyResult = CheckSceneryReady(site);
            readySite = site; readyFrame = frame;
            return readyResult;
        }

        private static bool customIslandsResolved;
        private static System.Reflection.FieldInfo customIslandScenes;

        // Look up the optional CustomIslandAPI by assembly name, once. A
        // TypeByName miss scans every assembly and logs a stack trace for each
        // assembly that cannot list its types, which froze startup for seconds.
        private static IDictionary CustomIslandScenes()
        {
            if (!customIslandsResolved)
            {
                customIslandsResolved = true;
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(item => item.GetName().Name == "CustomIslandAPI");
                var manager = assembly?.GetType("CustomIslandAPI.IslandManager", false);
                customIslandScenes = manager != null ? AccessTools.Field(manager, "sceneIndexes") : null;
            }
            return customIslandScenes?.GetValue(null) as IDictionary;
        }

        private static bool CheckSceneryReady(PortChoice site)
        {
            if (!IsCurrent(site) || !site.Island.SceneLoaded()) return false;
            if (site.SceneryRoot != null) return Loaded(site.SceneryRoot) && site.SceneryRoot.gameObject.activeInHierarchy;
            // CustomIslandAPI uses an explicit additive scene path, not islandIndex
            // as a build index. Its SceneLoaded flag is raised before load completes.
            var paths = CustomIslandScenes();
            if (paths != null && paths.Contains(site.Island.islandIndex))
            {
                var path = paths[site.Island.islandIndex] as string;
                if (string.IsNullOrEmpty(path)) return false;
                var custom = SceneManager.GetSceneByPath(path);
                return custom.IsValid() && custom.isLoaded;
            }
            // Match native IslandHorizon.LoadIslandScene / RegisterLoadingFinished:
            // SceneLoaded means requested, while this scene proves completion.
            // Only native islands use their index as the actual scene build index.
            var native = SceneManager.GetSceneByBuildIndex(site.Island.islandIndex);
            if (native.IsValid() && native.isLoaded) return true;
            return false;
        }
    }
}
