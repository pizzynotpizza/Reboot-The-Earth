using System.Collections.Generic;
using UnityEngine;

namespace RebootEarth
{
    /// <summary>Color palette: dusty orange future city (Wall-E like) and the green "after".</summary>
    public static class Palette
    {
        public static readonly Color SkyTopDirty = Hex("C98A4B");
        public static readonly Color SkyBottomDirty = Hex("F2C27B");
        public static readonly Color SkyTopClean = Hex("4F9BD9");
        public static readonly Color SkyBottomClean = Hex("BFE6F2");
        public static readonly Color HazeDirty = Hex("E7B06A");
        public static readonly Color CityFar = Hex("A8693C");
        public static readonly Color CityMid = Hex("7E4A2C");
        public static readonly Color CityNear = Hex("4E2E1E");
        public static readonly Color Ground = Hex("6B4A30");
        public static readonly Color GroundClean = Hex("5E8C3A");
        public static readonly Color Junk1 = Hex("8C6A4A");
        public static readonly Color Junk2 = Hex("5B4636");
        public static readonly Color Rust = Hex("A4532A");
        public static readonly Color Suit = Hex("F2A516");
        public static readonly Color Ink = Hex("2A1B12");
        public static readonly Color Cream = Hex("FFF4DE");
        public static readonly Color Panel = new Color(0.12f, 0.08f, 0.06f, 0.78f);
        public static readonly Color P1 = Hex("38B6FF");
        public static readonly Color P2 = Hex("FF8A3D");
        public static readonly Color Water = Hex("3FC3FF");
        public static readonly Color Sun = Hex("FFD23F");
        public static readonly Color Leaf = Hex("6CC24A");
        public static readonly Color LeafDark = Hex("3E8E3A");
        public static readonly Color Gold = Hex("FFD54A");
        public static readonly Color Good = Hex("7CE36B");
        public static readonly Color Bad = Hex("FF5A4E");

        public static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString("#" + h, out var c);
            return c;
        }
    }

    /// <summary>Procedural sprites (no image files needed). All white, tint with color.</summary>
    public static class Art
    {
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        static Font font;

        public static Font Font
        {
            get
            {
                if (font == null)
                {
                    font = Font.CreateDynamicFontFromOSFont(new[] { "Leelawadee UI", "Leelawadee", "Tahoma", "Arial" }, 48);
                    // never written into scene files; FontBinder re-applies it on load
                    font.hideFlags = HideFlags.HideAndDontSave;
                    if (font.material != null) font.material.hideFlags = HideFlags.HideAndDontSave;
                }
                return font;
            }
        }

        /// <summary>1 x 1 world unit square.</summary>
        public static Sprite Square => Get("square", () => Make(4, 4, (x, y) => 1f, 4, Vector4.zero));

        /// <summary>Circle, 1 world unit diameter.</summary>
        public static Sprite Circle => Get("circle", () => Make(128, 128, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(63.5f, 63.5f));
            return Mathf.Clamp01(64f - d);
        }, 128, Vector4.zero));

        /// <summary>Soft radial glow, 1 world unit.</summary>
        public static Sprite Glow => Get("glow", () => Make(128, 128, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(63.5f, 63.5f)) / 64f;
            float a = Mathf.Clamp01(1f - d);
            return a * a;
        }, 128, Vector4.zero));

        /// <summary>Rounded rectangle for SpriteDrawMode.Sliced (set renderer.size).</summary>
        public static Sprite Rounded => Get("rounded", () => Make(64, 64, (x, y) =>
        {
            const float r = 16f;
            float cx = Mathf.Clamp(x + 0.5f, r, 64 - r);
            float cy = Mathf.Clamp(y + 0.5f, r, 64 - r);
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
            return Mathf.Clamp01(r - d);
        }, 64, new Vector4(16, 16, 16, 16)));

        /// <summary>Triangle pointing up, 1 unit.</summary>
        public static Sprite Triangle => Get("triangle", () => Make(128, 128, (x, y) =>
        {
            float half = (1f - y / 127f) * 64f;
            return Mathf.Clamp01(half - Mathf.Abs(x - 63.5f));
        }, 128, Vector4.zero));

        /// <summary>Ring (outline circle), 1 unit.</summary>
        public static Sprite Ring => Get("ring", () => Make(128, 128, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(63.5f, 63.5f));
            return Mathf.Clamp01(64f - d) * Mathf.Clamp01(d - 52f);
        }, 128, Vector4.zero));

        /// <summary>Vertical gradient sprite (bottom -> top), 1 unit wide x 4 units tall.</summary>
        public static Sprite Gradient(Color bottom, Color top, string key)
        {
            return Get("grad_" + key, () =>
            {
                // 32 x 128 pixels, pixelsPerUnit 32 -> 1 unit wide (scale x/y = world size, y is 4 units)
                var tex = new Texture2D(32, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 128; y++)
                {
                    var c = Color.Lerp(bottom, top, y / 127f);
                    for (int x = 0; x < 32; x++) tex.SetPixel(x, y, c);
                }
                tex.Apply();
                return Sprite.Create(tex, new Rect(0, 0, 32, 128), new Vector2(0.5f, 0.5f), 32f);
            });
        }

        static Sprite Get(string key, System.Func<Sprite> make)
        {
            if (!cache.TryGetValue(key, out var s) || s == null)
            {
#if UNITY_EDITOR
                // edit mode (scene baking): sprites become real PNG assets so the scene keeps them
                if (!Application.isPlaying) s = ArtBaker.LoadSprite(key);
                if (s == null)
                {
                    s = make();
                    s.name = key;
                    if (!Application.isPlaying) s = ArtBaker.BakeSprite(key, s);
                }
#else
                s = make();
                s.name = key;
#endif
                cache[key] = s;
            }
            return s;
        }

        static Sprite Make(int w, int h, System.Func<int, int, float> alpha, float ppu, Vector4 border)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color(1, 1, 1, alpha(x, y));
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), ppu, 0,
                                 SpriteMeshType.FullRect, border);
        }

        // ---------------------------------------------------------------- world helpers
        public static SpriteRenderer Spr(Transform parent, string name, Sprite sprite, Color color,
                                            Vector2 pos, Vector2 scale, int order = 0, float z = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(pos.x, pos.y, z);
            go.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        public static SpriteRenderer Box(Transform parent, string name, Color color, Vector2 pos,
                                         Vector2 size, int order = 0, float z = 0f, float rotation = 0f)
        {
            var sr = Spr(parent, name, Square, color, pos, size, order, z);
            sr.transform.localRotation = Quaternion.Euler(0, 0, rotation);
            return sr;
        }

        public static SpriteRenderer RoundBox(Transform parent, string name, Color color, Vector2 pos,
                                              Vector2 size, int order = 0, float z = 0f)
        {
            var sr = Spr(parent, name, Rounded, color, pos, Vector2.one, order, z);
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = size;
            return sr;
        }

        public static TextMesh Text(Transform parent, string text, Vector2 pos, float size, Color color,
                                    int order = 20, TextAnchor anchor = TextAnchor.MiddleCenter, float z = -0.5f)
        {
            var go = new GameObject("Text_" + text);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(pos.x, pos.y, z);
            var tm = go.AddComponent<TextMesh>();
            tm.font = Font;
            tm.text = text;
            tm.fontSize = 64;
            tm.characterSize = size / 10f;
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = Font.material;
            go.AddComponent<FontBinder>();
            mr.sortingOrder = order;
            return tm;
        }

        // ---------------------------------------------------------------- 3D helpers
        public static Material Lit(Color c, float gloss = 0.2f)
        {
            var m = new Material(Shader.Find("Standard")) { color = c };
            m.SetFloat("_Glossiness", gloss);
            m.name = "Lit_" + ColorUtility.ToHtmlStringRGBA(c) + "_" + Mathf.RoundToInt(gloss * 100);
            return Keep(m);
        }

        /// <summary>Edit mode: save the material as an asset (same name = same asset).</summary>
        static Material Keep(Material m)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return ArtBaker.BakeMaterial(m);
#endif
            return m;
        }

        public static Material Transparent(Color c)
        {
            var m = new Material(Shader.Find("Standard"));
            m.SetFloat("_Mode", 3f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
            m.color = c;
            m.SetFloat("_Glossiness", 0.95f);
            m.name = "Glass_" + ColorUtility.ToHtmlStringRGBA(c);
            return Keep(m);
        }

        public static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 pos,
                                      Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(col); else Object.DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }
    }
}
