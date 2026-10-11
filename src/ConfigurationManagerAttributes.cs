using System;
using BepInEx.Configuration;

namespace NewBeginnings
{
    // Configuration Manager reads these optional config-tag fields by name.
    // The mod does not take a dependency on Configuration Manager.
    internal sealed class ConfigurationManagerAttributes
    {
        public Action<ConfigEntryBase> CustomDrawer;
        public bool? Browsable;
        public bool? HideDefaultButton;
        public bool? HideSettingName;
        public string DispName;
        public string Category;
    }
}
