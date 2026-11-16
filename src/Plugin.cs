using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    [BepInPlugin(Id, "New Beginnings", "1.6.4")]
    [BepInDependency(ScrambledSeasIntegration.Id, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.nandbrew.sailwinddifficulty", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "com.skeptic043.sailwind.newbeginnings";
        private Harmony harmony;
        internal Harmony Harmony => harmony;
        private SelectionConfig selection;
        private StartingOptionsConfig startingOptions;
        private readonly System.Random random = new System.Random();
        // Scenario profiles are created on first use. While one is active, the
        // menu edits and the next start use its own saved choices; the main
        // file keeps the menu switch, spoiler drawer and compatibility records.
        private readonly System.Collections.Generic.Dictionary<string, ScenarioProfile> profiles =
            new System.Collections.Generic.Dictionary<string, ScenarioProfile>(StringComparer.Ordinal);
        private ScenarioProfile activeProfile;

        internal static Plugin Instance { get; private set; }
        internal SelectionSettings MainSettings => selection.Settings;
        internal SelectionSettings Settings => (activeProfile?.Selection ?? selection).Settings;
        internal IndividualExclusions Exclusions => (activeProfile?.Selection ?? selection).IndividualExclusions;
        internal SelectionOverrides Overrides => selection.Overrides;
        internal StartingOptionsSettings StartOptions => (activeProfile?.Options ?? startingOptions).Settings;
        internal Scenario ActiveScenario => activeProfile?.Scenario;
        // Sailwind's own panel is shown for this opening of the new-game menu.
        // Each opening starts from ReplaceNewGameMenu; the menu's buttons
        // switch it for that opening only.
        internal bool NativeMenu { get; set; }
        internal event Action SettingsReloaded;
        internal event Action ProfileChanged;
        private int configWrites;

        private void Awake()
        {
            Instance = this;
            try
            {
                selection = new SelectionConfig(Config);
                startingOptions = new StartingOptionsConfig(Config);
                harmony = new Harmony(Id);
                harmony.PatchAll(typeof(Plugin).Assembly);
                ScrambledSeasIntegration.Initialize(harmony);
                // Subscribe last, so a failed initialization leaves no handler.
                Config.SettingChanged += OnConfigSettingChanged;
                if (FixedStart.CanSelect)
                    Logger.LogInfo("New Beginnings loaded with port and boat selection.");
                else
                    Logger.LogWarning("New Beginnings is inactive for this game build (see the warnings above); Sailwind's normal new-game menu is used.");
            }
            catch (Exception exception)
            {
                // PatchAll may have installed earlier patches before a changed
                // Sailwind method causes a later one to fail. Leave the native
                // start path alone instead of running with an incomplete set.
                Instance = null;
                enabled = false;
                try { harmony?.UnpatchSelf(); }
                catch (Exception unpatchException)
                {
                    Logger.LogError("New Beginnings could not remove its partial patches: " +
                        unpatchException);
                }
                Logger.LogError("New Beginnings disabled after initialization failed; " +
                    "check game/mod compatibility and configuration: " + exception);
            }
        }

        [HarmonyPatch(typeof(StartMenu), "Awake")]
        [HarmonyAfter("com.nandbrew.scrambledseas")]
        private static class MenuPatch
        {
            private static void Postfix(StartMenu __instance)
            {
                var plugin = Instance;
                if (plugin == null) return;
                // An unhooked start sequence or unknown Scrambled Seas build
                // keeps Sailwind's own menu and new game untouched.
                if (!FixedStart.CanSelect) return;
                try
                {
                    if (MenuUi.Attach(__instance, plugin.Settings, () => plugin.Overrides,
                        plugin.Exclusions,
                        plugin.SaveSelection) == null)
                        plugin.Warn("Start menu controls could not be attached; saved settings remain available.");
                }
                catch (Exception exception)
                {
                    plugin.Error("Start menu controls could not be built; saved settings remain available.", exception);
                }
            }
        }

        private void SaveSelection() => WriteConfig((activeProfile?.Selection ?? selection).Save);
        internal void SaveStartingOptions() => WriteConfig((activeProfile?.Options ?? startingOptions).Save);

        // Rereads the scenario's own file, so hand edits made while the game
        // runs are picked up, then makes it the profile the menu edits.
        internal void ActivateScenario(Scenario scenario)
        {
            try
            {
                if (!profiles.TryGetValue(scenario.Id, out var profile))
                    profiles[scenario.Id] = profile = new ScenarioProfile(scenario, selection.Settings);
                else profile.Reload(selection.Settings);
                activeProfile = profile;
            }
            catch (Exception exception)
            {
                Error("The " + scenario.Name + " scenario settings could not be loaded.", exception);
                return;
            }
            ProfileChanged?.Invoke();
        }

        internal void DeactivateScenario()
        {
            if (activeProfile == null) return;
            activeProfile = null;
            ProfileChanged?.Invoke();
        }

        // Each entry assignment raises SettingChanged synchronously. Our own
        // saves assign entries one by one from settings already in memory, so
        // ignore those notifications instead of rereading a half-written mix.
        private void WriteConfig(Action save)
        {
            configWrites++;
            try { save(); }
            finally { configWrites--; }
        }

        // Configuration Manager assigns entries at runtime. Reread them into the
        // shared settings objects, so the menu and the next start use the change
        // and a later menu save writes it back instead of the startup value.
        private void OnConfigSettingChanged(object sender, SettingChangedEventArgs args)
        {
            if (configWrites != 0) return;
            WriteConfig(() => selection.KeepShipyardFiltersExclusive(args.ChangedSetting));
            ReloadSettings();
        }

        private void ReloadSettings()
        {
            try
            {
                selection.Read();
                startingOptions.Read();
                SettingsReloaded?.Invoke();
            }
            catch (Exception exception)
            {
                Error("New Beginnings could not apply changed settings.", exception);
            }
        }

        // Called when the new-game menu opens, to pick up hand edits made to the
        // file while the game runs. Config.Reload assigns changed entries one by
        // one; suppress those notifications and the file rewrite each would
        // trigger, then reread once. Unchanged entries raise nothing.
        internal void ReloadConfigFile()
        {
            var saveOnSet = Config.SaveOnConfigSet;
            configWrites++;
            try
            {
                if (!System.IO.File.Exists(Config.ConfigFilePath)) return;
                Config.SaveOnConfigSet = false;
                Config.Reload();
            }
            catch (Exception exception)
            {
                Warn("New Beginnings could not reread its configuration file; current settings remain in use. " +
                    exception.Message);
            }
            finally
            {
                Config.SaveOnConfigSet = saveOnSet;
                configWrites--;
            }
            ReloadSettings();
        }

        internal bool TryChooseStart(out StartPair pair, out string reason)
        {
            pair = null;
            reason = null;
            // Overrides are current: every entry change and file reload rereads them.
            var catalog = SelectionCatalog.Discover(selection.Overrides);
            if (catalog.TryChoose(Settings, Exclusions, random, out pair, out reason))
                return true;
            if (catalog.Rejections.Count > 0)
                reason += " " + catalog.Rejections[0];
            return false;
        }

        private void Update()
        {
            if (GameState.currentlyLoading) FixedStart.ClearStartTiming();
            else if (GameState.playing || StartArrival.WaitingForPlayerInput()) FixedStart.ReportGameplayReached();
            if (StartArrival.HasSite && GameState.currentlyLoading)
            {
                // A cargo anchor can outlive the temporary player target.
                // Loading any save must invalidate both lifetimes before the
                // old scene and its recovery marker disappear.
                StartArrival.CancelTracking();
                StartArrival.ClearSite();
            }
            StartArrival.Tick();
            StarterCargo.Tick();
            ScenarioAlcohol.Tick();
        }

        internal void Report(string message) => Logger.LogInfo(message);
        internal void DebugLog(string message) => Logger.LogDebug(message);
        internal void Warn(string message) => Logger.LogWarning(message);
        internal void Error(string message, Exception exception) =>
            Logger.LogError(exception == null ? message : message + " " + exception);

        private void OnDestroy()
        {
            Config.SettingChanged -= OnConfigSettingChanged;
            FixedStart.CancelStartWork();
            harmony?.UnpatchSelf();
            if (ReferenceEquals(Instance, this)) Instance = null;
        }
    }
}
