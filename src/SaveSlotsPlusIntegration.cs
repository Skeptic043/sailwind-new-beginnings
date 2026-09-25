using UnityEngine;

namespace NewBeginnings
{
    internal static class SaveSlotsPlusIntegration
    {
        internal static bool TryLayoutNameInput(Transform scroll, bool visible)
        {
            if (scroll == null) return false;
            // Save Slots Plus adds its input to the native region-name scroll
            // in Start, after our Awake. Inspect the actual component each time
            // so a scroll hidden before that attachment can be recovered.
            foreach (var component in scroll.GetComponents<MonoBehaviour>())
            {
                if (component == null ||
                    component.GetType().FullName != "SaveSlotsPlus.SaveNameInput") continue;
                // Retain SSP's text, scale and rotation, with room between our
                // selection status and the native Continue button.
                scroll.localPosition = new Vector3(0f, .36f, scroll.localPosition.z);
                // Deactivating the object suspends text entry in the exclusion
                // editor. Reactivation preserves SSP's current name and cursor;
                // calling its Activate method here would clear the typed name.
                if (scroll.gameObject.activeSelf != visible)
                    scroll.gameObject.SetActive(visible);
                return true;
            }
            return false;
        }
    }
}
