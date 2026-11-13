using UnityEngine;

namespace RebootEarth.Game1
{
    public enum WasteType { Cable, Phone, CircuitBoard, Battery }

    public static class WasteInfo
    {
        public static int Points(WasteType t) => t switch
        {
            WasteType.Cable => 10, WasteType.Phone => 20, WasteType.CircuitBoard => 35, _ => 50
        };

        /// <summary>Impact points used for Air Recovery (same scale as the base score).</summary>
        public static int Impact(WasteType t) => Points(t);

        public static string Name(WasteType t) => t switch
        {
            WasteType.Cable => "สายไฟเก่า", WasteType.Phone => "โทรศัพท์เก่า",
            WasteType.CircuitBoard => "แผงวงจร / คอมพิวเตอร์เก่า", _ => "แบตเตอรี่เก่า"
        };

        public static string Tier(WasteType t) => t switch
        {
            WasteType.Cable => "LOW", WasteType.Phone => "MODERATE", WasteType.CircuitBoard => "HIGH", _ => "CRITICAL"
        };

        public static Color TierColor(WasteType t) => t switch
        {
            WasteType.Cable => Palette.Hex("B8C4C8"), WasteType.Phone => Palette.Hex("FFE066"),
            WasteType.CircuitBoard => Palette.Hex("FF9F43"), _ => Palette.Hex("FF4D4D")
        };

        /// <summary>Spawn weights: common low impact, rare critical.</summary>
        public static WasteType Random()
        {
            float r = UnityEngine.Random.value;
            if (r < 0.36f) return WasteType.Cable;
            if (r < 0.66f) return WasteType.Phone;
            if (r < 0.87f) return WasteType.CircuitBoard;
            return WasteType.Battery;
        }
    }

    /// <summary>One piece of e-waste: idle (floating), held by a grabber, falling, or gone.</summary>
    public class WasteItem : MonoBehaviour
    {
        public enum State { Idle, Held, Falling, Gone }

        public WasteType type;
        public bool rare;
        public int owner;                 // 0 = P1 side, 1 = P2 side
        public bool tutorial;             // practice item (not counted)
        public State state = State.Idle;
        public float rareLifetime = 9f;
        public Vector2 velocity;
        public bool bouncedOffFullBag;

        Transform visual;
        SpriteRenderer ring, glow;
        float age, bobPhase;
        Vector3 basePos;

        public Vector2 Pos => transform.position;

        public static WasteItem Create(Transform parent, WasteType type, bool rare, int owner, Vector2 pos)
        {
            var go = new GameObject((rare ? "Rare_" : "") + type);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            var w = go.AddComponent<WasteItem>();
            w.type = type; w.rare = rare; w.owner = owner;
            w.Build();
            return w;
        }

        void Build()
        {
            bobPhase = Random.value * 10f;
            basePos = transform.position;
            if (rare)
                glow = Art.Spr(transform, "RareGlow", Art.Glow, new Color(1f, 0.85f, 0.2f, 0.9f), Vector2.zero,
                               Vector2.one * 2.4f, 4);
            ring = Art.Spr(transform, "TierRing", Art.Ring, WasteInfo.TierColor(type), Vector2.zero,
                           Vector2.one * 1.25f, 5);
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            var v = visual;
            switch (type)
            {
                case WasteType.Cable:
                    var rubber = Palette.Hex("2E2A28");
                    for (int i = 0; i < 9; i++)
                    {
                        float x = -0.45f + i * 0.11f;
                        Art.Spr(v, "Seg", Art.Circle, rubber, new Vector2(x, Mathf.Sin(i * 1.1f) * 0.16f),
                                Vector2.one * 0.14f, 8);
                    }
                    Art.Box(v, "PlugA", Palette.Hex("D7D2C8"), new Vector2(-0.52f, 0), new Vector2(0.16f, 0.12f), 9);
                    Art.Box(v, "PinA", Palette.Hex("E0A84A"), new Vector2(-0.63f, 0.03f), new Vector2(0.08f, 0.03f), 9);
                    Art.Box(v, "PlugB", Palette.Hex("D7D2C8"), new Vector2(0.55f, 0.05f), new Vector2(0.16f, 0.12f), 9);
                    Art.Box(v, "Copper", Palette.Hex("E07A3A"), new Vector2(0.66f, 0.05f), new Vector2(0.07f, 0.05f), 9);
                    break;
                case WasteType.Phone:
                    Art.RoundBox(v, "Body", Palette.Hex("3B3F45"), Vector2.zero, new Vector2(0.48f, 0.82f), 8);
                    Art.Box(v, "Screen", Palette.Hex("6FD3E8"), new Vector2(0, 0.03f), new Vector2(0.38f, 0.6f), 9);
                    Art.Box(v, "Crack1", Palette.Hex("1C4D5A"), new Vector2(-0.04f, 0.1f), new Vector2(0.03f, 0.4f), 10, 0, 32f);
                    Art.Box(v, "Crack2", Palette.Hex("1C4D5A"), new Vector2(0.06f, -0.08f), new Vector2(0.03f, 0.25f), 10, 0, -40f);
                    Art.Spr(v, "Btn", Art.Circle, Palette.Hex("8A8F96"), new Vector2(0, -0.34f), Vector2.one * 0.07f, 10);
                    break;
                case WasteType.CircuitBoard:
                    Art.RoundBox(v, "Board", Palette.Hex("2F8F4E"), Vector2.zero, new Vector2(0.95f, 0.66f), 8);
                    for (int i = 0; i < 4; i++)
                        Art.Box(v, "Trace", Palette.Hex("E4C15A"), new Vector2(-0.1f + i * 0.04f, -0.12f + i * 0.08f),
                                new Vector2(0.7f, 0.025f), 9);
                    Art.Box(v, "Chip", Palette.Hex("1E1E22"), new Vector2(-0.18f, 0.08f), new Vector2(0.26f, 0.22f), 10);
                    Art.Box(v, "Chip2", Palette.Hex("1E1E22"), new Vector2(0.22f, -0.12f), new Vector2(0.18f, 0.14f), 10);
                    Art.Spr(v, "Cap", Art.Circle, Palette.Hex("3A6FD8"), new Vector2(0.3f, 0.17f), Vector2.one * 0.12f, 10);
                    break;
                default:
                    Art.RoundBox(v, "Cell", Palette.Hex("2B2B2B"), Vector2.zero, new Vector2(0.44f, 0.86f), 8);
                    Art.Box(v, "Label", Palette.Hex("F28C28"), new Vector2(0, 0.12f), new Vector2(0.44f, 0.36f), 9);
                    Art.Box(v, "Cap", Palette.Hex("C9C9C9"), new Vector2(0, 0.48f), new Vector2(0.16f, 0.1f), 9);
                    Art.Box(v, "Plus", Palette.Hex("2B2B2B"), new Vector2(0, 0.12f), new Vector2(0.14f, 0.04f), 10);
                    Art.Box(v, "Plus2", Palette.Hex("2B2B2B"), new Vector2(0, 0.12f), new Vector2(0.04f, 0.14f), 10);
                    Art.Box(v, "Leak", Palette.Hex("B6E04A"), new Vector2(0.14f, -0.38f), new Vector2(0.18f, 0.06f), 10);
                    break;
            }
            visual.localRotation = Quaternion.Euler(0, 0, Random.Range(-25f, 25f));
            transform.localScale = Vector3.zero;
        }

        /// <summary>Move without the idle bob (used when held / falling).</summary>
        public void SetPosition(Vector2 p)
        {
            transform.position = new Vector3(p.x, p.y, transform.position.z);
            basePos = transform.position;
        }

        public void SetSorting(bool front)
        {
            transform.position = new Vector3(transform.position.x, transform.position.y, front ? -1f : 0f);
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>())
                sr.sortingOrder = (sr.sortingOrder % 100) + (front ? 100 : 0);
        }

        void Update()
        {
            age += Time.deltaTime;
            float pop = Mathf.Clamp01(age / 0.35f);
            float s = pop < 1f ? Mathf.Sin(pop * Mathf.PI * 0.75f) / Mathf.Sin(Mathf.PI * 0.75f) : 1f;
            if (state != State.Gone) transform.localScale = Vector3.one * Mathf.Max(0.01f, s);

            if (state == State.Idle)
            {
                transform.position = basePos + new Vector3(0, Mathf.Sin(Time.time * 2f + bobPhase) * 0.06f, 0);
                visual.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 1.3f + bobPhase) * 6f);
            }
            if (ring) ring.enabled = state == State.Idle;
            if (glow)
            {
                float a = 0.65f + Mathf.Sin(Time.time * 8f) * 0.3f;
                glow.color = new Color(1f, 0.85f, 0.2f, a);
                glow.transform.localScale = Vector3.one * (2.2f + Mathf.Sin(Time.time * 5f) * 0.25f);
                glow.transform.Rotate(0, 0, 60f * Time.deltaTime);
            }
            // rare items blink in their last 2 seconds
            if (rare && state == State.Idle && !tutorial)
            {
                float left = rareLifetime - age;
                visual.gameObject.SetActive(left > 2f || Mathf.Repeat(Time.time * 6f, 1f) > 0.35f);
            }
            else visual.gameObject.SetActive(true);
        }

        /// <summary>Rare item that was not picked in time.</summary>
        public bool Expired => rare && !tutorial && state == State.Idle && age > rareLifetime;

        public void Vanish()
        {
            state = State.Gone;
            Destroy(gameObject);
        }
    }
}
