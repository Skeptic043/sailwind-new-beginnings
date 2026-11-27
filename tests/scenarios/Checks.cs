using System;
using System.Collections.Generic;
using System.Linq;
using NewBeginnings;
using UnityEngine;

static class ScenarioChecks
{
    static int checks;

    static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    static RandomCandidate Item(string key, int price, bool modded = false) =>
        new RandomCandidate { Key = key, Price = price, IsModded = modded };

    static readonly RandomCandidate[] Catalog =
    {
        Item("cheap", 10), Item("mid", 60), Item("dear", 400), Item("free", 0), Item("mod", 30, modded: true),
    };

    static Scenario Find(string id) => Scenarios.All.Single(item => item.Id == id);

    static int Main()
    {
        try
        {
            RollStaysInBudget();
            RollHonoursPoolRules();
            RandomLoadoutApply();
            RandomLoadoutGuaranteed();
            NavigationSetsStaySingle();
            ScenarioGuarantees();
            ScenarioDefaults();
            ModIntegrationValidation();
            OpenWater();
            Console.WriteLine($"PASS {checks} scenario and random loadout checks.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: " + exception.Message);
            return 1;
        }
    }

    static void RollStaysInBudget()
    {
        var prices = Catalog.ToDictionary(item => item.Key, item => item.Price);
        for (var seed = 0; seed < 200; seed++)
        {
            var roll = StartLoadout.Roll(Catalog, 1000, true, new System.Random(seed));
            var spent = roll.Sum(pair => prices[pair.Key] * pair.Value);
            Check(spent <= 1000, "roll spent more than its budget");
            Check(1000 - spent < 10, "roll stopped while the cheapest item was still affordable");
            Check(!roll.ContainsKey("free"), "a zero-price item entered the roll");
        }
        Check(StartLoadout.Roll(Catalog, 5, true, new System.Random(1)).Count == 0, "a budget below every price still rolled items");
    }

    static void RollHonoursPoolRules()
    {
        var single = StartLoadout.Roll(Catalog, 10000, false, new System.Random(3));
        Check(single.Values.All(count => count == 1), "multiples off picked an item twice");
        var everything = StartLoadout.Roll(Catalog, StartingOptionsSettings.MaximumRandomBudget, false, new System.Random(4));
        Check(everything.Count == Catalog.Count(item => item.Price > 0), "multiples off did not stop at one of every priced item");
        var options = new StartingOptionsSettings();
        options.RandomExclusions.Add("dear");
        var eligible = StartLoadout.Eligible(Catalog, options).Select(item => item.Key).ToArray();
        Check(!eligible.Contains("dear"), "an excluded item stayed eligible");
        Check(!eligible.Contains("mod"), "a mod item was eligible with mod items off");
        options.RandomModItems = true;
        Check(StartLoadout.Eligible(Catalog, options).Any(item => item.Key == "mod"), "mod items on left out a mod item");
    }

    static void RandomLoadoutApply()
    {
        var options = new StartingOptionsSettings { RandomLoadout = true, RandomBudget = 500 };
        options.EquipmentQuantities["chosen"] = 4;
        StartLoadout.Apply(options, 1, null, () => Catalog, new System.Random(7));
        Check(!options.EquipmentQuantities.ContainsKey("chosen"), "random loadout kept the manual selection");
        Check(!options.StandardSupplies, "random loadout kept standard supplies");
        Check(options.EquipmentQuantities.ContainsKey(StartLoadout.CompassKey(1)) && StartLoadout.CompassKey(1).Contains("compass E"),
            "random loadout lacks the start region's compass");
        Check(options.EquipmentQuantities.ContainsKey(StartLoadout.WaterBarrelKey), "random loadout lacks its water barrel");
        Check(options.EquipmentQuantities.ContainsKey(StartLoadout.SalmonKey) || options.EquipmentQuantities.ContainsKey(StartLoadout.OrangesKey),
            "random loadout lacks its food crate");
        var manual = new StartingOptionsSettings();
        manual.EquipmentQuantities["chosen"] = 2;
        StartLoadout.Apply(manual, 0, null, () => throw new Exception("catalog read without a random loadout"), new System.Random(1));
        Check(manual.EquipmentQuantities["chosen"] == 2 && manual.StandardSupplies, "a manual start changed without a scenario");
    }

    static void RandomLoadoutGuaranteed()
    {
        var options = new StartingOptionsSettings { RandomLoadout = true, RandomBudget = 0, RandomModItems = false, RandomMultiples = false };
        options.RandomExclusions.Add("dear");
        options.RandomGuaranteed["dear"] = 3;
        options.RandomGuaranteed["mod"] = 2;
        options.RandomGuaranteed[StartLoadout.CompassKey(2)] = 1;
        StartLoadout.Apply(options, 0, null, () => Catalog, new System.Random(1));
        Check(options.EquipmentQuantities["dear"] == 3, "guaranteed excluded item did not spawn with requested quantity");
        Check(options.EquipmentQuantities["mod"] == 2, "guaranteed mod item was blocked by RandomModItems=false");
        Check(Compasses.Count(options.EquipmentQuantities.ContainsKey) == 1 && options.EquipmentQuantities[StartLoadout.CompassKey(2)] == 1,
            "guaranteed compass did not satisfy the default compass check");
    }

    static void ModIntegrationValidation()
    {
        var valid = new StartingOptionsSettings { StartingSeason = 3, StartingSailDamage = 75, StartWithShepherd = true, StartWithCat = true, StartWithBooby = true };
        Check(valid.TryValidate(out _), "valid mod integration settings failed validation");
        var copy = valid.Copy();
        Check(copy.StartingSeason == 3 && copy.StartingSailDamage == 75 && copy.StartWithShepherd && copy.StartWithCat && copy.StartWithBooby,
            "Copy did not preserve mod integration settings");
        var fullDamage = new StartingOptionsSettings { StartingSailDamage = 100 };
        Check(!fullDamage.TryValidate(out _), "100% starting sail damage should be rejected");
        var badSeason = new StartingOptionsSettings { StartingSeason = 5 };
        Check(!badSeason.TryValidate(out _), "out-of-range season should be rejected");
    }

    static readonly string[] Compasses = { "native:80:80 compass A", "native:81:81 compass E", "native:82:82 compass M" };
    static readonly string[] Chronocompasses = { "native:83:83 chronometer A", "native:84:84 chronometer E", "native:85:85 chronometer M" };
    static readonly string[] Chronometers = { "native:170:170 clock A", "native:171:171 clock E", "native:172:172 clock M" };

    static void NavigationSetsStaySingle()
    {
        var sets = new[] { Compasses, Chronocompasses, Chronometers };
        var catalog = sets.SelectMany(set => set).Select(key => Item(key, 100))
            .Concat(new[] { Item("hooks", 20), Item("food", 30), Item("dear", 5000) }).ToArray();
        foreach (var multiples in new[] { true, false })
            for (var seed = 0; seed < 200; seed++)
            {
                // The dear item stays unaffordable, so not every item can be picked.
                var roll = StartLoadout.Roll(catalog, 3000, multiples, new System.Random(seed));
                Check(sets.All(set => set.Sum(key => roll.TryGetValue(key, out var count) ? count : 0) <= 1),
                    "a roll gave two kinds of one navigation tool before every other item");
            }
        var everything = StartLoadout.Roll(catalog, StartingOptionsSettings.MaximumRandomBudget, false, new System.Random(5));
        Check(everything.Count == catalog.Length, "a roll with every other item picked still held back navigation tools");
        var options = new StartingOptionsSettings { RandomLoadout = true, RandomBudget = 3000 };
        for (var seed = 0; seed < 50; seed++)
        {
            StartLoadout.Apply(options, 1, Find("poor-drunk"), () => catalog, new System.Random(seed));
            Check(Compasses.Count(options.EquipmentQuantities.ContainsKey) == 1,
                "the guaranteed compass was added beside a rolled one, or no compass was given");
        }
    }

    static void ScenarioGuarantees()
    {
        var lost = Find("lost-at-sea");
        var options = new StartingOptionsSettings();
        lost.Defaults(new SelectionSettings(), options);
        options.StandardSupplies = true;
        options.EquipmentQuantities[StartLoadout.CompassKey(2)] = 3;
        StartLoadout.Apply(options, 2, lost, () => Catalog, new System.Random(1));
        Check(!options.StandardSupplies, "an at-sea scenario kept shore supplies");
        Check(options.EquipmentQuantities[StartLoadout.CompassKey(2)] == 3, "a guarantee reduced or added to a larger chosen quantity");
        var fisher = Find("fishermans-luck");
        var fishing = new StartingOptionsSettings();
        StartLoadout.Apply(fishing, 0, fisher, () => Catalog, new System.Random(1));
        Check(fishing.EquipmentQuantities[StartLoadout.FishingRodKey] == 1 && fishing.EquipmentQuantities[StartLoadout.FishingHooksKey] == 1 &&
            fishing.EquipmentQuantities[StartLoadout.WaterBarrelKey] == 1 && fishing.EquipmentQuantities[StartLoadout.CompassKey(0)] == 1,
            "Fisherman's Luck lacks a guaranteed item");
        var drunk = new StartingOptionsSettings();
        StartLoadout.Apply(drunk, 1, Find("drunk-and-lost"), () => Catalog, new System.Random(1));
        Check(!drunk.EquipmentQuantities.Keys.Any(key => key.Contains("compass")), "Drunk at Sea gave a compass");
        var wreck = new StartingOptionsSettings();
        StartLoadout.Apply(wreck, 0, Find("shipwrecked"), () => Catalog, new System.Random(1));
        Check(wreck.StandardSupplies && wreck.EquipmentQuantities[StartLoadout.BucketKey] == 1, "Shipwrecked lost its supplies or bucket");
        var boned = new StartingOptionsSettings();
        StartLoadout.Apply(boned, 2, Find("boned"), () => Catalog, new System.Random(1));
        Check(boned.EquipmentQuantities[AdditionalEquipment.LargeWaterBottleKey] == 1 &&
            boned.EquipmentQuantities[StartLoadout.EelKey] == 1,
            "Boned lacks its large water bottle or uncooked eel");
    }

    static void ScenarioDefaults()
    {
        Check(Scenarios.All.Select(item => item.Id).Distinct().Count() == Scenarios.All.Length, "scenario identities repeat");
        var selection = new SelectionSettings();
        var options = new StartingOptionsSettings();
        Find("fishermans-luck").Defaults(selection, options);
        Check(selection.RandomPort && selection.RandomBoat && selection.BoatSizes.SetEquals(new[] { BoatSize.Small }),
            "Fisherman's Luck does not start with a random small boat");
        Check(!options.StandardSupplies && options.CurrencyOverrides.All(amount => amount == 0), "Fisherman's Luck has supplies or money");
        selection = new SelectionSettings();
        options = new StartingOptionsSettings();
        var wreck = Find("shipwrecked");
        wreck.Defaults(selection, options);
        Check(selection.RandomPort && wreck.Portless && wreck.HullDamage > .7f && !wreck.AtSea && wreck.DefaultSailDamage == 50,
            "Shipwrecked is not a damaged random start on an island without a port");
        selection = new SelectionSettings();
        options = new StartingOptionsSettings();
        var boned = Find("boned");
        boned.Defaults(selection, options);
        Check(!selection.RandomPort && selection.DestinationKey == KnownSites.BoneIsleKey && boned.AllowBoneIsle &&
            selection.RandomBoat && selection.BoatSizes.SetEquals(new[] { BoatSize.Small }) &&
            !options.StandardSupplies && options.CurrencyOverrides.All(amount => amount == 0),
            "Boned defaults do not match Bone Isle small-boat survival start");
        Check(Find("poor-drunk").Alcohol == 50f && Find("drunk-and-lost").Alcohol == 80f, "scenario drunkenness changed");
        Check(Find("lost-at-sea").Place == ScenarioPlace.FarAtSea && Find("drunk-and-lost").Place == ScenarioPlace.NearLand &&
            Find("drunk-and-lost").FillWithRum, "at-sea scenarios lost their place or rum");
    }

    static void OpenWater()
    {
        var anchor = new Vector2(0, 0);
        var islands = new List<Vector2> { anchor, new Vector2(3000, 0) };
        for (var seed = 0; seed < 50; seed++)
        {
            Check(OpenWaterPicker.TryPick(anchor, islands, ScenarioPlace.NearLand, point => true, new System.Random(seed),
                out var point, out var kept) && kept, "near-land pick found no point");
            var distance = point.magnitude;
            Check(distance >= 600 && distance <= 1500, "near-land point left its band");
            Check(islands.All(island => (island - point).magnitude >= 500), "near-land point too close to an island");
            Check(OpenWaterPicker.TryPick(anchor, islands, ScenarioPlace.FarAtSea, candidate => candidate.x < 0, new System.Random(seed),
                out var far, out _) && far.x < 0 && far.magnitude >= 2500 && far.magnitude <= 6000, "far pick ignored land or its band");
        }
        var crowded = Enumerable.Range(0, 17 * 17).Select(index => new Vector2(index % 17 * 1000 - 8000, index / 17 * 1000 - 8000)).ToList();
        Check(OpenWaterPicker.TryPick(anchor, crowded, ScenarioPlace.FarAtSea, point => true, new System.Random(2), out _, out var keptCrowded) &&
            !keptCrowded, "a crowded sea did not fall back to the clearest point");
        Check(!OpenWaterPicker.TryPick(anchor, islands, ScenarioPlace.NearLand, point => false, new System.Random(2), out _, out _),
            "an all-land sea produced a point");
    }
}
