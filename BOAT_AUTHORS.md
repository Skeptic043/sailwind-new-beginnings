# Naming a boat

New Beginnings normally identifies added boats automatically. If the detected name is not suitable for players, a boat author can register a display name for the live `PurchasableBoat`:

```csharp
NewBeginnings.BoatNames.Register(purchasableBoat, "My Boat");
```

Call this on Unity's main thread after creating the `PurchasableBoat`. The name is trimmed and replaces any earlier name registered for that exact live object. Blank names and missing or destroyed boats are rejected.

The registration changes only the name shown by New Beginnings. Boat identity, size detection, placement and save behavior are unchanged.

## Optional integration

Compile against New Beginnings and the matching game assemblies, but do not bundle `NewBeginnings.dll` with your mod. Add this attribute to your existing plugin class:

```csharp
[BepInDependency("com.skeptic043.sailwind.newbeginnings",
    BepInDependency.DependencyFlags.SoftDependency)]
```

Keep the New Beginnings call inside a separate method marked `NoInlining`, and call it only when the plugin is present:

```csharp
private void OfferBoatName(PurchasableBoat boat)
{
    if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(
        "com.skeptic043.sailwind.newbeginnings"))
        RegisterBoatName(boat);
}

[System.Runtime.CompilerServices.MethodImpl(
    System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
private void RegisterBoatName(PurchasableBoat boat)
{
    if (!NewBeginnings.BoatNames.Register(boat, "My Boat"))
        Logger.LogWarning("Boat name was not registered.");
}
```

Use `BepInEx` for the dependency attribute. Register each recreated boat object again after its old object is unloaded.
