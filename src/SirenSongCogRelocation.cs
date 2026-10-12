using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    internal static class SirenSongCogSaveState
    {
        internal const string DataKey = "com.skeptic043.sailwind.newbeginnings.sirensongcog.v1";

        internal static void ClearBeforeLoad() => GameState.modData?.Remove(DataKey);
    }

    // Age of Exploration gives Santa Maria Siren Song's only straight alternate
    // berth. In that exact layout, temporarily move the native unsold Cog into
    // the bay and retain its position in the save until normal purchase takes over.
    internal static class SirenSongCogRelocation
    {
        private const string ShipShuffleId = "com.skeptic043.sailwind.shipshuffle";
        private const string DataKey = SirenSongCogSaveState.DataKey;
        private const string CogName = "BOAT medi small (40)";
        private const string SantaMariaName = "BOAT SANTA MARIA (222)(Clone)";
        private const float MinimumStarterHullGap = 1f;
        private const float MinimumHullGap = 2f;
        private const float MinimumDockGap = 2f;
        private static readonly FieldInfo PurchaseUiField = AccessTools.Field(typeof(PurchasableBoat), "purchaseUI");
        private static readonly FieldInfo MooredSpringField = AccessTools.Field(typeof(PickupableBoatMooringRope), "mooredToSpring");
        private static readonly FieldInfo RecoveryPortsField = AccessTools.Field(typeof(Recovery), "ports");
        private static ActiveState active;
        private static bool pendingNewGameClear;

        internal sealed class Plan
        {
            internal Port Port;
            internal RecoveryPort Recovery;
            internal GPButtonDockMooring Front, Back;
            internal PurchasableBoat Cog;
            internal SaveableObject Saveable;
            internal Rigidbody Body;
            internal BoatMooringRopes Ropes;
            internal Vector3 StarterPosition;
            internal Quaternion StarterRotation;
            internal Vector3 CogPosition;
            internal Quaternion CogRotation;
            internal Transform Frame;
            internal bool RequireFreedRecovery;

            internal bool Revalidate(ResolvedStart selected, out string reason) =>
                TryResolve(selected, out var current, out reason) && Same(current);

            private bool Same(Plan current) => current != null && current.Port == Port &&
                current.Recovery == Recovery && current.Front == Front && current.Back == Back &&
                current.Cog == Cog && current.Saveable == Saveable && current.Body == Body &&
                current.Ropes == Ropes && current.Frame == Frame &&
                Close(current.StarterPosition, StarterPosition) &&
                Close(current.StarterRotation, StarterRotation) &&
                Close(current.CogPosition, CogPosition) && Close(current.CogRotation, CogRotation);
        }

        private sealed class ActiveState
        {
            internal PurchasableBoat Cog;
            internal SaveableObject Saveable;
            internal Rigidbody Body;
            internal BoatMooringRopes Ropes;
            internal RecoveryPort Recovery;
            internal Transform Frame;
            internal Vector3 LocalPosition;
            internal Quaternion LocalRotation;
            internal PirateStationKeeper Keeper;
        }

        private sealed class RopeSnapshot
        {
            internal PickupableBoatMooringRope Rope;
            internal Transform Parent;
            internal Vector3 LocalPosition;
            internal Quaternion LocalRotation;
            internal GPButtonDockMooring Mooring;
        }

        internal sealed class Transaction
        {
            private readonly Plan plan;
            private readonly Transform parent;
            private readonly Vector3 localPosition, velocity, angularVelocity;
            private readonly Quaternion localRotation;
            private readonly bool bodyKinematic;
            private readonly Anchor anchor;
            private readonly Rigidbody anchorBody;
            private readonly Transform anchorParent;
            private readonly Vector3 anchorLocal, anchorVelocity, anchorAngularVelocity, anchorBoatLocal;
            private readonly Quaternion anchorRotation, anchorBoatRotation;
            private readonly bool anchorKinematic, anchorWasSet;
            private readonly float anchorLength, anchorDrag;
            private readonly RopeSnapshot[] ropes;
            private ProbeVelocityGuard guard;
            private PirateStationKeeper keeper;
            private bool moved, committed, rolledBack;

            internal Transaction(Plan plan)
            {
                this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
                var body = plan.Body;
                parent = body.transform.parent;
                localPosition = body.transform.localPosition;
                localRotation = body.transform.localRotation;
                velocity = body.velocity;
                angularVelocity = body.angularVelocity;
                bodyKinematic = body.isKinematic;
                anchor = plan.Ropes.anchor;
                if (anchor == null || anchor.IsSet())
                    throw new InvalidOperationException("The native Cog anchor is unavailable or deployed.");
                anchorBody = anchor.GetComponent<Rigidbody>();
                if (anchorBody == null)
                    throw new InvalidOperationException("The native Cog anchor body is unavailable.");
                anchorParent = anchor.transform.parent;
                anchorLocal = anchor.transform.localPosition;
                anchorRotation = anchor.transform.localRotation;
                anchorVelocity = anchorBody.velocity;
                anchorAngularVelocity = anchorBody.angularVelocity;
                anchorKinematic = anchorBody.isKinematic;
                anchorDrag = anchorBody.drag;
                anchorLength = anchor.GetRopeLength();
                anchorWasSet = anchor.IsSet();
                anchorBoatLocal = body.transform.InverseTransformPoint(anchorBody.position);
                anchorBoatRotation = Quaternion.Inverse(body.rotation) * anchorBody.rotation;
                ropes = plan.Ropes.ropes.Select(rope => new RopeSnapshot
                {
                    Rope = rope,
                    Parent = rope.transform.parent,
                    LocalPosition = rope.transform.localPosition,
                    LocalRotation = rope.transform.localRotation,
                    Mooring = OriginalMooring(rope)
                }).ToArray();
            }

            internal void Move()
            {
                if (moved) throw new InvalidOperationException("The native Cog relocation was already started.");
                guard = new ProbeVelocityGuard(plan.Saveable);
                moved = true;
                plan.Ropes.UnmoorAllRopes();
                plan.Ropes.GetAnchorController().ResetAnchor();
                var body = plan.Body;
                body.transform.SetPositionAndRotation(plan.CogPosition, plan.CogRotation);
                body.position = plan.CogPosition;
                body.rotation = plan.CogRotation;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                anchorBody.position = body.transform.TransformPoint(anchorBoatLocal);
                anchorBody.rotation = body.rotation * anchorBoatRotation;
                anchorBody.velocity = Vector3.zero;
                anchorBody.angularVelocity = Vector3.zero;
                if (!Finite(body.position) || !Finite(body.rotation) ||
                    !Finite(anchorBody.position) || !Finite(anchorBody.rotation))
                    throw new InvalidOperationException("The native Cog relocation produced a non-finite pose.");
                Physics.SyncTransforms();
                if (plan.RequireFreedRecovery &&
                    (plan.Front.spring.connectedBody != null || plan.Back.spring.connectedBody != null))
                    throw new InvalidOperationException("The Cog did not release Siren Song's recovery springs.");
                keeper = PirateStationKeeper.Attach(body, plan.Frame, true);
                guard.Restore();
                guard = null;
            }

            internal void Commit()
            {
                if (!moved || committed || rolledBack)
                    throw new InvalidOperationException("The native Cog relocation is not ready to commit.");
                Activate(plan, keeper);
                committed = true;
                keeper = null;
            }

            internal void Rollback()
            {
                if (committed || rolledBack) return;
                rolledBack = true;
                PirateStationKeeper.Remove(keeper);
                keeper = null;
                try
                {
                    if (!moved || plan.Body == null) return;
                    plan.Ropes?.UnmoorAllRopes();
                    var body = plan.Body;
                    body.transform.SetParent(parent, false);
                    body.transform.localPosition = localPosition;
                    body.transform.localRotation = localRotation;
                    body.position = body.transform.position;
                    body.rotation = body.transform.rotation;
                    body.velocity = velocity;
                    body.angularVelocity = angularVelocity;
                    body.isKinematic = bodyKinematic;
                    if (anchorBody != null)
                    {
                        anchorBody.transform.SetParent(anchorParent, false);
                        anchorBody.transform.localPosition = anchorLocal;
                        anchorBody.transform.localRotation = anchorRotation;
                        anchorBody.position = anchorBody.transform.position;
                        anchorBody.rotation = anchorBody.transform.rotation;
                        anchorBody.velocity = anchorVelocity;
                        anchorBody.angularVelocity = anchorAngularVelocity;
                        anchorBody.isKinematic = anchorKinematic;
                        anchorBody.drag = anchorDrag;
                    }
                    if (anchor != null) anchor.OnLoad(anchorWasSet, anchorLength);
                    foreach (var saved in ropes)
                    {
                        if (saved.Rope == null) continue;
                        try
                        {
                            if (saved.Mooring != null) saved.Rope.MoorTo(saved.Mooring);
                            else
                            {
                                saved.Rope.transform.SetParent(saved.Parent, false);
                                saved.Rope.transform.localPosition = saved.LocalPosition;
                                saved.Rope.transform.localRotation = saved.LocalRotation;
                            }
                        }
                        catch (Exception exception)
                        {
                            Plugin.Instance?.Warn("A Cog rope could not be restored after failed Siren Song placement: " + exception.Message);
                        }
                    }
                    Physics.SyncTransforms();
                }
                finally
                {
                    guard?.Restore();
                    guard = null;
                }
            }
        }

        internal static bool TryResolve(ResolvedStart selected, out Plan plan, out string reason)
        {
            plan = null;
            reason = null;
            if (selected == null || !SirenSongBerth.IsTarget(selected.Port))
            { reason = "The Cog fallback only applies at the inspected Siren Song port."; return false; }
            if (Chainloader.PluginInfos.ContainsKey(ShipShuffleId))
            { reason = "Ship Shuffle owns sale-boat placement in this setup."; return false; }
            if (selected.Saveable == null || selected.Body == null || selected.Ropes == null ||
                selected.Saveable.sceneIndex == 40 || selected.Saveable.sceneIndex == 222 ||
                selected.Saveable.name == CogName || selected.Saveable.name == SantaMariaName)
            { reason = "The selected boat is one of Siren Song's resident boats."; return false; }
            if (!SirenSongBerth.TryReadCleats(selected.Port, selected.Recovery,
                out var cleats, out reason)) return false;
            if (!TryFindExactBoat(40, CogName, out var cog, out var cogSaveable, out reason) ||
                !TryFindExactBoat(222, SantaMariaName, out var santa, out var santaSaveable, out reason)) return false;
            if (cog.isPurchased() || santa.isPurchased() ||
                PurchaseUiField?.GetValue(cog) as GameObject == null ||
                PurchaseUiField.GetValue(santa) as GameObject == null)
            { reason = "Siren Song's Cog or Santa Maria is no longer in normal unpurchased sale state."; return false; }
            var cogBody = cogSaveable.GetComponent<Rigidbody>();
            var cogRopes = cogSaveable.GetComponent<BoatMooringRopes>();
            var santaBody = santaSaveable.GetComponent<Rigidbody>();
            var santaRopes = santaSaveable.GetComponent<BoatMooringRopes>();
            if (cogBody == null || cogRopes == null || santaBody == null || santaRopes == null ||
                cogRopes.mooringFront != cleats[1].transform || cogRopes.mooringBack != cleats[0].transform ||
                santaRopes.mooringFront != cleats[3].transform || santaRopes.mooringBack != cleats[2].transform)
            { reason = "Siren Song's Cog or Santa Maria no longer owns the inspected authored cleats."; return false; }
            if (cogRopes.ropes == null || cogRopes.ropes.Length < 2 || cogRopes.ropes.Any(rope => rope == null) ||
                cogRopes.anchor == null || cogRopes.anchor.IsSet() || cogRopes.GetAnchorController() == null ||
                !ProbeVelocityGuard.CanGuard(cogSaveable) ||
                !NativeBerth.TryResolve(selected.Port, cog, cogSaveable, cogBody, cogRopes, out var native))
            { reason = "The native Cog is not intact at its authored recovery berth."; return false; }
            if (native.Front != cleats[1] || native.Back != cleats[0] ||
                cleats[2].spring.connectedBody != null && cleats[2].spring.connectedBody != santaBody ||
                cleats[3].spring.connectedBody != null && cleats[3].spring.connectedBody != santaBody)
            { reason = "Siren Song's resident spring ownership changed."; return false; }
            var sourcePosition = cogBody.position;
            var sourceRotation = cogBody.rotation;
            var points = cleats.Select(item => new Vector2(item.transform.position.x,
                item.transform.position.z)).ToArray();
            if (!SirenSongBerth.TryOutward(points, new Vector2(sourcePosition.x, sourcePosition.z),
                out var outward))
            { reason = "Siren Song's direction into the bay could not be verified."; return false; }
            var selectedOffset = FixedStart.ExtraBerthDistance(
                FixedStart.IsLeopardStart(selected), selected.Size);
            var starterPosition = sourcePosition + new Vector3(outward.x, 0f, outward.y) * selectedOffset;
            var cogHull = SirenSongBerth.ReadHull(cogBody, sourcePosition, sourceRotation);
            var selectedHull = SirenSongBerth.ReadHull(selected.Body, starterPosition, sourceRotation);
            var residents = new List<BerthClearance.Footprint>();
            foreach (var ropes in SceneBoats())
            {
                var body = ropes.GetComponent<Rigidbody>();
                if (body == null || body == cogBody || body == selected.Body) continue;
                var offset = body.position - sourcePosition;
                offset.y = 0f;
                if (offset.sqrMagnitude > 100f * 100f) continue;
                residents.Add(SirenSongBerth.ReadHull(body, body.position, body.rotation));
            }
            if (!TryStarterClearance(selectedHull, residents.ToArray(), points,
                out var starterGap, out var starterDockGap))
            { reason = "The selected starter does not have verified clearance at the Cog's berth."; return false; }
            var obstacles = new List<BerthClearance.Footprint> { selectedHull };
            obstacles.AddRange(residents);
            if (!TryOffshorePosition(cogHull, obstacles.ToArray(), points,
                new Vector2(sourcePosition.x, sourcePosition.z), out var delta, out var distance, out var gap))
            { reason = "No bounded clear-water Cog station was available beside Siren Song."; return false; }
            plan = new Plan
            {
                Port = selected.Port,
                Recovery = selected.Recovery,
                Front = cleats[1],
                Back = cleats[0],
                Cog = cog,
                Saveable = cogSaveable,
                Body = cogBody,
                Ropes = cogRopes,
                StarterPosition = starterPosition,
                StarterRotation = sourceRotation,
                CogPosition = sourcePosition + new Vector3(delta.x, 0f, delta.y),
                CogRotation = sourceRotation,
                Frame = selected.Recovery.boatPos,
                RequireFreedRecovery = true
            };
            Plugin.Instance?.DebugLog($"Siren Song Cog fallback: selected berth gaps {starterGap:F2}m hull / {starterDockGap:F2}m dock; station {distance:F2}m into the bay, nearest hull gap {gap:F2}m; selected boat will use the Cog's preserved native pose.");
            return true;
        }

        internal static bool TryStarterClearance(BerthClearance.Footprint starter,
            BerthClearance.Footprint[] residents, Vector2[] cleats,
            out float hullGap, out float dockGap)
        {
            hullGap = dockGap = float.NegativeInfinity;
            if (residents == null || residents.Length == 0 || cleats == null || cleats.Length != 6 ||
                !Finite(starter.A) || !Finite(starter.B) || !Finite(starter.Radius) || starter.Radius <= 0f ||
                cleats.Any(point => !Finite(point))) return false;
            hullGap = BerthClearance.MinimumGap(starter, residents, Vector2.zero, out _);
            if (hullGap < MinimumStarterHullGap) return false;
            dockGap = float.PositiveInfinity;
            for (var index = 1; index < cleats.Length; ++index)
                dockGap = Math.Min(dockGap, BerthClearance.SegmentDistance(starter.A, starter.B,
                    cleats[index - 1], cleats[index]) - starter.Radius);
            return dockGap >= MinimumDockGap;
        }

        internal static bool TryOffshorePosition(BerthClearance.Footprint cog,
            BerthClearance.Footprint[] obstacles, Vector2[] cleats, Vector2 original,
            out Vector2 delta, out float distance, out float gap)
        {
            delta = Vector2.zero;
            distance = 0f;
            gap = float.NegativeInfinity;
            if (obstacles == null || obstacles.Length == 0 ||
                !SirenSongBerth.TryOutward(cleats, original, out var outward)) return false;
            for (var step = 16; step <= 120; ++step)
            {
                distance = step * 0.25f;
                delta = outward * distance;
                gap = BerthClearance.MinimumGap(cog, obstacles, delta, out _);
                if (gap < MinimumHullGap) continue;
                var dockGap = float.PositiveInfinity;
                for (var index = 1; index < cleats.Length; ++index)
                    dockGap = Math.Min(dockGap, BerthClearance.SegmentDistance(cog.A + delta,
                        cog.B + delta, cleats[index - 1], cleats[index]) - cog.Radius);
                if (dockGap >= MinimumDockGap) return true;
            }
            return false;
        }

        internal static Transaction Begin(Plan plan)
        {
            var transaction = new Transaction(plan);
            try
            {
                transaction.Move();
                return transaction;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        internal static void BeginNewGame()
        {
            ClearActive();
            // Do not mutate the currently loaded save from the menu. Remove a
            // stale record only when this new game actually reaches its save.
            pendingNewGameClear = true;
        }

        internal static void Shutdown()
        {
            pendingNewGameClear = false;
            ClearActive();
        }

        private static void Activate(Plan plan, PirateStationKeeper keeper)
        {
            var localPosition = plan.Frame.InverseTransformPoint(plan.CogPosition);
            var localRotation = Quaternion.Inverse(plan.Frame.rotation) * plan.CogRotation;
            if (!Finite(localPosition) || !Finite(localRotation))
                throw new InvalidOperationException("The Cog station could not be recorded in Siren Song's recovery frame.");
            ClearActive();
            pendingNewGameClear = false;
            active = new ActiveState
            {
                Cog = plan.Cog,
                Saveable = plan.Saveable,
                Body = plan.Body,
                Ropes = plan.Ropes,
                Recovery = plan.Recovery,
                Frame = plan.Frame,
                LocalPosition = localPosition,
                LocalRotation = localRotation,
                Keeper = keeper
            };
        }

        private static void ClearActive()
        {
            if (active != null) PirateStationKeeper.Remove(active.Keeper);
            active = null;
        }

        [HarmonyPatch(typeof(SaveLoadManager), "SaveModData")]
        private static class SavePatch
        {
            private static void Postfix()
            {
                if (active == null)
                {
                    if (pendingNewGameClear) GameState.modData?.Remove(DataKey);
                    pendingNewGameClear = false;
                    return;
                }
                if (active.Cog != null && active.Cog.isPurchased())
                {
                    GameState.modData?.Remove(DataKey);
                    ClearActive();
                    return;
                }
                if (!Current(active))
                {
                    Plugin.Instance?.Warn("The offshore Cog was incomplete during save capture, so New Beginnings left its previous save record unchanged.");
                    return;
                }
                if (!Finite(active.LocalPosition) || !Finite(active.LocalRotation))
                {
                    Plugin.Instance?.Warn("The offshore Cog pose was not finite, so New Beginnings left its previous save record unchanged.");
                    return;
                }
                if (GameState.modData == null) GameState.modData = new Dictionary<string, string>();
                GameState.modData[DataKey] = Serialize(active.LocalPosition, active.LocalRotation);
            }
        }

        [HarmonyPatch(typeof(SaveLoadManager), "LoadGame")]
        private static class LoadBeginPatch
        {
            private static void Prefix()
            {
                // Native LoadGame leaves the previous static dictionary in
                // place when the incoming save has no modData field.
                SirenSongCogSaveState.ClearBeforeLoad();
                pendingNewGameClear = false;
                ClearActive();
            }
        }

        [HarmonyPatch(typeof(SaveLoadManager), "LoadModData")]
        [HarmonyAfter(ScrambledSeasIntegration.Id)]
        private static class LoadEndPatch
        {
            private static void Postfix()
            {
                if (GameState.modData == null || !GameState.modData.TryGetValue(DataKey, out var data)) return;
                Transaction transaction = null;
                try
                {
                    string reason = null;
                    Plan plan = null;
                    if (!TryDeserialize(data, out var localPosition, out var localRotation) ||
                        !TryResolveRestore(out plan, out reason))
                    {
                        Plugin.Instance?.Warn("The saved offshore Cog position was not restored: " +
                            (reason ?? "the saved record was invalid."));
                        return;
                    }
                    if (plan.Cog.isPurchased())
                    {
                        GameState.modData.Remove(DataKey);
                        return;
                    }
                    var position = plan.Frame.TransformPoint(localPosition);
                    var rotation = plan.Frame.rotation * localRotation;
                    if (!TryValidateRestoredStation(plan, position, rotation, out reason))
                    {
                        Plugin.Instance?.Warn("The saved offshore Cog position was not restored: " + reason);
                        return;
                    }
                    plan.CogPosition = position;
                    plan.CogRotation = rotation;
                    transaction = Begin(plan);
                    transaction.Commit();
                    // A keeper restored inside LoadModData must wait for the
                    // current load to finish instead of treating it as a later load.
                    active.Keeper.AllowInitialLoad();
                    Plugin.Instance?.DebugLog("Restored the unpurchased Cog's offshore Siren Song station from the save.");
                }
                catch (Exception exception)
                {
                    transaction?.Rollback();
                    Plugin.Instance?.Error("The saved offshore Cog position could not be restored; the native Cog was left unchanged where possible.", exception);
                }
            }
        }

        private static bool TryResolveRestore(out Plan plan, out string reason)
        {
            plan = null;
            reason = null;
            if (!RestoreAllowed(!Chainloader.PluginInfos.ContainsKey(ShipShuffleId), out reason)) return false;
            var port = Port.ports != null && Port.ports.Length > 18 ? Port.ports[18] : null;
            var registered = RecoveryPortsField?.GetValue(null) as List<RecoveryPort>;
            var recovery = registered?.SingleOrDefault(item => item != null && item.parentPort == port &&
                item.boatPos != null && item.mooringFront != null && item.mooringBack != null);
            if (!SirenSongBerth.IsTarget(port) || recovery == null ||
                !SirenSongBerth.TryReadCleats(port, recovery, out var cleats, out reason) ||
                !TryFindExactBoat(40, CogName, out var cog, out var saveable, out reason)) return false;
            var body = saveable.GetComponent<Rigidbody>();
            var ropes = saveable.GetComponent<BoatMooringRopes>();
            if (body == null || ropes == null || ropes.ropes == null || ropes.ropes.Length < 2 ||
                ropes.ropes.Any(rope => rope == null) || ropes.anchor == null || ropes.anchor.IsSet() ||
                ropes.GetAnchorController() == null || !ProbeVelocityGuard.CanGuard(saveable) ||
                PurchaseUiField?.GetValue(cog) as GameObject == null ||
                ropes.mooringFront != cleats[1].transform || ropes.mooringBack != cleats[0].transform)
            { reason = "The native Cog or Siren Song recovery berth no longer matches the saved layout."; return false; }
            plan = new Plan
            {
                Port = port,
                Recovery = recovery,
                Front = cleats[1],
                Back = cleats[0],
                Cog = cog,
                Saveable = saveable,
                Body = body,
                Ropes = ropes,
                StarterPosition = body.position,
                StarterRotation = body.rotation,
                Frame = recovery.boatPos,
                RequireFreedRecovery = false
            };
            return true;
        }

        internal static bool RestoreAllowed(bool shipShuffleAbsent, out string reason)
        {
            reason = shipShuffleAbsent ? null :
                "Ship Shuffle is loaded and owns sale-boat placement for this save.";
            return shipShuffleAbsent;
        }

        private static bool TryValidateRestoredStation(Plan plan, Vector3 position,
            Quaternion rotation, out string reason)
        {
            reason = "the saved station was outside its bounded Siren Song bay corridor.";
            if (plan == null || !Finite(position) || !Finite(rotation) ||
                !SirenSongBerth.TryReadCleats(plan.Port, plan.Recovery, out var cleats, out _) ||
                !SirenSongBerth.TryOutward(cleats.Select(item => new Vector2(item.transform.position.x,
                    item.transform.position.z)).ToArray(),
                    new Vector2(plan.Body.position.x, plan.Body.position.z), out var outward)) return false;
            var displacement3 = position - plan.Body.position;
            var displacement = new Vector2(displacement3.x, displacement3.z);
            if (!IsBoundedStationPose(displacement3, displacement, outward,
                rotation, plan.Body.rotation))
                return false;

            var cog = SirenSongBerth.ReadHull(plan.Body, position, rotation);
            var obstacles = new List<BerthClearance.Footprint>();
            foreach (var ropes in SceneBoats())
            {
                var body = ropes.GetComponent<Rigidbody>();
                if (body == null || body == plan.Body) continue;
                var offset = body.position - plan.Body.position;
                offset.y = 0f;
                if (offset.sqrMagnitude > 100f * 100f) continue;
                obstacles.Add(SirenSongBerth.ReadHull(body, body.position, body.rotation));
            }
            if (obstacles.Count == 0 ||
                BerthClearance.MinimumGap(cog, obstacles.ToArray(), Vector2.zero, out _) < MinimumHullGap)
            {
                reason = "the saved station no longer has the required hull clearance.";
                return false;
            }
            var points = cleats.Select(item => new Vector2(item.transform.position.x,
                item.transform.position.z)).ToArray();
            var dockGap = float.PositiveInfinity;
            for (var index = 1; index < points.Length; ++index)
                dockGap = Math.Min(dockGap, BerthClearance.SegmentDistance(cog.A, cog.B,
                    points[index - 1], points[index]) - cog.Radius);
            if (dockGap < MinimumDockGap)
            {
                reason = "the saved station no longer has the required dock clearance.";
                return false;
            }
            reason = null;
            return true;
        }

        internal static bool IsBoundedStationPose(Vector3 displacement3, Vector2 displacement,
            Vector2 outward, Quaternion rotation, Quaternion nativeRotation)
        {
            if (!Finite(displacement3) || !Finite(outward) || !Finite(rotation) ||
                !Finite(nativeRotation)) return false;
            var distance = Vector2.Dot(displacement, outward);
            var lateral = Math.Abs(displacement.x * outward.y - displacement.y * outward.x);
            return Math.Abs(displacement3.y) <= 5f && distance >= 3.75f && distance <= 30.25f &&
                lateral <= 0.5f && Math.Abs(Quaternion.Dot(rotation, nativeRotation)) >= 0.9999f;
        }

        private static bool TryFindExactBoat(int index, string name, out PurchasableBoat boat,
            out SaveableObject saveable, out string reason)
        {
            boat = null;
            saveable = null;
            var matches = Resources.FindObjectsOfTypeAll<PurchasableBoat>()
                .Where(item => item != null && item.enabled && item.gameObject.scene.IsValid() &&
                    item.gameObject.scene.isLoaded && item.gameObject.activeInHierarchy)
                .Select(item => new { Boat = item, Saveable = item.GetComponent<SaveableObject>() })
                .Where(item => item.Saveable != null && item.Saveable.sceneIndex == index).ToArray();
            if (matches.Length != 1 || !string.Equals(matches[0].Boat.gameObject.name, name, StringComparison.Ordinal) ||
                matches[0].Saveable.gameObject != matches[0].Boat.gameObject)
            {
                reason = $"Boat scene index {index} did not resolve to the one expected Siren Song resident.";
                return false;
            }
            boat = matches[0].Boat;
            saveable = matches[0].Saveable;
            reason = null;
            return true;
        }

        private static IEnumerable<BoatMooringRopes> SceneBoats() =>
            Resources.FindObjectsOfTypeAll<BoatMooringRopes>().Where(ropes => ropes != null &&
                ropes.gameObject.scene.IsValid() && ropes.gameObject.scene.isLoaded);

        private static GPButtonDockMooring OriginalMooring(PickupableBoatMooringRope rope)
        {
            if (!rope.IsMoored()) return null;
            var spring = MooredSpringField?.GetValue(rope) as SpringJoint;
            var mooring = spring != null ? spring.GetComponent<GPButtonDockMooring>() : null;
            if (mooring == null || mooring.spring != spring)
                throw new InvalidOperationException("A native Cog rope has no recoverable dock spring.");
            return mooring;
        }

        private static bool Current(ActiveState state) => state != null && state.Cog != null &&
            state.Saveable != null && state.Body != null && state.Ropes != null &&
            state.Frame != null && state.Recovery != null && state.Recovery.boatPos == state.Frame &&
            state.Saveable.sceneIndex == 40 && state.Saveable.name == CogName &&
            state.Cog.GetComponent<SaveableObject>() == state.Saveable &&
            state.Saveable.GetComponent<Rigidbody>() == state.Body &&
            state.Saveable.GetComponent<BoatMooringRopes>() == state.Ropes;

        private static string Serialize(Vector3 position, Quaternion rotation) => string.Join("|",
            "1", F(position.x), F(position.y), F(position.z), F(rotation.x), F(rotation.y),
            F(rotation.z), F(rotation.w));

        private static bool TryDeserialize(string value, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            var parts = (value ?? "").Split('|');
            if (parts.Length != 8 || parts[0] != "1") return false;
            var numbers = new float[7];
            for (var i = 0; i < numbers.Length; ++i)
                if (!float.TryParse(parts[i + 1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out numbers[i]) || !Finite(numbers[i])) return false;
            position = new Vector3(numbers[0], numbers[1], numbers[2]);
            rotation = new Quaternion(numbers[3], numbers[4], numbers[5], numbers[6]);
            var magnitude = rotation.x * rotation.x + rotation.y * rotation.y +
                rotation.z * rotation.z + rotation.w * rotation.w;
            return Finite(position) && Finite(rotation) && magnitude > 0.98f && magnitude < 1.02f;
        }

        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static bool Close(Vector3 first, Vector3 second) => (first - second).sqrMagnitude <= 0.01f;
        private static bool Close(Quaternion first, Quaternion second) => Math.Abs(Quaternion.Dot(first, second)) >= 0.9999f;
        private static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
