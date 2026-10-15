using System;
using System.Collections;
using System.Reflection;
using NewBeginnings;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type t, string m)
        {
        }

        public HarmonyPatch(Type t, string m, Type[] args)
        {
        }
    }

    public static class AccessTools
    {
        public static FieldInfo Field(Type t, string n) => t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        public static MethodInfo Method(Type t, string n) => t.GetMethod(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
    }
}

namespace UnityEngine
{
    public class WaitForEndOfFrame
    {
    }

    public class GameObject
    {
        public bool activeInHierarchy = true;
    }

    public class Rigidbody
    {
        public bool isKinematic;
    }

    public class SpringJoint
    {
        public Rigidbody connectedBody;
        public float maxDistance;
    }

    public class Time
    {
        public static float realtimeSinceStartup;
    }

    public struct Vector3
    {
        public float x;
        public static float Distance(Vector3 a, Vector3 b) => Math.Abs(a.x - b.x);
    }

    public class Transform
    {
        public Vector3 position;
        public Vector3 TransformPoint(Vector3 local) => new Vector3
        {
            x = position.x + local.x
        };
    }
}

public class RopeEffect
{
    public float distanceCheckTimer = 8;
    public bool isActiveAndEnabled = true;
    public UnityEngine.Transform transform = new UnityEngine.Transform();
}

public class PlayerControllerMirror
{
    public UnityEngine.Transform transform = new UnityEngine.Transform();
}

public static class Refs
{
    public static PlayerControllerMirror observerMirror = new PlayerControllerMirror();
}

public class PickupableBoatMooringRope
{
    public string name = "test-rope";
    public bool isActiveAndEnabled = true;
    public UnityEngine.Rigidbody Body;
    public RopeEffect Effect = new RopeEffect();
    public UnityEngine.SpringJoint mooredToSpring;
    public float currentRopeLengthSquared = 72900;
    public bool wasKinematic;
    public static float maxLength = 900;
    public UnityEngine.Transform initialParent = new UnityEngine.Transform();
    public UnityEngine.Vector3 initialPos;
    public UnityEngine.Transform transform = new UnityEngine.Transform
    {
        position = new UnityEngine.Vector3
        {
            x = 4
        }
    };
    public float GetCurrentDistanceSquared()
    {
        var distance = UnityEngine.Vector3.Distance(initialParent.TransformPoint(initialPos), transform.position);
        return distance * distance;
    }

    public void FinishNativeKinematicReset()
    {
        currentRopeLengthSquared = GetCurrentDistanceSquared();
        mooredToSpring.maxDistance = (float)Math.Sqrt(currentRopeLengthSquared);
        wasKinematic = false;
    }

    public UnityEngine.Rigidbody GetBoatRigidbody() => Body;
    public T GetComponent<T>()
        where T : class => Effect as T;
    public bool ChangeRopeLength(float d) => true;
    public void Unmoor()
    {
    }
}

public class GPButtonDockMooring
{
    public UnityEngine.SpringJoint spring = new UnityEngine.SpringJoint();
    public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
}

public class StartMenu
{
}

public class GameState
{
    public static bool playing;
    public static bool currentlyLoading;
}

public class PurchasableBoat
{
    public bool Purchased = true;
    public bool isPurchased() => Purchased;
}

namespace NewBeginnings
{
    internal class ResolvedStart
    {
        public UnityEngine.Rigidbody Body = new UnityEngine.Rigidbody();
        public PurchasableBoat Boat = new PurchasableBoat();
        public GPButtonDockMooring FrontMooring = new GPButtonDockMooring(), BackMooring = new GPButtonDockMooring();
    }

    internal static class FixedStart
    {
        public static bool Active = true;
        internal static bool IsSelectionStillActive(StartMenu m, ResolvedStart s) => Active;
    }

    internal class Plugin
    {
        internal static Plugin Instance = new Plugin();
        internal IEnumerator Run;
        internal int DebugLogs, Warnings;
        internal bool Waiting;
        internal bool WaitingForPlayerInput() => Waiting;
        public void StartCoroutine(IEnumerator run)
        {
            Run = run;
            Run.MoveNext();
        }

        internal void Warn(string s)
        {
            Warnings++;
            Console.WriteLine("WARN " + s);
        }

        internal void DebugLog(string s)
        {
            DebugLogs++;
            Console.WriteLine("DEBUG " + s);
        }
    }
}

class TestHarness
{
    static int checks;
    static void Check(bool c, string label)
    {
        if (!c)
            throw new Exception(label);
        checks++;
        Console.WriteLine("PASS " + label);
    }

    static bool Near(float a, float b) => Math.Abs(a - b) < .001;
    static ResolvedStart selected;
    static PickupableBoatMooringRope front, back;
    static void Arm()
    {
        StarterMooring.Cancel();
        Plugin.Instance = new Plugin();
        Refs.observerMirror = new PlayerControllerMirror();
        FixedStart.Active = true;
        GameState.playing = false;
        GameState.currentlyLoading = true;
        UnityEngine.Time.realtimeSinceStartup = 0;
        selected = new ResolvedStart();
        selected.FrontMooring.spring.connectedBody = selected.Body;
        selected.BackMooring.spring.connectedBody = selected.Body;
        front = new PickupableBoatMooringRope
        {
            Body = selected.Body,
            mooredToSpring = selected.FrontMooring.spring
        };
        back = new PickupableBoatMooringRope
        {
            Body = selected.Body,
            mooredToSpring = selected.BackMooring.spring
        };
        StarterMooring.Arm(new StartMenu(), selected, front, back);
    }

    static bool Step() => Plugin.Instance.Run.MoveNext();
    static void Ready()
    {
        GameState.playing = true;
        GameState.currentlyLoading = false;
    }

    static void Patch(string name, params object[] args) => typeof(StarterMooring).GetNestedType(name, BindingFlags.NonPublic).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    static void Main()
    {
        Check(Near(StarterMooring.SlackLengthSquared(16, 925), 25), "four metre endpoint gains exactly one metre");
        Check(Near(StarterMooring.SlackLengthSquared(900, 925), 925), "native length cap respected");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1, 925, 72900 })
            Check(float.IsNaN(StarterMooring.SlackLengthSquared(invalid, 925)), "invalid/hidden endpoint rejected: " + invalid);
        Arm();
        Step();
        Check(front.Effect.distanceCheckTimer == 0, "old culling cache invalidated during startup");
        Plugin.Instance.Waiting = true;
        UnityEngine.Time.realtimeSinceStartup = 600;
        Step();
        Check(Plugin.Instance.Warnings == 0 && front.currentRopeLengthSquared == 72900, "disclaimer delay neither expires operation nor changes rope");
        Ready();
        front.wasKinematic = true;
        back.wasKinematic = true;
        Step();
        Check(front.currentRopeLengthSquared == 72900, "native kinematic reset must finish first");
        front.wasKinematic = back.wasKinematic = false;
        Step();
        Check(front.currentRopeLengthSquared == 25 && back.currentRopeLengthSquared == 25 && selected.FrontMooring.spring.maxDistance == 5 && selected.BackMooring.spring.maxDistance == 5, "both native length representations rebased from settled endpoints");
        front.currentRopeLengthSquared = 64;
        Check(!Step() && front.currentRopeLengthSquared == 64, "later player length persists after helper completion");
        Arm();
        front.transform.position = back.transform.position = new UnityEngine.Vector3
        {
            x = 270
        };
        front.wasKinematic = back.wasKinematic = true;
        GameState.playing = true;
        GameState.currentlyLoading = false;
        Step();
        Check(front.currentRopeLengthSquared == 72900 && Plugin.Instance.DebugLogs == 0, "hidden-world endpoint and enormous prior length remain untouched before final placement");
        front.initialParent.position = back.initialParent.position = new UnityEngine.Vector3
        {
            x = 1000
        };
        front.transform.position = new UnityEngine.Vector3
        {
            x = 1006.70f
        };
        back.transform.position = new UnityEngine.Vector3
        {
            x = 1006.74f
        };
        Step();
        Check(front.currentRopeLengthSquared == 72900 && Plugin.Instance.DebugLogs == 0, "final placement still waits for native kinematic reset");
        front.FinishNativeKinematicReset();
        back.FinishNativeKinematicReset();
        Step();
        Check(Near(front.currentRopeLengthSquared, 7.7f * 7.7f) && Near(back.currentRopeLengthSquared, 7.74f * 7.74f) && Near(selected.FrontMooring.spring.maxDistance, 7.7f) && Near(selected.BackMooring.spring.maxDistance, 7.74f), "post-settle live distances 6.70/6.74 m receive 7.70/7.74 m lines after native reset");
        Check(!Step() && Plugin.Instance.DebugLogs == 2, "late readiness applies each selected line once and finishes");
        Arm();
        Ready();
        selected.Body.isKinematic = true;
        Step();
        Check(front.currentRopeLengthSquared == 72900, "kinematic hull cannot receive final slack");
        selected.Body.isKinematic = false;
        Step();
        Check(front.currentRopeLengthSquared == 25, "dynamic hull receives slack");
        Arm();
        Ready();
        front.transform.position = new UnityEngine.Vector3
        {
            x = 30
        };
        Step();
        Check(Near(front.currentRopeLengthSquared, 925) && Near(selected.FrontMooring.spring.maxDistance, (float)Math.Sqrt(925)), "startup application caps both native length representations");
        Arm();
        Ready();
        Patch("PlayerAdjustment", front);
        front.currentRopeLengthSquared = 64;
        Step();
        Check(front.currentRopeLengthSquared == 64 && back.currentRopeLengthSquared == 25, "early player adjustment wins for only its line");
        Arm();
        Ready();
        Patch("CastOff", front);
        Step();
        Check(front.currentRopeLengthSquared == 72900 && back.currentRopeLengthSquared == 25, "cast-off line not reclaimed");
        Arm();
        Ready();
        selected.FrontMooring.spring.connectedBody = null;
        Step();
        Check(front.currentRopeLengthSquared == 72900 && back.currentRopeLengthSquared == 25, "disconnected spring not reclaimed");
        Arm();
        Ready();
        front.transform.position = back.transform.position = new UnityEngine.Vector3
        {
            x = 270
        };
        Step();
        UnityEngine.Time.realtimeSinceStartup = 21;
        Step();
        Check(Plugin.Instance.Warnings == 1 && front.currentRopeLengthSquared == 72900 && !Step(), "hidden island geometry expires once with no writes");
        Arm();
        Ready();
        // A saved-game load reaches this through FixedStart.CancelStartWork.
        StarterMooring.Cancel();
        Check(!Step() && front.currentRopeLengthSquared == 72900, "saved load cancels pending slack");
        Arm();
        Ready();
        FixedStart.Active = false;
        Check(!Step() && front.currentRopeLengthSquared == 72900, "changed selection cancels operation");
        Arm();
        Ready();
        selected.Boat.Purchased = false;
        Step();
        UnityEngine.Time.realtimeSinceStartup = 21;
        Step();
        Check(!Step() && Plugin.Instance.Warnings == 1 && front.currentRopeLengthSquared == 72900, "missing ownership expires after gameplay deadline");
        Arm();
        UnityEngine.Time.realtimeSinceStartup = 91;
        Check(!Step() && Plugin.Instance.Warnings == 1, "abandoned pre-play startup expires");
        Arm();
        Plugin.Instance.Waiting = true;
        UnityEngine.Time.realtimeSinceStartup = 1000;
        Step();
        Plugin.Instance.Waiting = false;
        UnityEngine.Time.realtimeSinceStartup = 1091;
        Check(!Step() && Plugin.Instance.Warnings == 1, "pre-play deadline resumes after prompt stops waiting");
        Arm();
        Ready();
        selected.Body.isKinematic = true;
        Step();
        GameState.currentlyLoading = true;
        Check(!Step() && front.currentRopeLengthSquared == 72900, "loading transition after gameplay immediately cancels");
        Arm();
        Ready();
        selected.Body.isKinematic = true;
        Step();
        GameState.playing = false;
        Check(!Step() && front.currentRopeLengthSquared == 72900, "leaving gameplay immediately cancels");
        Arm();
        Ready();
        selected.Boat.Purchased = false;
        Plugin.Instance.Waiting = true;
        UnityEngine.Time.realtimeSinceStartup = 1000;
        Step();
        Check(Plugin.Instance.Warnings == 0, "F prompt pauses timeout even after native playing flag");
        Plugin.Instance.Waiting = false;
        UnityEngine.Time.realtimeSinceStartup = 1011;
        Step();
        Plugin.Instance.Waiting = true;
        UnityEngine.Time.realtimeSinceStartup = 2011;
        Step();
        Plugin.Instance.Waiting = false;
        UnityEngine.Time.realtimeSinceStartup = 2021;
        Step();
        Check(!Step() && Plugin.Instance.Warnings == 1, "gameplay timeout accumulates across prompt pauses instead of resetting");
        Arm();
        Ready();
        Refs.observerMirror.transform.position = new UnityEngine.Vector3
        {
            x = 300
        };
        Step();
        Check(front.currentRopeLengthSquared == 25 && Plugin.Instance.DebugLogs == 2, "slack applies while observer mirror is still distant");
        front.Effect.distanceCheckTimer = 7;
        front.currentRopeLengthSquared = 64;
        Step();
        Check(front.Effect.distanceCheckTimer == 0 && front.currentRopeLengthSquared == 64 && Plugin.Instance.DebugLogs == 2, "distant-observer visual refresh continues without reapplying slack");
        Refs.observerMirror.transform.position = new UnityEngine.Vector3
        {
            x = 250
        };
        Step();
        front.Effect.distanceCheckTimer = 7;
        Check(!Step() && front.Effect.distanceCheckTimer == 7, "native inclusive 250 metre proximity ends cache refresh");
        Arm();
        Ready();
        Refs.observerMirror = null;
        Step();
        UnityEngine.Time.realtimeSinceStartup = 21;
        Step();
        Check(!Step() && Plugin.Instance.DebugLogs == 2 && Plugin.Instance.Warnings == 1, "missing observer expires visual work without retrying applied lengths");
        Console.WriteLine("Passed " + checks + " checks against linked production StarterMooring.cs with stub native lifecycle. No Unity physics/rendering/save simulation.");
    }
}
