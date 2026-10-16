using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object left, Object right) => ReferenceEquals(left, right) ||
            (ReferenceEquals(left, null) || left.Destroyed) && (ReferenceEquals(right, null) || right.Destroyed);
        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object value) => ReferenceEquals(this, value);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }

    public sealed class Scene
    {
        public bool valid = true, isLoaded = true;
        public bool IsValid() => valid;
    }

    public class GameObject : Object
    {
        private readonly Dictionary<Type, Component> components = new Dictionary<Type, Component>();
        public string name;
        public Scene scene = new Scene();
        public readonly Transform transform;

        public GameObject(string value = "")
        {
            name = value;
            transform = new Transform { gameObject = this };
            components[typeof(Transform)] = transform;
        }

        public T Add<T>() where T : Component, new()
        {
            var result = new T { gameObject = this };
            components[typeof(T)] = result;
            return result;
        }

        public T GetComponent<T>() where T : Component =>
            components.Values.OfType<T>().FirstOrDefault();
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public string name { get => gameObject.name; set => gameObject.name = value; }
        public Transform transform => gameObject.transform;

        public T GetComponentInParent<T>() where T : Component
        {
            for (var current = transform; current != null; current = current.parent)
            {
                var found = current.gameObject.GetComponent<T>();
                if (found != null) return found;
            }
            return null;
        }

        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component =>
            Resources.FindObjectsOfTypeAll<T>().Where(item => item.transform.IsChildOf(transform)).ToArray();
    }

    public class Transform : Component
    {
        public Transform parent;
        public void SetParent(Transform value) => parent = value;
        public bool IsChildOf(Transform value)
        {
            for (var current = this; current != null; current = current.parent)
                if (ReferenceEquals(current, value)) return true;
            return false;
        }
    }

    public static class Resources
    {
        public static Component[] Objects = new Component[0];
        public static T[] FindObjectsOfTypeAll<T>() where T : Component => Objects.OfType<T>().ToArray();
    }
}

public enum PortRegion { AlAnkh, Emerald, Aestrin }

public class IslandHorizon : UnityEngine.Component
{
    public int islandIndex;
}

public class IslandSceneryScene : UnityEngine.Component
{
    public int parentIslandIndex;
}

public static class Refs
{
    public static UnityEngine.Transform[] islands = new UnityEngine.Transform[100];
}

public class Port : UnityEngine.Component
{
    public static Port[] ports;
    public int portIndex;
    public PortRegion region;
    public bool enabled = true, Known;
    public string DisplayName;
    public string GetPortName() => DisplayName;
}

public class RecoveryPort : UnityEngine.Component
{
    public Port parentPort;
    public UnityEngine.Transform boatPos;
}

public class GPButtonDockMooring : UnityEngine.Component
{
}

public class Shipyard : UnityEngine.Component
{
    public bool enabled = true;
    public UnityEngine.Transform FortRoot;
}

namespace NewBeginnings
{
    internal enum HiddenSite { None, Pirate, Saffron }

    internal sealed class SelectionCatalog
    {
        internal readonly List<PortChoice> Ports = new List<PortChoice>();
        internal readonly List<string> Rejections = new List<string>();
        internal static bool IsKnownPort(Port port) => port.Known;
    }

    internal static class KnownSites
    {
        internal const string PirateHideoutKey = "vanilla:pirate-hideout";
        internal const string FortJuniperKey = "site:oldchronian|fort-juniper";
        internal static bool IsSaffronPort(Port port) => port != null && port.portIndex == 33 && port.name == "port A 33 (flower)";
        internal static bool IsPirateHideout(IslandHorizon island) => island.islandIndex == 32 && island.name == "island 32 A (hideout)";
        internal static bool IsChronos(PortChoice site) => site.Index == 21 && site.SiteIdentity == null &&
            string.Equals(site.IdentityName, "port 21 M chronos", StringComparison.Ordinal);
        internal static bool IsSirenSong(PortChoice site) => site.SiteIdentity == null && site.Index == 18 &&
            site.Port.name == "port M 18 Siren Song" && site.Island != null && site.Island.islandIndex == 21;
        internal static UnityEngine.Transform FortJuniperRoot(Shipyard yard) => yard.FortRoot;
    }

    internal static class ShipyardStart
    {
        internal static Shipyard PreferredYard(PortChoice destination, Shipyard[] yards) => destination.Yard;
        internal static bool HasNativeYard(PortChoice destination) => false;
    }
}
