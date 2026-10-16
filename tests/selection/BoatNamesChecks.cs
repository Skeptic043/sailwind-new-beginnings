using System;
using NewBeginnings;

public sealed class PurchasableBoat : UnityEngine.Component
{
    internal int SceneIndex;
    internal string RuntimeName;
}

internal static class BoatNamesChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(!BoatNames.Register(null, "Name"), "null boat registration was accepted");
        var boat = new PurchasableBoat { SceneIndex = 401, RuntimeName = "same boat" };
        check(!BoatNames.Register(boat, null) && !BoatNames.Register(boat, "   "),
            "blank boat display name was accepted");
        check(BoatNames.DisplayName(boat, "Detected") == "Detected", "unregistered boat lost detected fallback");
        check(BoatNames.Register(boat, "  Friendly Boat  ") &&
            BoatNames.DisplayName(boat, "Detected") == "Friendly Boat", "registered boat name was not trimmed or applied");
        check(BoatNames.Register(boat, "Replacement") &&
            BoatNames.DisplayName(boat, "Detected") == "Replacement", "same live boat did not replace its name");

        var recreated = new PurchasableBoat { SceneIndex = boat.SceneIndex, RuntimeName = boat.RuntimeName };
        check(BoatNames.DisplayName(recreated, "Recreated fallback") == "Recreated fallback",
            "registration matched a recreated boat by metadata instead of exact live object");
        boat.Destroyed = true;
        check(BoatNames.DisplayName(boat, "Destroyed fallback") == "Destroyed fallback",
            "destroyed boat registration remained visible");
        check(BoatNames.DisplayName(recreated, "Recreated fallback") == "Recreated fallback",
            "pruned stale registration affected a recreated live boat");
    }
}
