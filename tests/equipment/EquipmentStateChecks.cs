using System;
using System.Linq;
using NewBeginnings;
using UnityEngine;

static class EquipmentStateChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var packed in new[] { true, false })
        {
            var start = EquipmentChecks.Setup(crateUi: packed);
            var native = new GameObject("native fishing rod").AddComponent<ShipItemFishingRod>();
            native.gameObject.AddComponent<SaveablePrefab>();
            native.transform.SetParent(start.StarterSet.transform, true);
            var selection = AdditionalEquipment.GetCatalog().Single(entry => entry.Index == 95);
            start.Options.EquipmentQuantities[selection.Key] = 2;
            check(AdditionalEquipment.Append(start, EquipmentChecks.Children(start)), "fishing rod append failed");
            var extras = EquipmentChecks.Children(start).OfType<ShipItemFishingRod>().Where(AdditionalEquipment.IsExtra).ToArray();
            AdditionalEquipment.CompleteLoosePurchases();
            check(extras.All(rod => rod.purchaseCalls == 0), "purchase called before native ownership");
            EquipmentChecks.NativeInitialize(start);
            GameState.currentlyLoading = true;
            AdditionalEquipment.CompleteLoosePurchases();
            check(extras.All(rod => rod.purchaseCalls == 0), "loose purchase ran while loading");
            GameState.currentlyLoading = false;
            check(AdditionalEquipment.TryPack(start, _ => { }, out _), "fishing rod packing stalled");
            AdditionalEquipment.CompleteLoosePurchases();
            AdditionalEquipment.CompleteLoosePurchases();
            AdditionalEquipment.TryPack(start, _ => { }, out _);
            check(extras.Length == 2 && extras.All(rod => rod.purchaseCalls == 1), "extra rod purchase not completed exactly once");
            check(native.purchaseCalls == 0, "native rod received extra-equipment purchase callback");
            check(extras.All(rod => AdditionalEquipment.IsPacked(rod) == packed), "rod did not follow packing capability");
            AdditionalEquipment.Reset();
            AdditionalEquipment.CompleteLoosePurchases();
            check(extras.All(rod => rod.purchaseCalls == 1), "released rod received another purchase callback");
        }

        PackingFailurePreservesNativeMembership(check);
        ResetAllowsNewStart(check);
    }

    static void PackingFailurePreservesNativeMembership(Action<bool, string> check)
    {
        foreach (var afterMembership in new[] { false, true })
        {
            var start = EquipmentChecks.Setup();
            var selection = AdditionalEquipment.GetCatalog().Single(entry => entry.Index == 40);
            start.Options.EquipmentQuantities[selection.Key] = 3;
            check(AdditionalEquipment.Append(start, EquipmentChecks.Children(start)), "failure fixture append failed");
            EquipmentChecks.NativeInitialize(start);
            var items = EquipmentChecks.Children(start).ToArray();
            var carrier = items.Single(AdditionalEquipment.IsCarrier);
            var inventory = carrier.GetComponent<CrateInventory>();
            CrateInventory.failOnInsertCall = 2;
            CrateInventory.failAfterInsert = afterMembership;
            check(!AdditionalEquipment.TryPack(start, _ => { }, out _), "injected insertion failure succeeded");
            var contents = inventory.containedItems.ToArray();
            check(contents.Length == (afterMembership ? 2 : 1), "partial native insertion was rolled back or lost");
            check(contents.All(item => EquipmentChecks.InventoryContains(item, new[] { carrier })), "partial insertion lost saved native membership");
            check(items.All(item => !item.destroyed && !item.gameObject.destroyed), "insertion failure destroyed legitimate supplies");
            CrateInventory.failOnInsertCall = 0;
            check(!AdditionalEquipment.TryPack(start, _ => { }, out _) && CrateInventory.insertCalls == 2, "uncertain insertion retried");
            check(inventory.containedItems.SequenceEqual(contents), "retry duplicated or removed native contents");
            AdditionalEquipment.Reset();
            check(inventory.containedItems.SequenceEqual(contents), "reset removed partial native contents");
        }
    }

    static void ResetAllowsNewStart(Action<bool, string> check)
    {
        var previous = EquipmentChecks.Setup();
        var selection = AdditionalEquipment.GetCatalog().Single(entry => entry.Index == 95);
        previous.Options.EquipmentQuantities[selection.Key] = 1;
        AdditionalEquipment.Append(previous, EquipmentChecks.Children(previous));
        EquipmentChecks.NativeInitialize(previous);
        AdditionalEquipment.TryPack(previous, _ => { }, out _);
        var previousItems = EquipmentChecks.Children(previous).ToArray();
        var previousInventory = previousItems.Single(AdditionalEquipment.IsCarrier).GetComponent<CrateInventory>();
        var contents = previousInventory.containedItems.ToArray();
        AdditionalEquipment.Reset();
        var next = new ResolvedStart
        {
            StarterSet = new GameObject("next starter").AddComponent<StarterSet>(),
            Options = new StartingOptionsSettings()
        };
        next.Options.EquipmentQuantities[selection.Key] = 1;
        check(AdditionalEquipment.Append(next, Array.Empty<ShipItem>()), "next start could not append after reset");
        EquipmentChecks.NativeInitialize(next);
        check(AdditionalEquipment.TryPack(next, _ => { }, out _), "next start retained prior completion or failure state");
        var nextItems = EquipmentChecks.Children(next).ToArray();
        var nextRod = nextItems.OfType<ShipItemFishingRod>().Single();
        check(nextRod.purchaseCalls == 1 && EquipmentChecks.InventoryContains(nextRod, nextItems.Where(AdditionalEquipment.IsCarrier)), "next start did not initialize its own equipment");
        check(previousItems.All(item => !AdditionalEquipment.IsAdditional(item) && !AdditionalEquipment.IsPacked(item)), "previous start ownership leaked");
        check(previousInventory.containedItems.SequenceEqual(contents), "next start changed previous native inventory");
    }
}
