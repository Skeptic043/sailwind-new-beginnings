using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

namespace HarmonyLib
{
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch()
        {
        }

        public HarmonyPatch(Type t, string s)
        {
        }
    }

    public static class AccessTools
    {
        public static MethodInfo Method(Type t, string n) => t.GetMethod(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        public static MethodInfo Method(Type t, string n, Type[] a) => t.GetMethod(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance, null, a, null);
        public static FieldInfo Field(Type t, string n) => t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
    }
}

namespace UnityEngine
{
    public class Object
    {
        public static int cloneCalls, failCloneAt;
        public bool destroyed;
        public string name;
        public static bool operator ==(Object left, Object right) => ReferenceEquals(left, right) || ((ReferenceEquals(left, null) || left.destroyed) && (ReferenceEquals(right, null) || right.destroyed));
        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();
        public static GameObject Instantiate(GameObject source, Transform parent, bool stay)
        {
            if (++cloneCalls == failCloneAt)
                throw new Exception("injected clone failure");
            var go = new GameObject(source.name);
            go.activeSelf = source.activeSelf;
            go.transform.SetParent(parent, true);
            go.transform.localScale = source.transform.localScale;
            foreach (var component in source.components.Where(c => !(c is Transform)))
            {
                var copy = go.AddComponent(component.GetType());
                foreach (var f in component.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                    if (f.Name != "gameObject" && f.Name != "body")
                        f.SetValue(copy, f.GetValue(component));
            }

            return go;
        }

        public static void Destroy(Object o)
        {
            o.destroyed = true;
            if (o is GameObject g)
            {
                g.transform.SetParent(null, true);
                foreach (var c in g.components)
                    c.destroyed = true;
                foreach (var t in g.transform.children.ToArray())
                    Destroy(t.gameObject);
            }
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;

        public T GetComponent<T>()
            where T : Component => gameObject.GetComponent<T>();
        public Component GetComponent(Type t) => gameObject.GetComponent(t);
        public T[] GetComponentsInChildren<T>(bool inactive)
            where T : Component => gameObject.GetComponentsInChildren<T>(inactive);
    }

    public class GameObject : Object
    {
        public Scene scene;
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent?.gameObject.activeInHierarchy ?? true);

        public List<Component> components = new();
        public Transform transform;
        public GameObject(string n)
        {
            name = n;
            transform = new Transform
            {
                gameObject = this
            };
            components.Add(transform);
        }

        public void SetActive(bool a) => activeSelf = a;
        public T AddComponent<T>()
            where T : Component, new() => (T)AddComponent(typeof(T));
        public Component AddComponent(Type t)
        {
            var c = (Component)Activator.CreateInstance(t);
            c.gameObject = this;
            components.Add(c);
            Resources.all.Add(c);
            return c;
        }

        public T[] GetComponents<T>()
            where T : Component => components.OfType<T>().ToArray();
        public T GetComponent<T>()
            where T : Component => components.OfType<T>().FirstOrDefault();
        public Component GetComponent(Type t) => components.FirstOrDefault(t.IsInstanceOfType);
        public T[] GetComponentsInChildren<T>(bool b)
            where T : Component => components.OfType<T>().Concat(transform.children.SelectMany(t => t.gameObject.GetComponentsInChildren<T>(b))).ToArray();
    }

    public class Transform : Component
    {
        public Transform Find(string name) => children.FirstOrDefault(t => t.gameObject.name == name);
        public Vector3 localScale = Vector3.one;
        public Transform parent;
        public Vector3 position;
        public Quaternion rotation;
        public List<Transform> children = new();
        public void SetParent(Transform p, bool b)
        {
            parent?.children.Remove(this);
            parent = p;
            p?.children.Add(this);
        }

        public void SetPositionAndRotation(Vector3 p, Quaternion q)
        {
            position = p;
            rotation = q;
        }
    }

    public struct Vector3
    {
        public static Vector3 one => new(1, 1, 1);
        public float sqrMagnitude => x * x + y * y + z * z;

        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public float x, y, z;
        public Vector3(float a, float b, float c)
        {
            x = a;
            y = b;
            z = c;
        }

        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
    }

    public struct Quaternion
    {
        public static Quaternion identity => new();
    }

    public class Mesh : Object
    {
    }

    public class MeshFilter : Component
    {
        public Mesh sharedMesh;
    }

    public static class Resources
    {
        public static int crateUiDiscoveries;
        public static List<Object> all = new();
        public static T[] FindObjectsOfTypeAll<T>() => all.Where(o => !o.destroyed).OfType<T>().ToArray();
        public static Object[] FindObjectsOfTypeAll(Type t)
        {
            if (t == typeof(CrateInventoryUI))
                crateUiDiscoveries++;
            return all.Where(o => !o.destroyed && t.IsInstanceOfType(o)).ToArray();
        }
    }
}

public class PrefabsDirectory : Component
{
    public static PrefabsDirectory instance;
    public GameObject[] directory;
}

public class SaveablePrefab : Component
{
    public int prefabIndex, instanceId, currentCrateId;
}

public class StarterSet : Component
{
}

public class ItemRigidbody
{
    public int moved;
    public void ForceRigidbodyToWalkCol() => moved++;
}

public class ShipItem : MonoBehaviour
{
    public new string name;
    public TransactionCategory category;
    public bool sold, big;
    public float amount, health;
    public Transform held;
    public ItemRigidbody body = new();
    public ItemRigidbody GetItemRigidbody() => body;
    public virtual void OnBuy()
    {
    }
}

public static class GameState
{
    public static bool currentlyLoading;
}

public class ShipItemLampHook : ShipItem
{
}

public class ShipItemBottle : ShipItem
{
}

public class ShipItemStoveFuel : ShipItem
{
}

public class ShipItemCompass : ShipItem
{
}

public class ShipItemQuadrant : ShipItem
{
}

public class ShipItemChipLog : ShipItem
{
}

public class ShipItemBroom : ShipItem
{
}

public class ShipItemFishingRod : ShipItem
{
    public int purchaseCalls;
    public override void OnBuy() => purchaseCalls++;
}

public class ShipItemFishingHook : ShipItem
{
}

public class ShipItemLight : ShipItem
{
}

public class ShipItemFoldable : ShipItem
{
}

public class ShipItemLanternFuel : ShipItem
{
}

public class ShipItemSoup : ShipItem
{
}

public class ShipItemHammer : ShipItem
{
}

public class ShipItemSpyglass : ShipItem
{
}

public class ShipItemOakum : ShipItem
{
}

public class ShipItemInkSet : ShipItem
{
}

public class ShipItemOar : ShipItem
{
}

public class ShipItemClock : ShipItem
{
}

public class ShipItemKnife : ShipItem
{
}

public class ShipItemKettle : ShipItem
{
}

public class ShipItemCrate : ShipItem
{
}

public class CrateInventory : Component
{
    public static bool failInsert;
    public static bool failAfterInsert;
    public static int failOnInsertCall, insertCalls;
    public static List<ShipItem> inserted = new();
    public List<ShipItem> containedItems = new();
    public void InsertItem(ShipItem i)
    {
        insertCalls++;
        if (failInsert || (!failAfterInsert && insertCalls == failOnInsertCall))
            throw new Exception("injected native insertion failure");
        containedItems.Add(i);
        inserted.Add(i);
        i.transform.localScale = new Vector3(.33f, .33f, .33f);
        i.GetComponent<SaveablePrefab>().currentCrateId = GetComponent<SaveablePrefab>().instanceId;
        if (failAfterInsert && insertCalls == failOnInsertCall)
            throw new Exception("injected failure after native membership");
    }

    public void WithdrawItem(ShipItem i)
    {
        containedItems.Remove(i);
        i.transform.localScale = Vector3.one;
        i.GetComponent<SaveablePrefab>().currentCrateId = 0;
    }
}

public class CrateInventoryUI : Component
{
    public Mesh[] containerMeshes;
    public object[] buttons;
}

namespace NewBeginnings
{
    internal class ResolvedStart
    {
        public StarterSet StarterSet;
        public StartingOptionsSettings Options;
    }

    internal class Plugin
    {
        internal static Plugin Instance = new Plugin();
        internal void Warn(string x)
        {
        }

        internal void DebugLog(string x)
        {
        }

        internal void Error(string x, Exception e)
        {
        }
    }
}

namespace UnityEngine
{
    public class MonoBehaviour : Component
    {
    }

    public class Rigidbody : Component
    {
    }

    public class Collider : Component
    {
    }

    public struct Scene
    {
        public bool valid;
        public bool IsValid() => valid;
    }
}

namespace UnityEngine
{
    public class Shader : Object
    {
    }

    public class Material : Object
    {
        public Shader shader;
    }

    public class Renderer : Component
    {
        public bool enabled = true;
        public Material[] sharedMaterials = Array.Empty<Material>();
    }

    public class MeshRenderer : Renderer
    {
    }

    public class SkinnedMeshRenderer : Renderer
    {
        public Mesh sharedMesh;
    }
}

public class Good : Component
{
    public int missionIndex;
    public int GetMissionIndex() => missionIndex;
    public void RegisterAsMissionless() => missionIndex = -1;
}

public class ShipItemFood : ShipItem
{
}

public enum TransactionCategory
{
    retailFood,
    retailWater,
    retailAlco,
    toolsAndSupplies,
    furniture
}

namespace BepInEx.Bootstrap
{
    public static class Chainloader
    {
        public static Dictionary<string, Info> PluginInfos = new();
    }

    public class Info
    {
        public object Instance;
        public Metadata Metadata = new();
    }

    public class Metadata
    {
        public string GUID, Name;
    }
}

namespace KemyNavTools
{
    public class TestPlugin
    {
    }

    public static class PreloadDirectoryPatch
    {
        public static GameObject inclinometerPrefab, compassPrefab, binnaclePrefab;
    }
}

namespace JetPump
{
    public class TestPlugin
    {
    }

    public static class Plugin
    {
        public static bool Ready;
    }

    public static class PumpAssets
    {
        public static GameObject Pump, Controller, HugePump;
    }
}

namespace Windicators
{
    public class TestPlugin
    {
    }

    public static class AssetTools
    {
        public static Dictionary<int, GameObject> itemPrefabs = new();
    }
}

namespace KemyFurniture
{
    public class FurniturePlugin
    {
        public static GameObject[] LoadedPrefabs { get; set; } = Array.Empty<GameObject>();
    }

    public class FurnitureComponent : MonoBehaviour
    {
    }
}

public class Mug : Component
{
}
