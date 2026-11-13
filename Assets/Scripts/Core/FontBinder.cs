using UnityEngine;
using UnityEngine.UI;

namespace RebootEarth
{
    /// <summary>
    /// The Thai-capable font comes from the operating system (Leelawadee UI), so it cannot be
    /// saved in a scene. This puts it back on every Text / TextMesh below this object, in edit
    /// mode and in play mode.
    /// </summary>
    [ExecuteAlways]
    public class FontBinder : MonoBehaviour
    {
        void OnEnable() => Bind();

        public void Bind()
        {
            var f = Art.Font;
            foreach (var t in GetComponentsInChildren<Text>(true))
            {
                if (t.font != f) t.font = f;
                t.SetAllDirty();
            }
            foreach (var tm in GetComponentsInChildren<TextMesh>(true))
            {
                if (tm.font != f) tm.font = f;
                var mr = tm.GetComponent<MeshRenderer>();
                if (mr != null && mr.sharedMaterial != f.material) mr.sharedMaterial = f.material;
                RefreshMesh(tm);
            }
        }

        static void RefreshMesh(TextMesh tm)
        {
            // dynamic OS font: glyphs must be in the font texture before the mesh is built
            tm.font.RequestCharactersInTexture(tm.text, tm.fontSize, tm.fontStyle);
            var s = tm.text;
            tm.text = "";
            tm.text = s;
        }

        void Awake() => Font.textureRebuilt += OnFontRebuilt;
        void OnDestroy() => Font.textureRebuilt -= OnFontRebuilt;

        void OnFontRebuilt(Font f)
        {
            if (this == null || f != Art.Font) return;
            foreach (var tm in GetComponentsInChildren<TextMesh>(true)) { var s = tm.text; tm.text = ""; tm.text = s; }
        }
    }
}
