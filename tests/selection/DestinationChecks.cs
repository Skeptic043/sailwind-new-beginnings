using System;
using UnityEngine;

namespace Lifetime
{
    // The existing API boundary supplies loaded objects, ancestry and Unity
    // destroyed-object null behavior. These add only captured destination data.
    internal sealed class CapturedIsland : IslandHorizon
    {
    }

    internal sealed class CurrentPort : Port
    {
        internal int region;
    }

    internal sealed class PortChoice
    {
        internal CurrentPort Port;
        internal CapturedIsland Island;
        internal int Region;
        internal HiddenSite Hidden;
        internal string SiteIdentity;
        internal UnityEngine.Transform DockRoot, Marker, ShoreMarker, SceneryRoot;
        internal GPButtonDockMooring PreferredFront, PreferredBack;
        internal Shipyard Yard;
    }

    internal static partial class StartDestinations
    {
        internal static bool Loaded(Component item) => item != null && item.Loaded;
        internal static bool ValidPort(Port port) => Loaded(port) && port.Registered && Loaded(port.Island);
    }

    internal static class DestinationChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            var island = new CapturedIsland
            {
                islandIndex = 67,
                name = "Bottleneck"
            };
            var other = new CapturedIsland
            {
                islandIndex = 2,
                name = "Other"
            };
            var port = new CurrentPort
            {
                Island = island,
                region = 1
            };
            var marker = new UnityEngine.Transform
            {
                Island = island
            };
            var site = new PortChoice
            {
                Port = port,
                Island = island,
                Region = 1,
                DockRoot = island.transform,
                Marker = marker
            };
            check(StartDestinations.IsCurrent(site), "captured native destination remains valid");
            port.Island = other;
            check(!StartDestinations.IsCurrent(site), "reparented Port invalidates captured island");
            port.Island = island;
            port.region = 2;
            check(!StartDestinations.IsCurrent(site), "Port region change invalidates captured starter region");
            port.region = 1;
            marker.Island = other;
            check(!StartDestinations.IsCurrent(site), "marker moved to another island invalidates destination");
            marker.Island = island;
            marker.Destroyed = true;
            check(!StartDestinations.IsCurrent(site), "destroyed captured marker is not treated as absent optional marker");
            marker.Destroyed = false;
            marker.Loaded = false;
            check(!StartDestinations.IsCurrent(site), "unloaded marker invalidates destination");
            marker.Loaded = true;
            site.Marker = null;
            check(StartDestinations.IsCurrent(site), "genuinely absent optional marker remains supported");
            site.Hidden = HiddenSite.Saffron;
            check(!StartDestinations.IsCurrent(site), "Saffron requires its marker");
            site.Marker = marker;
            check(StartDestinations.IsCurrent(site), "Saffron accepts its live same-island marker");
            site.Hidden = HiddenSite.None;
            site.Yard = new Shipyard
            {
                Island = island,
                enabled = false
            };
            check(!StartDestinations.IsCurrent(site), "disabled captured yard invalidates destination");
            site.Yard = new Shipyard
            {
                Scenery = new IslandSceneryScene
                {
                    parentIslandIndex = island.islandIndex
                }
            };
            Refs.islands[island.islandIndex] = island.transform;
            check(StartDestinations.IsCurrent(site), "native additive scenery yard remains current without IslandHorizon ancestry");
            site.Yard.Scenery.parentIslandIndex++;
            check(!StartDestinations.IsCurrent(site), "native scenery for another island invalidates destination");
            site.Yard.Scenery.parentIslandIndex--;
            Refs.islands[island.islandIndex] = other.transform;
            check(!StartDestinations.IsCurrent(site), "same-index stale island cannot claim current native scenery");
            Refs.islands[island.islandIndex] = island.transform;
            site.Yard = null;
            site.DockRoot = other.transform;
            check(!StartDestinations.IsCurrent(site), "dock moved to another island invalidates destination");
            var pirateIsland = new CapturedIsland
            {
                islandIndex = 32,
                name = "island 32 A (hideout)"
            };
            var pirate = new PortChoice
            {
                Island = pirateIsland,
                DockRoot = pirateIsland.transform,
                Hidden = HiddenSite.Pirate,
                Region = 0
            };
            check(StartDestinations.IsCurrent(pirate), "correct Pirate destination works without a native Port");
            pirateIsland.islandIndex = 31;
            check(!StartDestinations.IsCurrent(pirate), "wrong null-Port island is rejected");
            pirateIsland.islandIndex = 32;
            pirateIsland.name = "another island";
            check(!StartDestinations.IsCurrent(pirate), "null-Port identity requires exact Pirate island name");
            var boneIsland = new CapturedIsland { islandIndex = 36, name = "island 36" };
            var bone = new PortChoice { Island = boneIsland, DockRoot = boneIsland.transform, Hidden = HiddenSite.BoneIsle, Region = 2 };
            check(StartDestinations.IsCurrent(bone), "correct Bone Isle destination works without a native Port");
            bone.Hidden = HiddenSite.Pirate;
            check(!StartDestinations.IsCurrent(bone), "Bone Isle island accepted as Eleann Island");
            bone.Hidden = HiddenSite.BoneIsle;
            boneIsland.name = "island 36 copy";
            check(!StartDestinations.IsCurrent(bone), "null-Port identity requires exact Bone Isle island name");
            var oracleIsland = new CapturedIsland { islandIndex = 23, name = "island 23 M oracle" };
            var oracle = new PortChoice { Island = oracleIsland, DockRoot = oracleIsland.transform, SiteIdentity = KnownSites.OracleKey, Region = 2 };
            check(StartDestinations.IsCurrent(oracle), "correct Oracle Island destination works without a native Port or spoiler kind");
            oracle.SiteIdentity = null;
            check(!StartDestinations.IsCurrent(oracle), "Oracle island accepted without its site identity");
            oracle.SiteIdentity = KnownSites.OracleKey;
            oracleIsland.name = "island 23 M oracle copy";
            check(!StartDestinations.IsCurrent(oracle), "null-Port identity requires exact Oracle island name");
            Console.WriteLine("PASS 23 actual-source destination-lifetime checks using shared API boundary doubles.");
        }
    }
}
