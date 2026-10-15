using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    // One optional record in the native per-save mod dictionary. No marker
    // means no outstanding hold, including saves from earlier versions.
    internal static class OffshoreHoldSave
    {
        internal const string Key = Plugin.Id + ".offshoreHold.v1";
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        [HarmonyPatch(typeof(SaveLoadManager), "LoadGame", new[] { typeof(int) })]
        private static class BeforeLoad
        {
            private static void Prefix() => ClearBeforeLoad();
        }

        // FixedStart.CancelStartWork removes the active hold before a load.
        internal static void ClearBeforeLoad()
        {
            // Native LoadGame replaces this dictionary only when the incoming
            // save has one. Do not inherit our previous save's marker otherwise.
            GameState.modData?.Remove(Key);
        }

        [HarmonyPatch(typeof(SaveLoadManager), "SaveModData")]
        private static class SavePatch
        {
            private static void Postfix() => Save();
        }

        internal static void Save()
        {
            GameState.modData?.Remove(Key);
            if (!PirateStationKeeper.TryGetHold(out var body, out var frame, out var position, out var yaw)) return;
            var boat = body.GetComponent<SaveableObject>();
            var island = frame.GetComponentInParent<IslandHorizon>();
            if (boat == null || island == null)
            {
                Plugin.Instance?.Warn("The offshore hold could not be saved because its boat or island identity is missing.");
                return;
            }
            var local = island.transform.InverseTransformPoint(frame.TransformPoint(position));
            var localYaw = Mathf.DeltaAngle(island.transform.eulerAngles.y, frame.eulerAngles.y + yaw);
            if (GameState.modData == null) GameState.modData = new Dictionary<string, string>();
            GameState.modData[Key] = string.Join("|", boat.sceneIndex.ToString(Invariant),
                island.islandIndex.ToString(Invariant), local.x.ToString("R", Invariant),
                local.y.ToString("R", Invariant), local.z.ToString("R", Invariant), localYaw.ToString("R", Invariant));
        }

        [HarmonyPatch(typeof(SaveLoadManager), "LoadModData")]
        private static class LoadPatch
        {
            private static void Postfix() => Load();
        }

        internal static void Load()
        {
            PirateStationKeeper.RemoveActive();
            if (GameState.modData == null || !GameState.modData.TryGetValue(Key, out var value)) return;
            var fields = value?.Split('|');
            if (fields == null || fields.Length != 6 ||
                !int.TryParse(fields[0], NumberStyles.Integer, Invariant, out var boatId) ||
                !int.TryParse(fields[1], NumberStyles.Integer, Invariant, out var islandId) ||
                !ReadFloat(fields[2], out var x) || !ReadFloat(fields[3], out var y) ||
                !ReadFloat(fields[4], out var z) || !ReadFloat(fields[5], out var yaw))
            {
                Plugin.Instance?.Warn("The saved offshore hold could not be read.");
                return;
            }
            var boats = SaveLoadManager.instance.GetCurrentObjects();
            var boat = boatId >= 0 && boatId < boats.Length ? boats[boatId] : null;
            var body = boat != null ? boat.GetComponent<Rigidbody>() : null;
            var island = Refs.islands != null && islandId >= 0 && islandId < Refs.islands.Length ? Refs.islands[islandId] : null;
            var purchase = body != null ? body.GetComponent<PurchasableBoat>() : null;
            var islandIdentity = island != null ? island.GetComponentInParent<IslandHorizon>() : null;
            if (body == null || purchase == null || !purchase.isPurchased() ||
                islandIdentity == null || islandIdentity.islandIndex != islandId)
            {
                Plugin.Instance?.Warn("The saved offshore hold's boat or island is unavailable.");
                return;
            }
            PirateStationKeeper.Restore(body, island, new Vector3(x, y, z), yaw);
        }

        private static bool ReadFloat(string value, out float result) =>
            float.TryParse(value, NumberStyles.Float, Invariant, out result) && !float.IsNaN(result) && !float.IsInfinity(result);
    }
}
