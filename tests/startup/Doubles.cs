using System.Collections;
using System.Reflection;

namespace BepInEx.Bootstrap
{
    public static class Chainloader
    {
        public class Info
        {
            public object Instance;
        }

        public static Dictionary<string, Info> PluginInfos = new();
    }
}

namespace HarmonyLib
{
    public class HarmonyMethod
    {
        public HarmonyMethod(MethodInfo method)
        {
        }
    }

    public class Patch
    {
        public string owner;
        public MethodInfo PatchMethod;
    }

    public class PatchInfo
    {
        public List<Patch> Prefixes = new();
    }

    public class Harmony
    {
        public void Patch(MethodInfo method, HarmonyMethod prefix)
        {
        }

        public static PatchInfo GetPatchInfo(MethodInfo method) => new();
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type t, string name)
        {
        }

        public HarmonyPatch(Type t, string name, Type[] args)
        {
        }
    }

    public static class AccessTools
    {
        public static FieldInfo Field(Type t, string n) => t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        public static MethodInfo Method(Type t, string n) => t.GetMethod(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
    }
}

namespace UnityEngine
{
    public static class Resources
    {
        public static Component[] Objects = Array.Empty<Component>();
        public static T[] FindObjectsOfTypeAll<T>()
            where T : Component => Objects.OfType<T>().ToArray();
    }

    public enum CursorLockMode
    {
        None,
        Locked,
        Confined
    }

    public static class Cursor
    {
        public static bool visible;
        public static CursorLockMode lockState;
    }

    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b) || (ReferenceEquals(a, null) && b.Destroyed) || (ReferenceEquals(b, null) && a.Destroyed);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object b) => ReferenceEquals(this, b);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
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

        public static Vector3 zero => new();

        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 Scale(Vector3 a, Vector3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);
        public override string ToString() => $"({x},{y},{z})";
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

        public static Quaternion identity => new(0, 0, 0, 1);

        public static Quaternion Inverse(Quaternion q)
        {
            var n = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return new(-q.x / n, -q.y / n, -q.z / n, q.w / n);
        }

        public static Quaternion operator *(Quaternion a, Quaternion b) => new(a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y, a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x, a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w, a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            var r = System.Numerics.Vector3.Transform(new(v.x, v.y, v.z), new System.Numerics.Quaternion(q.x, q.y, q.z, q.w));
            return new(r.X, r.Y, r.Z);
        }
    }

    public class Component : Object
    {
        public string name { get => gameObject.name; set => gameObject.name = value; }

        public GameObject gameObject;
        public T GetComponentInParent<T>()
            where T : Component
        {
            for (var t = transform; t != null; t = t.parent)
            {
                var c = t.GetComponent<T>();
                if (c != null)
                    return c;
            }

            return null;
        }

        public Transform transform => this is Transform t ? t : gameObject.transform;

        public T GetComponent<T>()
            where T : Component => gameObject.GetComponent<T>();
        public Component GetComponent(string n) => gameObject.GetComponent(n);
        public T GetComponentInChildren<T>(bool inactive)
            where T : Component => GetComponent<T>();
    }

    public class GameObject : Object
    {
        public string name;
        public readonly Transform transform;
        public bool activeSelf = true;
        public readonly Dictionary<Type, Component> components = new();
        public GameObject()
        {
            transform = new Transform(this);
        }

        public T Add<T>()
            where T : Component, new()
        {
            var c = new T()
            {
                gameObject = this
            };
            components[typeof(T)] = c;
            return c;
        }

        public T GetComponent<T>()
            where T : Component => components.TryGetValue(typeof(T), out var c) ? (T)c : null;
        public Component GetComponent(string n) => components.Values.FirstOrDefault(x => x.GetType().Name == n);
        public void SetActive(bool value) => activeSelf = value;
    }

    public class Transform : Component
    {
        public Transform parent;
        public Vector3 localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 lossyScale = new(1, 1, 1);
        public Transform(GameObject owner)
        {
            gameObject = owner;
        }

        public Vector3 position { get => localPosition + (parent == null ? Vector3.zero : parent.position); set => localPosition = value - (parent == null ? Vector3.zero : parent.position); }
        public Quaternion rotation { get => localRotation; set => localRotation = value; }

        public void SetParent(Transform p, bool keep)
        {
            var pos = position;
            parent = p;
            if (keep)
                position = pos;
        }

        public void SetPositionAndRotation(Vector3 p, Quaternion q)
        {
            position = p;
            rotation = q;
        }

        public Vector3 TransformPoint(Vector3 v) => position + v;
        public Vector3 InverseTransformPoint(Vector3 v) => v - position;
    }

    public class Behaviour : Component
    {
        public bool enabled;
    }

    public class CharacterController : Component
    {
        public bool enabled;
    }

    public class ConfigurableJoint : Component
    {
        public Rigidbody connectedBody;
        public Vector3 anchor, connectedAnchor;
    }

    public class SpringJoint : Component
    {
    }

    public class Rigidbody : Component
    {
        public Vector3 position { get => transform.position; set => transform.position = value; }
        public Quaternion rotation { get => transform.rotation; set => transform.rotation = value; }

        public Vector3 velocity, angularVelocity;
        public bool isKinematic;
        public float drag;
    }

    public static class Time
    {
        public static float realtimeSinceStartup;
        public static int frameCount;
    }

    public class WaitForFixedUpdate
    {
    }

    public static class Physics
    {
        public static void SyncTransforms()
        {
        }
    }
}

public enum PortRegion
{
    AlAnkh,
    Emerald,
    Aestrin
}

public class Port : UnityEngine.Component
{
    public int portIndex;
}

public class Shipyard : UnityEngine.Component
{
    public bool enabled = true;
    public UnityEngine.Transform shipReleasePosition;
}

public class ShipyardDocuments : UnityEngine.Component
{
    public Shipyard shipyard;
}

public class RecoveryPort : UnityEngine.Component
{
    public Port parentPort;
    public UnityEngine.Transform boatPos, mooringFront, mooringBack;
}

public static class Recovery
{
    public static List<RecoveryPort> ports;
}

public static class GameState
{
    public static bool currentlyLoading, inCursorMenu;
    public static PortRegion newGameRegion;
    public static UnityEngine.Transform lastBoat, lastOwnedBoat;
    public static Port lastVisitedPort;
}

public static class MouseLook
{
    public static bool cursorEnabled;
}

public class MouseButtonPointer : UnityEngine.Component
{
    public static MouseButtonPointer instance;
    public UnityEngine.Transform controllerPointer;
}

public static class PlayerGold
{
    public static int[] currency;
}

public class PlayerControllerMirror : UnityEngine.Behaviour
{
}

public class OVRPlayerController : UnityEngine.Behaviour
{
}

public class SaveLoadManager
{
}

public class CurrencySwitchButton
{
    public static CurrencySwitchButton masterButton = new();
    public int Currency;
    public void ForceSetCurrency(int x) => Currency = x;
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
    public UnityEngine.Vector3 GetPosition() => transform.position;
}

public class PurchasableBoat : UnityEngine.Component
{
    private bool regionalPurchased;
    public bool purchased
    {
        get => GetComponent<SaveableObject>()?.extraSetting ?? regionalPurchased;
        set
        {
            var save = GetComponent<SaveableObject>();
            if (save != null)
                save.extraSetting = value;
            else
                regionalPurchased = value;
        }
    }

    public UnityEngine.GameObject purchaseUI = new();
    public bool isPurchased() => purchased;
    public void LoadAsPurchased()
    {
        if (purchaseUI != null)
            purchased = true;
        typeof(NewBeginnings.StarterOwnership).GetNestedType("GrantPatch", BindingFlags.NonPublic).GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { this });
    }
}

public class SaveableObject : UnityEngine.Component
{
    public bool extraSetting;
}

public class StarterSet
{
    public UnityEngine.Transform starterBoat;
}

public class BoatDamage : UnityEngine.Component
{
}

public class BoatLocalItems : UnityEngine.Component
{
}

public class GPButtonDockMooring : UnityEngine.Component
{
    public UnityEngine.SpringJoint spring;
}

public class PickupableBoatMooringRope : UnityEngine.Component
{
    public bool ThrowMoor;
    public GPButtonDockMooring Mooring;
    public UnityEngine.SpringJoint mooredSpring;
    public bool IsMoored() => Mooring != null;
    public void Unmoor()
    {
        Mooring = null;
        mooredSpring = null;
    }

    public void MoorTo(GPButtonDockMooring d)
    {
        if (ThrowMoor)
            throw new InvalidOperationException("Injected rope connection failure");
        if (d == null)
            throw new InvalidOperationException("Missing cleat");
        Mooring = d;
        d.spring ??= d.gameObject.Add<UnityEngine.SpringJoint>();
        mooredSpring = d.spring;
        transform.SetParent(d.transform, true);
    }
}

public class Anchor : UnityEngine.Component
{
    public bool Set;
    public float Length;
    public readonly List<string> Calls = new();
    public bool IsSet() => Set;
    public float GetRopeLength() => Length;
    private void SetAnchor()
    {
        Calls.Add("set");
        Set = true;
        GetComponent<UnityEngine.Rigidbody>().isKinematic = true;
        GetComponent<UnityEngine.Rigidbody>().drag = 50;
    }

    private void ReleaseAnchor()
    {
        Calls.Add("release");
        Set = false;
        GetComponent<UnityEngine.Rigidbody>().isKinematic = false;
    }

    public void OnLoad(bool set, float length)
    {
        Calls.Add("load:" + set + ":" + length);
        Length = length;
        if (set)
            SetAnchor();
    }
}

public class RopeControllerAnchor
{
    public bool ThrowReset;
    public UnityEngine.ConfigurableJoint joint;
    public Anchor anchor;
    public void ResetAnchor()
    {
        if (ThrowReset)
            throw new InvalidOperationException("Injected anchor reset failure");
        anchor.Length = 0;
    }
}

public class BoatMooringRopes : UnityEngine.Component
{
    public Anchor anchor;
    public PickupableBoatMooringRope[] ropes;
    public bool ThrowUnmoor;
    public void UnmoorAllRopes()
    {
        if (ThrowUnmoor)
            throw new InvalidOperationException("Injected unmoor failure");
        foreach (var r in ropes)
            r.Mooring = null;
    }

    public RopeControllerAnchor Controller = new();
    public RopeControllerAnchor GetAnchorController()
    {
        Controller.anchor = anchor;
        return Controller;
    }
}

public class StartMenu : UnityEngine.Component
{
    public int currentRegion, animsPlaying, Reopened;
    public PurchasableBoat[] startingBoats;
    public UnityEngine.Transform Observer, NativeTarget;
    public UnityEngine.GameObject Controller, chooseIslandUI = new(), logo = new();
    public bool NativeHidePending, ThrowBeforeGrant;
    public NativeIterator Native;
    public bool NullFactory;
    public System.Collections.IEnumerator MovePlayerToStartPos(UnityEngine.Transform target)
    {
        NativeTarget = target;
        return NullFactory ? null : (Native = new NativeIterator(this));
    }

    public void EnableIslandMenu() => Reopened++;
    public void DisableIslandMenu()
    {
        if (NewBeginnings.FixedStart.HidePrefixFixture(this))
        {
            animsPlaying++;
            NativeHidePending = true;
        }
    }

    public void FinishNativeHide()
    {
        if (!NativeHidePending)
            return;
        NativeHidePending = false;
        chooseIslandUI.SetActive(false);
        chooseIslandUI.transform.localPosition += new UnityEngine.Vector3(0, 0, -2.6f);
        animsPlaying--;
    }
}

public class NativeIterator : IEnumerator, IDisposable
{
    private readonly StartMenu menu;
    public int Moves;
    public bool Disposed;
    public object Current => null;

    public NativeIterator(StartMenu menu)
    {
        this.menu = menu;
    }

    // Boundary model follows verified installed pre-purchase ordering. It is not
    // copied game code and does not model post-purchase fades/disclaimer/saving.
    public bool MoveNext()
    {
        Moves++;
        if (Moves == 1)
        {
            menu.logo.SetActive(false);
            menu.Observer.SetParent(menu.transform.parent, true);
            menu.Controller.transform.SetPositionAndRotation(menu.Observer.position, menu.Observer.rotation);
            return true;
        }

        if (Moves == 2)
        {
            menu.Controller.GetComponent<UnityEngine.CharacterController>().enabled = true;
            menu.Controller.GetComponent<OVRPlayerController>().enabled = true;
            menu.Observer.GetComponent<PlayerControllerMirror>().enabled = true;
            MouseLook.cursorEnabled = false;
            GameState.inCursorMenu = false;
            UnityEngine.Cursor.visible = false;
            UnityEngine.Cursor.lockState = UnityEngine.CursorLockMode.Locked;
            MouseButtonPointer.instance.controllerPointer.gameObject.SetActive(false);
            if (menu.ThrowBeforeGrant)
                throw new InvalidOperationException("Injected pre-purchase exception");
            menu.startingBoats[menu.currentRegion].LoadAsPurchased();
            return true;
        }

        return false;
    }

    public void Reset() => throw new NotSupportedException();
    public void Dispose() => Disposed = true;
}

// Models Harmony's prefix/original boundary, invoking the real production
// prefix both initially and when its reflected continuation reenters.
public static class ScrambledBoundary
{
    public static int PrefixCalls, FactoryCalls;
    public static UnityEngine.Transform Target, Observer;
    public static UnityEngine.GameObject Controller;
    public static void Reset()
    {
        PrefixCalls = FactoryCalls = 0;
        Target = Observer = null;
        Controller = null;
        typeof(NewBeginnings.ScrambledSeasIntegration).GetField("playerStartFactory", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, typeof(ScrambledBoundary).GetMethod(nameof(Invoke)));
    }

    public static IEnumerator Invoke(StartMenu menu, UnityEngine.Transform target, UnityEngine.Transform observer, UnityEngine.GameObject controller)
    {
        PrefixCalls++;
        object[] args =
        {
            menu,
            observer,
            controller,
            null
        };
        bool original = (bool)typeof(NewBeginnings.ScrambledSeasIntegration).GetMethod("BeforePlayerStart", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
        if (!original)
            return (IEnumerator)args[3];
        FactoryCalls++;
        Target = target;
        Observer = observer;
        Controller = controller;
        return menu.Native = new NativeIterator(menu);
    }
}

namespace NewBeginnings
{
    internal class PortChoice
    {
        internal RecoveryPort Recovery;
        internal Shipyard Yard;
        internal UnityEngine.Transform DockRoot => Island.transform;

        internal Port Port;
        internal HiddenSite Hidden;
        internal string SiteIdentity, IdentityName;
        internal int Index;
        internal IslandHorizon Island;
        internal bool Current = true, Scenery = true, NeedsPlannedBerth = true;
    }

    internal enum BoatSize
    {
        Small = 1,
        Medium = 2,
        Large = 3
    }

    internal class BoatChoice
    {
        internal PurchasableBoat Boat;
        internal SaveableObject Saveable;
        internal int Index;
        internal BoatSize? Size;
    }

    internal class StartPair
    {
        internal PortChoice Port;
        internal BoatChoice Boat;
        internal bool UseShipyardBerth;
    }

    internal class StartingOptionsSettings
    {
    }

    internal class OffshoreHold
    {
        internal static OffshoreHold Active;
        internal bool Removed;
        internal static OffshoreHold Attach(UnityEngine.Rigidbody body, UnityEngine.Transform frame)
        {
            RemoveActive();
            return Active = new();
        }

        internal static void RemoveActive() => Remove(Active);
        internal static void Remove(OffshoreHold keeper)
        {
            if (keeper == null)
                return;
            keeper.Removed = true;
            if (ReferenceEquals(keeper, Active))
                Active = null;
        }
    }

    internal enum StartBerthMode
    {
        Moored,
        OffshoreHeld,
        ShipyardHeld,
        Preserved
    }

    internal class StartBerthPlan
    {
        private UnityEngine.Transform frame;
        internal UnityEngine.Transform Frame { get => frame ?? Destination.Island.transform; set => frame = value; }

        internal StartBerthMode Mode;

        internal PortChoice Destination;
        internal bool OffshoreHeld => Mode == StartBerthMode.OffshoreHeld || Mode == StartBerthMode.ShipyardHeld;

        private UnityEngine.Vector3 position, shorePosition, localPosition, localShore;
        private UnityEngine.Quaternion rotation, localRotation;
        private bool localPose, localShorePose, localRot;
        internal UnityEngine.Vector3 LocalPosition
        {
            get => localPosition;
            set
            {
                localPosition = value;
                localPose = true;
            }
        }

        internal UnityEngine.Vector3 LocalShorePosition
        {
            get => localShore;
            set
            {
                localShore = value;
                localShorePose = true;
            }
        }

        internal UnityEngine.Quaternion LocalRotation
        {
            get => localRotation;
            set
            {
                localRotation = value;
                localRot = true;
            }
        }

        internal UnityEngine.Vector3 Position { get => localPose ? Frame.TransformPoint(localPosition) : position; set => position = value; }
        internal UnityEngine.Vector3 ShorePosition { get => localShorePose ? Frame.TransformPoint(localShore) : shorePosition; set => shorePosition = value; }
        internal UnityEngine.Quaternion Rotation { get => localRot ? Frame.rotation * localRotation : rotation; set => rotation = value; }

        internal GPButtonDockMooring Front, Back;
        internal PickupableBoatMooringRope FrontRope, BackRope;
    }

    internal class NativeBerth
    {
        internal class Plan
        {
            internal UnityEngine.Vector3 ShorePosition;
            internal PickupableBoatMooringRope FrontRope, BackRope;
        }
    }

    internal class ResolvedStart
    {
        internal bool UseShipyardBerth;
        internal RecoveryPort Recovery;
        internal NativeBerth.Plan NativeBerth;
        internal PurchasableBoat Boat;
        internal SaveableObject Saveable;
        internal UnityEngine.Rigidbody Body;
        internal BoatMooringRopes Ropes;
        internal StartBerthPlan Berth;
        internal PortChoice Destination;
        internal Port Port;
        internal GPButtonDockMooring FrontMooring, BackMooring;
        internal StarterSet StarterSet;
        internal int Region;
        internal BoatSize? Size;
        internal StartingOptionsSettings Options;
        internal StartMenu Menu;
    }

    internal static class StartBerthPlanner
    {
        internal static void PrepareShipyardMoorings(StartBerthPlan plan, UnityEngine.Rigidbody body, BoatMooringRopes ropes)
        {
        }

        internal static UnityEngine.Vector3? FortShore;
        internal static bool TryFortShipyardShore(PortChoice site, UnityEngine.Vector3 release, out UnityEngine.Vector3 shore)
        {
            shore = FortShore ?? default;
            return FortShore.HasValue;
        }

        internal static bool TryPlan(PortChoice site, UnityEngine.Rigidbody body, BoatMooringRopes ropes, BoatSize? size, out StartBerthPlan plan, out string reason)
        {
            plan = FortPlan;
            reason = null;
            return plan != null;
        }

        internal static bool TryNativeShipyard(PortChoice site, Shipyard yard, UnityEngine.Rigidbody body, BoatMooringRopes ropes, out StartBerthPlan plan, out string reason)
        {
            LastBody = body;
            LastRopes = ropes;
            plan = FortPlan;
            reason = null;
            return plan != null;
        }

        internal static StartBerthPlan FortPlan;
        internal static UnityEngine.Rigidbody LastBody;
        internal static BoatMooringRopes LastRopes;
    }

    internal static partial class StartDestinations
    {
        internal static bool Loaded(UnityEngine.Component c) => c != null;
        internal static bool IsCurrent(PortChoice site) => site.Current;
        internal static bool SceneryReady(PortChoice site) => site.Scenery && site.Current;
    }

    // Fixture state for the shore probe and arrival doubles lives on the
    // Plugin double, so each fixture's new Plugin resets it.
    internal static class ShoreProbe
    {
        internal static bool TryFindStartShore(PortChoice d, UnityEngine.Vector3 seed, UnityEngine.Vector3 boat, out UnityEngine.Vector3 shore, out object surface, out string reason)
        {
            var fixture = Plugin.Instance;
            fixture.LastShoreSeed = seed;
            shore = seed + new UnityEngine.Vector3(2, 0, 0);
            surface = null;
            reason = fixture.ShoreFound ? null : "injected shore miss";
            return fixture.ShoreFound;
        }
    }

    internal static class StartArrival
    {
        internal static void CancelTracking()
        {
            var fixture = Plugin.Instance;
            fixture.Cancellations++;
            if (fixture.Tracking)
                CancelRecord.Calls.Add("PlayerStartTracking");
            fixture.Tracking = false;
        }

        internal static void ClearSite()
        {
            if (Plugin.Instance.DocksideSet)
                CancelRecord.Calls.Add("DocksideSurface");
            Plugin.Instance.DocksideSet = false;
        }

        internal static UnityEngine.Transform MakeTarget(ResolvedStart s, UnityEngine.Quaternion rotation, UnityEngine.Vector3 shore, UnityEngine.Vector3 berth)
        {
            Plugin.Instance.LastBerth = berth;
            if (Plugin.Instance.FailTarget)
                throw new InvalidOperationException("Injected player target failure");
            var t = new UnityEngine.GameObject().transform;
            t.SetPositionAndRotation(shore, rotation);
            return t;
        }

        internal static void BeginTracking(StartMenu menu, UnityEngine.Transform target) => Plugin.Instance.Tracking = true;
    }

    internal class Plugin
    {
        internal bool ShoreFound = true;
        internal UnityEngine.Vector3 LastShoreSeed;

        internal const string Id = "fixture";
        internal void Report(string text)
        {
        }

        internal static Plugin Instance = new();
        internal bool FailTarget;
        internal int Cancellations;
        internal readonly List<string> Errors = new();
        internal readonly List<string> Warnings = new();
        internal bool Tracking, DocksideSet;
        internal void Warn(string text) => Warnings.Add(text);
        internal void Error(string text, Exception ex) => Errors.Add(text);
        internal UnityEngine.Vector3 LastBerth;
    }

    internal class MenuUi
    {
        internal static int BlockedShown;
        internal static MenuUi ActiveFor(StartMenu menu) => new();
        internal void ShowBlockedStart(string reason) => BlockedShown++;
    }

    internal static class BerthClearance
    {
        internal static bool Throw;
        internal static UnityEngine.Vector3 AdjustCrabBeach(ResolvedStart s, UnityEngine.Vector3 p) => Throw ? throw new InvalidOperationException("Injected Crab Beach failure") : p + new UnityEngine.Vector3(1, 0, 0);
    }

    // Armed side-system work that a cancellation actually released, in call
    // order. Like production, cancelling unarmed work records nothing.
    internal static class CancelRecord
    {
        internal static readonly List<string> Calls = new();
    }

    internal static class StarterCargo
    {
        internal static bool Armed, ThrowArm;
        internal static void Arm(ResolvedStart x)
        {
            if (ThrowArm)
                throw new InvalidOperationException("Injected cargo setup failure");
            Armed = true;
        }

        internal static void Disarm()
        {
            if (Armed)
                CancelRecord.Calls.Add("StarterCargo");
            Armed = false;
        }
    }

    internal static class StartingValues
    {
        internal static bool Armed;
        internal static void Arm(ResolvedStart x) => Armed = true;
        internal static void Cancel()
        {
            if (Armed)
                CancelRecord.Calls.Add("StartingValues");
            Armed = false;
        }
    }

    internal static class LeopardStarterRig
    {
        internal static bool Armed;
        internal static void Arm(StartMenu m, ResolvedStart x) => Armed = true;
        internal static void Cancel()
        {
            if (Armed)
                CancelRecord.Calls.Add("LeopardStarterRig");
            Armed = false;
        }
    }

    internal static class StarterMooring
    {
        internal static bool Armed;
        internal static void Arm(StartMenu m, ResolvedStart x, PickupableBoatMooringRope a, PickupableBoatMooringRope b) => Armed = true;

        internal static void Cancel()
        {
            if (Armed)
                CancelRecord.Calls.Add("StarterMooring");
            Armed = false;
        }
    }

    internal static class StarterBoatRepair
    {
        internal static bool Armed;
        internal static void Arm(StartMenu m, ResolvedStart x) => Armed = true;
        internal static void Cancel(string reason = null)
        {
            if (Armed)
                CancelRecord.Calls.Add("StarterBoatRepair:" + (reason ?? "default"));
            Armed = false;
        }
    }

    internal static partial class FixedStart
    {
        private static StartPair pending;
        private static StartMenu pendingMenu;
        private static StartingOptionsSettings pendingOptions;
        internal static bool RequestedYard;
        internal static bool ThrowLargeBoatBerth;
        private static UnityEngine.Vector3 AdjustLargeBoatBerth(ResolvedStart s, UnityEngine.Vector3 p, UnityEngine.Quaternion q) => ThrowLargeBoatBerth ? throw new InvalidOperationException("Injected large-boat failure") : p + new UnityEngine.Vector3(0, 0, 1);
        private static UnityEngine.Vector3 GetBerthPosition(ResolvedStart s) => s.Recovery.boatPos.position;
        private static void ChooseMooringRopes(ResolvedStart s, out PickupableBoatMooringRope front, out PickupableBoatMooringRope back)
        {
            front = s.Ropes.ropes[0];
            back = s.Ropes.ropes[1];
        }

        internal static bool IsSelectionStillActive(StartMenu menu, ResolvedStart s) => menu != null && s.Boat != null && s.Saveable != null && s.StarterSet.starterBoat == s.Saveable.transform && menu.startingBoats[s.Region] == s.Boat;
        private static readonly FieldInfo CurrentRegionField = HarmonyLib.AccessTools.Field(typeof(StartMenu), "currentRegion"), MenuAnimationsField = HarmonyLib.AccessTools.Field(typeof(StartMenu), "animsPlaying"), StartingBoatsField = HarmonyLib.AccessTools.Field(typeof(StartMenu), "startingBoats"), RecoveryPortsField = HarmonyLib.AccessTools.Field(typeof(Recovery), "ports"), PurchaseUiField = HarmonyLib.AccessTools.Field(typeof(PurchasableBoat), "purchaseUI");
        private class RopePosition
        {
            public PickupableBoatMooringRope Rope;
            public UnityEngine.Transform Parent;
            public UnityEngine.Vector3 Position;
            public UnityEngine.Quaternion Rotation;
            public GPButtonDockMooring Mooring;
        }

        internal static ResolvedStart Fixture;
        internal static UnityEngine.Transform NativeStart;
        internal static bool ResolveFails;
        private static bool TryGetNativeStartActors(StartMenu menu, out UnityEngine.Transform observer, out UnityEngine.GameObject actor)
        {
            observer = menu.Observer;
            actor = menu.Controller;
            return observer != null && actor != null;
        }

        private static bool TryResolve(PortChoice destination, int boat, BoatSize? size, out ResolvedStart selected, out string reason, bool useShipyardBerth = false)
        {
            RequestedYard = useShipyardBerth;
            selected = ResolveFails ? null : Fixture;
            reason = ResolveFails ? "Injected resolve failure" : null;
            return !ResolveFails;
        }

        private static readonly FieldInfo MooredSpringField = HarmonyLib.AccessTools.Field(typeof(PickupableBoatMooringRope), "mooredSpring");
        private static void MoorAt(PickupableBoatMooringRope r, GPButtonDockMooring d, UnityEngine.Rigidbody body) => r.MoorTo(d);
        private static void TeleportNativeStartObserver(StartMenu menu, UnityEngine.Transform target) => menu.Observer.SetPositionAndRotation(target.position, target.rotation);
        // Models Confirm's admission/snapshot phase before the linked coroutine runs.
        internal static IEnumerator BeginFixture(StartMenu menu, ResolvedStart s, bool scrambled = false)
        {
            if (TryGetStartBlock(out _))
                return null;
            Fixture = s;
            pendingSnapshot = new StartSnapshot(menu, menu.currentRegion);
            flightGeneration = startGeneration;
            menu.currentRegion = s.Region;
            menu.animsPlaying++;
            PlayerGold.currency[s.Region] = 999;
            GameState.newGameRegion = (PortRegion)s.Region;
            menu.DisableIslandMenu();
            pending = new StartPair
            {
                Port = s.Destination,
                UseShipyardBerth = s.UseShipyardBerth,
                Boat = new BoatChoice
                {
                    Boat = s.Boat,
                    Saveable = s.Saveable
                }
            };
            pendingMenu = menu;
            pendingOptions = new();
            if (scrambled)
                return ScrambledBoundary.Invoke(menu, NativeStart, menu.Observer, menu.Controller);
            TryPreparePendingStart(menu, out var sequence);
            return sequence;
        }

        // Each independent scenario models a fresh process. Never called between attempts.
        internal static void ResetStartGuardFixture()
        {
            CancelStart();
            flightGeneration = -1;
            ResolveFails = false;
            ThrowLargeBoatBerth = false;
            BerthClearance.Throw = false;
            pending = null;
            pendingMenu = null;
            pendingOptions = null;
        }

        internal static bool PendingConsumedFixture() => pending == null && pendingMenu == null && pendingOptions == null && pendingSnapshot == null;
        internal static void LoadPrefixFixture() => typeof(FixedStart).GetNestedType("CancelBeforeLoad", BindingFlags.NonPublic).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        internal static bool HidePrefixFixture(StartMenu menu) => (bool)typeof(FixedStart).GetNestedType("StartMenuHide", BindingFlags.NonPublic).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { menu });
        internal static void PendingSnapshotFixture(StartMenu menu) => pendingSnapshot = new StartSnapshot(menu, menu.currentRegion);
    }
}
