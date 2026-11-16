using System;
using System.Collections.Generic;
using System.Linq;

namespace NewBeginnings
{
    // The final equipment for a start: a random loadout roll and a scenario's
    // guaranteed items are applied once to the confirmed options copy.
    internal static class StartLoadout
    {
        internal const string WaterBarrelKey = "native:10:10 barrel water";
        internal const string RumBarrelKey = "native:11:11 barrel rum";
        internal const string BucketKey = "native:70:70 bucket";
        internal const string FishingRodKey = "native:95:95 fishing rod 1";
        internal const string FishingHooksKey = "native:104:104 crate of fishing hooks";
        internal const string SalmonKey = "native:1:1 crate salmon (E)";
        internal const string OrangesKey = "native:213:213 (43) crate oranges";
        private static readonly string[] CompassKeys =
            { "native:80:80 compass A", "native:81:81 compass E", "native:82:82 compass M" };
        private static readonly string[] FoodCrateKeys = { SalmonKey, OrangesKey };
        // Navigation tools that do the same job: a roll gives one of each set
        // until every other item has been picked at least once.
        private static readonly string[][] NavigationSets =
        {
            CompassKeys,
            new[] { "native:83:83 chronometer A", "native:84:84 chronometer E", "native:85:85 chronometer M" },
            new[] { "native:170:170 clock A", "native:171:171 clock E", "native:172:172 clock M" },
        };

        internal static string CompassKey(int region) =>
            CompassKeys[region >= 0 && region < CompassKeys.Length ? region : 0];

        // Pick affordable items at random until none fits the remaining budget.
        internal static Dictionary<string, int> Roll(IEnumerable<RandomCandidate> candidates, int budget,
            bool multiples, Random random)
        {
            var pool = candidates.Where(item => item.Price > 0).ToList();
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            var remaining = budget;
            while (true)
            {
                var everyOtherPicked = pool.All(item => result.ContainsKey(item.Key) || SetTaken(item.Key, result));
                var affordable = pool.Where(item => item.Price <= remaining &&
                    (multiples || !result.ContainsKey(item.Key)) &&
                    (everyOtherPicked || !SetTaken(item.Key, result))).ToList();
                if (affordable.Count == 0) return result;
                var choice = affordable[random.Next(affordable.Count)];
                result.TryGetValue(choice.Key, out var count);
                result[choice.Key] = count + 1;
                remaining -= choice.Price;
            }
        }

        // A member of this item's navigation set, itself included, is already in the result.
        private static bool SetTaken(string key, Dictionary<string, int> result) =>
            NavigationSets.Any(set => set.Contains(key) && set.Any(result.ContainsKey));

        private static bool HasCompass(StartingOptionsSettings options) =>
            CompassKeys.Any(key => options.EquipmentQuantities.TryGetValue(key, out var count) && count > 0);

        internal static IEnumerable<RandomCandidate> Eligible(IEnumerable<RandomCandidate> catalog,
            StartingOptionsSettings options) => catalog.Where(item =>
                !options.RandomExclusions.Contains(item.Key) && (options.RandomModItems || !item.IsModded));

        internal static void Apply(StartingOptionsSettings options, int region, Scenario scenario,
            Func<IEnumerable<RandomCandidate>> catalog, Random random)
        {
            if (options.RandomLoadout)
            {
                var rolled = Roll(Eligible(catalog(), options), options.RandomBudget, options.RandomMultiples, random);
                options.EquipmentQuantities.Clear();
                foreach (var pair in rolled) options.EquipmentQuantities.Add(pair.Key, pair.Value);
                options.StandardSupplies = false;
                // Any rolled compass already covers the guaranteed one.
                if (!HasCompass(options)) Add(options, CompassKey(region));
                Add(options, WaterBarrelKey);
                Add(options, FoodCrateKeys[random.Next(FoodCrateKeys.Length)]);
            }
            if (scenario == null) return;
            if (scenario.AtSea) options.StandardSupplies = false;
            foreach (var pair in scenario.Guaranteed) Ensure(options, pair.Key, pair.Value);
            if (scenario.RegionalCompass && !HasCompass(options)) Ensure(options, CompassKey(region), 1);
        }

        private static void Add(StartingOptionsSettings options, string key)
        {
            options.EquipmentQuantities.TryGetValue(key, out var count);
            options.EquipmentQuantities[key] = count + 1;
        }

        private static void Ensure(StartingOptionsSettings options, string key, int count)
        {
            options.EquipmentQuantities.TryGetValue(key, out var current);
            if (current < count) options.EquipmentQuantities[key] = count;
        }
    }
}
