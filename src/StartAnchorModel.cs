using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    // Frozen geometry for the two verified native anchor layouts. The optional
    // visual rope is retained for identity, never used as a physics attachment.
    internal sealed class StartAnchorModel
    {
        private BoatMooringRopes ropes;
        private RopeControllerAnchor controller;
        private Anchor anchor;
        private Rigidbody hull, anchorBody;
        private UnityEngine.Object visual;
        private ConfigurableJoint joint;
        private Vector3 hookLocal, pivotLocal, anchorScale, hullScale;
        private Vector3 jointAnchor, connectedAnchor, axis, secondaryAxis;
        private Vector3 contactLocalPosition, contactScale;
        private Quaternion contactLocalRotation;
        private bool autoConnected, collision, preprocessing;
        private float breakForce, breakTorque, massScale, connectedMassScale;
        private float spring, damper, bounciness, contactDistance;
        internal float Maximum { get; private set; }
        internal float BottomOffset { get; private set; }
        internal Collider Contact { get; private set; }
        internal Quaternion AnchorRotation { get; private set; }

        internal static bool TryCreate(BoatMooringRopes ropes, Rigidbody hull,
            out StartAnchorModel model, out string reason)
        {
            model = null; reason = null;
            var controller = ropes != null ? ropes.GetAnchorController() : null;
            var anchor = ropes != null ? ropes.anchor : null;
            var body = anchor != null ? anchor.GetComponent<Rigidbody>() : null;
            if (hull == null || anchor == null || controller == null || body == null ||
                !controller.transform.IsChildOf(ropes.transform) ||
                !(Read(controller, "maxLength") is float maximum) || !Finite(maximum) || maximum < 9f)
                return Fail("The boat has no supported native anchor model.", out reason);
            var contacts = anchor.GetComponentsInChildren<Collider>().Where(item => item.enabled && !item.isTrigger &&
                item.attachedRigidbody == body).ToArray();
            if (contacts.Length != 1) return Fail("The native anchor contact shape is ambiguous.", out reason);
            var visual = Read(controller, "anchorRope") as UnityEngine.Object;
            if (!ReferenceEquals(visual, null) && visual == null)
                return Fail("The native anchor rope reference was destroyed.", out reason);
            var result = new StartAnchorModel
            {
                ropes = ropes, controller = controller, anchor = anchor, hull = hull, anchorBody = body,
                visual = visual, Maximum = maximum, Contact = contacts[0], AnchorRotation = body.rotation,
                anchorScale = body.transform.lossyScale, hullScale = hull.transform.lossyScale
            };
            result.BottomOffset = body.position.y - result.Contact.bounds.min.y;
            result.contactLocalPosition = body.transform.InverseTransformPoint(result.Contact.transform.position);
            result.contactLocalRotation = Quaternion.Inverse(body.rotation) * result.Contact.transform.rotation;
            result.contactScale = result.Contact.transform.lossyScale;
            {
                var joint = Read(controller, "joint") as ConfigurableJoint;
                if (!SupportedJoint(joint, body, hull))
                    return Fail("The native anchor joint has an unsupported attachment or motion model.", out reason);
                result.joint = joint;
                result.jointAnchor = joint.anchor; result.connectedAnchor = joint.connectedAnchor;
                result.axis = joint.axis; result.secondaryAxis = joint.secondaryAxis;
                result.autoConnected = joint.autoConfigureConnectedAnchor;
                result.collision = joint.enableCollision; result.preprocessing = joint.enablePreprocessing;
                result.breakForce = joint.breakForce; result.breakTorque = joint.breakTorque;
                result.massScale = joint.massScale; result.connectedMassScale = joint.connectedMassScale;
                result.spring = joint.linearLimitSpring.spring; result.damper = joint.linearLimitSpring.damper;
                result.bounciness = joint.linearLimit.bounciness; result.contactDistance = joint.linearLimit.contactDistance;
                result.hookLocal = Vector3.Scale(joint.connectedAnchor, result.hullScale);
                result.pivotLocal = Vector3.Scale(body.transform.InverseTransformPoint(joint.transform.TransformPoint(joint.anchor)), result.anchorScale);
            }
            if (!Finite(result.hookLocal) || !Finite(result.pivotLocal) || !Finite(result.anchorScale) ||
                !Finite(result.hullScale) || !Finite(result.AnchorRotation) || !Finite(result.contactLocalPosition) ||
                !Finite(result.contactLocalRotation) || !Finite(result.contactScale))
                return Fail("The native anchor attachment geometry is invalid.", out reason);
            if (!Finite(result.BottomOffset) || result.BottomOffset < 0f || result.BottomOffset > 5f)
                return Fail("The anchor contact offset is invalid.", out reason);
            model = result;
            return true;
        }

        internal Vector3 HullPoint(Vector3 position, Quaternion rotation) => position + rotation * hookLocal;
        internal Vector3 AnchorPoint(Vector3 position) => position + AnchorRotation * pivotLocal;

        internal bool IsCurrent(BoatMooringRopes currentRopes, Rigidbody currentHull, out string reason)
        {
            reason = null;
            if (currentRopes != ropes || currentHull != hull || ropes == null || hull == null ||
                anchor == null || controller == null || anchorBody == null || Contact == null ||
                ropes.anchor != anchor || ropes.GetAnchorController() != controller ||
                anchor.GetComponent<Rigidbody>() != anchorBody || !Contact.enabled || Contact.isTrigger ||
                Contact.attachedRigidbody != anchorBody ||
                !ReferenceEquals(Read(controller, "anchorRope"), visual) ||
                (!ReferenceEquals(visual, null) && visual == null) ||
                !(Read(controller, "maxLength") is float maximum) || maximum != Maximum ||
                anchorBody.transform.lossyScale != anchorScale || hull.transform.lossyScale != hullScale ||
                !Finite(anchorBody.rotation) || Quaternion.Angle(anchorBody.rotation, AnchorRotation) > .1f ||
                Contact.transform.lossyScale != contactScale)
                return Fail("The native anchor model changed after its survey.", out reason);
            var contactPosition = anchorBody.transform.InverseTransformPoint(Contact.transform.position);
            var contactRotation = Quaternion.Inverse(anchorBody.rotation) * Contact.transform.rotation;
            if (!Finite(contactPosition) || !Finite(contactRotation) ||
                Vector3.Distance(contactPosition, contactLocalPosition) > .001f ||
                Quaternion.Angle(contactRotation, contactLocalRotation) > .01f)
                return Fail("The native anchor contact geometry changed after its survey.", out reason);
            {
                if (!SupportedJoint(joint, anchorBody, hull) || Read(controller, "joint") as ConfigurableJoint != joint ||
                    joint.anchor != jointAnchor || joint.connectedAnchor != connectedAnchor || joint.axis != axis ||
                    joint.secondaryAxis != secondaryAxis || joint.autoConfigureConnectedAnchor != autoConnected ||
                    joint.enableCollision != collision || joint.enablePreprocessing != preprocessing ||
                    joint.breakForce != breakForce || joint.breakTorque != breakTorque ||
                    joint.massScale != massScale || joint.connectedMassScale != connectedMassScale ||
                    joint.linearLimitSpring.spring != spring || joint.linearLimitSpring.damper != damper ||
                    joint.linearLimit.bounciness != bounciness || joint.linearLimit.contactDistance != contactDistance)
                    return Fail("The native anchor joint changed after its survey.", out reason);
            }
            return true;
        }

        internal bool ReadyToSecure(BoatMooringRopes currentRopes, Rigidbody currentHull, float normalized,
            Vector3 expectedAnchorPosition, out string reason)
        {
            if (!IsCurrent(currentRopes, currentHull, out reason)) return false;
            // Allow the planned 2 cm contact gap to settle, plus 3 cm of physics
            // tolerance. A sliding anchor must not secure an unsurveyed pivot.
            if (!Finite(expectedAnchorPosition) || !Finite(anchorBody.position) ||
                Vector3.Distance(anchorBody.position, expectedAnchorPosition) > .05f)
                return Fail("The native anchor moved away from the surveyed seabed contact.", out reason);
            if (!Finite(normalized) || normalized <= 0f || normalized > 1f)
                return Fail("The planned native anchor length is invalid.", out reason);
            if (!Finite(joint.linearLimit.limit) ||
                Mathf.Abs(joint.linearLimit.limit - Maximum * normalized) > .01f)
                return Fail("The native anchor joint has not applied the planned line length.", out reason);
            return true;
        }

        private static bool SupportedJoint(ConfigurableJoint joint, Rigidbody anchor, Rigidbody hull) =>
            joint != null && joint.GetComponent<Rigidbody>() == anchor && joint.connectedBody == hull &&
            joint.xMotion == ConfigurableJointMotion.Limited && joint.yMotion == ConfigurableJointMotion.Limited &&
            joint.zMotion == ConfigurableJointMotion.Limited && joint.angularXMotion == ConfigurableJointMotion.Free &&
            joint.angularYMotion == ConfigurableJointMotion.Free && joint.angularZMotion == ConfigurableJointMotion.Free &&
            Finite(joint.anchor) && Finite(joint.connectedAnchor) && Finite(joint.axis) && Finite(joint.secondaryAxis) &&
            Finite(joint.linearLimit.limit) && Finite(joint.linearLimit.bounciness) && Finite(joint.linearLimit.contactDistance) &&
            Finite(joint.linearLimitSpring.spring) && Finite(joint.linearLimitSpring.damper) &&
            Finite(joint.massScale) && joint.massScale > 0f && Finite(joint.connectedMassScale) && joint.connectedMassScale > 0f &&
            !float.IsNaN(joint.breakForce) && joint.breakForce > 0f && !float.IsNaN(joint.breakTorque) && joint.breakTorque > 0f;

        private static object Read(object owner, string name) => AccessTools.Field(owner.GetType(), name)?.GetValue(owner);
        private static bool Fail(string message, out string reason) { reason = message; return false; }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
    }
}
