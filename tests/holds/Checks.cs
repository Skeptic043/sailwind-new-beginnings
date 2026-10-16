using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using UnityEngine;
using NewBeginnings;

static class Checks
{
    static int count;
    static void Check(bool value, string message)
    {
        count++;
        if (!value)
            throw new Exception(message);
    }

    static void Tick(OffshoreHold keeper) => typeof(OffshoreHold).GetMethod("FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(keeper, null);
    static Transform BoardedChild(Rigidbody hull) => new()
    {
        parent = hull.transform
    };
    static OffshoreHold Active() => (OffshoreHold)typeof(OffshoreHold).GetField("active", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    sealed class Fixture
    {
        internal Rigidbody body = new();
        internal IslandHorizon island = new()
        {
            islandIndex = 9
        };
        internal OffshoreHold keeper;
        internal Fixture()
        {
            OffshoreHoldSave.ClearBeforeLoad();
            GameState.currentlyLoading = false;
            GameState.playing = true;
            GameState.currentBoat = null;
            GameState.currentShipyard = null;
            GameState.modData = new()
            {
                {
                    "other.mod",
                    "preserved"
                }
            };
            SaveLoadManager.instance = new();
            Refs.islands = new Transform[43];
            body.position = new(30, 2, -20);
            body.purchase = new()
            {
                purchased = true
            };
            body.saveable = new SaveableObject()
            {
                sceneIndex = 197,
                body = body
            };
            SaveLoadManager.instance.objects[197] = (SaveableObject)body.saveable;
            island.transform.island = island;
            island.transform.position = new(10, 0, -10);
            Refs.islands[9] = island.transform;
            keeper = OffshoreHold.Attach(body, island.transform);
        }

        // Production LoadGame prefixes: FixedStart.CancelStartWork removes the
        // active hold, then OffshoreHoldSave drops the previous save's marker.
        internal static void BeforeLoad()
        {
            OffshoreHold.RemoveActive();
            OffshoreHoldSave.ClearBeforeLoad();
        }

        internal Dictionary<string, string> SaveRoundtrip()
        {
            OffshoreHoldSave.Save();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(GameState.modData));
        }

        internal void Load(Dictionary<string, string> data)
        {
            BeforeLoad();
            GameState.currentlyLoading = true;
            GameState.modData = data;
            OffshoreHoldSave.Load();
        }
    }

    static void Main()
    {
        var f = new Fixture();
        CultureInfo.CurrentCulture = new("fr-FR");
        var saved = f.SaveRoundtrip();
        Check(saved.ContainsKey(OffshoreHoldSave.Key), "pending hold missing from serialized dictionary");
        Check(saved["other.mod"] == "preserved", "foreign mod state changed");
        f.island.transform.position = new(1000, 8, 1000);
        f.island.transform.rotation = new(0, MathF.Sqrt(.5f), 0, MathF.Sqrt(.5f));
        f.Load(saved);
        Check(f.keeper.Destroyed, "old hold survived load");
        Check(OffshoreHold.TryGetHold(out var restored, out var frame, out var local, out var yaw) && restored == f.body && frame == f.island.transform && local == new Vector3(20, 2, -10), "serialized target or identity lost");
        var active = Active();
        Tick(active);
        Check(!active.Destroyed && f.body.ForceCalls == 0, "hold applied or destroyed during native loading");
        GameState.currentlyLoading = false;
        f.body.position = f.island.transform.TransformPoint(local);
        Tick(active);
        Check(f.body.LastForce.magnitude < .001f && f.body.LastTorque.y > 0, "restored hold lost moving-world position or yaw");
        f.body.position -= Vector3.right * 2;
        Tick(active);
        Check(f.body.LastForce.x > 0 && f.body.LastForce.y == 0, "restored hold did not resume horizontal force");
        GameState.currentBoat = BoardedChild(f.body);
        var boarded = f.SaveRoundtrip();
        Check(!boarded.ContainsKey(OffshoreHoldSave.Key) && active.Destroyed, "native child boarding between physics ticks retained saved hold");
        f.Load(boarded);
        Check(!OffshoreHold.TryGetHold(out _, out _, out _, out _), "after-boarding reload recreated hold");
        f = new Fixture();
        saved = f.SaveRoundtrip();
        f.Load(new() { { "other.mod", "incoming" } });
        Check(!OffshoreHold.TryGetHold(out _, out _, out _, out _) && GameState.modData["other.mod"] == "incoming", "switching saves leaked old hold");
        f = new Fixture();
        f.SaveRoundtrip();
        Fixture.BeforeLoad();
        GameState.currentlyLoading = true;
        OffshoreHoldSave.Load();
        Check(!GameState.modData.ContainsKey(OffshoreHoldSave.Key) && !OffshoreHold.TryGetHold(out _, out _, out _, out _), "legacy null mod dictionary inherited prior marker");
        f = new Fixture();
        saved = f.SaveRoundtrip();
        GameState.currentBoat = BoardedChild(f.body);
        Tick(f.keeper);
        Check(!f.SaveRoundtrip().ContainsKey(OffshoreHoldSave.Key), "physics native-child boarding did not clear next save");
        // Existing records from the earlier predicate must also release on an aboard load.
        f = new Fixture();
        saved = f.SaveRoundtrip();
        GameState.playing = false;
        GameState.currentBoat = BoardedChild(f.body);
        f.Load(saved);
        active = Active();
        Tick(active);
        Check(!active.Destroyed && f.body.ForceCalls == 0 && f.body.TorqueCalls == 0, "aboard restore escaped native loading guard");
        GameState.currentlyLoading = false;
        Tick(active);
        Check(!active.Destroyed, "aboard restore released before playing");
        int calls = f.body.ForceCalls;
        int torques = f.body.TorqueCalls;
        GameState.playing = true;
        Tick(active);
        Check(active.Destroyed && f.body.ForceCalls == calls && f.body.TorqueCalls == torques, "stale aboard hold did not release before force or torque");
        boarded = f.SaveRoundtrip();
        Check(!boarded.ContainsKey(OffshoreHoldSave.Key), "released stale record persisted into next save");
        f.Load(boarded);
        Check(!OffshoreHold.TryGetHold(out _, out _, out _, out _), "released stale hold restored on subsequent load");
        f = new Fixture();
        saved = f.SaveRoundtrip();
        f.Load(saved);
        active = Active();
        GameState.currentlyLoading = false;
        GameState.currentBoat = BoardedChild(f.body);
        Tick(active);
        Check(active.Destroyed && f.body.ForceCalls == 0 && f.body.TorqueCalls == 0, "restored native-child hold applied force before release");
        f = new Fixture();
        GameState.currentBoat = BoardedChild(new Rigidbody());
        Check(f.SaveRoundtrip().ContainsKey(OffshoreHoldSave.Key), "another boat boarding removed selected saved hold");
        var nestedBoat = new Rigidbody();
        nestedBoat.transform.parent = f.body.transform;
        GameState.currentBoat = BoardedChild(nestedBoat);
        Check(f.SaveRoundtrip().ContainsKey(OffshoreHoldSave.Key), "nested boat boarding removed selected saved hold");
        GameState.currentBoat = BoardedChild(f.body);
        GameState.playing = false;
        Check(f.SaveRoundtrip().ContainsKey(OffshoreHoldSave.Key), "pre-playing native child removed saved hold");
        GameState.playing = true;
        Check(!f.SaveRoundtrip().ContainsKey(OffshoreHoldSave.Key), "native child failed to remove saved hold once playing");
        foreach (var bad in new[] { "garbage", "197|9|NaN|0|0|0", "-1|9|0|0|0|0", "999|9|0|0|0|0", "197|999|0|0|0|0" })
        {
            f.Load(new() { { OffshoreHoldSave.Key, bad } });
            Check(!OffshoreHold.TryGetHold(out _, out _, out _, out _), "unreadable/unresolved marker restored hold");
        }

        f = new Fixture();
        saved = f.SaveRoundtrip();
        f.body.purchase.purchased = false;
        f.Load(saved);
        Check(!OffshoreHold.TryGetHold(out _, out _, out _, out _), "unowned boat received restored hold");
        f = new Fixture();
        saved = f.SaveRoundtrip();
        f.island.islandIndex = 17;
        f.Load(saved);
        Check(!OffshoreHold.TryGetHold(out _, out _, out _, out _), "wrong island identity received hold");
        f = new Fixture();
        saved = f.SaveRoundtrip();
        GameState.currentShipyard = new()
        {
            ship = f.body.gameObject
        };
        var serviced = f.SaveRoundtrip();
        Check(!serviced.ContainsKey(OffshoreHoldSave.Key), "service admission persisted hold before next physics tick");
        f.Load(serviced);
        Check(!OffshoreHold.TryGetHold(out _, out _, out _, out _), "reload restored hold after shipyard use");
        Console.WriteLine($"PASS {count} whole-source offshore save/restore checks. Native/Unity boundaries are doubles, no live game save or physics acceptance.");
    }
}
