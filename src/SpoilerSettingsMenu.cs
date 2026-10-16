using BepInEx.Configuration;
using UnityEngine;

namespace NewBeginnings
{
    // Configuration Manager calls this drawer. No polling or game-menu overlay.
    internal sealed class SpoilerSettingsMenu
    {
        private readonly SelectionSettings settings;
        private readonly ConfigEntry<bool> pirate, saffron, chronos;
        private bool expanded, revealed;
        private GUIStyle noteStyle;

        internal SpoilerSettingsMenu(SelectionSettings settings, ConfigEntry<bool> pirate, ConfigEntry<bool> saffron,
            ConfigEntry<bool> chronos)
        {
            this.settings = settings; this.pirate = pirate; this.saffron = saffron; this.chronos = chronos;
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
                GUILayout.Label("Secret islands include Chronos, Pirate Island and Saffron Island. Enabled locations are available for manual starts. Random starts also require Small Islands.",
                    noteStyle, GUILayout.ExpandWidth(true));
                var enabled = settings.SecretIslandsEnabled;
                var next = GUILayout.Toggle(enabled, settings.AnySecretIslandsEnabled && !enabled
                    ? "Secret islands (some enabled)" : "Secret islands");
                if (next != enabled)
                {
                    pirate.Value = next;
                    saffron.Value = next;
                    chronos.Value = next;
                }
            }
        }
    }
}
