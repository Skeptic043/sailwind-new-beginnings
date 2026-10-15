using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3 zero => new(0, 0, 0);
        public static Vector3 one => new(1, 1, 1);
        public static Vector3 right => new(1, 0, 0);
        public static Vector3 up => new(0, 1, 0);
        public static Vector3 forward => new(0, 0, 1);
        public float magnitude => MathF.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 left => right * -1;
        public static Vector3 back => forward * -1;

        public void Normalize()
        {
            var m = magnitude;
            x /= m;
            y /= m;
            z /= m;
        }

        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < .0000001f;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x * b, a.y * b, a.z * b);
        public static Vector3 Cross(Vector3 a, Vector3 b) => new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static Vector3 Scale(Vector3 a, Vector3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);
        public override string ToString() => $"({x},{y},{z})";
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public float magnitude => MathF.Sqrt(x * x + y * y);
        public float sqrMagnitude => x * x + y * y;
    }

    public struct Quaternion
    {
        public static bool operator ==(Quaternion a, Quaternion b) => MathF.Abs(System.Numerics.Quaternion.Dot(a.value, b.value)) > .999999f;
        public static bool operator !=(Quaternion a, Quaternion b) => !(a == b);
        internal System.Numerics.Quaternion value;
        public Quaternion(float x, float y, float z, float w)
        {
            value = new(x, y, z, w);
        }

        public static Quaternion identity => new(0, 0, 0, 1);

        public static Quaternion Euler(float x, float y, float z) => new()
        {
            value = System.Numerics.Quaternion.CreateFromYawPitchRoll(y * MathF.PI / 180, x * MathF.PI / 180, z * MathF.PI / 180)
        };
        public static Quaternion LookRotation(Vector3 forward) => Euler(0, MathF.Atan2(forward.x, forward.z) * 180 / MathF.PI, 0);
        public static Quaternion Inverse(Quaternion q) => new()
        {
            value = System.Numerics.Quaternion.Inverse(q.value)
        };
        public static Quaternion operator *(Quaternion a, Quaternion b) => new()
        {
            value = a.value * b.value
        };
        public static Vector3 operator *(Quaternion q, Vector3 p)
        {
            var v = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(p.x, p.y, p.z), q.value);
            return new(v.X, v.Y, v.Z);
        }
    }

    public struct Bounds
    {
        public Vector3 center, size;
        public Bounds(Vector3 c, Vector3 s)
        {
            center = c;
            size = s;
        }

        public Vector3 extents => size * .5f;
        public Vector3 min => center - extents;
        public Vector3 max => center + extents;

        public void Encapsulate(Vector3 p)
        {
            var a = min;
            var b = max;
            var lo = new Vector3(MathF.Min(a.x, p.x), MathF.Min(a.y, p.y), MathF.Min(a.z, p.z));
            var hi = new Vector3(MathF.Max(b.x, p.x), MathF.Max(b.y, p.y), MathF.Max(b.z, p.z));
            center = (lo + hi) * .5f;
            size = hi - lo;
        }

        public void Encapsulate(Bounds b)
        {
            Encapsulate(b.min);
            Encapsulate(b.max);
        }

        public bool Intersects(Bounds b) => min.x <= b.max.x && max.x >= b.min.x && min.y <= b.max.y && max.y >= b.min.y && min.z <= b.max.z && max.z >= b.min.z;
    }

    public static class Mathf
    {
        public const float PI = MathF.PI;
        public static float Atan2(float x, float y) => MathF.Atan2(x, y);
        public static float Cos(float x) => MathF.Cos(x);
        public static float Sin(float x) => MathF.Sin(x);
        public static float Clamp(float x, float a, float b) => Math.Clamp(x, a, b);
        public static float Abs(float x) => MathF.Abs(x);
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Sqrt(float x) => MathF.Sqrt(x);
    }

    public class Component
    {
        public string name => gameObject.name;

        public GameObject gameObject;
        public Transform transform => this is Transform t ? t : gameObject.transform;

        public T GetComponent<T>()
            where T : class => gameObject.GetComponent<T>();
        public T[] GetComponents<T>()
            where T : class => gameObject.GetComponents<T>();
        public T[] GetComponentsInChildren<T>(bool inactive)
            where T : class => gameObject.GetComponentsInChildren<T>(inactive);
        public T GetComponentInParent<T>()
            where T : class => GetComponent<T>() ?? transform.parent?.GetComponentInParent<T>();
        public bool CompareTag(string tag) => gameObject.tag == tag;
    }

    public sealed class GameObject
    {
        public string name = "item";
        public readonly List<Component> components = new();
        public Transform transform;
        public string tag = "Untagged";
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent?.gameObject.activeInHierarchy ?? true);

        public GameObject()
        {
            transform = new Transform
            {
                gameObject = this
            };
            components.Add(transform);
        }

        public T Add<T>()
            where T : Component, new()
        {
            var c = new T
            {
                gameObject = this
            };
            components.Add(c);
            return c;
        }

        public T GetComponent<T>()
            where T : class => components.OfType<T>().FirstOrDefault();
        public T[] GetComponents<T>()
            where T : class => components.OfType<T>().ToArray();
        public T[] GetComponentsInChildren<T>(bool inactive)
            where T : class => GetComponents<T>().Concat(transform.children.SelectMany(t => t.GetComponentsInChildren<T>(inactive))).ToArray();
    }

    public class Transform : Component, IEnumerable
    {
        public Vector3 forward => rotation * Vector3.forward;

        public void SetPositionAndRotation(Vector3 p, Quaternion r)
        {
            position = p;
            rotation = r;
        }

        public void SetParent(Transform p, bool world)
        {
            var pos = position;
            var rot = rotation;
            parent = p;
            if (p != null)
                p.children.Add(this);
            if (world)
            {
                position = pos;
                rotation = rot;
            }
        }

        public Transform parent;
        public List<Transform> children = new();
        public Vector3 localPosition, localScale = Vector3.one;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 position { get => parent == null ? localPosition : parent.TransformPoint(localPosition); set => localPosition = parent == null ? value : parent.InverseTransformPoint(value); }
        public Quaternion rotation { get => parent == null ? localRotation : parent.rotation * localRotation; set => localRotation = parent == null ? value : Quaternion.Inverse(parent.rotation) * value; }
        public Vector3 lossyScale => new(TransformVector(Vector3.right).magnitude, TransformVector(Vector3.up).magnitude, TransformVector(Vector3.forward).magnitude);

        public Vector3 TransformPoint(Vector3 p)
        {
            var r = localPosition + localRotation * Vector3.Scale(localScale, p);
            return parent == null ? r : parent.TransformPoint(r);
        }

        public Vector3 TransformVector(Vector3 p)
        {
            var r = localRotation * Vector3.Scale(localScale, p);
            return parent == null ? r : parent.TransformVector(r);
        }

        public Vector3 InverseTransformPoint(Vector3 p)
        {
            if (parent != null)
                p = parent.InverseTransformPoint(p);
            p = Quaternion.Inverse(localRotation) * (p - localPosition);
            return new(p.x / localScale.x, p.y / localScale.y, p.z / localScale.z);
        }

        public void SetParent(Transform p)
        {
            parent = p;
            p.children.Add(this);
        }

        public IEnumerator GetEnumerator() => children.GetEnumerator();
    }

    public class Collider : Component
    {
        public bool enabled = true, isTrigger;
        public Bounds rawWorldBounds;
        public Bounds bounds => enabled && gameObject.activeInHierarchy ? rawWorldBounds : default;
    }

    public class BoxCollider : Collider
    {
        public Vector3 center, size = Vector3.one;
    }

    public class MeshCollider : Collider
    {
        public Mesh sharedMesh;
    }

    public class Mesh
    {
        public Bounds bounds;
    }

    public class CapsuleCollider : Collider
    {
        public Vector3 center;
        public float radius, height;
        public int direction = 1;
    }

    public class SphereCollider : Collider
    {
        public Vector3 center;
        public float radius;
    }

    public enum QueryTriggerInteraction
    {
        Ignore,
        Collide
    }

    public static class Physics
    {
        public static int Queries;
        public static void SyncTransforms()
        {
        }

        public static List<Collider> obstacles = new();
        public static Bounds LastQuery;
        public static Collider[] OverlapBox(Vector3 p, Vector3 e, Quaternion q, int mask, QueryTriggerInteraction trigger)
        {
            Queries++;
            LastQuery = new(p, e * 2);
            return obstacles.Where(c => c.enabled && c.gameObject.activeInHierarchy && (trigger == QueryTriggerInteraction.Collide || !c.isTrigger) && LastQuery.Intersects(c.bounds)).ToArray();
        }
    }
}

internal class ShipItem : UnityEngine.Component
{
    public object currentActualBoat;
    public bool IsOar;
    public ItemRigidbody Body;
    public UnityEngine.Quaternion Planned { get => transform.rotation; set => transform.rotation = value; }

    public ItemRigidbody GetItemRigidbody() => Body;
}

internal class ItemRigidbody : UnityEngine.Component
{
    public static readonly List<ShipItem> Moved = new();
    public bool attached;
    public bool OutOfRange = true;
    public int DestroyFrames = 11;
    public float DistanceTimer = 7;
    public int ResetCalls;
    public bool ThrowOnReset;
    public readonly UnityEngine.Rigidbody rigidbody = new();
    public UnityEngine.Rigidbody GetBody() => rigidbody;
    public void ResetPos()
    {
        ResetCalls++;
        Moved.Add(Item);
        if (ThrowOnReset)
            throw new Exception("injected native move failure");
        transform.SetPositionAndRotation(Item.transform.position, Item.transform.rotation);
    }

    public ShipItem Item;
    public ShipItem GetShipItem() => Item;
}

internal class ShipItemCrate : ShipItem
{
}
