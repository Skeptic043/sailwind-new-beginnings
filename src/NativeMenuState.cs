using System.Collections.Generic;
using UnityEngine;

namespace NewBeginnings
{
    // Sailwind's new-game panel as it was before New Beginnings rearranged it.
    // Captured once, after Scrambled Seas' own layout, so turning
    // ReplaceNewGameMenu off restores whichever panel the player would have had.
    internal sealed class NativeMenuState
    {
        private sealed class Placement
        {
            internal Transform Target;
            internal bool Active;
            internal Vector3 Position;
            internal Vector3 Scale;
        }

        private sealed class Text
        {
            internal TextMesh Target;
            internal string Value;
            internal Color Color;
            internal Font Font;
            internal FontStyle Style;
            internal int Size;
            internal float CharacterSize;
            internal Material Material;
        }

        private readonly List<Placement> placements = new List<Placement>();
        private readonly List<Text> texts = new List<Text>();
        private StartMenuButton confirm;
        private bool confirmUnclickable;
        private string confirmLookText;

        // Every native object MenuUi hides, moves, retitles or recolors.
        internal static NativeMenuState Capture(Transform choiceRoot, Transform confirm,
            Transform back, StartMenuButton nativeConfirm, TextMesh title)
        {
            var state = new NativeMenuState();
            foreach (var name in new[] { "button left", "button right", "start deco Al'Ankh",
                "start deco Emerald", "start deco Medi", "bg (1)", "bg" })
                state.Record(choiceRoot.Find(name));
            state.Record(confirm);
            state.Record(back);
            state.Record(title.transform);
            state.Record(title);
            state.Record(confirm.Find("text")?.GetComponent<TextMesh>());
            if (back != null)
                foreach (var text in back.GetComponentsInChildren<TextMesh>(true)) state.Record(text);
            state.confirm = nativeConfirm;
            state.confirmUnclickable = nativeConfirm.unclickable;
            state.confirmLookText = nativeConfirm.lookText;
            return state;
        }

        internal void Restore()
        {
            foreach (var item in placements)
            {
                if (item.Target == null) continue;
                item.Target.localPosition = item.Position;
                item.Target.localScale = item.Scale;
                if (item.Target.gameObject.activeSelf != item.Active) item.Target.gameObject.SetActive(item.Active);
            }
            foreach (var item in texts)
            {
                if (item.Target == null) continue;
                item.Target.text = item.Value;
                item.Target.color = item.Color;
                item.Target.font = item.Font;
                item.Target.fontStyle = item.Style;
                item.Target.fontSize = item.Size;
                item.Target.characterSize = item.CharacterSize;
                var renderer = item.Target.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = item.Material;
            }
            if (confirm != null)
            {
                confirm.unclickable = confirmUnclickable;
                confirm.lookText = confirmLookText;
            }
        }

        private void Record(Transform target)
        {
            if (target == null) return;
            placements.Add(new Placement { Target = target, Active = target.gameObject.activeSelf,
                Position = target.localPosition, Scale = target.localScale });
        }

        private void Record(TextMesh target)
        {
            if (target == null) return;
            texts.Add(new Text { Target = target, Value = target.text, Color = target.color,
                Font = target.font, Style = target.fontStyle, Size = target.fontSize,
                CharacterSize = target.characterSize, Material = target.GetComponent<Renderer>()?.sharedMaterial });
        }
    }
}
