using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    // The inspected Leopard 1.6.0 has no shipped default sails. Supply the
    // requested single medium lateen only for our successfully selected new start.
    internal static class LeopardStarterRig
    {
        private const int BoatIndex = 207;
        private const string BoatName = "BOAT LEOPARD (207)(Clone)";
        private const string MastPath = "boat leopard/structure_container/MAINMAST/mainmast";
        private const int SailIndex = 60;
        private const string SailName = "60 SAIL Am lateen mid";
        private static readonly MethodInfo AttachInitialSail = AccessTools.Method(typeof(Mast),
            "AttachInitialSail", new[] { typeof(GameObject), typeof(float) });
        private static readonly MethodInfo UpdateSailUnroll = AccessTools.Method(typeof(RopeControllerSailReef),
            "UpdateSailUnroll", Type.EmptyTypes);
        private static readonly FieldInfo VisualAnimator = AccessTools.Field(typeof(ReefEffectAnimUniversal), "anim");
        private static readonly FieldInfo VisualRefreshing = AccessTools.Field(typeof(ReefEffectAnimUniversal), "refreshing");
        private static readonly FieldInfo VisualFurled = AccessTools.Field(typeof(ReefEffectAnimUniversal), "isFurled");
        private static readonly FieldInfo VisualUnfurledMaterial = AccessTools.Field(typeof(ReefEffectAnimUniversal), "unfurledMaterial");
        private static readonly FieldInfo WinchClicked = AccessTools.Field(typeof(GoPointerButton), "isClicked");
        private static readonly FieldInfo WinchStickyClickedBy = AccessTools.Field(typeof(GoPointerButton), "stickyClickedBy");
        private static GPButtonRopeWinch pendingWinch;
        private static ShipyardSailColChecker pendingChecker;
        private static StartMenu pendingMenu;
        private static ResolvedStart pendingStart;
        private static int pendingRefreshGeneration;
        private static GPButtonRopeWinch diagnosticWinch;
        private static RopeControllerSailReef diagnosticReef;
        private static float diagnosticExpiresAt;
        private static int generation;

        internal static void Cancel()
        {
            ++generation;
            pendingWinch = null;
            ClearClothRestart();
            ClearControlDiagnostic();
        }

        private static void ClearClothRestart()
        {
            pendingChecker = null;
            pendingMenu = null;
            pendingStart = null;
        }

        [HarmonyPatch(typeof(ShipyardSailColChecker), "ParentToWalkColMast")]
        private static class ResumeClothAfterNativeReparent
        {
            private static void Postfix(ShipyardSailColChecker __instance)
            {
                RestartInterruptedCloth(__instance);
            }
        }

        private static void RestartInterruptedCloth(ShipyardSailColChecker checker)
        {
            if (!ReferenceEquals(checker, pendingChecker) || pendingChecker == null) return;
            var menu = pendingMenu;
            var selected = pendingStart;
            var token = pendingRefreshGeneration;
            // Consume before invoking native code: one attachment transition,
            // never a repair loop or a hook on later player sail changes.
            ClearClothRestart();
            try
            {
                if (selected == null || !StillSelected(menu, selected, token) ||
                    !GameState.playing || GameState.currentlyLoading || pendingWinch == null) return;
                if (WinchInUse(pendingWinch))
                {
                    ObserveWinchInput(pendingWinch);
                    return;
                }
                var reef = pendingWinch.rope as RopeControllerSailReef;
                if (reef == null || reef.sail == null || reef.sail.shipRigidbody != selected.Body ||
                    !HasInitialFurlLength(reef.reverseReefing, reef.currentLength)) return;
                var visual = reef.sail.GetComponent<ReefEffectAnimUniversal>();
                if (visual == null || !visual.isActiveAndEnabled ||
                    !(VisualAnimator.GetValue(visual) is Animator animator) || animator == null ||
                    !(bool)VisualRefreshing.GetValue(visual)) return;
                // Native ParentToWalkColMast deactivates/reactivates the sail,
                // stopping Start's cloth coroutine without clearing refreshing.
                // Restart its own finite refresh after that exact transition.
                // If Start has not run, it remains responsible for initialization.
                visual.RefreshCloth();
                Plugin.Instance.Report("Leopard starter sail: resumed native cloth refresh after initial collision-checker reparenting.");
            }
            catch (Exception exception)
            {
                Plugin.Instance?.Warn("Leopard native cloth refresh could not resume: " + exception.Message);
            }
        }

        [HarmonyPatch(typeof(GPButtonRopeWinch), "OnActivate")]
        private static class ReleaseOnWinchActivation
        {
            private static void Prefix(GPButtonRopeWinch __instance)
            {
                ObserveWinchInput(__instance);
            }
        }

        [HarmonyPatch(typeof(GPButtonRopeWinch), "Update")]
        private static class ReleaseOnWinchInput
        {
            private static void Prefix(GPButtonRopeWinch __instance)
            {
                if (diagnosticWinch != null && Time.realtimeSinceStartup >= diagnosticExpiresAt)
                    ClearControlDiagnostic();
                if ((ReferenceEquals(__instance, pendingWinch) || ReferenceEquals(__instance, diagnosticWinch)) &&
                    WinchInUse(__instance)) ObserveWinchInput(__instance);
            }
        }

        private static bool WinchInUse(GPButtonRopeWinch winch) =>
            (bool)WinchClicked.GetValue(winch) ||
            WinchStickyClickedBy.GetValue(winch) is GoPointer pointer && pointer != null ||
            winch.rotHandle != null && winch.rotHandle.IsGrabbed();

        private static void ClearControlDiagnostic()
        {
            diagnosticWinch = null;
            diagnosticReef = null;
            diagnosticExpiresAt = 0f;
        }

        private static void ObserveWinchInput(GPButtonRopeWinch winch)
        {
            // Diagnostics must never prevent the native winch from handling input.
            try
            {
                var observe = ReferenceEquals(winch, diagnosticWinch) &&
                    Time.realtimeSinceStartup < diagnosticExpiresAt ? diagnosticReef : null;
                if (observe != null) ClearControlDiagnostic();
                if (ReferenceEquals(winch, pendingWinch)) ReleaseToPlayer();
                if (observe != null && Plugin.Instance != null)
                    Plugin.Instance.StartCoroutine(ObserveControlChange(observe, generation));
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(winch, pendingWinch)) Cancel();
                ClearControlDiagnostic();
                Plugin.Instance?.Warn("Leopard first-input diagnostic unavailable: " + exception.Message);
            }
        }

        private static IEnumerator ObserveControlChange(RopeControllerSailReef reef, int token)
        {
            if (reef == null || token != generation || GameState.currentlyLoading) yield break;
            var deadline = Time.realtimeSinceStartup + 3f;
            var original = reef.currentLength;
            while (token == generation && reef != null &&
                reef.currentLength == original && Time.realtimeSinceStartup < deadline)
                yield return null;
            // Observe after native reef/visual Update have consumed the input.
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            if (token == generation && !GameState.currentlyLoading && reef != null)
                ReportVisualState(reef, "first player reef input (read-only)");
        }

        private static void ReleaseToPlayer()
        {
            Plugin.Instance?.Report("Leopard starter furl initialization released to player winch input.");
            Cancel();
        }

        internal static void Arm(StartMenu menu, ResolvedStart selected)
        {
            if (selected.Saveable.sceneIndex != BoatIndex || selected.Saveable.name != BoatName) return;
            Cancel();
            var token = generation;
            try { Plugin.Instance.StartCoroutine(InstallAfterStart(menu, selected, token)); }
            catch (Exception exception) { Plugin.Instance.Warn("Leopard starter sail could not be scheduled: " + exception.Message); }
        }

        [HarmonyPatch(typeof(SaveLoadManager), "LoadGame")]
        private static class CancelOnLoad
        {
            private static void Prefix() => Cancel();
        }

        private static bool StillSelected(StartMenu menu, ResolvedStart selected, int token)
        {
            return token == generation && Plugin.Instance != null &&
                FixedStart.IsSelectionStillActive(menu, selected);
        }

        private static IEnumerator InstallAfterStart(StartMenu menu, ResolvedStart selected, int token)
        {
            // Native ownership is granted later in MovePlayerToStartPos. Never
            // mutate equipment during the placement transaction or a saved load.
            // The native disclaimer waits for the player's confirmation. Time
            // spent reading it must not expire this pending new-game operation.
            while (true)
            {
                if (!StillSelected(menu, selected, token)) yield break;
                // The native reef controller opens sails while currentlyLoading.
                // Install only after that loading override has ended.
                if (GameState.playing && !GameState.currentlyLoading && selected.Boat.isPurchased()) break;
                yield return null;
            }
            // Let the existing hull settling pass complete before creating a
            // hinged sail. Native attachment handles ropes, registration and angles.
            for (var frame = 0; frame < 4; ++frame) yield return new WaitForFixedUpdate();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            if (!StillSelected(menu, selected, token) || !GameState.playing || GameState.currentlyLoading || !selected.Boat.isPurchased()) yield break;
            RopeControllerSailReef reef = null;
            try { reef = Install(selected); }
            catch (Exception exception)
            {
                // Do not retry an attachment that could have partly succeeded.
                Plugin.Instance.Warn("Leopard starter sail needs an in-game check; attachment was not retried: " + exception);
            }
            if (reef != null)
            {
                pendingChecker = reef.sail.GetComponent<SailConnections>().colChecker;
                pendingMenu = menu;
                pendingStart = selected;
                pendingRefreshGeneration = token;
                yield return FinalizeFurl(menu, selected, token, reef);
            }
        }

        private static IEnumerator FinalizeFurl(StartMenu menu, ResolvedStart selected,
            int token, RopeControllerSailReef reef)
        {
            var deadline = Time.realtimeSinceStartup + 3f;
            var frames = 0;
            var lastState = "native visual Start not observed";
            try
            {
                // Newly attached sails run Start and an asynchronous cloth
                // refresh. Numeric unroll alone is not a rendered furl check.
                while (Time.realtimeSinceStartup < deadline)
                {
                    if (!StillSelected(menu, selected, token) || !GameState.playing ||
                        GameState.currentlyLoading || reef == null || reef.sail == null ||
                        pendingWinch == null || pendingWinch.rope != reef) yield break;
                    var failed = false;
                    var complete = false;
                    try
                    {
                        if (WinchInUse(pendingWinch))
                        {
                            ObserveWinchInput(pendingWinch);
                            failed = true;
                        }
                        else if (!HasInitialFurlLength(reef.reverseReefing, reef.currentLength))
                        {
                            // A winch or another owner changed the rope after
                            // installation. Do not claim its new setting.
                            Plugin.Instance.Warn($"Leopard starter furl initialization released: reef length changed to {reef.currentLength:F3}, unroll {reef.sail.currentUnroll:F3}.");
                            failed = true;
                        }
                        else
                        {
                            var visual = reef.sail.GetComponent<ReefEffectAnimUniversal>();
                            var initialized = visual != null && VisualAnimator.GetValue(visual) is Animator animator && animator != null;
                            var refreshing = visual == null || (bool)VisualRefreshing.GetValue(visual);
                            lastState = $"reef {reef.currentLength:F3}, unroll {reef.sail.currentUnroll:F3}, animator ready {initialized}, refreshing {refreshing}, native visual update {visual != null && visual.debugToggleCloth}";
                            if (frames >= 2 && initialized && !refreshing && visual.debugToggleCloth)
                            {
                                // Native animation alone owns the visuals. Flags
                                // and a hidden material did not establish actual
                                // movement of the sail's animated geometry.
                                ReportVisualState(reef, "native initialization (read-only)");
                                Plugin.Instance.Report("Leopard native sail initialization observed; visible furl and animation remain unconfirmed. First reef input within 60 seconds will record one diagnostic.");
                                complete = true;
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Plugin.Instance.Warn("Leopard starter furl could not be confirmed: " + exception.Message);
                        failed = true;
                    }
                    if (failed || complete) yield break;
                    ++frames;
                    yield return null;
                }
                ReportVisualState(reef, "native initialization timeout (read-only)");
                Plugin.Instance.Warn("Leopard starter sail installed, but native visual readiness was not observed within the bounded initialization window: " + lastState + ".");
            }
            finally
            {
                if (token == generation)
                {
                    pendingWinch = null;
                    ClearClothRestart();
                }
            }
        }

        private static bool HasInitialFurlLength(bool reverse, float length) =>
            length == (reverse ? 1f : 0f);

        private static void ReportVisualState(RopeControllerSailReef reef, string phase)
        {
            try
            {
                if (reef == null || reef.sail == null) return;
                var sail = reef.sail;
                var visual = sail.GetComponent<ReefEffectAnimUniversal>();
                var animator = visual == null ? null : VisualAnimator.GetValue(visual) as Animator;
                var cloth = sail.cloth == null ? null : sail.cloth.GetComponent<SkinnedMeshRenderer>();
                var cached = visual == null ? null : VisualUnfurledMaterial.GetValue(visual) as Material;
                var animation = animator == null ? "missing" :
                    $"enabled {animator.enabled}, initialized {animator.isInitialized}, speed {animator.speed:F3}, culling {animator.cullingMode}, controller {animator.runtimeAnimatorController?.name}";
                if (animator != null && animator.isInitialized && visual != null)
                {
                    var state = animator.GetCurrentAnimatorStateInfo(visual.layer);
                    animation += $", state {state.shortNameHash}, expected {Animator.StringToHash(visual.clipName)}, time {state.normalizedTime:F3}";
                }
                var bones = cloth == null ? "<missing cloth>" : string.Join("; ",
                    (cloth.bones ?? new Transform[0]).Where(bone => bone != null).Take(4).Select(bone =>
                        $"{bone.name}: position {bone.localPosition.ToString("F3")}, rotation {bone.localRotation.ToString("F3")}, scale {bone.localScale.ToString("F3")}"));
                Plugin.Instance.Report($"Leopard sail diagnostic [{phase}]: reef {reef.currentLength:F3}, unroll {sail.currentUnroll:F3}; animator {animation}; " +
                    $"refreshing {(visual == null ? "missing" : VisualRefreshing.GetValue(visual))}, isFurled {(visual == null ? "missing" : VisualFurled.GetValue(visual))}; cached deployed material {MaterialLabel(cached)}; " +
                    $"cloth {RendererLabel(cloth)}; furled {RendererLabel(visual?.furledSail)}; bones {bones}.");
            }
            catch (Exception exception)
            {
                Plugin.Instance?.Warn("Leopard sail diagnostic unavailable: " + exception.Message);
            }
        }

        private static string MaterialLabel(Material material) => material == null ? "<none>" :
            $"{material.name} [shader {material.shader?.name}, native empty {material == Refs.emptyMaterial}]";

        private static string RendererLabel(Renderer renderer) => renderer == null ? "<none>" :
            $"{renderer.name} [enabled {renderer.enabled}, visible {renderer.isVisible}, bounds {renderer.bounds.center.ToString("F3")}/{renderer.bounds.size.ToString("F3")}, material {MaterialLabel(renderer.sharedMaterial)}]";

        private static RopeControllerSailReef Install(ResolvedStart selected)
        {
            var root = selected.Saveable.transform;
            var masts = root.GetComponentsInChildren<Mast>(true);
            if (root.GetComponentsInChildren<Sail>(true).Length != 0 ||
                masts.Any(mast => mast.sails == null || mast.sails.Count != 0 ||
                    mast.startSailPrefab != null || mast.startSailPrefabs == null || mast.startSailPrefabs.Length != 0) ||
                GameState.modData != null && GameState.modData.ContainsKey("SEboatSails.207"))
            {
                Plugin.Instance.Report("Leopard starter sail skipped: existing sails or rig configuration preserved.");
                return null;
            }
            var target = root.Find(MastPath);
            var mast = target != null ? target.GetComponent<Mast>() : null;
            var refs = root.GetComponent<BoatRefs>();
            var customization = root.GetComponent<SaveableBoatCustomization>();
            var directory = PrefabsDirectory.instance != null ? PrefabsDirectory.instance.sails : null;
            var prefab = directory != null && directory.Length > SailIndex ? directory[SailIndex] : null;
            var sail = prefab != null ? prefab.GetComponent<Sail>() : null;
            var connections = prefab != null ? prefab.GetComponent<SailConnections>() : null;
            var prefabReef = connections != null ? connections.reefController as RopeControllerSailReef : null;
            if (AttachInitialSail == null || UpdateSailUnroll == null ||
                VisualAnimator == null || VisualRefreshing == null || VisualFurled == null ||
                VisualUnfurledMaterial == null ||
                WinchClicked == null || WinchStickyClickedBy == null || mast == null || !mast.isActiveAndEnabled ||
                mast.orderIndex != 7 || mast.shipRigidbody != selected.Body ||
                refs == null || refs.masts == null || refs.masts.Length <= 7 || refs.masts[7] != mast ||
                customization == null || !customization.enabled ||
                mast.maxSails < 1 || mast.onlySquareSails || mast.onlyStaysails || mast.walkColMast == null ||
                !Present(mast.reefWinch) || !Present(mast.leftAngleWinch) || !Present(mast.rightAngleWinch) ||
                !Present(mast.midAngleWinch) || !Present(mast.midRopeAtt) || !Present(mast.mastReefAtt) ||
                prefab == null || prefab.name != SailName || sail == null || sail.prefabIndex != SailIndex ||
                sail.category != SailCategory.lateen || connections == null || connections.colChecker == null ||
                prefabReef == null || !prefabReef.enabled || prefabReef.sail != sail ||
                prefab.GetComponent<Rigidbody>() == null || prefab.GetComponent<HingeJoint>() == null ||
                !Finite(sail.installHeight) || sail.installHeight <= 0f ||
                !Finite(mast.mastHeight) || sail.installHeight > mast.mastHeight)
            {
                Plugin.Instance.Warn("Leopard starter sail skipped: inspected mainmast or medium lateen structure is unavailable or incompatible.");
                return null;
            }
            AttachInitialSail.Invoke(mast, new object[] { prefab, sail.installHeight - mast.mastHeight });
            var installed = mast.sails.Count == 1 && mast.sails[0] != null ? mast.sails[0].GetComponent<Sail>() : null;
            if (installed == null || installed.prefabIndex != SailIndex || installed.shipRigidbody != selected.Body || !installed.IsInstalled())
                throw new InvalidOperationException("Native medium lateen attachment did not report one installed sail on Leopard.");
            var installedConnections = installed.GetComponent<SailConnections>();
            var reef = installedConnections != null ? installedConnections.reefController as RopeControllerSailReef : null;
            if (reef == null || reef.sail != installed || mast.reefWinch[0].rope != reef)
                throw new InvalidOperationException("Native medium lateen reef control was not attached to the mainmast winch.");
            FurlInstalledSail(reef);
            pendingWinch = mast.reefWinch[0];
            diagnosticWinch = pendingWinch;
            diagnosticReef = reef;
            diagnosticExpiresAt = Time.realtimeSinceStartup + 60f;
            Plugin.Instance.Report($"Leopard starter sail: installed one medium lateen (prefab {SailIndex}) on mainmast 7 at {sail.installHeight:F2}m; initial reef value set, native visual initialization pending.");
            return reef;
        }

        private static void FurlInstalledSail(RopeControllerSailReef reef)
        {
            // The winch edits this controller length. Update the native sail
            // state immediately as well, before its first physics/render update.
            reef.currentLength = reef.reverseReefing ? 1f : 0f;
            reef.changed = true;
            UpdateSailUnroll.Invoke(reef, null);
            if (reef.sail.currentUnroll != 0f)
                throw new InvalidOperationException("Native reef control did not fully furl the Leopard starter sail.");
        }

        private static bool Present<T>(T[] values) where T : UnityEngine.Object =>
            values != null && values.Length > 0 && values[0] != null;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
