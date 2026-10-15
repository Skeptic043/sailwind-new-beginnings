using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using NewBeginnings;

// IMGUI drawing is the boundary. The actual drawer and real ConfigFile run,
// including SettingChanged and Plugin's extracted reload behavior.
namespace UnityEngine
{
    public class GUIStyle
    {
        public bool wordWrap;
        public GUIStyle()
        {
        }

        public GUIStyle(GUIStyle source)
        {
        }
    }

    public class GUISkin
    {
        public GUIStyle label = new GUIStyle(), button = new GUIStyle();
    }

    public static class GUI
    {
        public static GUISkin skin = new GUISkin();
    }

    public class GUILayoutOption
    {
    }

    public static class GUILayout
    {
        internal static List<string> Drawn = new List<string>();
        internal static bool Open, Reveal;
        internal static bool? Secret;
        public sealed class VerticalScope : IDisposable
        {
            public VerticalScope(params GUILayoutOption[] options)
            {
            }

            public void Dispose()
            {
            }
        }

        public static GUILayoutOption ExpandWidth(bool value) => new GUILayoutOption();
        public static GUILayoutOption MinWidth(float value) => new GUILayoutOption();
        public static bool Toggle(bool value, string label, GUIStyle style, params GUILayoutOption[] options) => Toggle(value, label, options);
        public static bool Toggle(bool value, string label, params GUILayoutOption[] options)
        {
            Drawn.Add(label);
            if (label.Contains("Spoilers"))
                return Open;
            if (label.StartsWith("Secret islands"))
                return Secret ?? value;
            return value;
        }

        public static bool Button(string label, params GUILayoutOption[] options)
        {
            Drawn.Add(label);
            return Reveal;
        }

        public static void Label(string label, GUIStyle style, params GUILayoutOption[] options) => Drawn.Add(label);
    }
}

internal static class SpoilerDrawerChecks
{
    internal static void Run(Plugin plugin, Action<bool, string> check)
    {
        var pirate = plugin.Config[new ConfigDefinition("Spoilers", "EnablePirateIsland")];
        var saffron = plugin.Config[new ConfigDefinition("Spoilers", "EnableSaffronIsland")];
        var chronos = plugin.Config[new ConfigDefinition("Random Port Pools", "Chronos")];
        var anchor = plugin.Config[new ConfigDefinition("Advanced", "SpoilerOptions")];
        var metadata = anchor.Description.Tags.OfType<ConfigurationManagerAttributes>().Single();
        check(pirate.Description.Tags.OfType<ConfigurationManagerAttributes>().Single().Browsable == false && saffron.Description.Tags.OfType<ConfigurationManagerAttributes>().Single().Browsable == false && chronos.Description.Tags.OfType<ConfigurationManagerAttributes>().Single().Browsable == false, "spoiler config entries remain visible in Configuration Manager");
        check(metadata.CustomDrawer != null && metadata.HideSettingName == true && metadata.HideDefaultButton == true, "spoiler custom drawer metadata incomplete");
        Action<bool, bool> draw = (open, reveal) =>
        {
            UnityEngine.GUILayout.Drawn.Clear();
            UnityEngine.GUILayout.Open = open;
            UnityEngine.GUILayout.Reveal = reveal;
            metadata.CustomDrawer(anchor);
        };
        Func<bool> namesDrawn = () => UnityEngine.GUILayout.Drawn.Any(text => text.Contains("Pirate") || text.Contains("Saffron") || text.Contains("Chronos"));
        draw(false, false);
        check(!namesDrawn() && !plugin.Settings.EnablePirateIsland && !plugin.Settings.EnableSaffronIsland, "collapsed drawer revealed names or enabled hidden locations");
        draw(true, false);
        check(!namesDrawn() && UnityEngine.GUILayout.Drawn.Contains("Reveal spoiler locations"), "opening Spoilers revealed names before explicit opt-in");
        draw(true, true);
        check(namesDrawn() && !plugin.Settings.EnablePirateIsland && !plugin.Settings.EnableSaffronIsland, "revealing names changed starting options");
        var reloads = plugin.ReloadEvents;
        UnityEngine.GUILayout.Secret = true;
        draw(true, false);
        UnityEngine.GUILayout.Secret = null;
        check(plugin.Settings.SecretIslandsEnabled && (bool)pirate.BoxedValue && (bool)saffron.BoxedValue && (bool)chronos.BoxedValue && plugin.ReloadEvents == reloads + 3, "group drawer did not apply all existing keys through event-driven reload");
        pirate.BoxedValue = false;
        draw(true, false);
        check(!plugin.Settings.SecretIslandsEnabled && plugin.Settings.AnySecretIslandsEnabled && !(bool)pirate.BoxedValue && UnityEngine.GUILayout.Drawn.Contains("Secret islands (some enabled)"), "drawer overwrote externally changed mixed state");
        UnityEngine.GUILayout.Secret = true;
        draw(true, false);
        UnityEngine.GUILayout.Secret = null;
        check(plugin.Settings.SecretIslandsEnabled, "clicking mixed group did not enable all secrets");
        UnityEngine.GUILayout.Secret = false;
        draw(true, false);
        UnityEngine.GUILayout.Secret = null;
        check(!plugin.Settings.AnySecretIslandsEnabled && !(bool)pirate.BoxedValue && !(bool)saffron.BoxedValue && !(bool)chronos.BoxedValue, "group drawer left a secret enabled when switched off");
        draw(false, false);
        draw(true, false);
        check(!namesDrawn(), "reopening Spoilers skipped explicit spoiler reveal");
        Plugin.Instance = null;
        UnityEngine.GUILayout.Drawn.Clear();
        metadata.CustomDrawer(anchor);
        check(UnityEngine.GUILayout.Drawn.Count == 0, "retained drawer drew after plugin teardown");
        Plugin.Instance = plugin;
    }
}
