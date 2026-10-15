using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    internal static class HandoffHarness
    {
        private static ResolvedStart armed;
        private static string placementStage;
        private static int releases;
        private static readonly HashSet<ShipItem> Captured = new();
        internal static string PlacementStage => placementStage;

        private static void Disarm()
        {
            armed = null;
            releases++;
            AdditionalEquipment.Reset();
        }

        internal static bool Invoke(ResolvedStart selected, Component inventory)
        {
            armed = selected;
            releases = 0;
            typeof(ReleaseEquipmentCrateOnOpenPatch)
                .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { inventory });
            return releases == 1;
        }

        internal static bool Pickup(ResolvedStart selected, ShipItem item)
        {
            armed = selected;
            releases = 0;
            Captured.Clear();
            Captured.Add(item);
            typeof(ReleaseStarterCupOnPickupPatch)
                .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { item });
            return releases == 1;
        }

/* CURRENT_PATCHES */
    }

    internal static class RosterHarness
    {
        private static HashSet<ShipItem> DifficultyRemovalRequests;
        private static Func<ShipItem, bool> IsIntentionalScrambledSeasMap;
        private static IEnumerable<ShipItem> CollectItems(Transform transform) =>
            transform.children.Select(child => child.gameObject.GetComponent<ShipItem>()).Where(item => item != null);

        internal static ShipItem[] Filter(StarterSet __instance, HashSet<ShipItem> removed, Func<ShipItem, bool> skip)
        {
            DifficultyRemovalRequests = removed;
            IsIntentionalScrambledSeasMap = skip;
/* CURRENT_ROSTER */
            return survivors;
        }
    }
}
