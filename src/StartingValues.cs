using System;
using System.Collections;
using HarmonyLib;

namespace NewBeginnings
{
    internal static class StartingValues
    {
        private static ResolvedStart pending;

        internal static void Arm(ResolvedStart selected) => pending = selected;
        internal static void Cancel() => pending = null;

        internal static int MultiplyCurrency(int amount, float multiplier)
        {
            if (amount < 0 || float.IsNaN(multiplier) || float.IsInfinity(multiplier) ||
                multiplier < 0f || multiplier > 100f)
                throw new ArgumentOutOfRangeException(nameof(multiplier));
            // Work in double before rounding so a large modded grant cannot overflow.
            return (int)Math.Min(int.MaxValue, Math.Round(amount * (double)multiplier,
                MidpointRounding.AwayFromZero));
        }

        internal static int ReputationLevel(StartingOptionsSettings options, int faction, int startingFaction) =>
            options.FactionReputation[faction] > 0 ? options.FactionReputation[faction] :
                faction == startingFaction ? options.StartingReputation : 0;

        private static void Apply(ResolvedStart selected)
        {
            var options = selected.Options;
            if (options == null || !options.TryValidate(out var reason))
                throw new InvalidOperationException("The confirmed starting options are invalid.");
            var region = selected.Region;
            if (region < 0 || region >= 3) throw new InvalidOperationException("Unknown starting faction.");
            var balances = PlayerGold.currency;
            var reputation = PlayerReputation.GetSaveData();
            var currencyChanges = new int?[4];
            var reputationChanges = new int?[3];
            for (var i = 0; i < currencyChanges.Length; i++)
            {
                if (options.CurrencyOverrides[i].HasValue) currencyChanges[i] = options.CurrencyOverrides[i];
                else if (i == region && options.CurrencyMultiplier != 1f)
                {
                    if (balances == null || balances.Length < 4)
                        throw new InvalidOperationException("Native starting currencies are unavailable.");
                    currencyChanges[i] = MultiplyCurrency(balances[i], options.CurrencyMultiplier);
                }
                if (currencyChanges[i].HasValue && (balances == null || balances.Length < 4))
                    throw new InvalidOperationException("Native starting currencies are unavailable.");
            }
            var changedReputation = false;
            for (var i = 0; i < reputationChanges.Length; i++)
            {
                var level = ReputationLevel(options, i, region);
                if (level == 0) continue;
                if (reputation == null || reputation.Length < 3)
                    throw new InvalidOperationException("Native starting reputation is unavailable.");
                reputationChanges[i] = PlayerReputation.GetRequiredRep(level);
                changedReputation = true;
            }
            for (var i = 0; i < currencyChanges.Length; i++)
                if (currencyChanges[i].HasValue) balances[i] = currencyChanges[i].Value;
            for (var i = 0; i < reputationChanges.Length; i++)
                if (reputationChanges[i].HasValue) reputation[i] = reputationChanges[i].Value;
            if (changedReputation) PlayerReputation.UpdateReputation();
        }

        // Both native and Scrambled Seas starts reach this factory after their
        // currency grant. Difficulty adjusts its balance in a factory prefix.
        // A postfix observes that resulting balance without changing mod order.
        [HarmonyPatch(typeof(StarterSet), "InitiateStarterSet")]
        [HarmonyAfter("com.nandbrew.sailwinddifficulty", ScrambledSeasIntegration.Id)]
        [HarmonyPriority(Priority.Last)]
        private static class ApplyPatch
        {
            private static void Postfix(StarterSet __instance, IEnumerator __result)
            {
                if (pending == null || !ReferenceEquals(pending.StarterSet, __instance)) return;
                var selected = pending;
                Cancel();
                if (__result == null || !GameState.justStarted ||
                    (int)GameState.newGameRegion != selected.Region) return;
                try { Apply(selected); }
                catch (Exception exception)
                {
                    Plugin.Instance?.Error("Starting currency or reputation could not be applied.", exception);
                }
            }

            private static Exception Finalizer(StarterSet __instance, Exception __exception)
            {
                if (__exception != null && pending != null &&
                    ReferenceEquals(pending.StarterSet, __instance)) Cancel();
                return __exception;
            }
        }

        // Loading a save never arms values. Clear an interrupted new-game request
        // before native loading can raise its own justStarted flag.
        [HarmonyPatch(typeof(SaveLoadManager), "LoadGame", new[] { typeof(int) })]
        private static class LoadPatch
        {
            private static void Prefix() => Cancel();
        }
    }
}
