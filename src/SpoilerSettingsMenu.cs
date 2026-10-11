using BepInEx.Configuration;
using UnityEngine;

namespace NewBeginnings
{
    // Configuration Manager calls this drawer. No polling or game-menu overlay.
    internal sealed class SpoilerSettingsMenu
    {
        private readonly SelectionSettings settings;
        private readonly ConfigEntry<bool> pirate, saffron;
        private bool expanded, revealed;
        private GUIStyle noteStyle;

        internal SpoilerSettingsMenu(SelectionSettings settings, ConfigEntry<bool> pirate, ConfigEntry<bool> saffron)
        {
            this.settings = settings; this.pirate = pirate; this.saffron = saffron;
        }

        internal void Draw(ConfigEntryBase entry)
        {
            if (Plugin.Instance == null || !ReferenceEquals(Plugin.Instance.Settings, settings)) return;
            if (noteStyle == null) noteStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            using (new GUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.MinWidth(0)))
            {
                expanded = GUILayout.Toggle(expanded, expanded ? "▼ Spoilers" : "▶ Spoilers", GUI.skin.button);
                if (!expanded) { revealed = false; return; }
                if (!revealed)
                {
                    GUILayout.Label("These options reveal hidden starting locations.", noteStyle, GUILayout.ExpandWidth(true));
                    revealed = GUILayout.Button("Reveal spoiler locations");
                }
                if (!revealed) return;
                GUILayout.Label("Enabled locations are available manually and in the Small Islands random pool.",
                    noteStyle, GUILayout.ExpandWidth(true));
                var allowPirate = GUILayout.Toggle(pirate.Value, "Enable Pirate Island");
                if (allowPirate != pirate.Value) pirate.Value = allowPirate;
                var allowSaffron = GUILayout.Toggle(saffron.Value, "Enable Saffron Island");
                if (allowSaffron != saffron.Value) saffron.Value = allowSaffron;
            }
        }
    }
}
