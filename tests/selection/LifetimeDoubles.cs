namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b) || (ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }

    public class Component : Object
    {
        public bool Loaded = true;
        public IslandHorizon Island;
        public IslandSceneryScene Scenery;
        public Transform transform;
        public T GetComponentInParent<T>()
            where T : Component => Island as T ?? Scenery as T;
    }

    public class Transform : Component
    {
    }
}

public class IslandSceneryScene : UnityEngine.Component
{
    public int parentIslandIndex;
}

public static class Refs
{
    public static UnityEngine.Transform[] islands = new UnityEngine.Transform[100];
}

public class IslandHorizon : UnityEngine.Component
{
    public int islandIndex;
    public string name;
    public IslandHorizon()
    {
        Island = this;
        transform = new()
        {
            Island = this
        };
    }
}

public class Port : UnityEngine.Component
{
    public bool Registered = true;
}

public class GPButtonDockMooring : UnityEngine.Component
{
}

public class Shipyard : UnityEngine.Component
{
    public bool enabled = true;
}
