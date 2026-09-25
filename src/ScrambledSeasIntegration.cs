using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    internal static class ScrambledSeasIntegration
    {
        internal const string Id = "com.nandbrew.scrambledseas";
        private static bool installed;
        private static bool seamReady;

        internal static bool IsInstalled => installed;
        internal static bool CanSelect => !installed || seamReady;

        internal static void Initialize(Harmony harmony)
        {
            installed = Chainloader.PluginInfos.TryGetValue(Id, out var info);
            if (!installed) return;
            try
            {
                // The public Scrambled Seas startup prefix moves the world before
                // calling this coroutine factory. There is no assembly reference:
                // presence, exact argument types, and return type are checked here.
                var assembly = info.Instance?.GetType().Assembly;
                var type = assembly?.GetType("ScrambledSeas.Patches+StartNewGamePatch", false);
                var method = type?.GetMethod("MovePlayerToStartPos",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null, new[] { typeof(StartMenu), typeof(Transform),
                        typeof(Transform), typeof(GameObject) }, null);
                if (method == null || method.ReturnType != typeof(IEnumerator))
                {
                    Plugin.Instance.Warn("Scrambled Seas startup coroutine seam was not found. New Beginnings selection is suspended while this Scrambled Seas build is installed.");
                    return;
                }
                var prefix = AccessTools.Method(typeof(ScrambledSeasIntegration), nameof(BeforePlayerStart));
                if (prefix == null) throw new MissingMethodException(nameof(BeforePlayerStart));
                harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                var patchInfo = Harmony.GetPatchInfo(method);
                if (patchInfo == null || !patchInfo.Prefixes.Any(patch =>
                    patch.owner == Plugin.Id && patch.PatchMethod == prefix))
                    throw new InvalidOperationException("Harmony did not install the Scrambled Seas coroutine prefix.");
                seamReady = true;
                Plugin.Instance.Report("Scrambled Seas startup seam patched; selected port and boat will be placed after world movement.");
            }
            catch (Exception exception)
            {
                seamReady = false;
                Plugin.Instance.Error("Scrambled Seas startup seam could not be patched. New Beginnings selection is suspended while it is installed.", exception);
            }
        }

        // Harmony indexes refer to the factory's first two arguments. This runs
        // after WorldScrambler.Move but before its IEnumerator captures startPos.
        private static void BeforePlayerStart(StartMenu __0, ref Transform __1)
        {
            FixedStart.ApplyPendingAtStart(__0, ref __1);
        }
    }
}
