using UnityEngine;

namespace NewBeginnings
{
    // Native item bodies can exist while their visible ShipItem is inactive.
    // Collider.bounds is empty then. Project the authored physical shapes into
    // the requested item pose without activating or moving either object.
    internal static class StarterCargoGeometry
    {
        internal static bool TryBounds(ShipItem item, Quaternion rotation, out Bounds bounds)
        {
            bounds = default;
            if (item == null) return false;
            var body = item.GetItemRigidbody();
            var root = body != null && (body.GetComponent<BoxCollider>() != null ||
                body.GetComponent<MeshCollider>() != null || body.GetComponent<CapsuleCollider>() != null)
                ? body.transform : item.transform;
            var turn = rotation * Quaternion.Inverse(root.rotation);
            var found = false;
            foreach (var collider in root.GetComponents<Collider>())
                Include(collider, root, item.transform.position, turn, ref bounds, ref found);
            foreach (Transform child in root)
            {
                // ItemRigidbody.Start copies only these authored child objects.
                if (root == item.transform && !child.CompareTag("ItemSubcollider")) continue;
                foreach (var collider in child.GetComponentsInChildren<Collider>(true))
                    Include(collider, root, item.transform.position, turn, ref bounds, ref found);
            }
            return found;
        }

        internal static bool IsUprightBox(ShipItem item, Quaternion rotation)
        {
            if ((rotation * Vector3.up).y < .995f) return false;
            return SimpleBox(item) != null;
        }

        internal static bool TryTabletopRotation(ShipItem item, Quaternion rotation,
            out Quaternion stackRotation)
        {
            stackRotation = rotation;
            var box = SimpleBox(item);
            if (box == null) return false;
            var axes = new[] { rotation * Vector3.right, rotation * Vector3.up, rotation * Vector3.forward };
            var size = Vector3.Scale(box.size, box.transform.lossyScale);
            var lengths = new[] { Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z) };
            var vertical = -1;
            for (var i = 0; i < axes.Length; i++)
                if (Mathf.Abs(axes[i].y) >= .995f) vertical = i;
            if (vertical < 0) return false;
            var longest = (vertical + 1) % 3;
            var other = (vertical + 2) % 3;
            if (lengths[other] > lengths[longest]) longest = other;
            var along = axes[longest];
            along.y = 0f;
            stackRotation = Quaternion.LookRotation(Vector3.Cross(along, Vector3.up));
            return true;
        }

        private static BoxCollider SimpleBox(ShipItem item)
        {
            var body = item.GetItemRigidbody();
            var root = body != null && body.GetComponent<BoxCollider>() != null ? body.transform : item.transform;
            var box = root.GetComponent<BoxCollider>();
            if (box == null || box.isTrigger) return null;
            // A simple flat crate can carry another box. Compound bodies,
            // barrels and authored angled boxes keep ordinary ground placement.
            foreach (var collider in root.GetComponents<Collider>())
                if (collider != box && !collider.isTrigger) return null;
            foreach (Transform child in root)
            {
                if (root == item.transform && !child.CompareTag("ItemSubcollider")) continue;
                foreach (var collider in child.GetComponentsInChildren<Collider>(true))
                    if (!collider.isTrigger) return null;
            }
            return box;
        }

        private static void Include(Collider collider, Transform root, Vector3 position,
            Quaternion turn, ref Bounds result, ref bool found)
        {
            Bounds local;
            if (collider is BoxCollider box) local = new Bounds(box.center, box.size);
            else if (collider is MeshCollider mesh && mesh.sharedMesh != null) local = mesh.sharedMesh.bounds;
            else if (collider is CapsuleCollider capsule)
            {
                var scale = collider.transform.lossyScale;
                scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                var axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                var along = capsule.direction == 0 ? scale.x : capsule.direction == 1 ? scale.y : scale.z;
                var across = capsule.direction == 0 ? Mathf.Max(scale.y, scale.z) :
                    capsule.direction == 1 ? Mathf.Max(scale.x, scale.z) : Mathf.Max(scale.x, scale.y);
                var radius = capsule.radius * across;
                var segment = Mathf.Max(0f, capsule.height * along * .5f - radius);
                var center = position + turn * (collider.transform.TransformPoint(capsule.center) - root.position);
                var direction = turn * collider.transform.rotation * axis * segment;
                var extent = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z)) + Vector3.one * radius;
                Add(new Bounds(center, extent * 2f), ref result, ref found);
                return;
            }
            else if (collider is SphereCollider sphere)
            {
                // Native ItemSubcollider copies can use a sphere.
                var scale = collider.transform.lossyScale;
                var radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                var center = position + turn * (collider.transform.TransformPoint(sphere.center) - root.position);
                Add(new Bounds(center, Vector3.one * radius * 2f), ref result, ref found);
                return;
            }
            else return;

            var projected = new Bounds();
            for (var i = 0; i < 8; i++)
            {
                var corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                var point = position + turn * (collider.transform.TransformPoint(corner) - root.position);
                if (i == 0) projected = new Bounds(point, Vector3.zero);
                else projected.Encapsulate(point);
            }
            Add(projected, ref result, ref found);
        }

        private static void Add(Bounds bounds, ref Bounds result, ref bool found)
        {
            if (!found) result = bounds;
            else result.Encapsulate(bounds);
            found = true;
        }
    }
}
