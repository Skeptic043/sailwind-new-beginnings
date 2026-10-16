using UnityEngine;

namespace NewBeginnings
{
    // Holds every offshore start (Pirate, Saffron, Siren Song, Fort Juniper,
    // shipyard and best-effort holds) in its island frame. Ownership can be
    // granted while the player is still ashore; boarding, rather than
    // purchase, hands the hull to physics.
    internal sealed class OffshoreHold : MonoBehaviour
    {
        private static OffshoreHold active;
        private Rigidbody body;
        private Transform frame;
        private Vector3 localPosition;
        private float localYaw;
        private bool awaitingLoad;

        internal static OffshoreHold Attach(Rigidbody body, Transform frame)
        {
            RemoveActive();
            var keeper = body.gameObject.AddComponent<OffshoreHold>();
            keeper.body = body; keeper.frame = frame;
            keeper.localPosition = frame.InverseTransformPoint(body.position);
            keeper.localYaw = Mathf.DeltaAngle(frame.eulerAngles.y, body.rotation.eulerAngles.y);
            active = keeper;
            return keeper;
        }

        internal static void RemoveActive() => Remove(active);

        internal static void Restore(Rigidbody body, Transform frame, Vector3 position, float yaw)
        {
            var keeper = Attach(body, frame);
            keeper.localPosition = position;
            keeper.localYaw = yaw;
            keeper.awaitingLoad = true;
        }

        internal static bool TryGetHold(out Rigidbody heldBody, out Transform heldFrame,
            out Vector3 position, out float yaw)
        {
            heldBody = null; heldFrame = null; position = default(Vector3); yaw = 0f;
            if (active == null || active.body == null || active.frame == null) return false;
            // A save can run after boarding but before the next physics tick.
            if (GameState.playing && UserHasTakenControl(active.body))
            { RemoveActive(); return false; }
            heldBody = active.body; heldFrame = active.frame;
            position = active.localPosition; yaw = active.localYaw;
            return true;
        }

        internal static void Remove(OffshoreHold keeper)
        {
            if (keeper == null) return;
            if (active == keeper) active = null;
            keeper.enabled = false;
            DestroyImmediate(keeper);
        }

        private void FixedUpdate()
        {
            // Native LoadGame restores objects before its animation finishes
            // moving the world. Keep the saved target in its island frame.
            if (awaitingLoad && GameState.currentlyLoading) return;
            awaitingLoad = false;
            if (Plugin.Instance == null || !Plugin.Instance.isActiveAndEnabled ||
                GameState.currentlyLoading || body == null || frame == null ||
                (GameState.playing && UserHasTakenControl(body)))
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

        private static bool UserHasTakenControl(Rigidbody heldBody) =>
            // Native boarding stores the hull's immediate worldBoat child.
            // Ancestor matching would also accept a separate boat nested here.
            GameState.currentBoat != null && (GameState.currentBoat == heldBody.transform ||
                GameState.currentBoat.parent == heldBody.transform) || GameState.currentShipyard != null &&
                GameState.currentShipyard.GetCurrentBoat() == heldBody.gameObject;

        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
