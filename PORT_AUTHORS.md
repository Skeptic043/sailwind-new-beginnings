# Describing a starting port

New Beginnings discovers loaded, registered ports automatically. An author registration is useful when a port has several docks or needs a preferred berth, shore location, mooring pair or shipyard. It enriches that one automatic entry. It does not create another starting location, native Port or recovery destination.

Call this on Unity's main thread after your native Port has registered and the scene objects you supply are ready:

```csharp
StartPorts.Register(providerId, siteId, port, options, out string reason);
```

Use your plugin GUID as `providerId` and a stable name such as `main-dock` as `siteId`. A successful call accepts the description for that exact live Port. At startup New Beginnings still checks the selected boat, real moorings, scenery, dry landing and supplies. If a check fails, the start carries on with the closest fit and writes a warning to the log.

## Optional integration

Compile against New Beginnings and the matching game assemblies, but do not bundle `NewBeginnings.dll` with your mod. Add this attribute to your existing plugin class:

```csharp
[BepInDependency("com.skeptic043.sailwind.newbeginnings",
    BepInDependency.DependencyFlags.SoftDependency)]
```

Keep New Beginnings types inside a separate method marked `NoInlining`. Call that method only when the plugin is present. For example, these methods can live inside your existing `BaseUnityPlugin`:

```csharp
private void OfferStartingLocation(Port port, Transform dock, Transform shore)
{
    if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(
        "com.skeptic043.sailwind.newbeginnings"))
        RegisterStartingLocation(port, dock, shore);
}

[System.Runtime.CompilerServices.MethodImpl(
    System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
private void RegisterStartingLocation(Port port, Transform dock, Transform shore)
{
    var options = new NewBeginnings.StartPortOptions
    {
        DisplayName = "My port",
        DockRoot = dock,
        ShoreMarker = shore
    };
    if (!NewBeginnings.StartPorts.Register(
        "your.plugin.guid", "main-dock", port, options, out string reason))
        Logger.LogWarning("Starting location was not registered: " + reason);
}
```

Use `BepInEx` and `UnityEngine` for the attribute and Unity types above. Call `OfferStartingLocation` from your own port initialization after native registration, rather than assuming the port already exists in your plugin's `Awake`.

## Options

All options are optional. Omitted values leave automatic discovery unchanged.

| Option | Purpose |
| --- | --- |
| `DisplayName` | Name shown in the starting-location list. Defaults to the port name. |
| `DockRoot` | Limits berth and shipyard discovery to this part of the island. Defaults to the island root. |
| `BerthMarker` | Preferred boat position and heading, subject to clearance and rope checks. |
| `ShoreMarker` | Starting point for the dry-land search. |
| `SceneryRoot` | Identifies the loaded scenery required for this site. |
| `FrontMooring`, `BackMooring` | A pair of distinct real dock-mooring components. Supply both or neither. |
| `Shipyard` | Uses this real loaded shipyard instead of an automatically discovered one. |

Supply live objects belonging to the same island as the registered Port. Supplying a dock, berth, shore or mooring pair tells New Beginnings to plan the starting berth from those details. A display name, scenery root or shipyard by itself does not move the ordinary starting berth. Native starter regions remain the game's three supported regions. Registration cannot declare arbitrary placement safe or add a custom recovery rule.

One live Port accepts one registration. Repeating the same identity and description is accepted. A different identity or description for that Port is rejected with a reason. New Beginnings copies the options, so changing the options object afterward does not update an accepted registration. Register the recreated Port again after unloading the old object. Supplied objects that are destroyed or moved to another island invalidate their declaration.

The native Port remains the destination and recovery identity. Once a start is accepted, it retains its chosen port and berth checks. Registration cannot redirect that start while it is loading.
