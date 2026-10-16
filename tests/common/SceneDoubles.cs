using System;
using System.Linq;
using UnityEngine;

// Native scene objects are boundaries. System.Numerics supplies independent
// quaternion/vector math.
namespace UnityEngine
{
    public class Object
    {
        public static void DestroyImmediate(Object value)
        {
            value.Destroyed = true;
        }

        public bool Destroyed;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b) || (ReferenceEquals(a, null) && b.Destroyed) || (ReferenceEquals(b, null) && a.Destroyed);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();
    }

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
        public static Vector3 up => new(0, 1, 0);
        public static Vector3 forward => new(0, 0, 1);
        public static Vector3 right => new(1, 0, 0);
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public Vector3 normalized => magnitude > 0 ? this / magnitude : zero;

        public static Vector3 operator *(Vector3 a, float n) => new(a.x * n, a.y * n, a.z * n);
        public static Vector3 operator /(Vector3 a, float n) => new(a.x / n, a.y / n, a.z / n);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 ClampMagnitude(Vector3 a, float max) => a.magnitude > max ? a.normalized * max : a;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static Vector3 ProjectOnPlane(Vector3 v, Vector3 n) => v - n * ((v.x * n.x + v.y * n.y + v.z * n.z) / n.sqrMagnitude);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 Scale(Vector3 a, Vector3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);
        public static float Distance(Vector3 a, Vector3 b)
        {
            var d = a - b;
            return MathF.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z);
        }

        public static bool operator ==(Vector3 a, Vector3 b) => Distance(a, b) < .00001f;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public override bool Equals(object o) => o is Vector3 v && this == v;
        public override int GetHashCode() => HashCode.Combine(x, y, z);
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        static System.Numerics.Quaternion N(Quaternion q) => new(q.x, q.y, q.z, q.w);
        static Quaternion U(System.Numerics.Quaternion q) => new(q.X, q.Y, q.Z, q.W);
        public Vector3 eulerAngles
        {
            get
            {
                var f = this * Vector3.forward;
                return new(0, MathF.Atan2(f.x, f.z) * 180 / MathF.PI, 0);
            }
        }

        public static Quaternion identity => new(0, 0, 0, 1);

        public static Quaternion Inverse(Quaternion q) => U(System.Numerics.Quaternion.Inverse(N(q)));
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            var f = forward.normalized;
            var r = Vector3.Cross(up, f).normalized;
            var u = Vector3.Cross(f, r);
            return U(System.Numerics.Quaternion.CreateFromRotationMatrix(new System.Numerics.Matrix4x4(r.x, r.y, r.z, 0, u.x, u.y, u.z, 0, f.x, f.y, f.z, 0, 0, 0, 0, 1)));
        }

        public static Quaternion operator *(Quaternion a, Quaternion b) => U(N(a) * N(b));
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            var r = System.Numerics.Vector3.Transform(new(v.x, v.y, v.z), N(q));
            return new(r.X, r.Y, r.Z);
        }
    }

    public class Transform : Object
    {
        public Component island;
        public T GetComponentInParent<T>()
            where T : class => island as T ?? parent?.GetComponentInParent<T>();
        public string name;
        public object[] Components = Array.Empty<object>();
        public T[] GetComponentsInChildren<T>(bool inactive) => Components.OfType<T>().ToArray();
        public Vector3 position, lossyScale = new(1, 1, 1);
        public Quaternion rotation = Quaternion.identity;
        public Transform parent;
        public Vector3 eulerAngles => rotation.eulerAngles;
        public Vector3 forward => rotation * Vector3.forward;

        public bool IsChildOf(Transform other) => this == other || parent != null && parent.IsChildOf(other);
        public Vector3 TransformPoint(Vector3 p) => position + rotation * Vector3.Scale(p, lossyScale);
        public Vector3 InverseTransformPoint(Vector3 p)
        {
            var v = Quaternion.Inverse(rotation) * (p - position);
            return new(v.x / lossyScale.x, v.y / lossyScale.y, v.z / lossyScale.z);
        }
    }

    public class GameObject
    {
        public T AddComponent<T>()
            where T : Component, new() => new T();
    }

    public class MonoBehaviour : Component
    {
        public bool enabled = true;
    }

    public enum ForceMode
    {
        Acceleration
    }

    public class Component : Object
    {
        public GameObject gameObject = new();
        public Transform transform = new();
        public Rigidbody body;
        public PurchasableBoat purchase;
        public Component saveable;
        public T GetComponent<T>()
            where T : class => body as T ?? purchase as T ?? saveable as T;
    }

    public class Rigidbody : Component
    {
        public bool isKinematic;
        public Vector3 velocity, angularVelocity, LastForce, LastTorque;
        public int ForceCalls, TorqueCalls;
        public void AddForce(Vector3 f, ForceMode mode)
        {
            LastForce = f;
            ForceCalls++;
        }

        public void AddTorque(float x, float y, float z, ForceMode mode)
        {
            LastTorque = new(x, y, z);
            TorqueCalls++;
        }

        public Vector3 position { get => transform.position; set => transform.position = value; }
        public Quaternion rotation { get => transform.rotation; set => transform.rotation = value; }
    }

    public class Collider : Component
    {
    }

    public class ConfigurableJoint : Component
    {
        public Rigidbody connectedBody;
        public Vector3 anchor, connectedAnchor;
    }

    public static class Mathf
    {
        public const float PI = MathF.PI;
        public const float Deg2Rad = MathF.PI / 180;
        public static float Clamp(float v, float min, float max) => Math.Clamp(v, min, max);
        public static float DeltaAngle(float a, float b)
        {
            var d = (b - a) % 360;
            if (d > 180)
                d -= 360;
            if (d < -180)
                d += 360;
            return d;
        }

        public static float Sqrt(float v) => MathF.Sqrt(v);
        public static float Abs(float v) => MathF.Abs(v);
        public static float Min(float a, float b) => MathF.Min(a, b);
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float Sin(float v) => MathF.Sin(v);
        public static float Cos(float v) => MathF.Cos(v);
    }
}

public class PurchasableBoat : UnityEngine.Component
{
    public bool purchased;
    public bool isPurchased() => purchased;
}

class BoatMooringRopes : Component
{
}
