using System;
using System.Collections.Generic;
using UnityEngine;

namespace NewBeginnings
{
    internal static partial class StarterCargo
    {
        static readonly HashSet<ShipItem> Captured = new();
        static Quaternion CupUprightRotation(Quaternion rotation) => rotation;
        static int checks;
        static void Check(bool ok, string text)
        {
            if (!ok)
                throw new Exception(text);
            checks++;
        }

        static bool Near(float a, float b) => MathF.Abs(a - b) < .0015f;
        static bool Near(Vector3 a, Vector3 b) => (a - b).magnitude < .002f;
        static bool Near(Bounds a, Bounds b) => Near(a.center, b.center) && Near(a.size, b.size);
        static ShipItem Item(bool inactive = false)
        {
            var i = new GameObject().Add<ShipItem>();
            i.gameObject.activeSelf = !inactive;
            return i;
        }

        static ItemRigidbody Body(ShipItem i)
        {
            var b = new GameObject().Add<ItemRigidbody>();
            i.Body = b;
            b.Item = i;
            return b;
        }

        static BoxCollider Box(Component c, Vector3 center, Vector3 size)
        {
            var b = c.gameObject.Add<BoxCollider>();
            b.center = center;
            b.size = size;
            return b;
        }

        static Collider Obstacle(Vector3 center, Vector3 size, bool embark = false)
        {
            var c = new GameObject().Add<Collider>();
            c.rawWorldBounds = new(center, size);
            c.isTrigger = embark;
            if (embark)
                c.gameObject.tag = "EmbarkCol";
            Physics.obstacles.Add(c);
            return c;
        }

        static Bounds Bounds(ShipItem i)
        {
            Check(StarterCargoGeometry.TryBounds(i, i.Planned, out var b), "shape unavailable");
            return b;
        }

        static void Main()
        {
            // Installed carrier prefab104 exact BoxCollider dimensions, projected from a stale native body.
            var i = Item(true);
            i.transform.position = new(1000, 50, -2000);
            i.Planned = Quaternion.Euler(0, 90, 0);
            var body = Body(i);
            body.transform.position = new(-800, 600, 500);
            body.transform.rotation = Quaternion.Euler(0, 37, 0);
            body.gameObject.activeSelf = false;
            var box = Box(body, new(.001874f, .174686f, 0), new(1.05562f, .462466f, .626068f));
            var inactive = Bounds(i);
            Check(Near(inactive.center, new(1000, 50.174686f, -2000.001874f)) && Near(inactive.size, new(.626068f, .462466f, 1.05562f)), "inactive body box was not projected through the planned yaw");
            var radius = FootprintRadius(i);
            var floor = new Vector3(3, 1.8f, 5);
            var placed = PutBottomOnSurface(i, floor, i.Planned);
            Check(Near(placed.y, 1.8f + .462466f / 2 - .174686f + .04f), "inactive item was dropped from fallback height or embedded in floor");
            Check(IsInsideEmbarkVolume(i, floor, i.Planned) == false, "empty scene has an embark overlap");
            var query = Physics.LastQuery;
            Check(query.size.x > .6f && query.size.z > 1f && Near(query.center.x, 3), "inactive embark query collapsed or queried the old origin");
            Physics.obstacles.Clear();
            Obstacle(new(3.3f, 2.0f, 5), new(.15f, .4f, .2f));
            Check(!HasCargoClearance(i, floor, i.Planned, null), "inactive carrier accepted an intersecting fixture");
            var solidQuery = Physics.LastQuery;
            i.gameObject.activeSelf = true;
            body.gameObject.activeSelf = true;
            Check(Near(Bounds(i), inactive) && Near(radius, FootprintRadius(i)) && Near(placed, PutBottomOnSurface(i, floor, i.Planned)), "activation changed authored bounds, spacing or floor placement");
            Check(!HasCargoClearance(i, floor, i.Planned, null) && Near(solidQuery, Physics.LastQuery), "activation changed collision clearance query");
            box.enabled = false;
            Check(Near(Bounds(i), inactive), "temporarily disabled native collider lost its physical shape");
            Physics.obstacles.Clear();
            Obstacle(new(3.3f, 2.0f, 5), new(.15f, .4f, .2f), true);
            Check(IsInsideEmbarkVolume(i, floor, i.Planned), "crate inside embark volume was accepted");
            Check(HasCargoClearance(i, floor, i.Planned, null), "trigger was incorrectly treated as a solid fixture");
            var reserved = new List<GroundSlot>
            {
                new()
                {
                    Point = floor,
                    Radius = radius
                }
            };
            Check(!IsSlotClear(new() { Point = floor + Vector3.right * .6f, Radius = radius }, reserved), "two full carrier shapes still fit an overlapping0.6m spacing");
            Check(!IsSlotClear(new() { Point = floor + new Vector3(.6f, 0, 1f), Radius = radius }, reserved), "neighboring carrier corners overlap when centre spacing is reduced");
            Check(IsSlotClear(new() { Point = floor + Vector3.right * (2 * radius + .05f), Radius = radius }, reserved), "separate carrier shapes were rejected");
            // Native body's child collider offset, rotation and nonuniform scale survive its unrelated world pose.
            var j = Item(true);
            j.Planned = Quaternion.Euler(0, 90, 0);
            var jb = Body(j);
            jb.transform.position = new(80, 200, -40);
            jb.transform.rotation = Quaternion.Euler(0, -25, 0);
            Box(jb, Vector3.zero, Vector3.one * .2f);
            var child = new GameObject();
            child.transform.SetParent(jb.transform);
            child.transform.localPosition = new(4, 1, 0);
            child.transform.localRotation = Quaternion.Euler(0, 90, 0);
            child.transform.localScale = new(2, .5f, 1);
            Box(child.transform, new(.25f, .2f, .4f), new(1, 2, 3));
            var childBounds = Bounds(j);
            Check(Near(childBounds.min, new(-1.5f, -.1f, -5.9f)) && Near(childBounds.max, new(.5f, 1.6f, .1f)), "child offset, rotation or scale omitted");
            // Before native AddCollider runs, read supported authored geometry without activation.
            var k = Item(true);
            k.Planned = Quaternion.Euler(0, 90, 0);
            k.transform.localScale = new(2, .5f, 3);
            var mesh = k.gameObject.Add<MeshCollider>();
            mesh.sharedMesh = new()
            {
                bounds = new(new(.2f, .3f, .4f), new(1, 2, 3))
            };
            var meshBounds = Bounds(k);
            Check(Near(meshBounds.center, new(1.2f, .15f, -.4f)) && Near(meshBounds.size, new(9, 1, 2)), "authored mesh bounds or scale omitted");
            var cap = Item(true);
            cap.transform.localScale = new(3, 2, 4);
            var c = cap.gameObject.Add<CapsuleCollider>();
            c.radius = .2f;
            c.height = 2;
            c.direction = 1;
            var capsule = Bounds(cap);
            Check(Near(capsule.size, new(1.6f, 4, 1.6f)), "capsule radius did not use perpendicular scale");
            var nativeSub = new GameObject();
            nativeSub.tag = "ItemSubcollider";
            nativeSub.transform.SetParent(cap.transform);
            nativeSub.transform.localPosition = new(3, 0, 0);
            Box(nativeSub.transform, Vector3.zero, Vector3.one);
            Check(Bounds(cap).max.x > 10, "authored native subcollider missing before initialization");
            Check(!StarterCargoGeometry.TryBounds(Item(true), Quaternion.identity, out _), "missing shape fabricated bounds");
            RunStackChecks();
            Console.WriteLine($"PASS {checks} actual cargo geometry/placement checks: inactive/active, stale body, planned yaw, child offsets and scales, native shapes, floor, solid and embark volume, neighbor separation. Managed scene doubles; no live PhysX acceptance.");
        }
    }
}
