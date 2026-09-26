using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    // Read-only proof of the selected boat's live authored berth. A native
    // startup rope may already touch its cleat before its trigger connects it.
    internal static class NativeBerth
    {
        private static readonly FieldInfo SpringField = AccessTools.Field(typeof(PickupableBoatMooringRope), "mooredToSpring");
        private static readonly FieldInfo ParentField = AccessTools.Field(typeof(PickupableBoatMooringRope), "initialParent");
        private static readonly FieldInfo PositionField = AccessTools.Field(typeof(PickupableBoatMooringRope), "initialPos");
        private static readonly FieldInfo ThrowingField = AccessTools.Field(typeof(PickupableBoatMooringRope), "throwing");
        private static readonly FieldInfo HeldField = AccessTools.Field(typeof(PickupableBoatMooringRope), "held");
        private static readonly FieldInfo LengthField = AccessTools.Field(typeof(PickupableBoatMooringRope), "maxLength");
        private static readonly FieldInfo IslandHeightField = AccessTools.Field(typeof(IslandHorizon), "initialHeight");
        private const float EndpointToleranceSquared = 0.15f * 0.15f;

        internal sealed class Plan
        {
            internal Port Port;
            internal PurchasableBoat Boat;
            internal SaveableObject Saveable;
            internal Rigidbody Body;
            internal BoatMooringRopes Ropes;
            internal IslandHorizon Island;
            internal GPButtonDockMooring Front, Back;
            internal SpringJoint FrontSpring, BackSpring;
            internal PickupableBoatMooringRope FrontRope, BackRope;
            internal bool FrontConnected, BackConnected;

            internal bool Revalidate()
            {
                if (!TryResolve(Port, Boat, Saveable, Body, Ropes, out var current)) return false;
                // Native trigger processing may connect a pending rope. Its
                // identity and authored dock must remain unchanged.
                return current.Island == Island && current.Front == Front && current.Back == Back &&
                    current.FrontSpring == FrontSpring && current.BackSpring == BackSpring &&
                    current.FrontRope == FrontRope && current.BackRope == BackRope &&
                    (!FrontConnected || current.FrontConnected) && (!BackConnected || current.BackConnected);
            }

            internal Vector3 ShorePosition
            {
                get
                {
                    if (!TryHorizonOffset(Island, out var offset))
                        throw new InvalidOperationException("Native dock height could not be verified.");
                    // RecoveryPort is detached from the island. Its private
                    // player/cargo anchor must use the dock's visible height.
                    return (Front.transform.position + Back.transform.position) * 0.5f - offset;
                }
            }
        }

        internal static bool TryResolve(Port port, PurchasableBoat boat, SaveableObject saveable,
            Rigidbody body, BoatMooringRopes ropes, out Plan plan)
        {
            plan = null;
            if (port == null || boat == null || saveable == null || body == null || ropes == null ||
                !Loaded(port) || !Loaded(boat) || !Loaded(saveable) || !Loaded(body) || !Loaded(ropes) ||
                boat.isPurchased() || boat.GetComponent<SaveableObject>() != saveable ||
                saveable.GetComponent<Rigidbody>() != body || saveable.GetComponent<BoatMooringRopes>() != ropes ||
                !FinitePose(saveable.transform) || !Finite(body.position) || !Finite(body.rotation) ||
                ropes.ropes == null || ropes.ropes.Length < 2 || ropes.ropes.Any(rope => rope == null) ||
                ropes.mooringFront == null || ropes.mooringBack == null ||
                SpringField == null || ParentField == null || PositionField == null ||
                ThrowingField == null || HeldField == null || LengthField == null || IslandHeightField == null) return false;
            var front = ropes.mooringFront.GetComponent<GPButtonDockMooring>();
            var back = ropes.mooringBack.GetComponent<GPButtonDockMooring>();
            if (front == null || back == null || front == back || !Loaded(front) || !Loaded(back) ||
                front.spring == null || back.spring == null || front.spring == back.spring ||
                front.GetComponent<SpringJoint>() != front.spring || back.GetComponent<SpringJoint>() != back.spring ||
                !FinitePose(front.transform) || !FinitePose(back.transform)) return false;
            var island = port.GetComponentInParent<IslandHorizon>();
            if (island == null || front.GetComponentInParent<IslandHorizon>() != island ||
                back.GetComponentInParent<IslandHorizon>() != island || !UniquePort(port, island)) return false;
            if (!TryHorizonOffset(island, out var horizonOffset)) return false;
            // An unconnected spring does not mean its authored berth is free.
            // Reject shared claims rather than assuming which boat owns it.
            if (Resources.FindObjectsOfTypeAll<BoatMooringRopes>().Any(other => other != null &&
                other != ropes && Loaded(other) && other.gameObject.activeInHierarchy &&
                (other.mooringFront == front.transform || other.mooringBack == front.transform ||
                 other.mooringFront == back.transform || other.mooringBack == back.transform))) return false;
            var maxLengthSquared = (float)LengthField.GetValue(null);
            if (!Finite(maxLengthSquared) || maxLengthSquared <= 0f) return false;
            var frontRopes = ropes.ropes.Where(rope => Matches(rope, front, body, maxLengthSquared, horizonOffset)).ToArray();
            var backRopes = ropes.ropes.Where(rope => Matches(rope, back, body, maxLengthSquared, horizonOffset)).ToArray();
            if (frontRopes.Length != 1 || backRopes.Length != 1 || frontRopes[0] == backRopes[0]) return false;
            plan = new Plan { Port = port, Boat = boat, Saveable = saveable, Body = body, Ropes = ropes,
                Island = island, Front = front, Back = back, FrontSpring = front.spring, BackSpring = back.spring,
                FrontRope = frontRopes[0], BackRope = backRopes[0],
                FrontConnected = frontRopes[0].IsMoored(), BackConnected = backRopes[0].IsMoored() };
            return Finite(plan.ShorePosition);
        }

        private static bool UniquePort(Port selected, IslandHorizon island)
        {
            if (Port.ports == null) return false;
            var candidates = Port.ports.Where(port => port != null && port.enabled && Loaded(port) &&
                port.GetComponentInParent<IslandHorizon>() == island &&
                !(port.portIndex == 7 && string.Equals(port.gameObject.name, "port 7 (test port)",
                    StringComparison.OrdinalIgnoreCase))).Distinct().ToArray();
            return candidates.Length == 1 && candidates[0] == selected &&
                selected.portIndex >= 0 && selected.portIndex < Port.ports.Length &&
                Port.ports[selected.portIndex] == selected;
        }

        private static bool Matches(PickupableBoatMooringRope rope, GPButtonDockMooring dock,
            Rigidbody body, float maxLengthSquared, Vector3 horizonOffset)
        {
            if (!Loaded(rope) || rope.GetBoatRigidbody() != body || !FinitePose(rope.transform) ||
                (bool)ThrowingField.GetValue(rope) || HeldField.GetValue(rope) is UnityEngine.Object held && held != null)
                return false;
            var initialParent = ParentField.GetValue(rope) as Transform;
            if (initialParent == null || !initialParent.IsChildOf(body.transform) ||
                !FinitePose(initialParent) || !(PositionField.GetValue(rope) is Vector3 initialPosition) ||
                !Finite(initialPosition)) return false;
            var anchor = initialParent.TransformPoint(initialPosition);
            var correctedDock = dock.transform.position - horizonOffset;
            var endpointDistance = (rope.transform.position - dock.transform.position).sqrMagnitude;
            var correctedEndpointDistance = (rope.transform.position - correctedDock).sqrMagnitude;
            var anchorDistance = (correctedDock - anchor).sqrMagnitude;
            if (!Finite(anchor) || !Finite(correctedDock) || !Finite(endpointDistance) ||
                !Finite(correctedEndpointDistance) || !Finite(anchorDistance) || anchorDistance > maxLengthSquared)
                return false;
            var spring = SpringField.GetValue(rope) as SpringJoint;
            if (spring != null)
                return rope.IsMoored() && spring == dock.spring && spring.connectedBody == body &&
                    rope.transform.parent == spring.transform && endpointDistance <= EndpointToleranceSquared;
            // This is the native pre-trigger endpoint layout, not permission to
            // connect a spring ourselves. Preserve it exactly as observed.
            return !rope.IsMoored() && dock.spring.connectedBody == null &&
                rope.transform.parent == initialParent && !rope.IsAtInitialPos() &&
                (endpointDistance <= EndpointToleranceSquared || correctedEndpointDistance <= EndpointToleranceSquared);
        }

        private static bool TryHorizonOffset(IslandHorizon island, out Vector3 offset)
        {
            offset = Vector3.zero;
            if (!(IslandHeightField.GetValue(island) is float initialHeight) || !Finite(initialHeight) ||
                !FinitePose(island.transform) || !Finite(island.transform.localPosition)) return false;
            // IslandHorizon changes local Y while BoatHorizon.SetHeight is empty.
            // Undo exactly that authored-height delta, including a scaled or
            // rotated parent, rather than accepting arbitrary vertical errors.
            var localDelta = new Vector3(0f, island.transform.localPosition.y - initialHeight, 0f);
            offset = island.transform.parent != null ? island.transform.parent.TransformVector(localDelta) : localDelta;
            return Finite(offset);
        }

        private static bool Loaded(Component component) => component != null &&
            component.gameObject.scene.IsValid() && component.gameObject.scene.isLoaded;
        private static bool FinitePose(Transform transform) => Finite(transform.position) &&
            Finite(transform.rotation) && Finite(transform.lossyScale);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
