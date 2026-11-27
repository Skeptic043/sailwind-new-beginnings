using System;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    // Optional reflection-based integrations with Climate, Sail Damage and FurstMate.
    // None of these are hard dependencies; absent mods are skipped silently and
    // unexpected signature mismatches log a warning without blocking the start.
    internal static class ModIntegrations
    {
        internal const string ClimateId = "com.raddude.climate";
        internal const string SailDamageId = "kap.saildamage";
        internal const string FurstMateId = "com.roy.furstmate";

        internal static bool HasClimate => IsLoaded(ClimateId);
        internal static bool HasSailDamage => IsLoaded(SailDamageId);
        internal static bool HasFurstMate => IsLoaded(FurstMateId);
        internal static bool HasAny => HasClimate || HasSailDamage || HasFurstMate;

        private static bool IsLoaded(string guid) =>
            Chainloader.PluginInfos != null &&
            Chainloader.PluginInfos.TryGetValue(guid, out var info) &&
            info?.Instance != null;

        private static Assembly PluginAssembly(string guid) =>
            Chainloader.PluginInfos != null &&
            Chainloader.PluginInfos.TryGetValue(guid, out var info) &&
            info?.Instance != null
                ? info.Instance.GetType().Assembly : null;

        // Computes the starting GameState.day for a Climate season (1=Winter..4=Autumn).
        internal static int SeasonStartDay(int season, int yearLength)
        {
            if (yearLength == 365)
            {
                switch (season)
                {
                    case 1: return 355;
                    case 2: return 81;
                    case 3: return 172;
                    case 4: return 264;
                    default: return 0;
                }
            }
            switch (season)
            {
                case 1: return 0;
                case 2: return 23;
                case 3: return 46;
                case 4: return 69;
                default: return 0;
            }
        }

        internal static void ApplyClimateSeason(StartingOptionsSettings options)
        {
            if (options == null || options.StartingSeason <= 0) return;
            var assembly = PluginAssembly(ClimateId);
            if (assembly == null) return;
            try
            {
                var yearLength = 92;
                var configsType = assembly.GetType("Climate.Configs", false);
                var yearEntry = configsType != null
                    ? AccessTools.Field(configsType, "yearLength")?.GetValue(null) as ConfigEntry<int>
                    : null;
                if (yearEntry != null && yearEntry.Value > 0) yearLength = yearEntry.Value;

                var targetDay = SeasonStartDay(options.StartingSeason, yearLength);
                GameState.day = targetDay;

                var windType = assembly.GetType("Climate.WindService", false);
                AccessTools.Method(windType, "UpdateDailyWindField")?.Invoke(null, null);

                var dateUiType = assembly.GetType("Climate.DateTextUI", false);
                var textMesh = dateUiType != null
                    ? AccessTools.Field(dateUiType, "textMesh")?.GetValue(null) as TextMesh
                    : null;
                if (textMesh != null)
                    AccessTools.Method(dateUiType, "UpdateDateText")?.Invoke(null, null);

                Plugin.Instance?.Report($"Climate starting season set to {StartingOptionsSettings.SeasonNames[options.StartingSeason]} (day {targetDay}, {yearLength}-day year).");
            }
            catch (Exception exception)
            {
                Plugin.Instance?.Warn("Climate starting season could not be applied: " + exception.Message);
            }
        }

        internal static int EffectiveSailDamage(StartingOptionsSettings options, Scenario scenario)
        {
            var configured = options?.StartingSailDamage ?? 0;
            if (configured > 0) return configured;
            return scenario?.DefaultSailDamage ?? 0;
        }

        internal static void ApplySailDamage(ResolvedStart selected)
        {
            var percent = EffectiveSailDamage(selected?.Options, selected?.Scenario);
            if (percent <= 0 || selected?.Body == null) return;
            var assembly = PluginAssembly(SailDamageId);
            if (assembly == null) return;
            try
            {
                var modelType = assembly.GetType("SailDamage.DamageModel", false);
                var instance = modelType != null ? AccessTools.Field(modelType, "Instance")?.GetValue(null) : null;
                var boatSailsMethod = modelType != null
                    ? AccessTools.Method(modelType, "BoatSails", new[] { typeof(Transform) })
                    : null;
                var setDamageMethod = modelType != null
                    ? AccessTools.Method(modelType, "SetDamage", new[] { typeof(Sail), typeof(float) })
                    : null;
                if (instance == null || boatSailsMethod == null || setDamageMethod == null)
                {
                    Plugin.Instance?.Warn("Sail Damage integration could not find DamageModel methods; starting sail damage was skipped.");
                    return;
                }
                var sails = boatSailsMethod.Invoke(null, new object[] { selected.Body.transform }) as Sail[];
                if (sails == null || sails.Length == 0)
                {
                    Plugin.Instance?.Warn("Starting sail damage skipped: no sails were found on the starting boat.");
                    return;
                }
                // Never apply 100% damage.
                var fraction = Mathf.Clamp(percent / 100f, 0f, 0.75f);
                var applied = 0;
                foreach (var sail in sails)
                {
                    if (sail == null) continue;
                    setDamageMethod.Invoke(instance, new object[] { sail, fraction });
                    applied++;
                }
                Plugin.Instance?.Report($"Applied {percent}% starting sail damage to {applied} sail(s) via Sail Damage.");
            }
            catch (Exception exception)
            {
                Plugin.Instance?.Warn("Starting sail damage could not be applied: " + exception.Message);
            }
        }

        internal static void ApplyFurstMatePets(ResolvedStart selected)
        {
            var options = selected?.Options;
            if (options == null || selected.Body == null) return;
            if (!options.StartWithShepherd && !options.StartWithCat && !options.StartWithBooby) return;
            var assembly = PluginAssembly(FurstMateId);
            if (assembly == null) return;

            var boatRefs = selected.Body.GetComponentInParent<BoatRefs>() ??
                selected.Body.GetComponentInChildren<BoatRefs>(true);
            if (boatRefs == null)
            {
                Plugin.Instance?.Warn("FurstMate starting pets skipped: BoatRefs was not found on the starting boat.");
                return;
            }

            if (options.StartWithShepherd) TrySpawnShepherd(assembly, boatRefs);
            if (options.StartWithCat) TrySpawnCat(assembly, boatRefs);
            if (options.StartWithBooby) TrySpawnBooby(assembly, boatRefs);
        }

        private static void TrySpawnShepherd(Assembly assembly, BoatRefs boatRefs)
        {
            try
            {
                var pluginType = assembly.GetType("FurstMate.FurstMatePlugin", false);
                var commandType = assembly.GetType("FurstMate.Command", false);
                var instance = pluginType != null ? AccessTools.Field(pluginType, "Instance")?.GetValue(null) : null;
                var spawn = pluginType != null && commandType != null
                    ? AccessTools.Method(pluginType, "Spawn", new[]
                        { typeof(BoatRefs), typeof(Vector3?), commandType, typeof(Vector3?) })
                    : null;
                if (instance == null || spawn == null)
                {
                    Plugin.Instance?.Warn("FurstMate shepherd could not be spawned: FurstMatePlugin.Spawn was not found.");
                    return;
                }
                AccessTools.Field(pluginType, "Food")?.SetValue(instance, 100f);
                AccessTools.Field(pluginType, "BoughtDay")?.SetValue(instance, GameState.day);
                var heel = Enum.ToObject(commandType, 0);
                spawn.Invoke(instance, new object[] { boatRefs, null, heel, null });
                Plugin.Instance?.Report("Spawned starting Aestrin Shepherd aboard via FurstMate.");
            }
            catch (Exception exception)
            {
                Plugin.Instance?.Warn("FurstMate shepherd could not be spawned: " + exception.Message);
            }
        }

        private static void TrySpawnCat(Assembly assembly, BoatRefs boatRefs)
        {
            try
            {
                var keeperType = assembly.GetType("FurstMate.CatKeeper", false);
                var instance = keeperType != null ? AccessTools.Field(keeperType, "Instance")?.GetValue(null) : null;
                var spawn = keeperType != null
                    ? AccessTools.Method(keeperType, "Spawn", new[]
                        { typeof(BoatRefs), typeof(Vector3?), typeof(Vector3?) })
                    : null;
                if (instance == null || spawn == null)
                {
                    Plugin.Instance?.Warn("FurstMate cat could not be spawned: CatKeeper.Spawn was not found.");
                    return;
                }
                AccessTools.Field(keeperType, "Food")?.SetValue(instance, 100f);
                spawn.Invoke(instance, new object[] { boatRefs, null, null });
                Plugin.Instance?.Report("Spawned starting Tavern Cat aboard via FurstMate.");
            }
            catch (Exception exception)
            {
                Plugin.Instance?.Warn("FurstMate cat could not be spawned: " + exception.Message);
            }
        }

        private static void TrySpawnBooby(Assembly assembly, BoatRefs boatRefs)
        {
            try
            {
                var keeperType = assembly.GetType("FurstMate.BirdKeeper", false);
                var instance = keeperType != null ? AccessTools.Field(keeperType, "Instance")?.GetValue(null) : null;
                var tame = keeperType != null ? AccessTools.Method(keeperType, "Tame", Type.EmptyTypes) : null;
                var come = keeperType != null
                    ? AccessTools.Method(keeperType, "Come", new[] { typeof(BoatRefs), typeof(bool) })
                    : null;
                if (instance == null || tame == null || come == null)
                {
                    Plugin.Instance?.Warn("FurstMate booby could not be spawned: BirdKeeper methods were not found.");
                    return;
                }
                tame.Invoke(instance, null);
                come.Invoke(instance, new object[] { boatRefs, true });
                Plugin.Instance?.Report("Spawned starting Wild Booby aboard via FurstMate.");
            }
            catch (Exception exception)
            {
                Plugin.Instance?.Warn("FurstMate booby could not be spawned: " + exception.Message);
            }
        }
    }
}
