using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public class Rigidbody
    {
        public bool isKinematic;
        public Vector3 velocity, angularVelocity;
    }

    public static class Time
    {
        public static float realtimeSinceStartup;
    }

    public class WaitForFixedUpdate
    {
    }

    public class Camera
    {
        public static Camera main = new();
        public Transform transform = new GameObject().transform;
    }
}

internal class SaveablePrefab : Component
{
    int parent = -1;
    public int GetParentObject() => parent;
    public void SetParentObject(int value) => parent = value;
}

internal class FloatingOriginManager
{
    public static FloatingOriginManager instance = new();
    public Transform transform = new GameObject().transform;
}

namespace NewBeginnings
{
    internal class ResolvedStart
    {
        public GameObject StarterSet = new();
        public Transform ShoreFrame = new GameObject().transform;
        public int Region;
    }

    internal class Plugin
    {
        public static Plugin Instance = new();
        public string CargoSurfaceStatus;
        public int Probes, Failures;
        public bool GroundExists = true;
        public Func<Vector3, Vector3?> Surface;
        public float HalfWidth = 20, HalfDepth = 20;
        public List<string> Messages = new();
        public Collider ProbeCollider;
        public Collider Ground = new GameObject().Add<Collider>();
        public void DebugLog(string s) => Messages.Add(s);
        public void Report(string s) => Messages.Add(s);
        public void Warn(string s) => Messages.Add(s);
        public void Error(string s, Exception e) => Messages.Add(s);
    }

    // StarterCargo's surface boundary. Fixture state lives on the Plugin
    // double, which each check replaces.
    internal static class StartArrival
    {
        public static string CargoSurfaceStatus => Plugin.Instance.CargoSurfaceStatus;
        public static bool TryGetCargoSurface(out Vector3 p, out Collider c)
        {
            p = Vector3.zero;
            c = Plugin.Instance.Ground;
            return true;
        }

        public static bool TryProbeCargoSurface(Vector3 q, out Vector3 p, out Collider c)
        {
            var fixture = Plugin.Instance;
            fixture.Probes++;
            p = new(q.x, 0, q.z);
            c = fixture.ProbeCollider ?? fixture.Ground;
            if (fixture.Surface != null)
            {
                var hit = fixture.Surface(q);
                p = hit ?? p;
                return fixture.GroundExists && hit.HasValue;
            }

            return fixture.GroundExists && MathF.Abs(q.x) <= fixture.HalfWidth && MathF.Abs(q.z) <= fixture.HalfDepth;
        }
    }

    internal static class AdditionalEquipment
    {
        public static readonly HashSet<ShipItem> Packed = new(), Additional = new();
        public static int MovedPacked;
        public static bool IsAdditional(ShipItem item) => Additional.Contains(item);
        public static bool IsPacked(ShipItem i) => Packed.Contains(i);
        public static bool TryPack(ResolvedStart s, Action<ShipItem> prep, out string reason)
        {
            reason = null;
            return true;
        }

        public static void MovePacked(Action<ShipItem> reset)
        {
            MovedPacked = Packed.Count;
            foreach (var i in Packed)
                reset(i);
        }
    }

    internal static partial class StarterCargo
    {
        static readonly IEqualityComparer<ShipItem> ReferenceComparer = EqualityComparer<ShipItem>.Default;
        static Dictionary<ShipItem, CargoShape> planningShapes;
        static readonly Dictionary<ShipItem, Vector3> AuthoredPositions = new();
        static readonly Dictionary<ShipItem, Quaternion> AuthoredRotations = new();
        static readonly Dictionary<ShipItem, string> AuthoredNames = new();
        static ResolvedStart armed;
        static bool placementClaimed, standardSuppliesSuppressed, rosterFinalized;
        static string placementStage;
        sealed class Field
        {
            readonly string name;
            public Field(string value = "")
            {
                name = value;
            }

            public void SetValue(object o, object value)
            {
                if (o is ItemRigidbody body)
                {
                    if (name == "range")
                        body.OutOfRange = (bool)value;
                    if (name == "frames")
                        body.DestroyFrames = (int)value;
                    if (name == "timer")
                        body.DistanceTimer = (float)value;
                }
            }
        }

        sealed class Method
        {
            public void Invoke(ShipItem i, object args)
            {
                i.currentActualBoat = null;
                i.transform.SetParent(FloatingOriginManager.instance.transform, true);
                i.GetItemRigidbody().transform.SetParent(FloatingOriginManager.instance.transform, true);
                i.GetComponent<SaveablePrefab>().SetParentObject(-1);
            }
        }

        static readonly Field StayedEmbarkField = new(), BodyOutOfRangeField = new("range"), BodyDestroyFramesField = new("frames"), BodyDistanceTimerField = new("timer");
        static readonly Method ExitBoatMethod = new();
        static bool NativeItemsReady(out string r)
        {
            r = null;
            return true;
        }

        static void RefreshBoatWalkRoots()
        {
        }

        static bool IsVerifiedLandCollider(Collider c) => c != null && (c == Plugin.Instance.Ground || c == Plugin.Instance.ProbeCollider);
        static string AuthoredName(ShipItem i) => i.gameObject.name;
        static bool AuthoredNameContains(ShipItem i, string s) => AuthoredName(i).IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
        static bool IsNativeOar(ShipItem i) => i.IsOar;
        static bool IsNativeCup(ShipItem i) => false;
        static float LooseFootprintRadius(ShipItem i) => i.IsOar ? .4f : FootprintRadius(i);
        static void PreparePackedItem(ShipItem i)
        {
        }

        static bool IsCurrent(ResolvedStart s) => ReferenceEquals(armed, s);
        static IEnumerable<ShipItem> CollectItems(Transform t) => Array.Empty<ShipItem>();
        static void Capture(ShipItem i) => Captured.Add(i);
        static int CountLive() => Captured.Count;
        static void Disarm()
        {
            armed = null;
        }

        static void FailAndDisarm(string reason)
        {
            Plugin.Instance.Failures++;
            Plugin.Instance.Messages.Add(reason);
            Disarm();
        }
    }
}
