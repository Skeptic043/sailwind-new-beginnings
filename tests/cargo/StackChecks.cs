using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace NewBeginnings
{
    internal static partial class StarterCargo
    {
        static ShipItem Crate(float height = .462466f, float width = 1.05562f, float depth = .626068f)
        {
            var i = new GameObject().Add<ShipItemCrate>();
            i.gameObject.name = "104 crate of fishing hooks";
            i.transform.position = new(1000, 20, 1000);
            Box(Body(i), new(.001874f, .174686f, 0), new(width, height, depth));
            i.gameObject.Add<SaveablePrefab>();
            return i;
        }

        static ShipItem GroundItem(string name, Vector3 size)
        {
            var i = Item();
            i.gameObject.name = name;
            i.transform.position = new(1000, 20, 1000);
            Box(Body(i), Vector3.zero, size);
            i.gameObject.Add<SaveablePrefab>();
            return i;
        }

        static void Reset()
        {
            Captured.Clear();
            AuthoredPositions.Clear();
            AuthoredRotations.Clear();
            AdditionalEquipment.Packed.Clear();
            AdditionalEquipment.Additional.Clear();
            AdditionalEquipment.MovedPacked = 0;
            Physics.obstacles.Clear();
            Physics.Queries = 0;
            ItemRigidbody.Moved.Clear();
            Plugin.Instance = new();
            armed = new();
            placementClaimed = false;
            rosterFinalized = false;
            planningShapes = null;
            Time.realtimeSinceStartup = 0;
        }

        static void AddItem(ShipItem i, bool packed = false)
        {
            Captured.Add(i);
            AuthoredPositions[i] = new(1000, 20, 1000);
            AuthoredRotations[i] = i.Planned;
            if (packed)
                AdditionalEquipment.Packed.Add(i);
        }

        static Bounds At(ShipItem i)
        {
            StarterCargoGeometry.TryBounds(i, i.transform.rotation, out var b);
            return b;
        }

        static Vector3 GroundOf(ShipItem item) => new((float)Math.Round(At(item).center.x, 3), 0, (float)Math.Round(At(item).center.z, 3));
        static bool Above(ShipItem item, ShipItem support)
        {
            var a = At(item);
            var b = At(support);
            return a.min.y >= b.max.y + .039f && a.min.x >= b.min.x - .002f && a.max.x <= b.max.x + .002f && a.min.z >= b.min.z - .002f && a.max.z <= b.max.z + .002f;
        }

        static ShipItem NativeBoxItem(string name, Vector3 size, Vector3 center)
        {
            var item = GroundItem(name, size);
            item.GetItemRigidbody().GetComponent<BoxCollider>().center = center;
            AddItem(item);
            return item;
        }

        static ShipItem[] NativeBarrelTableSet()
        {
            // Unit-scale installed authored BoxCollider shapes from fort-followup-20261013-002201802/cargo/items.json.
            var barrelSize = new Vector3(.66f, 1.07193005f, .66f);
            var barrelCenter = new Vector3(-.0001004934f, .529638f, .0015565157f);
            return new[]
            {
                NativeBoxItem("water barrel", barrelSize, barrelCenter),
                NativeBoxItem("table", new(.7825261f, 1.209702f, .593302f), new(0, 0, -.201366f)),
                NativeBoxItem("extra water barrel", barrelSize, barrelCenter)
            };
        }

        static void RunAnchorChecks()
        {
            Reset();
            Plugin.Instance.HalfWidth = 1.1f;
            Plugin.Instance.HalfDepth = 1.9f;
            var items = NativeBarrelTableSet();
            Check(TryChooseGround(Vector3.zero, Plugin.Instance.Ground, items[0], null, out var nearest, out _), "nearest native barrel anchor missing");
            Check(TryChooseGround(Vector3.zero, Plugin.Instance.Ground, items[1], nearest, out var tableFallback, out _) && !HasSupportFootprint(items[1], tableFallback), "center-supported table at dock edge should remain usable without full footprint approval");
            Check(items.All(i => i.GetItemRigidbody().ResetCalls == 0 && Near(i.transform.position, new(1000, 20, 1000))), "planning the blocked first anchor moved native items");
            Check(TryPlaceAll(armed, Vector3.zero, Plugin.Instance.Ground, out var reason), "another complete anchor layout was not tried: " + reason);
            Check(reason == null, "permissive layout retained a failure reason");
            Check(items.All(i => i.GetItemRigidbody().ResetCalls == 1), "alternative layout moved an item more than once");
            for (int a = 0; a < items.Length; a++)
                for (int b = a + 1; b < items.Length; b++)
                    Check(!At(items[a]).Intersects(At(items[b])), "alternate native supplies initially overlap");
            Check(planningShapes == null && items.All(i => i.GetItemRigidbody().ResetCalls == 1), "alternate plan did not finish all native items or leaked its shape cache");
            Console.WriteLine($"Cramped2.2x3.8m dock: blocked anchor {nearest}, successful anchor {items[0].transform.position}; {Plugin.Instance.Probes} surface probes/{Physics.Queries} overlap queries.");
            // A missing native body still prevents any movement and fails in this frame.
            Reset();
            items = NativeBarrelTableSet();
            items[2].Body = null;
            var operation = PlaceCapturedCore(armed);
            Check(!operation.MoveNext() && Plugin.Instance.Failures == 1, "missing native body stayed in a timed retry loop");
            var probes = Plugin.Instance.Probes;
            Check(!operation.MoveNext() && Plugin.Instance.Probes == probes, "failed alternative planning continued on later frames");
            Check(items.All(i => (i.GetItemRigidbody()?.ResetCalls ?? 0) == 0 && Near(i.transform.position, new(1000, 20, 1000))) && items.All(i => (i.GetItemRigidbody()?.ResetCalls ?? 0) == 0), "failed native body validation partially moved the roster");
            Check(probes < 600 && planningShapes == null, "failed alternative search was unbounded or leaked its cache");
            // A native move failure is past the planning boundary and must never choose another layout.
            Reset();
            items = NativeBarrelTableSet();
            items[0].GetItemRigidbody().ThrowOnReset = true;
            Check(!TryPlaceAll(armed, Vector3.zero, Plugin.Instance.Ground, out reason), "injected native move failure was hidden");
            Check(items[0].GetItemRigidbody().ResetCalls == 1 && items.Skip(1).All(i => i.GetItemRigidbody().ResetCalls == 0), "native mutation failure restarted whole-layout placement");
        }

        static void RunSupplyPriorityChecks(bool goat = false, bool dockEdge = false)
        {
            Reset();
            if (dockEdge)
                Plugin.Instance.Surface = DockSurfaces.Edge;
            var barrel = NativeBoxItem("10 barrel water", new(.66f, 1.07193005f, .66f), new(-.00010049f, .529638f, .0015565f));
            var table = NativeBoxItem("64 table M small", new(.7825261f, 1.209702f, .593302f), new(0, 0, -.201366f));
            table.Planned = new(-.566421568f, .426947892f, .434058309f, .555405736f);
            AuthoredRotations[table] = table.Planned;
            var carriers = Enumerable.Range(0, 13).Select(_ => Crate()).ToArray();
            foreach (var item in carriers)
            {
                AddItem(item);
                AdditionalEquipment.Additional.Add(item);
            }

            // Difficulty adds the larger native food crate after NB's additional equipment.
            var cheese = Crate(.709837139f, 1.269517899f, 1.028908968f);
            cheese.gameObject.name = goat ? "8 crate goat cheese (good)" : "7 crate cheese (good)(Clone)";
            cheese.GetItemRigidbody().GetComponent<BoxCollider>().center = new(-.000393003f, .354195535f, .001135479f);
            cheese.Planned = goat ? new(-.863282084f, .000000271f, .50472188f, -.000000463f) : Quaternion.Euler(0, 75.478f, 0);
            AddItem(cheese);
            for (int n = 0; n < 135; n++)
            {
                var item = GroundItem(n < 120 ? "79 lamp hanger generic" : n < 125 ? "133 lantern M big" : new[] { "102 mug metal(Clone)", "114 lantern M", "156 pot", "159 hammer", "517 anemometer 1", "82 compass M", "91 scroll tutorial M", "95 fishing rod 1", "Sailwind Radio item 43040", "Sailwind Radio item 43041" }[n - 125], new(.1f, .1f, .1f));
                AddItem(item, true);
            }

            Check(Captured.Count == 151 && AdditionalEquipment.Packed.Count == 135, "actual configured151-item roster differs");
            // Native physics can tip a remote starter before cargo gets its first planning pass.
            cheese.Planned = Quaternion.Euler(35, 75.478f, 0);
            Check(StarterCargoGeometry.IsUprightBox(cheese, PlacementRotation(cheese)), "captured native cheese pose was lost to transient physics");
            Check(TryPlaceAll(armed, Vector3.zero, Plugin.Instance.Ground, out var reason), "actual151-item native cheese/table roster failed: " + reason);
            Check(ItemRigidbody.Moved.Take(3).All(i => !AdditionalEquipment.IsAdditional(i)) && ItemRigidbody.Moved[2] == cheese, "added equipment consumed ground before the last-created native cheese");
            var onTable = carriers.Where(i => Above(i, table)).ToArray();
            var onCheese = carriers.Where(i => Above(i, cheese)).ToArray();
            Check(onTable.Length > 0, "native tabletop was not used for fitting carrier boxes");
            Check(onCheese.Length > 0, "larger native food crate did not support added carrier boxes");
            Check(onTable.All(i => At(i).min.y >= At(table).max.y + .039f) && onCheese.All(i => At(i).min.y >= At(cheese).max.y + .039f), "supported boxes initially penetrate native furniture/food crate");
            var groundObjects = new[] { barrel, table, cheese }.Concat(carriers).ToArray();
            for (int a = 0; a < groundObjects.Length; a++)
                for (int b = a + 1; b < groundObjects.Length; b++)
                    Check(!At(groundObjects[a]).Intersects(At(groundObjects[b])), "actual native roster overlaps " + AuthoredName(groundObjects[a]) + " and " + AuthoredName(groundObjects[b]));
            Check(groundObjects.All(i => new Vector2(GroundOf(i).x, GroundOf(i).z).magnitude < (dockEdge ? 4.251f : 3.7f)), "actual151-item loadout expanded its tightened placement radius");
            Check(groundObjects.GroupBy(i => GroundOf(i).ToString()).All(g => g.Count() <= 3), "column exceeded three total items including its native support");
            Check(AdditionalEquipment.MovedPacked == 135, "real packed roster was lost");
            Console.WriteLine($"Measured-shape 151-item fixture ({AuthoredName(cheese)}, synthetic dock edge={dockEdge}):16ground/135packed, {onTable.Length} carriers on native table/{onCheese.Length} on native cheese; maximum ground distance {groundObjects.Max(i => new Vector2(GroundOf(i).x, GroundOf(i).z).magnitude):F2}m; {Plugin.Instance.Probes} surface probes/{Physics.Queries} overlap queries.");
        }

        static void RunRoofedDockChecks()
        {
            Reset();
            Plugin.Instance.Surface = DockSurfaces.WithRoof;
            var barrel = NativeBoxItem("10 barrel water", new(.66f, 1.07193005f, .66f), new(-.00010049f, .529638f, .0015565f));
            barrel.Planned = Quaternion.Euler(0, 35.485f, 0);
            AuthoredRotations[barrel] = barrel.Planned;
            var table = NativeBoxItem("68 table E small", new(.847452044f, 1.233042002f, .509570003f), new(0, 0, -.243232012f));
            table.Planned = new(.283646673f, .655487299f, .647722542f, -.265210360f);
            AuthoredRotations[table] = table.Planned;
            var salmon = Crate(.709837139f, 1.269517899f, 1.028908968f);
            salmon.gameObject.name = "1 crate salmon (E)";
            salmon.GetItemRigidbody().GetComponent<BoxCollider>().center = new(-.000393003f, .354195535f, .001135479f);
            salmon.Planned = new(-.933722496f, .000000737f, -.357997745f, -.000002203f);
            AddItem(salmon);
            var oar = NativeBoxItem("168 oar", new(.23f, 2.283262014f, .04f), new(0, -.325564027f, -.000000499f));
            oar.Planned = new(.034767378f, .954247057f, -.107533716f, .276840001f);
            AuthoredRotations[oar] = oar.Planned;
            oar.IsOar = true;
            Box(oar.transform, new(0, -.325564027f, -.000000499f), new(.23f, 2.283262014f, .04f));
            var carriers = Enumerable.Range(0, 13).Select(_ => Crate()).ToArray();
            foreach (var i in carriers)
            {
                AddItem(i);
                AdditionalEquipment.Additional.Add(i);
            }

            for (int n = 0; n < 135; n++)
                AddItem(GroundItem(n < 120 ? "lamp hanger" : "packed native/tool", new(.1f, .1f, .1f)), true);
            Check(Captured.Count == 152, "roofed dock captured roster differs");
            var success = TryPlaceAll(armed, Vector3.zero, Plugin.Instance.Ground, out var reason);
            Console.WriteLine($"Synthetic roofed dock, 152-item fixture success={success}; {reason}; probes={Plugin.Instance.Probes}, overlaps={Physics.Queries}");
            Check(success, "Roofed dock fixture supplies failed: " + reason);
            Check(ItemRigidbody.Moved.Count == 17 && AdditionalEquipment.MovedPacked == 135, "roofed dock roster not preserved");
            var ground = new[] { barrel, table, salmon, oar }.Concat(carriers).ToArray();
            Check(ground.All(item => DockSurfaces.WithRoof(GroundOf(item)).HasValue), "supplies were placed outside the dock or beneath its occluded roof");
            Check(ground.GroupBy(i => GroundOf(i).ToString()).All(g => g.Count() <= 3), "roofed dock column exceeds3total items");
            Check(ground.All(i => GroundOf(i).magnitude <= 4.251f), "roofed dock layout exceeds4.25m");
            Check(Plugin.Instance.Probes < 300 && Physics.Queries < 140, "roofed dock layout repeated exhaustive planning");
            Console.WriteLine($"Roofed dock maximum ground distance {ground.Max(i => GroundOf(i).magnitude):F2}m; measured item shapes, synthetic roof occlusion and production oar base used.");
            for (int x = 0; x < ground.Length; x++)
                for (int z = x + 1; z < ground.Length; z++)
                    Check(!At(ground[x]).Intersects(At(ground[z])), "roofed dock initial bounds overlap");
        }

        static void RunBestEffortChecks()
        {
            // Only the already resolved player landing has support. No strict anchor or
            // complete4.25m dock layout fits, but the real sold roster must still arrive.
            Reset();
            Plugin.Instance.Surface = p => new Vector2(p.x, p.z).magnitude < .05f ? new Vector3(p.x, 0, p.z) : null;
            var loose = Enumerable.Range(0, 17).Select(n => n == 0 ? GroundItem("water barrel", new(.7f, 1.1f, .7f)) : Crate(.45f, 1.05f, .63f)).ToArray();
            foreach (var item in loose)
            {
                AddItem(item);
                item.GetComponent<SaveablePrefab>().SetParentObject(37);
                item.GetItemRigidbody().attached = true;
            }

            loose[0].currentActualBoat = new();
            for (int n = 0; n < 128; n++)
                AddItem(GroundItem("packed original item", new(.1f, .1f, .1f)), true);
            var selected = armed;
            var identities = Captured.ToArray();
            Check(!TryChooseGround(Vector3.zero, Plugin.Instance.Ground, loose[0], null, out _, out _), "forced cramped fixture unexpectedly has a strict anchor");
            var operation = PlaceCapturedCore(selected);
            Check(!operation.MoveNext(), "best-effort placement waited for a full-roster settling check");
            Check(Plugin.Instance.Failures == 0 && armed == null && Captured.SetEquals(identities), "fallback failed or replaced native identities");
            Check(loose.All(i => i.GetItemRigidbody().ResetCalls == 1) && ItemRigidbody.Moved.Count == 17, "fallback omitted or moved a native loose item more than once");
            Check(AdditionalEquipment.MovedPacked == 128, "fallback lost packed native identities");
            Check(loose.All(i => i.currentActualBoat == null && i.GetComponent<SaveablePrefab>().GetParentObject() == -1 && i.transform.parent == FloatingOriginManager.instance.transform && i.GetItemRigidbody().transform.parent == FloatingOriginManager.instance.transform && !i.GetItemRigidbody().attached), "fallback changed native ownership/world/save parenting");
            Check(identities.All(i => !i.GetItemRigidbody().OutOfRange && i.GetItemRigidbody().DestroyFrames == 0 && i.GetItemRigidbody().DistanceTimer == 0), "fallback retained distant native cleanup state");
            for (int a = 0; a < loose.Length; a++)
                for (int b = a + 1; b < loose.Length; b++)
                    Check(!At(loose[a]).Intersects(At(loose[b])), "best-effort actual cargo bounds initially overlap");
            Check(loose.All(i => new Vector2(At(i).center.x, At(i).center.z).magnitude < 4.25f), "forced unsupported cargo unnecessarily spread far from the landed player");
            Console.WriteLine($"Forced no-slot fixture:145 original identities,17 loose/128 packed, all moved once and caches reset; max loose distance {loose.Max(i => new Vector2(At(i).center.x, At(i).center.z).magnitude):F2}m, no initial cargo overlap or post-settle retry.");
            // A later wide item cannot fit the strict plan. Retain its valid first barrel
            // position and append the missing real item beyond the preferred radius.
            Reset();
            Plugin.Instance.Surface = p => new Vector2(p.x, p.z).magnitude < .05f ? new Vector3(p.x, 0, p.z) : null;
            var barrel = GroundItem("water barrel", new(.7f, 1, .7f));
            AddItem(barrel);
            Plugin.Instance.Surface = null;
            Check(TryChooseGround(Vector3.zero, Plugin.Instance.Ground, barrel, null, out var expectedBarrel, out _), "partial plan has no strict first anchor");
            var wide = GroundItem("wide real mod item", new(9, 1, 9));
            AddItem(wide);
            Check(TryPlaceAll(armed, Vector3.zero, Plugin.Instance.Ground, out _) && wide.GetItemRigidbody().ResetCalls == 1, "preferred radius withheld a large valid item");
            Check(Near(barrel.transform.position.x, expectedBarrel.x) && Near(barrel.transform.position.z, expectedBarrel.z) && barrel.GetItemRigidbody().ResetCalls == 1 && !At(barrel).Intersects(At(wide)), "partial strict plan was discarded or overlapped by fallback");
            Check(Plugin.Instance.Messages.Any(s => s.Contains("best-effort")), "wide later item did not exercise incomplete strict-plan completion");
            // Floating-origin-sized world coordinates and offset collider centers must
            // preserve the same local separation without a retry or replacement object.
            Reset();
            var landing = new Vector3(120000, 7, -85000);
            Plugin.Instance.Surface = p => new Vector2(p.x - landing.x, p.z - landing.z).magnitude < .05f ? new Vector3(p.x, landing.y, p.z) : null;
            var translated = Enumerable.Range(0, 7).Select(_ => Crate()).ToArray();
            foreach (var item in translated)
                AddItem(item);
            Check(TryPlaceAll(armed, landing, Plugin.Instance.Ground, out _) && translated.All(i => i.GetItemRigidbody().ResetCalls == 1), "translated-coordinate best effort withheld native items");
            for (int a = 0; a < translated.Length; a++)
                for (int b = a + 1; b < translated.Length; b++)
                    Check(!At(translated[a]).Intersects(At(translated[b])), "translated-coordinate fallback bounds overlap");
            Check(translated.All(i => (At(i).center - landing).magnitude < 4), "translated fallback used world origin instead of player landing");
            // An adjacent plank winning the refresh raycast is not a supply veto.
            Reset();
            var plankItem = Crate();
            AddItem(plankItem);
            Plugin.Instance.ProbeCollider = new GameObject().Add<Collider>();
            Check(TryPlaceAll(armed, Vector3.zero, Plugin.Instance.Ground, out _) && plankItem.GetItemRigidbody().ResetCalls == 1, "different usable dock plank withheld the full load");
            // A failed refresh retains the usable landing already resolved by the caller.
            Reset();
            plankItem = Crate();
            AddItem(plankItem);
            Plugin.Instance.GroundExists = false;
            Check(TryPlaceAll(armed, Vector3.zero, Plugin.Instance.Ground, out _) && plankItem.GetItemRigidbody().ResetCalls == 1, "missed ground refresh withheld the full load");
            // Missing native shape is a placement limitation, not permission to hide it.
            Reset();
            var unshaped = Item();
            unshaped.gameObject.name = "unmeasured native item";
            unshaped.transform.position = new(1000, 20, 1000);
            Body(unshaped);
            unshaped.gameObject.Add<SaveablePrefab>();
            AddItem(unshaped);
            Check(TryPlaceAll(armed, Vector3.zero, Plugin.Instance.Ground, out _) && unshaped.GetItemRigidbody().ResetCalls == 1, "unavailable geometry withheld a valid native body");
        }

        static void RunStackChecks()
        {
            RunRoofedDockChecks();
            Reset();
            var crates = Enumerable.Range(0, 13).Select(_ => Crate()).ToArray();
            foreach (var c in crates)
                AddItem(c);
            AddItem(GroundItem("water barrel", new(.65f, .9f, .65f)));
            AddItem(GroundItem("table", new(.8f, .65f, .7f)));
            AddItem(GroundItem("cheese", new(.65f, .45f, .4f)));
            for (int n = 0; n < 135; n++)
                AddItem(GroundItem(n < 120 ? "lamp hanger" : "packed tool", new(.1f, .1f, .1f)), true);
            var selected = armed;
            Check(Captured.Count == 151, "large fixture roster not151");
            Check(TryPlaceAll(selected, Vector3.zero, Plugin.Instance.Ground, out var reason), "151-item placement failed: " + reason);
            Check(AdditionalEquipment.MovedPacked == 135, "packed identities lost or placed as ground objects");
            var groups = crates.GroupBy(c => GroundOf(c).ToString()).ToArray();
            Check(groups.Length == 5, "13 crates required " + groups.Length + " ground columns");
            Check(groups.All(g => g.Count() <= 3), "too many boxes in one column");
            foreach (var group in groups)
            {
                var boxes = group.OrderBy(c => At(c).min.y).ToArray();
                Check(At(boxes[^1]).max.y < 2.101f, "column exceeds physical height limit");
                for (int n = 1; n < boxes.Length; n++)
                    Check(Near(At(boxes[n]).min.y - At(boxes[n - 1]).max.y, .04f), "stack has initial overlap or wrong gap");
            }

            var centers = groups.Select(g => At(g.First()).center).ToArray();
            Check(centers.Max(p => p.x) - centers.Min(p => p.x) < 3.6f && centers.Max(p => p.z) - centers.Min(p => p.z) < 3.6f, "13crate columns spread into a wide ring");
            var footprintWidth = centers.Max(p => p.x) - centers.Min(p => p.x);
            var footprintDepth = centers.Max(p => p.z) - centers.Min(p => p.z);
            Console.WriteLine($"Column-center footprint {footprintWidth:F2} x {footprintDepth:F2}m");
            Check(footprintWidth * footprintDepth < 9f, "crate columns spread around a ring instead of adjacent rows");
            Console.WriteLine($"151-item layout used {Plugin.Instance.Probes} surface probes and {Physics.Queries} overlap queries.");
            Check(Physics.Queries < 140 && Plugin.Instance.Probes < 300, "large layout still exhaustively reprobes occupied slots");
            Check(planningShapes == null, "shape cache leaked beyond planning");
            Reset();
            var bottom = Crate(.6f);
            var mid = Crate(.45f);
            var top = Crate(.8f);
            var extra = Crate(.2f);
            foreach (var c in new[] { bottom, mid, top, extra })
                AddItem(c);
            Check(TryPlaceAll(armed, Vector3.zero, Plugin.Instance.Ground, out reason), "varied height boxes failed: " + reason);
            Check(new[] { bottom, mid, top, extra }.All(c => At(c).max.y <= 2.101f), "varied height stack exceeded height limit");
            var all = new[] { bottom, mid, top, extra };
            for (int a = 0; a < all.Length; a++)
                for (int b = a + 1; b < all.Length; b++)
                    Check(!At(all[a]).Intersects(At(all[b])), "varied-height initial boxes overlap");
            Reset();
            bottom = Crate();
            var bounds = At(bottom);
            var column = NewCrateColumn(bottom, Vector3.zero, Quaternion.identity, bounds, Plugin.Instance.Ground);
            column.Top = .04f + bounds.size.y;
            column.Items.Add(bottom);
            var columns = new List<CrateColumn>
            {
                column
            };
            var next = Crate();
            Obstacle(new Vector3(0, .75f, 0), new(1.2f, .4f, .8f), true);
            Check(!TryStackCrate(next, columns, out _, out _, out _), "upper crate put inside elevated boat trigger");
            Physics.obstacles.Clear();
            Obstacle(new Vector3(0, .75f, 0), new(1.2f, .4f, .8f));
            Check(!TryStackCrate(next, columns, out _, out _, out _), "upper crate placed through overhead solid");
            Physics.obstacles.Clear();
            var barrel = GroundItem("barrel", Vector3.one);
            Check(!TryStackCrate(barrel, columns, out _, out _, out _), "noncrate stacked");
            var oar = GroundItem("oar", new(.1f, 2, .1f));
            oar.IsOar = true;
            Check(!TryStackCrate(oar, columns, out _, out _, out _), "oar stacked");
            next.Planned = Quaternion.Euler(30, 0, 0);
            Check(!TryStackCrate(next, columns, out _, out _, out _), "tilted crate stacked");
            next.Planned = Quaternion.identity;
            var child = new GameObject();
            child.transform.SetParent(next.GetItemRigidbody().transform);
            Box(child.transform, Vector3.zero, Vector3.one);
            Check(!TryStackCrate(next, columns, out _, out _, out _), "compound nonbox crate stacked");
            Reset();
            AddItem(Crate());
            Plugin.Instance.Ground = null;
            var operation = PlaceCapturedCore(armed);
            Check(!operation.MoveNext(), "failed plan remained in a30second retry loop");
            var probes = Plugin.Instance.Probes;
            Check(Plugin.Instance.Failures == 1 && !operation.MoveNext() && Plugin.Instance.Probes == probes, "failed plan was attempted repeatedly");
            Reset();
            foreach (var c in Enumerable.Range(0, 13).Select(_ => Crate()))
                AddItem(c);
            operation = PlaceCapturedCore(armed);
            int frames = 0;
            while (operation.MoveNext())
            {
                frames++;
                Time.realtimeSinceStartup += .02f;
            }

            Check(frames == 0 && Plugin.Instance.Failures == 0 && ItemRigidbody.Moved.Count == 13 && ItemRigidbody.Moved.All(i => i.GetItemRigidbody().ResetCalls == 1), "successful placement waited for whole-roster settling or retried movement");
            Reset();
            var distant = Crate();
            AddItem(distant);
            Plugin.Instance.Surface = p => new Vector2(p.x, p.z).magnitude > 4.25f ? new Vector3(p.x, 0, p.z) : null;
            Check(!TryChooseLooseGround(distant, Vector3.zero, Vector3.zero, new(), out _, out _), "loose item placed beyond the tightened4.25m distance");
            Console.WriteLine($"Stack fixture: 151 identities,135packed,13crates/5columns, physical gaps/heights, elevated triggers/solids, settling/ownership, excluded shapes and one-attempt failure behavior checked. Scene/native ownership seams are doubles.");
            RunBestEffortChecks();
            RunAnchorChecks();
            RunSupplyPriorityChecks();
            RunSupplyPriorityChecks(true);
            RunSupplyPriorityChecks(false, true);
            RunPartialSupportChecks();
        }
    }
}
