using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    // Siren Song's recovery cleats are also the berth of the Cog for sale.
    // Moor that unsold Cog at the two free cleats at the end of the dock, so a
    // new start or an owned boat loaded into that berth keeps the ordinary
    // recovery berth. Nothing is saved: an unsold boat is not restored on load.
    internal static class SirenSongCog
    {
        private const int CogSceneIndex = 40;
        private const string IslandName = "island 21 M (Siren Song)";
        private const string EndCleatA = "dock_mooring M (5)";
        private const string EndCleatB = "dock_mooring M (4)";
        // Cleats sit slightly inside the dock edge. This leaves room for that
        // inset and a gap between the hull and the dock.
        internal const float DockClearance = 2f;
        private const float CleatContact = 0.5f;
        private static readonly FieldInfo RecoveryPortsField = AccessTools.Field(typeof(Recovery), "ports");

        internal static bool IsSirenSong(Port port) => port != null && port.transform.parent != null &&
            port.transform.parent.name == IslandName;

        internal static bool VacateForStart(ResolvedStart selected)
        {
            if (selected == null || !IsSirenSong(selected.Port)) return false;
            var cog = FindCog();
            if (cog != null && cog.GetComponent<SaveableObject>() == selected.Saveable)
            {
                Plugin.Instance?.Report("Siren Song: the Cog is the selected starting boat, so it keeps the recovery berth.");
                return false;
            }
            return Move(cog, selected.Port, selected.Recovery, "new game");
        }

        [HarmonyPatch(typeof(SaveLoadManager), "LoadGame", new[] { typeof(int) })]
        private static class LoadPatch
        {
            // Native LoadGame has placed every owned boat by now. Unsold boats
            // keep their scene pose, so this decides once per load.
            private static void Postfix() => VacateAfterLoad();
        }

        private static void VacateAfterLoad()
        {
            try
            {
                var port = Port.ports?.FirstOrDefault(IsSirenSong);
                var recovery = port == null ? null : (RecoveryPortsField?.GetValue(null) as List<RecoveryPort>)?
                    .FirstOrDefault(item => item != null && item.parentPort == port && item.boatPos != null &&
                        item.mooringFront != null && item.mooringBack != null);
                if (recovery == null)
                {
                    Plugin.Instance?.DebugLog("Siren Song Cog check after load skipped: Siren Song's recovery berth is not registered.");
                    return;
                }
                var cog = FindCog();
                var cogBody = cog != null ? cog.GetComponent<Rigidbody>() : null;
                if (cogBody == null || !TryReadHull(cogBody, out var cogHull))
                {
                    Plugin.Instance?.Warn("Siren Song Cog check after load skipped: the Cog for sale or its hull capsule was not found.");
                    return;
                }
                var owned = new List<BerthClearance.Footprint>();
                var atRecovery = false;
                foreach (var boat in Resources.FindObjectsOfTypeAll<PurchasableBoat>())
                {
                    if (boat == null || boat == cog || !boat.gameObject.scene.IsValid() ||
                        !boat.gameObject.scene.isLoaded || !boat.isPurchased()) continue;
                    var body = boat.GetComponent<Rigidbody>();
                    if (body != null && TryReadHull(body, out var hull)) owned.Add(hull);
                    else Plugin.Instance?.DebugLog($"Siren Song Cog check after load: owned boat {boat.name} has no single hull capsule and is not counted for overlap.");
                    var ropes = boat.GetComponent<BoatMooringRopes>();
                    if (ropes != null && ropes.ropes != null && ropes.ropes.Any(rope => rope != null &&
                        (AtCleat(rope.transform.position, recovery.mooringFront.position) ||
                         AtCleat(rope.transform.position, recovery.mooringBack.position))))
                        atRecovery = true;
                }
                if (!ShouldVacateAfterLoad(cog.isPurchased(), cogHull, owned.ToArray(), atRecovery))
                {
                    Plugin.Instance?.DebugLog(cog.isPurchased()
                        ? "Siren Song Cog check after load: the Cog is owned, so it stays where it is."
                        : "Siren Song Cog check after load: no owned boat is in the Cog's berth, so it stays where it is.");
                    return;
                }
                Move(cog, port, recovery, "an owned boat was loaded into its berth");
            }
            catch (Exception exception)
            {
                Plugin.Instance?.Warn("Siren Song Cog check after load failed; the Cog was left where it was. " + exception.Message);
            }
        }

        internal static bool ShouldVacateAfterLoad(bool cogPurchased, BerthClearance.Footprint cog,
            BerthClearance.Footprint[] owned, bool ownedAtRecoveryCleats) =>
            !cogPurchased && (ownedAtRecoveryCleats || owned != null && owned.Length > 0 &&
                BerthClearance.MinimumGap(cog, owned, Vector2.zero, out _) < 0f);

        // Never throws and never blocks the caller. A step that cannot be done
        // is logged and the Cog is left as it is at that point.
        private static bool Move(PurchasableBoat cog, Port port, RecoveryPort recovery, string occasion)
        {
            var moved = false;
            void Skip(string reason) => Plugin.Instance?.Warn(moved
                ? $"Siren Song: the Cog reached the end of the dock, but its mooring setup did not finish ({occasion}): {reason}"
                : $"Siren Song: the Cog did not reach the end of the dock ({occasion}): {reason}");
            try
            {
                if (cog == null) { Skip("the Cog for sale (scene boat 40) was not found."); return false; }
                if (cog.isPurchased())
                {
                    Plugin.Instance?.Report($"Siren Song: the Cog is owned, so it stays where it is ({occasion}).");
                    return false;
                }
                if (recovery == null || recovery.boatPos == null) { Skip("Siren Song's recovery berth is unavailable."); return false; }
                var island = port != null ? port.transform.parent : null;
                var docks = island == null ? new GPButtonDockMooring[0] :
                    island.GetComponentsInChildren<GPButtonDockMooring>(true).Where(dock => dock != null &&
                        dock.transform.parent == island).ToArray();
                var a = docks.Where(dock => dock.name == EndCleatA).ToArray();
                var b = docks.Where(dock => dock.name == EndCleatB).ToArray();
                if (a.Length != 1 || b.Length != 1) { Skip($"{EndCleatA} and {EndCleatB} were not found once each."); return false; }
                var front = a[0];
                var back = b[0];
                var body = cog.GetComponent<Rigidbody>();
                var ropes = cog.GetComponent<BoatMooringRopes>();
                if (body == null || ropes == null || ropes.ropes == null || ropes.ropes.Length < 2 || ropes.ropes.Any(rope => rope == null))
                { Skip("the Cog has no hull body or mooring ropes."); return false; }
                if (!TryReadHull(body, out var hull)) { Skip("the Cog's hull capsule could not be measured."); return false; }

                var root = body.transform.position;
                var rotation = body.transform.rotation;
                var poses = new List<KeyValuePair<Vector3, Quaternion>>();
                foreach (var towardBack in new[] { false, true })
                {
                    if (!TryBerthPose(hull, Flat(root), Flat(front.transform.position), Flat(back.transform.position),
                        Flat(recovery.boatPos.position), towardBack, out var position, out var yaw))
                    { Skip("the end-of-dock berth geometry could not be computed."); return false; }
                    poses.Add(new KeyValuePair<Vector3, Quaternion>(new Vector3(position.x, root.y, position.y),
                        YawRotation(yaw) * rotation));
                }

                var anchorBody = ropes.anchor != null ? ropes.anchor.GetComponent<Rigidbody>() : null;
                var anchorLocal = anchorBody != null ? body.transform.InverseTransformPoint(anchorBody.position) : Vector3.zero;
                var anchorRotation = anchorBody != null ? Quaternion.Inverse(rotation) * anchorBody.rotation : Quaternion.identity;
                try { ropes.UnmoorAllRopes(); }
                catch (Exception exception)
                {
                    Plugin.Instance?.Warn("Siren Song: an original Cog mooring could not be released. Moving it to the requested berth anyway. " + exception.Message);
                }
                // Face whichever way gives the boat's own ropes the better
                // dock-facing pair, using the same rope choice as a starter.
                var best = -1;
                var bestPriority = int.MaxValue;
                var bestDistance = float.PositiveInfinity;
                for (var index = 0; index < poses.Count; ++index)
                {
                    Place(body, poses[index].Key, poses[index].Value);
                    moved = true;
                    if (!FixedStart.TryPairMooringRopes(body.transform, ropes.ropes, front.transform.position,
                        back.transform.position, out _, out _, out var priority, out var distance, out _)) continue;
                    if (priority < bestPriority || priority == bestPriority && distance < bestDistance)
                    { best = index; bestPriority = priority; bestDistance = distance; }
                }
                if (best < 0) best = 0;
                Place(body, poses[best].Key, poses[best].Value);
                moved = true;
                if (anchorBody != null)
                {
                    anchorBody.position = body.transform.TransformPoint(anchorLocal);
                    anchorBody.rotation = body.rotation * anchorRotation;
                    anchorBody.velocity = Vector3.zero;
                    anchorBody.angularVelocity = Vector3.zero;
                }
                Physics.SyncTransforms();
                if (!FixedStart.TryPairMooringRopes(body.transform, ropes.ropes, front.transform.position,
                    back.transform.position, out var frontRope, out var backRope, out _, out _, out _))
                { Skip("the Cog has no distinct mooring ropes at opposite ends; it was left unmoored at the end of the dock."); return true; }
                var frontReady = FixedStart.TryMoorAt(frontRope, front, body);
                var backReady = FixedStart.TryMoorAt(backRope, back, body);
                if (!frontReady || !backReady)
                { Skip("one or both end cleats could not be connected. The selected start continues."); return true; }
                var line = Flat(back.transform.position) - Flat(front.transform.position);
                var bow = Vector2.Dot(Flat(body.transform.forward), line) >= 0f ? back : front;
                Plugin.Instance?.Report($"Siren Song: moved the unsold Cog to {front.name} and {back.name} ({occasion}), " +
                    $"hull axis {hull.Radius + DockClearance:F2} m out from the cleats, bow toward {bow.name}.");
                return true;
            }
            catch (Exception exception)
            {
                Skip(exception.Message);
                return moved;
            }
        }

        // Pure XZ geometry. The hull axis runs parallel to the cleat line, centred
        // between the cleats and offset to the water side by its radius plus
        // DockClearance. Returns the new root position and the yaw in radians
        // about +Y to apply to the current rotation. axisTowardB points the
        // footprint's A end at cleat B.
        internal static bool TryBerthPose(BerthClearance.Footprint hull, Vector2 root, Vector2 cleatA,
            Vector2 cleatB, Vector2 water, bool axisTowardB, out Vector2 position, out float yaw)
        {
            position = Vector2.zero;
            yaw = 0f;
            var axis = hull.A - hull.B;
            var line = cleatB - cleatA;
            if (!Finite(hull.A) || !Finite(hull.B) || !Finite(root) || !Finite(cleatA) || !Finite(cleatB) ||
                !Finite(water) || !(hull.Radius > 0f) || float.IsInfinity(hull.Radius) ||
                axis.sqrMagnitude < 0.01f || line.sqrMagnitude < 1f) return false;
            axis.Normalize();
            line.Normalize();
            var normal = new Vector2(-line.y, line.x);
            var side = Vector2.Dot(water - cleatA, normal);
            // The water reference must lie clearly off the cleat line.
            if (Math.Abs(side) < 1f) return false;
            if (side < 0f) normal = -normal;
            var direction = axisTowardB ? line : -line;
            yaw = (float)Math.Atan2(axis.y * direction.x - axis.x * direction.y,
                axis.x * direction.x + axis.y * direction.y);
            var centre = (hull.A + hull.B) * 0.5f;
            var target = (cleatA + cleatB) * 0.5f + normal * (hull.Radius + DockClearance);
            position = target + Rotate(root - centre, yaw);
            return Finite(position) && !float.IsNaN(yaw);
        }

        // Unity's rotation about +Y applied to world X/Z.
        internal static Vector2 Rotate(Vector2 value, float yaw)
        {
            var cos = (float)Math.Cos(yaw);
            var sin = (float)Math.Sin(yaw);
            return new Vector2(value.x * cos + value.y * sin, -value.x * sin + value.y * cos);
        }

        internal static Quaternion YawRotation(float yaw) =>
            new Quaternion(0f, (float)Math.Sin(yaw * 0.5f), 0f, (float)Math.Cos(yaw * 0.5f));

        private static PurchasableBoat FindCog() => Resources.FindObjectsOfTypeAll<PurchasableBoat>()
            .FirstOrDefault(boat => boat != null && boat.gameObject.scene.IsValid() && boat.gameObject.scene.isLoaded &&
                boat.GetComponent<SaveableObject>() is SaveableObject saveable && saveable != null &&
                saveable.sceneIndex == CogSceneIndex);

        // A hidden boat can have its root capsule disabled. Its authored
        // capsule still describes the space the hull occupies.
        private static bool TryReadHull(Rigidbody body, out BerthClearance.Footprint hull)
        {
            hull = default(BerthClearance.Footprint);
            var hulls = body.GetComponents<CapsuleCollider>().Where(item => item != null && !item.isTrigger).ToArray();
            if (hulls.Length != 1) return false;
            hull = BerthClearance.MakeFootprint(body.transform.position, body.transform.rotation,
                body.transform.lossyScale, hulls[0].center, hulls[0].radius, hulls[0].height, hulls[0].direction);
            return true;
        }

        private static void Place(Rigidbody body, Vector3 position, Quaternion rotation)
        {
            body.transform.SetPositionAndRotation(position, rotation);
            body.position = position;
            body.rotation = rotation;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        // IslandHorizon can lower the dock while the island is far away, so
        // compare only the horizontal position.
        private static bool AtCleat(Vector3 rope, Vector3 cleat) => (Flat(rope) - Flat(cleat)).sqrMagnitude <= CleatContact * CleatContact;

        private static Vector2 Flat(Vector3 value) => new Vector2(value.x, value.z);
        private static bool Finite(Vector2 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    }
}
