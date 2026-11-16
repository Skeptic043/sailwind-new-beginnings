using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NewBeginnings
{
    // Open-water point choice, kept free of Unity scene access for tests.
    internal static class OpenWaterPicker
    {
        internal const int Attempts = 200;

        // Distance band from the anchor island and the clearance every island
        // centre must keep, in metres, for each at-sea place.
        internal static void Band(ScenarioPlace place, out float minimum, out float maximum, out float clearance)
        {
            if (place == ScenarioPlace.NearLand) { minimum = 600f; maximum = 1500f; clearance = 500f; }
            else { minimum = 2500f; maximum = 6000f; clearance = 2000f; }
        }

        // A random direction and distance from the anchor. The first point that
        // keeps its clearance and is open water wins; otherwise the open-water
        // point farthest from every island, so the start still goes ahead.
        internal static bool TryPick(Vector2 anchor, IList<Vector2> islands, ScenarioPlace place,
            Func<Vector2, bool> openWater, System.Random random, out Vector2 point, out bool keptClearance)
        {
            Band(place, out var minimum, out var maximum, out var clearance);
            point = default(Vector2);
            keptClearance = false;
            var best = float.NegativeInfinity;
            for (var attempt = 0; attempt < Attempts; attempt++)
            {
                var angle = random.NextDouble() * Math.PI * 2;
                var distance = minimum + (float)random.NextDouble() * (maximum - minimum);
                var candidate = anchor + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * distance;
                var nearest = islands.Count == 0 ? float.PositiveInfinity :
                    islands.Min(island => (island - candidate).magnitude);
                if (nearest <= best || !openWater(candidate)) continue;
                best = nearest;
                point = candidate;
                if (nearest >= clearance) { keptClearance = true; return true; }
            }
            return !float.IsNegativeInfinity(best);
        }
    }

    // A start on the boat's deck at an open-water point, with no mooring,
    // hold or shore landing. The boat keeps native physics throughout.
    internal static class AtSeaStart
    {
        private const float DeckGrid = 1.1f;
        // Room a barrel needs under a deck above it.
        private const float MinimumHeadroom = 1.3f;
        private const float DeckBand = 2f;
        private const float LayerHeight = 1.2f;
        private const float FirstLayerHeight = .8f;
        private const int MaximumRumBarrels = 40;
        // Enough for any sensible loadout; a huge customized quantity would
        // otherwise stall the start building one record per item.
        private const int MaximumDeckItems = 500;
        private static readonly System.Reflection.FieldInfo CachedItemsField =
            HarmonyLib.AccessTools.Field(typeof(BoatLocalItems), "cachedItems");
        private static readonly System.Reflection.FieldInfo ItemsLoadedField =
            HarmonyLib.AccessTools.Field(typeof(BoatLocalItems), "itemsLoaded");

        internal static bool TryPlan(ResolvedStart selected, ScenarioPlace place, out StartBerthPlan plan, out string reason)
        {
            plan = null;
            reason = null;
            var island = selected.Island;
            if (island == null) { reason = "the anchor island is unavailable"; return false; }
            var anchor = Flat(island.GetPosition());
            var islands = Resources.FindObjectsOfTypeAll<IslandHorizon>()
                .Where(item => item != null && item.gameObject.scene.isLoaded && item.gameObject.activeInHierarchy)
                .Select(item => Flat(item.GetPosition())).ToList();
            if (!StartBerthPlanner.TryHeight(selected.Body, out var height, out reason)) return false;
            var random = new System.Random();
            if (!OpenWaterPicker.TryPick(anchor, islands, place, point => OpenWater(point, height), random,
                out var picked, out var kept))
            {
                reason = "no open water was found around " + island.name;
                return false;
            }
            if (!kept)
                Plugin.Instance?.Warn("No open-water point kept its full distance from every island; using the clearest one found.");
            var position = new Vector3(picked.x, height, picked.y);
            var rotation = Quaternion.Euler(0f, (float)(random.NextDouble() * 360.0), 0f);
            var frame = island.transform;
            plan = new StartBerthPlan
            {
                Destination = selected.Destination, Frame = frame, Mode = StartBerthMode.AtSea,
                LocalPosition = frame.InverseTransformPoint(position),
                LocalShorePosition = frame.InverseTransformPoint(position),
                LocalRotation = Quaternion.Inverse(frame.rotation) * rotation
            };
            Plugin.Instance?.Report($"At-sea start {place} chosen {Vector2.Distance(anchor, picked):F0} m from {island.name}.");
            return true;
        }

        // Land, rock or seabed within a hull's draft rejects the point. Some
        // islands carry flat sea-level sheets kilometres wide (Clear Mind's
        // Plane colliders), which are not land.
        private static bool OpenWater(Vector2 point, float height) =>
            !Physics.RaycastAll(new Vector3(point.x, height + 400f, point.y), Vector3.down, 800f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .Any(hit => hit.point.y > height - 8f && hit.collider.bounds.size.y > .1f);

        // A boat-root local place for one item, with the room above it.
        internal sealed class CargoSlot
        {
            internal Vector3 Point;
            internal float Headroom;
        }

        // Where the player stands and where cargo goes, from the boat's own
        // walk colliders, so modded boats work as long as they can be walked on.
        internal sealed class DeckSurvey
        {
            internal Vector3 Player;
            internal readonly List<CargoSlot> Slots = new List<CargoSlot>();
        }

        // Every upward floor in each grid column, top deck first, holds below.
        internal static DeckSurvey SurveyDeck(ResolvedStart selected)
        {
            var embark = selected.Saveable.GetComponentsInChildren<BoatEmbarkCollider>(true)
                .FirstOrDefault(item => item != null && item.walkCollider != null);
            if (embark == null) return null;
            var walk = embark.walkCollider;
            var worldBoat = embark.transform.parent ?? selected.Saveable.transform;
            var colliders = walk.GetComponentsInChildren<Collider>(true)
                .Where(item => item.enabled && !item.isTrigger).ToArray();
            if (colliders.Length == 0) return null;
            var bounds = colliders[0].bounds;
            foreach (var collider in colliders) bounds.Encapsulate(collider.bounds);
            var columns = new Dictionary<long, List<Vector3>>();
            var columnsX = Mathf.CeilToInt(bounds.size.x / DeckGrid);
            var columnsZ = Mathf.CeilToInt(bounds.size.z / DeckGrid);
            for (var i = 0; i < columnsX; i++)
            for (var j = 0; j < columnsZ; j++)
            {
                var origin = new Vector3(bounds.min.x + (i + .5f) * DeckGrid, bounds.max.y + 2f, bounds.min.z + (j + .5f) * DeckGrid);
                var floors = new List<Vector3>();
                // A collider is hit once per ray, so step below each surface
                // to find the floors beneath it, such as a hold under the deck.
                for (var step = 0; step < 8; step++)
                {
                    if (!Physics.Raycast(origin, Vector3.down, out var hit, origin.y - bounds.min.y + 1f, 1 << 8,
                        QueryTriggerInteraction.Ignore)) break;
                    if (hit.collider.transform.IsChildOf(walk) && hit.normal.y > .8f) floors.Add(hit.point);
                    origin = hit.point + Vector3.down * .05f;
                }
                if (floors.Count > 0) columns[Key(i, j)] = floors;
            }
            if (columns.Count == 0) return null;
            // The main deck is the most common top height; cabin roofs and
            // raised platforms more than a couple of metres above it are left out.
            var deck = columns.Values.GroupBy(floors => Mathf.Round(floors[0].y * 2f) / 2f)
                .OrderByDescending(group => group.Count()).First().Key;
            var survey = new DeckSurvey();
            var standing = new List<Vector3>();
            // Main-deck points kept even at an edge, for a deck too narrow to
            // have interior cells; never a hold floor.
            var deckEdge = new List<Vector3>();
            var slots = new List<CargoSlot>();
            foreach (var pair in columns)
            {
                int i = (int)(pair.Key >> 32), j = (int)(pair.Key & 0xffffffff);
                var floors = pair.Value;
                for (var level = 0; level < floors.Count; level++)
                {
                    var floor = floors[level];
                    if (floor.y > deck + DeckBand) continue;
                    var local = selected.Saveable.transform.InverseTransformPoint(
                        worldBoat.TransformPoint(walk.InverseTransformPoint(floor)));
                    var mainDeck = level == 0 && Mathf.Abs(floor.y - deck) < .6f;
                    if (mainDeck) deckEdge.Add(local);
                    // Rails and hull edges let items slip overboard.
                    if (!Interior(columns, i, j, floor.y)) continue;
                    var headroom = level == 0 ? float.PositiveInfinity : floors[level - 1].y - floor.y;
                    if (mainDeck) standing.Add(local);
                    if (headroom >= MinimumHeadroom) slots.Add(new CargoSlot { Point = local, Headroom = headroom });
                }
            }
            if (standing.Count == 0) standing.AddRange(deckEdge);
            if (standing.Count == 0) return null;
            var centre = new Vector3(standing.Average(point => point.x), 0f, standing.Average(point => point.z));
            survey.Player = standing.OrderBy(point => Flat(point - centre).sqrMagnitude).First();
            // Lowest floors first, so a hold fills before the deck; then
            // nearest the middle of the boat.
            survey.Slots.AddRange(slots
                .Where(slot => Flat(slot.Point - survey.Player).sqrMagnitude > 1.5f * 1.5f)
                .OrderBy(slot => Mathf.Round(slot.Point.y / .5f))
                .ThenBy(slot => Flat(slot.Point - centre).sqrMagnitude));
            return survey;
        }

        private static long Key(int i, int j) => ((long)i << 32) | (uint)j;

        private static bool Interior(Dictionary<long, List<Vector3>> columns, int i, int j, float height)
        {
            foreach (var neighbour in new[] { Key(i - 1, j), Key(i + 1, j), Key(i, j - 1), Key(i, j + 1) })
                if (!columns.TryGetValue(neighbour, out var floors) || !floors.Any(floor => Mathf.Abs(floor.y - height) < .4f))
                    return false;
            return true;
        }

        private static Vector2 Flat(Vector3 value) => new Vector2(value.x, value.z);

        // Parented to the boat, facing the bow. Native start code reads it once,
        // so it is removed shortly after.
        internal static Transform MakePlayerTarget(ResolvedStart selected, Vector3 player)
        {
            var marker = new GameObject("New Beginnings at-sea player target").transform;
            marker.SetParent(selected.Saveable.transform, false);
            marker.localPosition = player + Vector3.up * 1.05f;
            var bow = Vector3.ProjectOnPlane(selected.Saveable.transform.forward, Vector3.up);
            marker.rotation = bow.sqrMagnitude > .01f ? Quaternion.LookRotation(bow) : Quaternion.identity;
            UnityEngine.Object.Destroy(marker.gameObject, 30f);
            return marker;
        }

        private sealed class Cargo
        {
            internal SavePrefabData Data;
            internal bool Packable, Rum;
        }

        // Hands the chosen equipment to the boat's native saved-item cache, the
        // same path a saved game uses to put cargo back aboard. Sailwind spawns
        // it while the hull is held still and owns it from then on. Small items
        // go into crates the way a saved crate's contents are restored.
        internal static void Stow(ResolvedStart selected, Dictionary<string, int> equipment, DeckSurvey survey, bool fillWithRum)
        {
            var items = selected.Saveable.GetComponent<BoatLocalItems>();
            if (items == null) { Plugin.Instance?.Warn("The starting boat has no native item cache; no deck cargo was added."); return; }
            // Each floor takes stacked items up to its headroom; open deck takes three high.
            // Each floor fills, stacked as its headroom allows, before the next
            // floor up; Slots are already lowest floor first.
            var places = survey.Slots.Select((slot, index) => new { slot, index })
                .SelectMany(entry => Enumerable.Range(0, 3)
                    .Where(layer => layer == 0 || FirstLayerHeight + layer * LayerHeight + .6f <= entry.slot.Headroom)
                    .Select(layer => new { entry.slot.Point, entry.index, Layer = layer }))
                .OrderBy(place => Mathf.Round(place.Point.y / .5f)).ThenBy(place => place.Layer).ThenBy(place => place.index)
                .Select(place => place.Point + Vector3.up * (FirstLayerHeight + place.Layer * LayerHeight)).ToList();
            if (places.Count == 0) places.Add(survey.Player + Vector3.up * FirstLayerHeight + Vector3.forward);
            var cargo = new List<Cargo>();
            if (equipment.Values.Sum(value => (long)value) > MaximumDeckItems)
                Plugin.Instance?.Warn($"More than {MaximumDeckItems} items were chosen for an at-sea start; only the first {MaximumDeckItems} are stowed.");
            foreach (var pair in equipment.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var data = Template(pair.Key, out var packable);
                if (data == null) continue;
                for (var count = 0; count < pair.Value && cargo.Count < MaximumDeckItems; count++)
                    cargo.Add(new Cargo { Data = Copy(data), Packable = packable });
            }
            var carrier = AdditionalEquipment.CarrierPrefab(out var capacity);
            var carrierIndex = carrier != null ? carrier.GetComponent<SaveablePrefab>()?.prefabIndex ?? -1 : -1;
            var loose = cargo.Where(item => item.Packable).ToList();
            var crates = new List<Cargo>();
            if (capacity > 0 && carrierIndex >= 0 && loose.Count > 0)
            {
                cargo.RemoveAll(item => item.Packable);
                var used = new HashSet<int>(SaveablePrefab.existingInstanceIds ?? new List<int>());
                var random = new System.Random();
                for (var index = 0; index < loose.Count; index += capacity)
                {
                    int id;
                    do id = random.Next(1, int.MaxValue); while (!used.Add(id));
                    var crate = new Cargo { Data = new SavePrefabData(Vector3.zero, Quaternion.identity, carrierIndex, true, false,
                        carrier.GetComponent<ShipItem>().health, 0f, -1, 0, -1, 0, 0, id) };
                    crates.Add(crate);
                    foreach (var item in loose.Skip(index).Take(capacity)) item.Data.crateId = id;
                }
            }
            var rumCount = 0;
            if (fillWithRum)
            {
                var rum = Template(StartLoadout.RumBarrelKey, out _);
                var free = Math.Min(MaximumRumBarrels, survey.Slots.Count - cargo.Count - crates.Count);
                for (; rum != null && rumCount < free; rumCount++) cargo.Add(new Cargo { Data = Copy(rum), Rum = true });
            }
            // The lowest places go first: rum and water mixed together for a
            // drunken hold, then barrels and other large items, then crates.
            var shuffle = new System.Random();
            var order = cargo.Where(item => item.Rum || fillWithRum && item.Data.prefabIndex == WaterBarrelIndex)
                .OrderBy(item => shuffle.Next())
                .Concat(cargo.Where(item => !(item.Rum || fillWithRum && item.Data.prefabIndex == WaterBarrelIndex)))
                .Concat(crates).ToList();
            var rotation = selected.Saveable.transform.rotation;
            if (order.Count > places.Count)
                Plugin.Instance?.Warn($"The boat has room for {places.Count} stacked items; {order.Count - places.Count} were left out.");
            // Crates go last, so a crate left out takes its contents with it.
            var placed = order.Take(places.Count).ToList();
            for (var index = 0; index < placed.Count; index++)
                Add(items, selected, placed[index].Data, places[index], rotation);
            // Packed items load at their crate and are put inside it natively.
            foreach (var item in loose.Where(item => item.Data.crateId != 0))
            {
                var crate = placed.FirstOrDefault(entry => entry.Data.instanceId == item.Data.crateId);
                if (crate != null) Add(items, selected, item.Data, crate.Data.position, rotation);
            }
            items.SetItemsLoaded(false);
            Plugin.Instance?.Report($"At-sea cargo: {placed.Count} items and crates ({loose.Count(item => item.Data.crateId != 0)} packed in {crates.Count} crates, {rumCount} rum barrels) on {survey.Slots.Count} floor points.");
        }

        private const int WaterBarrelIndex = 10;

        private static void Add(BoatLocalItems items, ResolvedStart selected, SavePrefabData data, Vector3 place, Quaternion rotation)
        {
            data.position = place;
            data.rotation = rotation;
            data.itemParentObject = selected.Saveable.sceneIndex;
            items.AddCachedItemFromSaveData(data);
        }

        // Undo Stow on a start cancelled before its cargo spawned, so a later
        // save load or start does not find these records in the boat's cache.
        internal static Action Snapshot(ResolvedStart selected)
        {
            var items = selected.Saveable != null ? selected.Saveable.GetComponent<BoatLocalItems>() : null;
            if (items == null || CachedItemsField == null || ItemsLoadedField == null) return () => { };
            var cached = CachedItemsField.GetValue(items) as List<SavePrefabData>;
            var previous = cached != null ? new List<SavePrefabData>(cached) : null;
            var loaded = (bool)ItemsLoadedField.GetValue(items);
            return () =>
            {
                if (items == null) return;
                CachedItemsField.SetValue(items, previous);
                ItemsLoadedField.SetValue(items, loaded);
            };
        }

        private static SavePrefabData Template(string key, out bool packable)
        {
            packable = false;
            if (key == AdditionalEquipment.WallHooksKey)
            {
                Plugin.Instance?.Warn("A box of wall hooks cannot be stowed at sea; it was left out.");
                return null;
            }
            var prefab = AdditionalEquipment.ResolveKey(key, out var entry);
            var item = prefab != null ? prefab.GetComponent<ShipItem>() : null;
            var saveable = prefab != null ? prefab.GetComponent<SaveablePrefab>() : null;
            if (item == null || saveable == null)
            {
                Plugin.Instance?.Warn("Starting equipment " + key + " is unavailable and was left out.");
                return null;
            }
            float health = item.health, amount = item.amount;
            if (entry.Water.HasValue && item is ShipItemBottle bottle)
            {
                amount = entry.Water.Value ? 1f : 0f;
                health = entry.Water.Value ? bottle.GetCapacity() : 0f;
            }
            packable = AdditionalEquipment.CanPackPrefab(prefab, entry);
            return new SavePrefabData(Vector3.zero, Quaternion.identity, saveable.prefabIndex, true, false,
                health, amount, -1, 0, -1, 0, 0, 0);
        }

        private static SavePrefabData Copy(SavePrefabData data) =>
            new SavePrefabData(Vector3.zero, Quaternion.identity, data.prefabIndex, true, false,
                data.itemHealth, data.itemAmount, -1, 0, -1, 0, 0, 0);

        // Native recovery returns the boat to the last visited port.
        internal static Port NearestRecoveryPort(Vector3 position, IEnumerable<RecoveryPort> ports) =>
            ports?.Where(port => port != null && port.parentPort != null && port.boatPos != null)
                .OrderBy(port => (port.transform.position - position).sqrMagnitude)
                .Select(port => port.parentPort).FirstOrDefault();
    }

    // Drunkenness set once when the player takes control of a scenario start.
    internal static class ScenarioAlcohol
    {
        private static float pending = -1f;

        internal static void Arm(float alcohol) => pending = alcohol > 0f ? alcohol : -1f;
        internal static void Cancel() => pending = -1f;

        internal static void Tick()
        {
            if (pending < 0f || !GameState.playing || GameState.currentlyLoading) return;
            PlayerNeeds.alcohol = pending;
            Plugin.Instance?.Report($"Scenario drunkenness set to {pending:F0}.");
            pending = -1f;
        }
    }
}
