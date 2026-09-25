using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    // A native embark moves the controller into the separate boat walk scene.
    // Our unfinished new-game arrival must keep both actors in world space.
    internal static class PlayerStartEmbarkGuard
    {
        private static readonly FieldInfo ObserverField = AccessTools.Field(typeof(PlayerEmbarkerNew), "playerObserver");
        private static readonly FieldInfo ControllerField = AccessTools.Field(typeof(PlayerEmbarkerNew), "playerController");
        private static readonly FieldInfo EmbarkedField = AccessTools.Field(typeof(PlayerEmbarkerNew), "embarked");
        private static readonly MethodInfo DisembarkMethod = AccessTools.Method(typeof(PlayerEmbarkerNew), "PlayerDisembark");
        private static PlayerEmbarkerNew guarded;

        internal static void Begin(Transform observer, Transform controller)
        {
            Release();
            if (ObserverField == null || ControllerField == null || EmbarkedField == null || DisembarkMethod == null)
                throw new InvalidOperationException("Native player embark methods are unavailable for the selected shore start.");
            var matches = Resources.FindObjectsOfTypeAll<PlayerEmbarkerNew>().Where(item =>
                item != null && item.gameObject.scene.IsValid() && item.gameObject.scene.isLoaded &&
                ObserverField.GetValue(item) as Transform == observer &&
                ControllerField.GetValue(item) as Transform == controller).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("Expected one native player embarker for the selected shore start.");
            guarded = matches[0];
            EnsureWorldSpace();
        }

        internal static void EnsureWorldSpace()
        {
            if (guarded == null) throw new InvalidOperationException("The selected shore start lost its native embark guard.");
            if (EmbarkedField.GetValue(guarded) is bool embarked && embarked)
            {
                // Native disembark restores actor parents and observer-to-controller
                // world pose, and clears GameState.currentBoat and the embark flag.
                DisembarkMethod.Invoke(guarded, null);
                Plugin.Instance?.DebugLog("Selected shore start restored native world-space player parenting before arrival.");
            }
        }

        internal static void Release()
        {
            guarded = null;
        }

        private static bool Blocks(PlayerEmbarkerNew instance) =>
            guarded != null && instance == guarded;

        [HarmonyPatch(typeof(PlayerEmbarkerNew), "ObserverTriggerEnter")]
        private static class ObserverEntryPatch
        {
            private static bool Prefix(PlayerEmbarkerNew __instance, Collider __0) =>
                __0 == null || !(__0.CompareTag("EmbarkCol") || __0.CompareTag("EmbarkColPlayer")) || !Blocks(__instance);
        }

        [HarmonyPatch(typeof(PlayerEmbarkerNew), "PlayerEmbark")]
        private static class EmbarkPatch
        {
            private static bool Prefix(PlayerEmbarkerNew __instance) => !Blocks(__instance);
        }

        [HarmonyPatch(typeof(SaveLoadManager), "LoadGame")]
        private static class LoadPatch
        {
            private static void Prefix() => Plugin.Instance?.CancelPlayerStartTracking();
        }

        [HarmonyPatch(typeof(StartMenu), "EnableStartMenu")]
        private static class MenuPatch
        {
            private static void Prefix() => Plugin.Instance?.CancelPlayerStartTracking();
        }
    }
}
