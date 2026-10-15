using System;
using System.Collections.Generic;
using UnityEngine;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string method)
        {
        }

        public HarmonyPatch(Type type, string method, Type[] arguments)
        {
        }
    }
}

namespace NewBeginnings
{
    class Plugin
    {
        internal const string Id = "com.skeptic043.sailwind.newbeginnings";
        internal static Plugin Instance = new();
        internal bool isActiveAndEnabled = true;
        internal void Warn(string message)
        {
        }
    }
}

static class GameState
{
    internal static bool currentlyLoading, playing;
    internal static Transform currentBoat;
    internal static Shipyard currentShipyard;
    internal static Dictionary<string, string> modData = new();
}

public class SaveableObject : Component
{
    public int sceneIndex;
}

public class IslandHorizon : Component
{
    public int islandIndex;
}

class Shipyard
{
    internal GameObject ship;
    internal GameObject GetCurrentBoat() => ship;
}

class SaveLoadManager
{
    internal static SaveLoadManager instance = new();
    internal SaveableObject[] objects = new SaveableObject[255];
    public SaveableObject[] GetCurrentObjects() => objects;
}

static class Refs
{
    internal static Transform[] islands = new Transform[43];
}
