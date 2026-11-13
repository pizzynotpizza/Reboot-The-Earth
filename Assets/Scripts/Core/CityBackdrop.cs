using System.Collections.Generic;
using UnityEngine;

namespace RebootEarth
{
    /// <summary>
    /// Procedural future city: dusty orange smog, towers, piles of e-waste (Wall-E mood).
    /// Clean (0..1) blends it to a blue sky with grass, small trees and no junk.
    /// Camera: orthographic size 5.4 (19.2 x 10.8 units at 16:9).
    /// </summary>
    [ExecuteAlways]
    public class CityBackdrop : MonoBehaviour
    {
        [Range(0, 1)] public float clean;
        public float groundY = -4.2f;
        public int seed = 7;

        // built once (edit mode bake or at runtime) and saved in the scene
        [SerializeField, HideInInspector] SpriteRenderer skyDirty, skyClean, sun, haze, ground, groundClean;
        [SerializeField, HideInInspector] List<SpriteRenderer> buildings = new List<SpriteRenderer>();
        [SerializeField, HideInInspector] List<Color> buildingColors = new List<Color>();
        [SerializeField, HideInInspector] List<Transform> junk = new List<Transform>();
        [SerializeField, HideInInspector] List<Transform> green = new List<Transform>();
        [SerializeField, HideInInspector] List<SpriteRenderer> dust = new List<SpriteRenderer>();
        [SerializeField, HideInInspector] List<Transform> grassT = new List<Transform>();
        [SerializeField, HideInInspector] List<float> grassH = new List<float>();
        float shownClean = -1f;

        public static CityBackdrop Create(Transform parent = null, float clean = 0f, int seed = 7)
        {
            var go = new GameObject("CityBackdrop");
            if (parent) go.transform.SetParent(parent, false);
            var b = go.AddComponent<CityBackdrop>();
            b.seed = seed;
            b.clean = clean;
            b.Build();
            return b;
        }

        void Build()
        {
            var rnd = new System.Random(seed);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var t = transform;

            skyDirty = Art.Spr(t, "SkyDirty", Art.Gradient(Palette.SkyBottomDirty, Palette.SkyTopDirty, "dirty"),
                               Color.white, new Vector2(0, 0), new Vector2(26, 3.2f), -100, 60);
            skyClean = Art.Spr(t, "SkyClean", Art.Gradient(Palette.SkyBottomClean, Palette.SkyTopClean, "clean"),
                               new Color(1, 1, 1, 0), new Vector2(0, 0), new Vector2(26, 3.2f), -99, 59);
            sun = Art.Spr(t, "Sun", Art.Glow, new Color(1f, 0.9f, 0.6f, 0.9f), new Vector2(4.5f, 2.8f),
                          new Vector2(6, 6), -98, 58);
            Art.Spr(t, "SunDisc", Art.Circle, new Color(1f, 0.95f, 0.8f, 0.85f), new Vector2(4.5f, 2.8f),
                    new Vector2(1.3f, 1.3f), -97, 57.5f);

            // 3 skyline layers (far -> near)
            Color[] layerCol = { Palette.CityFar, Palette.CityMid, Palette.CityNear };
            for (int layer = 0; layer < 3; layer++)
            {
                float x = -13f;
                while (x < 13f)
                {
                    float w = R(0.8f, 2.2f) - layer * 0.15f;
                    float h = R(2.5f, 7.5f) - layer * 1.6f;
                    float baseY = groundY + 0.4f - layer * 0.1f;
                    var b = Art.Box(t, "Tower", layerCol[layer], new Vector2(x + w / 2, baseY + h / 2),
                                    new Vector2(w, h), -90 + layer * 3, 50 - layer * 5);
                    buildings.Add(b); buildingColors.Add(layerCol[layer]);
                    if (layer > 0 && rnd.NextDouble() < 0.6)
                    {
                        // dim windows
                        for (float wy = baseY + 0.5f; wy < baseY + h - 0.4f; wy += 0.55f)
                            for (float wx = x + 0.25f; wx < x + w - 0.2f; wx += 0.42f)
                                if (rnd.NextDouble() < 0.35)
                                {
                                    var win = Art.Box(t, "Win", new Color(1f, 0.8f, 0.45f, 0.35f),
                                                      new Vector2(wx + 0.1f, wy), new Vector2(0.16f, 0.22f),
                                                      -89 + layer * 3, 49.9f - layer * 5);
                                    buildings.Add(win); buildingColors.Add(win.color);
                                }
                    }
                    if (rnd.NextDouble() < 0.25)
                    {
                        var ant = Art.Box(t, "Antenna", layerCol[layer], new Vector2(x + w / 2, baseY + h + 0.5f),
                                          new Vector2(0.06f, 1f), -90 + layer * 3, 50 - layer * 5);
                        buildings.Add(ant); buildingColors.Add(layerCol[layer]);
                    }
                    x += w + R(0.05f, 0.6f);
                }
            }

            haze = Art.Spr(t, "Smog", Art.Gradient(new Color(0.9f, 0.65f, 0.35f, 0.55f),
                                                   new Color(0.9f, 0.65f, 0.35f, 0f), "smog"),
                           Color.white, new Vector2(0, -1.5f), new Vector2(26, 1.75f), -80, 35);

            ground = Art.Box(t, "Ground", Palette.Ground, new Vector2(0, groundY - 1.5f), new Vector2(26, 3.2f), -70, 30);
            groundClean = Art.Box(t, "GroundClean", new Color(Palette.GroundClean.r, Palette.GroundClean.g,
                                  Palette.GroundClean.b, 0f), new Vector2(0, groundY - 1.5f), new Vector2(26, 3.2f), -69, 29.9f);

            // e-waste piles along the horizon
            for (int i = 0; i < 18; i++)
            {
                var pile = new GameObject("JunkPile").transform;
                pile.SetParent(t, false);
                pile.localPosition = new Vector3(R(-12f, 12f), groundY + 0.15f, 25f + i * 0.01f);
                float s = R(0.7f, 1.6f);
                for (int k = 0; k < 7; k++)
                {
                    var c = rnd.NextDouble() < 0.5 ? Palette.Junk1 : Palette.Junk2;
                    if (rnd.NextDouble() < 0.2) c = Palette.Rust;
                    var piece = rnd.NextDouble() < 0.5
                        ? Art.Box(pile, "Scrap", c, new Vector2(R(-0.9f, 0.9f) * s, R(0f, 0.6f) * s),
                                  new Vector2(R(0.3f, 0.9f), R(0.2f, 0.6f)) * s, -60, 0, R(-35f, 35f))
                        : Art.Spr(pile, "Scrap", Art.Circle, c, new Vector2(R(-0.9f, 0.9f) * s, R(-0.1f, 0.4f) * s),
                                  Vector2.one * R(0.4f, 1.1f) * s, -61);
                }
                junk.Add(pile);
            }

            // clean-world props (hidden while dirty)
            for (int i = 0; i < 14; i++)
            {
                var tree = new GameObject("Bush").transform;
                tree.SetParent(t, false);
                tree.localPosition = new Vector3(R(-12f, 12f), groundY + 0.1f, 24f);
                float s = R(0.6f, 1.3f);
                Art.Box(tree, "Trunk", Palette.Hex("6B4426"), new Vector2(0, 0.45f * s), new Vector2(0.18f, 0.9f) * s, -58);
                Art.Spr(tree, "Leaves", Art.Circle, Palette.LeafDark, new Vector2(0, 1.1f * s), Vector2.one * 1.1f * s, -57);
                Art.Spr(tree, "Leaves2", Art.Circle, Palette.Leaf, new Vector2(0.25f * s, 1.3f * s), Vector2.one * 0.7f * s, -56);
                tree.localScale = Vector3.zero;
                green.Add(tree);
            }
            for (int i = 0; i < 40; i++)
            {
                var g = Art.Spr(t, "Grass", Art.Triangle, Palette.Leaf, new Vector2(R(-12.5f, 12.5f), groundY + 0.05f),
                                new Vector2(R(0.15f, 0.3f), R(0.3f, 0.7f)), -55, 23).transform;
                g.localScale = new Vector3(g.localScale.x, 0f, 1f);
                g.name = "Grass" + g.GetInstanceID();
                green.Add(g);
                grassT.Add(g); grassH.Add(R(0.3f, 0.7f));
            }

            // floating dust specks
            for (int i = 0; i < 30; i++)
                dust.Add(Art.Spr(t, "Dust", Art.Circle, new Color(1f, 0.85f, 0.6f, 0.35f),
                                 new Vector2(R(-10, 10), R(-4, 5)), Vector2.one * R(0.04f, 0.1f), -50, 20));
        }


        void Update()
        {
            if (skyClean == null) return;
            if (Application.isPlaying)
            foreach (var d in dust)
            {
                if (d == null) continue;
                var p = d.transform.localPosition;
                p.x += Time.deltaTime * 0.3f;
                p.y += Mathf.Sin(Time.time + p.x) * Time.deltaTime * 0.1f;
                if (p.x > 10.5f) p.x = -10.5f;
                d.transform.localPosition = p;
            }
            if (Mathf.Abs(shownClean - clean) < 0.001f) return;
            shownClean = clean;
            float c = clean;
            skyClean.color = new Color(1, 1, 1, c);
            haze.color = new Color(1, 1, 1, 1f - c);
            sun.color = Color.Lerp(new Color(1f, 0.9f, 0.6f, 0.9f), new Color(1f, 1f, 0.85f, 0.7f), c);
            groundClean.color = new Color(Palette.GroundClean.r, Palette.GroundClean.g, Palette.GroundClean.b, c);
            for (int i = 0; i < buildings.Count; i++)
            {
                var bc = buildingColors[i];
                var target = new Color(Mathf.Lerp(bc.r, 0.45f + bc.r * 0.2f, 0.6f), Mathf.Lerp(bc.g, 0.55f, 0.5f),
                                       Mathf.Lerp(bc.b, 0.72f, 0.6f), bc.a);
                buildings[i].color = Color.Lerp(bc, target, c);
            }
            for (int i = 0; i < junk.Count; i++)
            {
                // piles disappear one by one as the city gets cleaner
                float k = Mathf.Clamp01((c * junk.Count - i) * 1.5f);
                junk[i].localScale = Vector3.one * (1f - k);
            }
            for (int i = 0; i < green.Count; i++)
            {
                float k = Mathf.Clamp01((c - 0.25f - (i % 14) * 0.03f) * 3f);
                var g = green[i];
                if (g == null) continue;
                int gi = grassT.IndexOf(g);
                if (gi >= 0) g.localScale = new Vector3(g.localScale.x, grassH[gi] * k, 1f);
                else g.localScale = Vector3.one * k;
            }
            foreach (var d in dust) d.color = new Color(1f, 0.85f, 0.6f, 0.35f * (1f - c));
        }
    }
}
