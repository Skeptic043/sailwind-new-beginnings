# Adding a starting location

New Beginnings discovers loaded, registered ports automatically. An author registration is useful when a port has several docks or needs a preferred berth or shore location. It does not create a native Port or recovery destination.

Call this on Unity's main thread after your native Port has registered and the scene objects you supply are ready:

```csharp
StartPorts.Register(providerId, siteId, port, options, out string reason);
```

Use your plugin GUID as `providerId` and a stable name such as `main-dock` as `siteId`. A successful call accepts the description. The selected boat, real moorings, scenery, dry landing and supplies still have to pass startup checks.

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

All options are optional. An empty `StartPortOptions` asks New Beginnings to inspect the port's island.

| Option | Purpose |
| --- | --- |
| `DisplayName` | Name shown in the starting-location list. Defaults to the port name. |
| `DockRoot` | Limits dock discovery to this part of the island. Defaults to the island root. |
| `BerthMarker` | Preferred boat position and heading, subject to clearance and rope checks. |
| `ShoreMarker` | Starting point for the dry-land search. |
| `SceneryRoot` | Identifies the loaded scenery required for this site. |
| `FrontMooring`, `BackMooring` | A pair of distinct real dock-mooring components. Supply both or neither. |
| `Shipyard` | Associates a real loaded shipyard with this site. |

Supply live objects belonging to the same island as the registered Port. Native starter regions remain the game's three supported regions. Registration cannot declare arbitrary placement safe or add a custom recovery rule.

Repeat calls with the same identity and description are accepted. Conflicting descriptions and registrations that reuse the same dock scope or berth marker are rejected with a reason. New Beginnings copies the options, so changing the options object afterward does not update an accepted registration. Register again with the new scene objects after unloading and recreating the port. Supplied objects that are destroyed or moved to another island invalidate their declaration.

Once a start is accepted, it retains its chosen site and berth checks. Registration cannot redirect that start while it is loading.
