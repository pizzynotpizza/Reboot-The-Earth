using System.Collections.Generic;
using UnityEngine;

namespace RebootEarth.Game1
{
    /// <summary>
    /// One player's side: waste bag (left hand), trash grabber (right hand, pinch toggle),
    /// recycling station, personal score / combo / bag capacity.
    /// </summary>
    public class WastePlayer : MonoBehaviour
    {
        public const int BagCapacity = 5;
        public const float DumpHoldSeconds = 1.2f;
        public const float GrabRadius = 0.8f;
        public static readonly float[] ComboSteps = { 3, 5, 8 };
        public static readonly int[] ComboBonus = { 10, 20, 40 };

        public int index;
        public Rect area;                     // world rect the bag / grabber can move in
        public Rect stationRect;              // recycling station drop zone
        public WasteGame game;

        // ---- stats ----
        public int score, baseScore, comboBonusTotal, rareBonusTotal;
        public int combo, bestCombo, comboBonusInStreak;
        public int collected, rareCollected, misses, dumps;
        public readonly int[] perType = new int[4];
        public readonly List<WasteItem> bag = new List<WasteItem>();

        public WasteItem held;
        public bool controlsEnabled;
        public float dumpProgress;
        public float stableTime;              // both hands tracked continuously (tutorial)
        public bool practiced;

        // visuals: built once (edit mode bake) and saved in the scene - move / recolor them freely
        public Transform bagRoot, grabRoot;
        [SerializeField, HideInInspector] SpriteRenderer jawL, jawR, bagBody;
        [SerializeField, HideInInspector] List<SpriteRenderer> pips = new List<SpriteRenderer>();
        [SerializeField, HideInInspector] SpriteRenderer stationFill, stationGlow, lostBag, lostGrab;
        [SerializeField, HideInInspector] Vector2 bagPos, grabPos;
        [SerializeField, HideInInspector] Color bagColor;
        float jawAngle = 28f;

        public Vector2 BagMouth => bagPos + new Vector2(0, 0.78f);
        public Vector2 GrabTip => grabPos + new Vector2(0, -0.45f);
        public bool BagFull => bag.Count >= BagCapacity;
        public PlayerControl Input => TrackingReceiver.P(index);
        public Color Color => index == 0 ? Palette.P1 : Palette.P2;

        public void Build()
        {
            bagPos = new Vector2(area.center.x - area.width * 0.25f * (index == 0 ? -1 : 1), area.yMin + 2.6f);
            grabPos = area.center;

            // ---------------- recycling station
            var st = new GameObject("RecyclingStation").transform;
            st.SetParent(transform, false);
            st.position = new Vector3(stationRect.center.x, stationRect.center.y, 1f);
            stationGlow = Art.Spr(st, "Glow", Art.Glow, new Color(0.4f, 1f, 0.5f, 0.0f), new Vector2(0, 0.2f),
                                  new Vector2(4.2f, 4.2f), 1);
            Art.RoundBox(st, "Bin", Palette.Hex("2F7D4F"), new Vector2(0, -0.35f), new Vector2(2.2f, 1.5f), 2);
            Art.RoundBox(st, "Lid", Palette.Hex("3FA866"), new Vector2(0, 0.45f), new Vector2(2.4f, 0.35f), 3);
            Art.Box(st, "Slot", Palette.Hex("173D27"), new Vector2(0, 0.45f), new Vector2(1.6f, 0.12f), 4);
            Art.Spr(st, "Icon", Art.Ring, Palette.Cream, new Vector2(0, -0.35f), Vector2.one * 0.8f, 4);
            Art.Spr(st, "IconTri", Art.Triangle, Palette.Cream, new Vector2(0, -0.35f), Vector2.one * 0.4f, 4);
            Art.Text(st, "RECYCLING\nSTATION", new Vector2(0, 1.25f), 0.32f, Palette.Cream, 6);
            Art.Box(st, "BarBack", new Color(0, 0, 0, 0.5f), new Vector2(0, -1.25f), new Vector2(2.0f, 0.18f), 5);
            stationFill = Art.Box(st, "BarFill", Palette.Good, new Vector2(-1f, -1.25f), new Vector2(0.001f, 0.14f), 6);

            // ---------------- bag (open mouth on top, 5 capacity pips)
            bagRoot = new GameObject("WasteBag").transform;
            bagRoot.SetParent(transform, false);
            bagBody = Art.RoundBox(bagRoot, "Body", Palette.Hex("8A8F7A"), new Vector2(0, 0), new Vector2(1.6f, 1.55f), 30);
            bagColor = bagBody.color;
            Art.RoundBox(bagRoot, "Shade", Palette.Hex("6E7362"), new Vector2(0, -0.5f), new Vector2(1.5f, 0.45f), 31);
            Art.Spr(bagRoot, "MouthRim", Art.Circle, Palette.Hex("B5BA9E"), new Vector2(0, 0.78f), new Vector2(1.7f, 0.5f), 32);
            Art.Spr(bagRoot, "Mouth", Art.Circle, Palette.Hex("1E1A16"), new Vector2(0, 0.8f), new Vector2(1.45f, 0.34f), 33);
            Art.Box(bagRoot, "Tag", Color, new Vector2(0.55f, 0.35f), new Vector2(0.28f, 0.2f), 34, 0, 12f);
            for (int i = 0; i < BagCapacity; i++)
                pips.Add(Art.Spr(bagRoot, "Pip", Art.Circle, new Color(0, 0, 0, 0.35f),
                                 new Vector2(-0.5f + i * 0.25f, -0.15f), Vector2.one * 0.2f, 35));
            lostBag = Art.Spr(bagRoot, "Lost", Art.Ring, Palette.Bad, new Vector2(0, 0.2f), Vector2.one * 2.2f, 36);

            // ---------------- grabber (long handle + two jaws, tip = transform position)
            grabRoot = new GameObject("TrashGrabber").transform;
            grabRoot.SetParent(transform, false);
            var steel = Palette.Hex("C9CED3");
            var handle = Art.Box(grabRoot, "Handle", steel, new Vector2(0.55f * Side, 1.35f), new Vector2(0.12f, 2.6f), 60, 0,
                                 -22f * Side);
            Art.Box(grabRoot, "Grip", Color, new Vector2(1.02f * Side, 2.5f), new Vector2(0.26f, 0.55f), 61, 0, -22f * Side);
            Art.Box(grabRoot, "Trigger", Palette.Hex("333333"), new Vector2(0.86f * Side, 2.2f), new Vector2(0.1f, 0.3f), 61);
            jawL = MakeJaw("JawL", steel);
            jawR = MakeJaw("JawR", steel);
            Art.Spr(grabRoot, "Pivot", Art.Circle, Palette.Hex("6D737A"), new Vector2(0, 0.1f), Vector2.one * 0.22f, 63);
            lostGrab = Art.Spr(grabRoot, "Lost", Art.Ring, Palette.Bad, new Vector2(0, 0.3f), Vector2.one * 1.6f, 64);
            _ = handle;
            lostBag.enabled = false;
            lostGrab.enabled = false;
            UpdateVisuals(0f);
        }

        float Side => index == 0 ? 1f : -1f;   // handle leans toward the screen center

        SpriteRenderer MakeJaw(string n, Color c)
        {
            var pivot = new GameObject(n).transform;
            pivot.SetParent(grabRoot, false);
            pivot.localPosition = new Vector3(0, 0.1f, 0);
            var jaw = Art.Box(pivot, "Blade", c, new Vector2(0, -0.32f), new Vector2(0.1f, 0.6f), 62);
            Art.Box(pivot, "Tooth", Palette.Hex("8E949A"), new Vector2(0, -0.62f), new Vector2(0.18f, 0.08f), 62);
            return jaw;
        }

        Vector2 Map(Vector2 n) => new Vector2(Mathf.Lerp(area.xMin, area.xMax, n.x), Mathf.Lerp(area.yMin, area.yMax, n.y));

        public void Tick(float dt)
        {
            var inp = Input;
            bool bagOk = inp != null && inp.Present && inp.BagTracked;
            bool grabOk = inp != null && inp.Present && inp.GrabTracked;

            stableTime = bagOk && grabOk ? stableTime + dt : 0f;

            if (controlsEnabled)
            {
                if (bagOk) bagPos = Map(inp.Bag);
                if (grabOk) grabPos = Map(inp.Grab);
                if (grabOk && inp.PinchDown) TogglePinch();
            }

            // held item follows the claw
            if (held != null) held.SetPosition(GrabTip + new Vector2(0, -0.12f));

            // recycling station: bag mouth inside the zone for 1.2 s = dump
            bool inStation = stationRect.Contains(BagMouth) || stationRect.Contains(bagPos);
            if (controlsEnabled && inStation && bag.Count > 0)
            {
                dumpProgress += dt / DumpHoldSeconds;
                if (dumpProgress >= 1f) Dump();
            }
            else dumpProgress = Mathf.MoveTowards(dumpProgress, 0f, dt * 3f);

            UpdateVisuals(dt);
            lostBag.enabled = controlsEnabled && !bagOk && Mathf.Repeat(Time.time * 3f, 1f) > 0.3f;
            lostGrab.enabled = controlsEnabled && !grabOk && Mathf.Repeat(Time.time * 3f, 1f) > 0.3f;
        }

        void TogglePinch()
        {
            if (held == null)
            {
                var w = game.NearestWaste(index, GrabTip, GrabRadius);
                if (w != null)
                {
                    held = w;
                    w.state = WasteItem.State.Held;
                    w.SetSorting(true);
                    game.OnGrab(this, w);
                }
                else
                {
                    misses++;
                    ResetCombo();
                    game.OnMiss(this);
                }
            }
            else
            {
                var w = held;
                held = null;
                w.state = WasteItem.State.Falling;
                w.velocity = Vector2.zero;
                w.bouncedOffFullBag = false;
                game.OnRelease(this, w);
            }
        }

        /// <summary>Collision check: falling item crossing the open bag mouth this frame.</summary>
        public bool CatchCheck(WasteItem w, Vector2 prev, Vector2 now)
        {
            Vector2 m = BagMouth;
            bool crossed = prev.y >= m.y - 0.05f && now.y <= m.y + 0.15f;
            bool inside = Mathf.Abs(now.x - m.x) < 0.78f && now.y > m.y - 0.6f;
            return crossed && inside;
        }

        public void AddToBag(WasteItem w)
        {
            bag.Add(w);
            w.state = WasteItem.State.Gone;
            w.gameObject.SetActive(false);
            int pts = WasteInfo.Points(w.type);
            baseScore += pts;
            score += pts;
            if (w.rare && !w.tutorial)
            {
                rareBonusTotal += 30;
                score += 30;
                rareCollected++;
            }
            collected++;
            perType[(int)w.type]++;
            combo++;
            bestCombo = Mathf.Max(bestCombo, combo);
            // milestone bonus: only the highest milestone of this streak counts (no stacking)
            int target = 0;
            for (int i = 0; i < ComboSteps.Length; i++) if (combo >= ComboSteps[i]) target = ComboBonus[i];
            int extra = target - comboBonusInStreak;
            if (extra > 0)
            {
                comboBonusInStreak = target;
                comboBonusTotal += extra;
                score += extra;
                game.OnComboBonus(this, extra);
            }
        }

        public void ResetCombo()
        {
            combo = 0;
            comboBonusInStreak = 0;
        }

        void Dump()
        {
            dumpProgress = 0f;
            dumps++;
            game.OnDump(this, bag.Count);
            foreach (var w in bag) if (w != null) Destroy(w.gameObject);
            bag.Clear();
        }

        public void ClearForTutorial()
        {
            foreach (var w in bag) if (w != null) Destroy(w.gameObject);
            bag.Clear();
            score = baseScore = comboBonusTotal = rareBonusTotal = 0;
            combo = bestCombo = comboBonusInStreak = collected = rareCollected = misses = dumps = 0;
            for (int i = 0; i < perType.Length; i++) perType[i] = 0;
            if (held != null) { held.Vanish(); held = null; }
        }

        void UpdateVisuals(float dt)
        {
            bagRoot.position = new Vector3(bagPos.x, bagPos.y, 0.5f);
            grabRoot.position = new Vector3(grabPos.x, grabPos.y, -1.5f);
            float targetJaw = held != null ? 6f : 28f;
            jawAngle = Mathf.MoveTowards(jawAngle, targetJaw, dt * 220f);
            jawL.transform.parent.localRotation = Quaternion.Euler(0, 0, -jawAngle);
            jawR.transform.parent.localRotation = Quaternion.Euler(0, 0, jawAngle);
            for (int i = 0; i < pips.Count; i++)
            {
                Color full = bag.Count >= BagCapacity ? Palette.Bad : Palette.Good;
                pips[i].color = i < bag.Count ? full : new Color(0, 0, 0, 0.35f);
            }
            bagBody.color = BagFull && Mathf.Repeat(Time.time * 3f, 1f) > 0.5f ? Palette.Hex("B0775A") : bagColor;
            stationFill.transform.localScale = new Vector3(Mathf.Max(0.001f, 2.0f * dumpProgress), 0.14f, 1f);
            stationFill.transform.localPosition = new Vector3(-1f + dumpProgress, -1.25f, 0f);
            float g = BagFull ? 0.35f + Mathf.Sin(Time.time * 6f) * 0.25f : dumpProgress * 0.8f;
            stationGlow.color = new Color(0.4f, 1f, 0.5f, g);
        }
    }
}
