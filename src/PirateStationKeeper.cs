using UnityEngine;

namespace NewBeginnings
{
    // Runtime-only offshore hold. Ownership can be granted while the player is
    // still ashore; boarding, rather than purchase, hands the hull to physics.
    internal sealed class PirateStationKeeper : MonoBehaviour
    {
        private static PirateStationKeeper active;
        private Rigidbody body;
        private Transform frame;
        private Vector3 localPosition;
        private float localYaw;
        private bool releaseWhenPurchased;
        private bool allowInitialLoad;
        private bool sawReadyState;

        internal static PirateStationKeeper Attach(Rigidbody body, Transform frame,
            bool releaseWhenPurchased = false)
        {
            RemoveActive();
            var keeper = body.gameObject.AddComponent<PirateStationKeeper>();
            keeper.body = body; keeper.frame = frame;
            keeper.localPosition = frame.InverseTransformPoint(body.position);
            keeper.localYaw = Mathf.DeltaAngle(frame.eulerAngles.y, body.rotation.eulerAngles.y);
            keeper.releaseWhenPurchased = releaseWhenPurchased;
            active = keeper;
            return keeper;
        }

        internal void AllowInitialLoad() => allowInitialLoad = true;

        internal static void RemoveActive() => Remove(active);

        internal static void Remove(PirateStationKeeper keeper)
        {
            if (keeper == null) return;
            if (active == keeper) active = null;
            keeper.enabled = false;
            DestroyImmediate(keeper);
        }

        private void FixedUpdate()
        {
            if (GameState.currentlyLoading)
            {
                if (allowInitialLoad && !sawReadyState) return;
                Remove(this);
                return;
            }
            sawReadyState = true;
            var saleBoat = releaseWhenPurchased && body != null
                ? body.GetComponent<PurchasableBoat>() : null;
            if (Plugin.Instance == null || !Plugin.Instance.isActiveAndEnabled ||
                body == null || frame == null || saleBoat != null && saleBoat.isPurchased() ||
                (!releaseWhenPurchased && GameState.playing && GameState.currentBoat == body.transform))
            { Remove(this); return; }
            if (body.isKinematic) return;
            var target = frame.TransformPoint(localPosition);
            if (!Finite(target) || !Finite(body.position) || !Finite(body.velocity) || !Finite(body.angularVelocity))
            { Remove(this); return; }
            body.AddForce(HorizontalAcceleration(target - body.position, body.velocity), ForceMode.Acceleration);
            var error = Mathf.DeltaAngle(body.rotation.eulerAngles.y, frame.eulerAngles.y + localYaw) * Mathf.Deg2Rad;
            var acceleration = Mathf.Clamp(error * 0.2f - body.angularVelocity.y * 0.9f, -0.3f, 0.3f);
            if (Finite(acceleration)) body.AddTorque(0f, acceleration, 0f, ForceMode.Acceleration);
        }

        internal static Vector3 HorizontalAcceleration(Vector3 error, Vector3 velocity)
        {
            if (!Finite(error) || !Finite(velocity)) return Vector3.zero;
            error.y = 0f; velocity.y = 0f;
            var distance = error.magnitude;
            if (!Finite(distance) || distance <= 0.25f) return Vector3.zero;
            var direction = error / distance;
            var outwardSpeed = Mathf.Min(Vector3.Dot(velocity, direction), 0f);
            return Vector3.ClampMagnitude(direction * (distance - 0.25f - outwardSpeed * 2f), 1f);
        }

        internal static bool TryStowedPosition(Rigidbody hull, Rigidbody anchor, ConfigurableJoint joint,
            Quaternion rotation, out Vector3 position)
        {
            position = default(Vector3);
            if (hull == null || anchor == null || joint == null || joint.GetComponent<Rigidbody>() != anchor ||
                joint.connectedBody != hull || !Finite(rotation.x) || !Finite(rotation.y) ||
                !Finite(rotation.z) || !Finite(rotation.w)) return false;
            var endpoint = hull.transform.TransformPoint(joint.connectedAnchor);
            var pivot = rotation * Vector3.Scale(joint.anchor, anchor.transform.lossyScale);
            position = endpoint - pivot;
            return Finite(endpoint) && Finite(pivot) && Finite(position);
        }

        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
