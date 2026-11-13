using UnityEngine;

namespace RebootEarth
{
    /// <summary>The old library book with the last seed glued on its final page.</summary>
    public class StoryBook : MonoBehaviour
    {
        public Transform seed;
        Transform coverPivot;
        public float open;            // 0 = closed, 1 = open

        public static StoryBook Create(Transform parent, Vector2 pos, float scale, bool withSeed = true)
        {
            var root = new GameObject("StoryBook").transform;
            root.SetParent(parent, false);
            root.localPosition = new Vector3(pos.x, pos.y, -2f);
            root.localScale = Vector3.one * scale;
            var b = root.gameObject.AddComponent<StoryBook>();
            Art.Spr(root, "Glow", Art.Glow, new Color(1f, 0.9f, 0.5f, 0.5f), Vector2.zero, new Vector2(6, 5), 200);
            Art.RoundBox(root, "PageL", Palette.Hex("F3E6C4"), new Vector2(-1.05f, 0), new Vector2(2.1f, 2.8f), 201);
            Art.RoundBox(root, "PageR", Palette.Hex("F7EDD3"), new Vector2(1.05f, 0), new Vector2(2.1f, 2.8f), 201);
            Art.Box(root, "Spine", Palette.Hex("C9B48A"), Vector2.zero, new Vector2(0.06f, 2.8f), 202);
            // forest drawing on the left page
            for (int i = 0; i < 5; i++)
            {
                float x = -1.75f + i * 0.35f;
                Art.Box(root, "T", Palette.Hex("7A5A3A"), new Vector2(x, -0.55f), new Vector2(0.07f, 0.45f), 202);
                Art.Spr(root, "L", Art.Circle, i % 2 == 0 ? Palette.Leaf : Palette.LeafDark, new Vector2(x, -0.2f),
                        Vector2.one * 0.45f, 203);
            }
            Art.Spr(root, "SunDraw", Art.Circle, Palette.Sun, new Vector2(-0.4f, 0.8f), Vector2.one * 0.35f, 203);
            for (int i = 0; i < 4; i++)
                Art.Box(root, "Line", new Color(0.4f, 0.3f, 0.2f, 0.5f), new Vector2(1.05f, 0.9f - i * 0.25f),
                        new Vector2(1.5f, 0.05f), 202);
            if (withSeed)
            {
                b.seed = new GameObject("LastSeed").transform;
                b.seed.SetParent(root, false);
                b.seed.localPosition = new Vector3(1.05f, -0.55f, -0.1f);
                Art.Spr(b.seed, "SeedGlow", Art.Glow, new Color(0.7f, 1f, 0.5f, 0.9f), Vector2.zero, Vector2.one * 1.2f, 204);
                Art.Spr(b.seed, "Seed", Art.Circle, Palette.Hex("8A5A2B"), Vector2.zero, new Vector2(0.28f, 0.38f), 205);
                Art.Spr(b.seed, "SeedHi", Art.Circle, Palette.Hex("C08A50"), new Vector2(-0.04f, 0.06f), new Vector2(0.1f, 0.14f), 206);
            }
            // cover that swings open
            b.coverPivot = new GameObject("CoverPivot").transform;
            b.coverPivot.SetParent(root, false);
            Art.RoundBox(b.coverPivot, "Cover", Palette.Hex("4E7A3A"), new Vector2(1.08f, 0), new Vector2(2.2f, 2.95f), 210);
            Art.Text(b.coverPivot, "THE GREEN\nEARTH", new Vector2(1.08f, 0.4f), 0.32f, Palette.Cream, 211);
            Art.Spr(b.coverPivot, "Leaf", Art.Circle, Palette.Leaf, new Vector2(1.08f, -0.6f), new Vector2(0.5f, 0.8f), 211);
            return b;
        }

        void Update()
        {
            // flip the cover around the spine (x scale trick, 2D)
            float a = Mathf.SmoothStep(0, 1, open);
            coverPivot.localScale = new Vector3(Mathf.Cos(a * Mathf.PI), 1f, 1f);
            coverPivot.gameObject.SetActive(a < 0.5f);
            if (seed != null)
                seed.localScale = Vector3.one * (1f + Mathf.Sin(Time.time * 4f) * 0.08f);
        }
    }
}
