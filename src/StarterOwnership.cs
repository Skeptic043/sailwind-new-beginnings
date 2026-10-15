using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    // LoadAsPurchased normally sets extraSetting only while its purchase UI
    // exists. A confirmed starter still belongs to the player if that UI went
    // away during the new-game transition.
    internal static class StarterOwnership
    {
        private static ResolvedStart pending;
        private static readonly System.Reflection.FieldInfo PurchaseUiField = AccessTools.Field(typeof(PurchasableBoat), "purchaseUI");

        internal static void Arm(ResolvedStart selected) => pending = selected;
        internal static void Cancel() => pending = null;
        internal static void Cancel(ResolvedStart selected)
        {
            if (ReferenceEquals(pending, selected)) pending = null;
        }

        [HarmonyPatch(typeof(PurchasableBoat), "LoadAsPurchased")]
        private static class GrantPatch
        {
            private static void Postfix(PurchasableBoat __instance)
            {
                var selected = pending;
                if (selected == null || selected.Boat != __instance) return;
                Cancel();
                if (GameState.currentlyLoading || selected.Saveable == null ||
                    selected.Boat == null || !FixedStart.IsSelectionStillActive(selected.Menu, selected) ||
                    selected.Boat.isPurchased()) return;
                var purchaseUi = PurchaseUiField?.GetValue(__instance) as GameObject;
                if (purchaseUi != null) return;
                selected.Saveable.extraSetting = true;
                Plugin.Instance?.Warn("The selected starter's purchase prompt disappeared. Its native new-game ownership grant was completed.");
            }
        }
    }
}
