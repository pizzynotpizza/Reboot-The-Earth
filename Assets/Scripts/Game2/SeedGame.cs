using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RebootEarth.Game2
{
    /// <summary>
    /// GAME 2 - SEED OF TOMORROW: Grow the Last Hope.
    /// P1 Water Guardian: raise both arms (collect water) -> lower arms -> push both hands forward (send water)
    /// P2 Sun Guardian:   raise both arms (collect sunlight) -> push both hands forward (send sunlight)
    /// Both guardians collect at the same time with the same collect time (collectSeconds).
    /// Shared Growth Bar to 100%, Nature Balance between water and sunlight.
    /// </summary>
    public class SeedGame : MonoBehaviour
    {
        public enum Phase { Story, Roles, Playing, Final, Result }
        enum G { Collect, Ready, Armed, Sending, Cooldown }

        [Header("Tuning")]
        public float collectSeconds = 2.6f;        // pose held this long fills the gauge
        public float collectStartDelay = 0.35f;     // pose must be stable before filling starts
        public float releaseHoldSeconds = 0.8f;     // push hold to confirm (both guardians)
        public float travelSeconds = 1.1f;          // water / light flight time to the tree
        public float growthPerDelivery = 0.09f;
        public float coopWindow = 8f;
        public float coopBonusGrowth = 0.03f;

        [Header("Scene refs (optional)")]
        public GameObject player1Prefab;
        public GameObject player2Prefab;
        [Tooltip("Existing tree model (Assets/Model/Tree.glb)")]
        public GameObject treeModel;

        public Phase phase;
        public float growth;
        public int waterDelivered, sunDelivered, coopBonuses;
        float lastWaterArrive = -99f, lastSunArrive = -99f;

        // per guardian (0 = water, 1 = sun)
        readonly G[] state = { G.Collect, G.Collect };
        readonly float[] gauge = new float[2];
        readonly float[] poseStable = new float[2];
        readonly HoldTimer[] releaseHold = { new HoldTimer(0.8f), new HoldTimer(0.8f) };

        // ---- scene objects: built by BuildScene() (menu Reboot The Earth/Build Scenes) and saved in the scene
        [SerializeField, HideInInspector] bool built;
        [SerializeField, HideInInspector] Canvas hudCanvas;
        [SerializeField, HideInInspector] CityBackdrop city;
        [SerializeField, HideInInspector] Transform world;
        [SerializeField, HideInInspector] TreeGrowth tree;
        UIKit ui;
        StoryBook book;
        [SerializeField, HideInInspector] Astronaut[] guardians = new Astronaut[2];
        Vector3[] guardianPos = { new Vector3(-6.3f, -4.7f, 2f), new Vector3(6.3f, -4.7f, 2f) };
        Vector3 rootPos = new Vector3(0, -3.3f, 0);

        // fx
        [SerializeField, HideInInspector] SpriteRenderer[] orb = new SpriteRenderer[2];
        readonly List<(SpriteRenderer s, Vector3 v, float life, int kind)> motes = new List<(SpriteRenderer, Vector3, float, int)>();

        // UI
        [SerializeField, HideInInspector] Text centerText, subText, growthText, stageText, balanceText, statusText;
        [SerializeField, HideInInspector] Text[] playerStatus = new Text[2], playerTitle = new Text[2], readyAlert = new Text[2];
        [SerializeField, HideInInspector] Image growthFill, balanceMarker, fader;
        [SerializeField, HideInInspector] Image[] gaugeFill = new Image[2], holdFill = new Image[2];
        [SerializeField, HideInInspector] GameObject hud;
        readonly ContinueGesture cont = new ContinueGesture();

        void Start()
        {
            TrackingReceiver.Ensure();
            Application.targetFrameRate = 60;
            foreach (var h in releaseHold) h.duration = releaseHoldSeconds;
            if (!built) BuildScene();
            else ui = new UIKit(this, hudCanvas);
            // effects follow the characters if they were moved in the scene
            for (int i = 0; i < 2; i++) if (guardians[i] != null) guardianPos[i] = guardians[i].transform.position;
            city.clean = Mathf.Clamp01(GameSession.AirRecovery) * 0.35f;
            tree.growth = 0f;
            fader.gameObject.SetActive(true);
            fader.color = Color.black;
            fader.transform.SetAsLastSibling();
            StartCoroutine(Flow());
        }

        /// <summary>Creates every visual (city, soil, tree, guardians, HUD). Called in the editor by
        /// Reboot The Earth/Build Scenes so everything is visible and editable in the scene.</summary>
        public void BuildScene()
        {
            world = new GameObject("World").transform;
            city = CityBackdrop.Create(world, 0f, 23);

            // soil bed in the middle
            Art.Spr(world, "SoilBack", Art.Circle, Palette.Hex("4A3020"), new Vector2(0, -3.55f), new Vector2(4.8f, 1.1f), -30, 6);
            Art.Spr(world, "Soil", Art.Circle, Palette.Hex("6A4630"), new Vector2(0, -3.5f), new Vector2(4.2f, 0.8f), -29, 5.9f);
            tree = TreeGrowth.Create(world, new Vector3(0, -3.45f, 3f), treeModel);

            guardians[0] = Astronaut.Create(player1Prefab, world, guardianPos[0], 3.6f, 0);
            guardians[1] = Astronaut.Create(player2Prefab, world, guardianPos[1], 3.6f, 1);
            for (int i = 0; i < 2; i++)
            {
                var c = i == 0 ? Palette.Water : Palette.Sun;
                orb[i] = Art.Spr(world, "Orb", Art.Glow, new Color(c.r, c.g, c.b, 0f),
                                 new Vector2(guardianPos[i].x, guardianPos[i].y + 4.6f), Vector2.one, 50, -2f);
            }
            BuildHud();
            if (!Application.isPlaying)
            {
                fader.color = new Color(0, 0, 0, 0);
                fader.gameObject.SetActive(false);
            }
            built = true;
        }

        // =================================================================== flow
        IEnumerator Flow()
        {
            hud.SetActive(false);
            yield return ui.Fade(fader, 1f, 0f, 0.8f);

            // ---- STEP 1: the last seed
            phase = Phase.Story;
            Center("MISSION 2\n<size=110><b>" + GameSession.Game2Title + "</b></size>", GameSession.Game2Tagline);
            yield return new WaitForSeconds(3f);
            Center("", "เมืองสะอาดขึ้นแล้ว... เพื่อนซี้ทั้งสองเปิดหนังสือจากห้องสมุดอีกครั้ง");
            book = StoryBook.Create(world, new Vector2(0, 0.6f), 1.2f);
            yield return Tween(1.6f, t => book.open = t);
            Center("", "หน้าสุดท้าย... มีเมล็ดพันธุ์สุดท้ายที่เหลืออยู่บนโลก");
            yield return new WaitForSeconds(2.2f);
            // seed flies from the book into the soil
            var seed = book.seed;
            seed.SetParent(world, true);
            Vector3 from = seed.position, to = rootPos + new Vector3(0, 0.1f, -1f);
            Center("", "ทั้งคู่นำเมล็ดพันธุ์ลงปลูกบนดินที่เตรียมไว้");
            yield return Tween(1.6f, t =>
            {
                seed.position = Vector3.Lerp(from, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 2f;
                book.transform.localScale = Vector3.one * 1.2f * (1f - t);
            });
            Destroy(book.gameObject);
            Destroy(seed.gameObject, 0.3f);
            Center("", "เมล็ดเริ่มงอกเป็นต้นอ่อน...");
            yield return Tween(2f, t => tree.sproutT = t);

            // ---- STEP 2: roles
            phase = Phase.Roles;
            Center("<size=60><b>ดูแลต้นไม้ด้วยกัน</b></size>",
                   "<color=#3FC3FF><b>PLAYER 1 – WATER GUARDIAN</b></color>  ยกแขนทั้งสองข้างขึ้นสะสมน้ำ → ลดแขนลง → ผลักมือทั้งสองไปข้างหน้า\n" +
                   "<color=#FFD23F><b>PLAYER 2 – SUN GUARDIAN</b></color>  ยกแขนทั้งสองข้างขึ้นสะสมแสงอาทิตย์ → ผลักมือทั้งสองไปข้างหน้า\n" +
                   "<size=28>ส่งน้ำและแสงแดดให้ <b>สมดุล</b> ต้นไม้จะโตเร็วที่สุด   (ยกมือทั้งสองข้างค้างไว้ = เริ่ม)</size>");
            hud.SetActive(true);
            cont.Reset();
            float wait = 0f;
            while (!cont.Tick(Time.deltaTime) && (wait += Time.deltaTime) < 12f) yield return null;
            Center("", "");

            // ---- STEP 3-5: play
            phase = Phase.Playing;
            while (growth < 1f) yield return null;

            // ---- STEP 6: final growth
            phase = Phase.Final;
            Center("<size=90><b>THE TREE IS FULLY GROWN!</b></size>", "ความร่วมมือของคนสองคน ทำให้เมล็ดพันธุ์สุดท้ายกลายเป็นความหวังของโลก");
            while (tree.shown < 0.999f) yield return null;
            float startClean = city.clean;
            yield return Tween(5f, t =>
            {
                city.clean = Mathf.Lerp(startClean, 1f, t);
                if (Random.value < 0.4f) Sparkle(new Vector3(Random.Range(-3f, 3f), Random.Range(-1f, 3f), -1f));
            });
            yield return new WaitForSeconds(1.5f);

            // ---- result
            phase = Phase.Result;
            hud.SetActive(false);
            Center("", "");
            BuildResult();
            cont.Reset();
            yield return new WaitForSeconds(2f);
            while (!cont.Tick(Time.deltaTime)) yield return null;
            yield return ui.Fade(fader, 0f, 1f, 1f);
            GameSession.Load(GameSession.IntroScene);
        }

        IEnumerator Tween(float seconds, System.Action<float> f)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                f(Mathf.SmoothStep(0, 1, t / seconds));
                yield return null;
            }
            f(1f);
        }

        // =================================================================== gameplay
        void Update()
        {
            float dt = Time.deltaTime;
            if (phase == Phase.Playing)
            {
                Guardian(0, dt);
                Guardian(1, dt);
            }
            tree.growth = growth;
            UpdateFx(dt);
            UpdateHud();
        }

        void Guardian(int i, float dt)
        {
            var p = TrackingReceiver.P(i);
            bool present = p != null && p.Present;
            if (!present)
            {
                // tracking lost: pause collecting / sending (gauge is kept)
                poseStable[i] = 0f;
                releaseHold[i].Reset();
                return;
            }
            // same gestures for both guardians: raise both arms = collect, push both hands = send
            bool collectPose = p.HandsUp;
            bool releasePose = p.Push;

            switch (state[i])
            {
                case G.Collect:
                    poseStable[i] = collectPose ? poseStable[i] + dt : 0f;
                    if (poseStable[i] >= collectStartDelay)
                    {
                        gauge[i] = Mathf.Min(1f, gauge[i] + dt / collectSeconds);
                        if (Random.value < 0.5f) CollectMote(i);
                        if (gauge[i] >= 1f)
                        {
                            // water must lower the arms first; sun can push right away
                            state[i] = i == 0 ? G.Ready : G.Armed;
                            ui.Popup(guardianPos[i] + Vector3.up * 5.4f, i == 0 ? "WATER READY!" : "SUNLIGHT READY!",
                                     i == 0 ? Palette.Water : Palette.Sun, 46);
                        }
                    }
                    break;
                case G.Ready:          // water only: lower the hands
                    if (p.HandsDown)
                    {
                        state[i] = G.Armed;
                        ui.Popup(guardianPos[i] + Vector3.up * 5.4f, "ดีมาก! ผลักมือทั้งสองไปข้างหน้า", Palette.Water, 36);
                    }
                    break;
                case G.Armed:
                    if (releaseHold[i].Tick(releasePose, dt))
                    {
                        state[i] = G.Sending;
                        StartCoroutine(Send(i));
                    }
                    break;
                case G.Cooldown:       // no repeat from holding the same pose
                    if (!releasePose) { state[i] = G.Collect; poseStable[i] = 0f; }
                    break;
            }
        }

        IEnumerator Send(int i)
        {
            ui.Popup(guardianPos[i] + Vector3.up * 5.4f, i == 0 ? "SEND WATER!" : "SEND SUNLIGHT!",
                     i == 0 ? Palette.Water : Palette.Sun, 50);
            float start = gauge[i];
            Vector3 target = i == 0 ? rootPos + new Vector3(0, 0.2f, -1f) : TreeTop();
            if (i == 0)
            {
                // water stream: droplets arc from the guardian's hands to the roots
                Vector3 src = guardianPos[i] + new Vector3(1.0f, 2.6f, -2f);
                for (float t = 0; t < travelSeconds; t += Time.deltaTime)
                {
                    gauge[i] = Mathf.Lerp(start, 0f, t / travelSeconds);
                    for (int k = 0; k < 3; k++) WaterDrop(src, target);
                    yield return null;
                }
            }
            else
            {
                // sunbeam from the sky down onto the tree
                var beam = Art.Box(world, "SunBeam", new Color(1f, 0.9f, 0.4f, 0.55f), new Vector2(0, 5.5f), new Vector2(1.2f, 0.01f), 40, -1f);
                var beamGlow = Art.Spr(world, "BeamGlow", Art.Glow, new Color(1f, 0.9f, 0.4f, 0f), (Vector2)target, Vector2.one * 4f, 41, -1f);
                float top = 5.6f;
                for (float t = 0; t < travelSeconds; t += Time.deltaTime)
                {
                    float k = t / travelSeconds;
                    gauge[i] = Mathf.Lerp(start, 0f, k);
                    float bottom = Mathf.Lerp(top, target.y, k);
                    beam.transform.position = new Vector3(target.x, (top + bottom) / 2f, -1f);
                    beam.transform.localScale = new Vector3(1.2f + Mathf.Sin(Time.time * 20f) * 0.1f, top - bottom, 1f);
                    beamGlow.color = new Color(1f, 0.9f, 0.4f, k * 0.8f);
                    yield return null;
                }
                StartCoroutine(FadeOut(beam, 0.6f));
                StartCoroutine(FadeOut(beamGlow, 0.8f));
            }
            gauge[i] = 0f;
            Deliver(i);
            state[i] = G.Cooldown;
        }

        IEnumerator FadeOut(SpriteRenderer s, float time)
        {
            var c = s.color;
            for (float t = 0; t < time; t += Time.deltaTime)
            {
                s.color = new Color(c.r, c.g, c.b, c.a * (1f - t / time));
                yield return null;
            }
            Destroy(s.gameObject);
        }

        /// <summary>Resource reached the tree: growth with Nature Balance + cooperation bonus.</summary>
        void Deliver(int i)
        {
            int mine = i == 0 ? waterDelivered : sunDelivered;
            int other = i == 0 ? sunDelivered : waterDelivered;
            int ahead = mine - other;
            float factor = ahead <= 0 ? 1f : ahead == 1 ? 0.7f : 0.4f;   // imbalance slows growth
            float add = growthPerDelivery * factor;

            float now = Time.time;
            bool coop = (i == 0 ? now - lastSunArrive : now - lastWaterArrive) < coopWindow && ahead <= 0;
            if (coop)
            {
                add += coopBonusGrowth;
                coopBonuses++;
                ui.Popup(new Vector3(0, 2.5f, 0), "TEAMWORK BONUS!", Palette.Good, 56);
            }
            if (i == 0) { waterDelivered++; lastWaterArrive = now; }
            else { sunDelivered++; lastSunArrive = now; }

            growth = Mathf.Min(1f, growth + add);
            ui.Popup(TreeTop() + Vector3.up * 0.6f, $"+{Mathf.RoundToInt(add * 100)}% GROWTH",
                     factor < 1f ? Palette.Bad : Palette.Good, 44);
            if (factor < 1f)
                ui.Popup(TreeTop() + Vector3.up * 1.4f, i == 0 ? "ต้นไม้ต้องการแสงแดด!" : "ต้นไม้ต้องการน้ำ!", Palette.Gold, 34);
            for (int k = 0; k < 14; k++) Sparkle(TreeTop() + (Vector3)Random.insideUnitCircle * 1.2f);
        }

        Vector3 TreeTop() => new Vector3(0, -3.45f + Mathf.Lerp(0.6f, 4.6f, tree.shown), -1f);

        public float BalancePercent
        {
            get
            {
                int total = waterDelivered + sunDelivered;
                return total == 0 ? 100f : 100f * (1f - Mathf.Abs(waterDelivered - sunDelivered) / (float)total);
            }
        }

        // =================================================================== fx
        void CollectMote(int i)
        {
            Vector3 hand = guardianPos[i] + new Vector3(0, 4.4f, -2f);
            if (i == 0)
            {
                // rain drops falling into the raised hands
                var s = Art.Spr(world, "Drop", Art.Circle, Palette.Water, (Vector2)(hand + new Vector3(Random.Range(-0.8f, 0.8f), 2.5f)),
                                new Vector2(0.12f, 0.2f), 55, -2f);
                motes.Add((s, new Vector3(0, -6f, 0), 0.45f, 0));
            }
            else
            {
                // light specks converging to the hands
                Vector3 p = hand + (Vector3)(Random.insideUnitCircle.normalized * 2.4f);
                var s = Art.Spr(world, "Light", Art.Glow, Palette.Sun, (Vector2)p, Vector2.one * 0.35f, 55, -2f);
                motes.Add((s, (hand - p) / 0.5f, 0.5f, 1));
            }
        }

        void WaterDrop(Vector3 from, Vector3 to)
        {
            var s = Art.Spr(world, "Stream", Art.Circle, Palette.Water, (Vector2)from, Vector2.one * Random.Range(0.12f, 0.22f), 55, -2f);
            float t = 0.7f;
            Vector3 v = (to - from) / t + new Vector3(0, 0.5f * 9f * t, 0) + (Vector3)Random.insideUnitCircle * 0.4f;
            motes.Add((s, v, t, 2));
        }

        void Sparkle(Vector3 p)
        {
            var s = Art.Spr(world, "Sparkle", Art.Glow, new Color(0.8f, 1f, 0.6f, 1f), (Vector2)p, Vector2.one * 0.4f, 56, -2.5f);
            motes.Add((s, (Vector3)Random.insideUnitCircle * 1.5f + Vector3.up, 0.8f, 3));
        }

        void UpdateFx(float dt)
        {
            for (int k = motes.Count - 1; k >= 0; k--)
            {
                var m = motes[k];
                if (m.s == null) { motes.RemoveAt(k); continue; }
                m.life -= dt;
                if (m.kind == 2) m.v += Vector3.down * 9f * dt;
                m.s.transform.position += m.v * dt;
                if (m.kind == 3) m.s.color = new Color(0.8f, 1f, 0.6f, Mathf.Clamp01(m.life / 0.8f));
                if (m.life <= 0f) { Destroy(m.s.gameObject); motes.RemoveAt(k); continue; }
                motes[k] = m;
            }
            for (int i = 0; i < 2; i++)
            {
                var c = i == 0 ? Palette.Water : Palette.Sun;
                float g = gauge[i];
                bool ready = state[i] == G.Ready || state[i] == G.Armed;
                orb[i].color = new Color(c.r, c.g, c.b, Mathf.Clamp01(g * 1.2f) * (ready ? 0.75f + Mathf.Sin(Time.time * 8f) * 0.25f : 0.8f));
                orb[i].transform.localScale = Vector3.one * (0.4f + g * 1.6f);
            }
        }

        // =================================================================== HUD
        void BuildHud()
        {
            ui = new UIKit(this, "HUD");
            hudCanvas = ui.canvas;
            hud = new GameObject("HudRoot", typeof(RectTransform));
            hud.transform.SetParent(ui.root, false);
            UIKit.Stretch((RectTransform)hud.transform);
            var h = hud.transform;

            // shared growth bar
            ui.Panel(h, "GrowthBack", new Vector2(0.5f, 1f), new Vector2(0, -92), new Vector2(760, 170), Palette.Panel);
            ui.Label(h, "<b>SHARED GROWTH</b>", new Vector2(0.5f, 1f), new Vector2(-180, -35), new Vector2(380, 40), 28, Palette.Leaf);
            growthText = ui.Label(h, "0%", new Vector2(0.5f, 1f), new Vector2(250, -35), new Vector2(200, 40), 34, Palette.Cream);
            growthFill = ui.Bar(h, new Vector2(0.5f, 1f), new Vector2(0, -78), new Vector2(700, 34), Palette.Leaf, new Color(0, 0, 0, 0.6f));
            stageText = ui.Label(h, "", new Vector2(0.5f, 1f), new Vector2(0, -116), new Vector2(700, 34), 24, Palette.Cream);
            // nature balance meter: water <-> sun
            var bal = ui.Panel(h, "Balance", new Vector2(0.5f, 1f), new Vector2(0, -150), new Vector2(420, 18), Palette.Leaf, false);
            var grad = bal.gameObject.AddComponent<Outline>();
            grad.effectColor = new Color(0, 0, 0, 0.5f);
            ui.Panel(bal.transform, "W", new Vector2(0f, 0.5f), new Vector2(70, 0), new Vector2(140, 18), Palette.Water, false);
            ui.Panel(bal.transform, "S", new Vector2(1f, 0.5f), new Vector2(-70, 0), new Vector2(140, 18), Palette.Sun, false);
            balanceMarker = ui.Panel(bal.transform, "Marker", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12, 34), Palette.Cream, false);
            balanceText = ui.Label(h, "", new Vector2(0.5f, 1f), new Vector2(0, -186), new Vector2(700, 36), 24, Palette.Cream);

            for (int i = 0; i < 2; i++)
            {
                float ax = i == 0 ? 0f : 1f, sx = i == 0 ? 1f : -1f;
                var c = i == 0 ? Palette.Water : Palette.Sun;
                var a = new Vector2(ax, 1f);
                ui.Panel(h, "Panel", a, new Vector2(sx * 270, -110), new Vector2(500, 200), Palette.Panel);
                playerTitle[i] = ui.Label(h, i == 0 ? "<b>P1 WATER GUARDIAN</b>" : "<b>P2 SUN GUARDIAN</b>", a,
                                          new Vector2(sx * 270, -38), new Vector2(480, 40), 30, c);
                ui.Label(h, i == 0 ? "WATER GAUGE" : "SUNLIGHT GAUGE", a, new Vector2(sx * 270, -78), new Vector2(480, 30), 22, Palette.Cream);
                gaugeFill[i] = ui.Bar(h, a, new Vector2(sx * 270, -108), new Vector2(440, 30), c, new Color(0, 0, 0, 0.6f));
                holdFill[i] = ui.Bar(h, a, new Vector2(sx * 270, -134), new Vector2(440, 10), Palette.Cream, new Color(0, 0, 0, 0.4f));
                playerStatus[i] = ui.Label(h, "", a, new Vector2(sx * 270, -172), new Vector2(480, 44), 24, Palette.Cream);
                readyAlert[i] = ui.Label(h, "", new Vector2(i == 0 ? 0.2f : 0.8f, 0.42f), Vector2.zero, new Vector2(700, 80), 40, c);
                readyAlert[i].fontStyle = FontStyle.Bold;
            }
            statusText = ui.Label(h, "", new Vector2(0.5f, 0f), new Vector2(0, 22), new Vector2(1500, 34), 20, new Color(1, 1, 1, 0.7f));
            centerText = ui.Label(ui.root, "", new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(1700, 300), 72, Palette.Cream);
            subText = ui.Label(ui.root, "", new Vector2(0.5f, 0.2f), Vector2.zero, new Vector2(1700, 200), 34, Palette.Cream);
            fader = ui.Fader();
        }

        void Center(string big, string sub)
        {
            centerText.text = big;
            subText.text = sub;
        }

        void UpdateHud()
        {
            if (hud == null || !hud.activeSelf) return;
            growthFill.fillAmount = tree.shown;
            growthText.text = Mathf.RoundToInt(tree.shown * 100) + "%";
            stageText.text = TreeGrowth.StageName(tree.shown);
            int diff = waterDelivered - sunDelivered;
            float pos = Mathf.Clamp(diff / 3f, -1f, 1f);
            balanceMarker.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(balanceMarker.rectTransform.anchoredPosition.x,
                                                                                   -pos * 200f, Time.deltaTime * 6f), 0);
            balanceText.text = $"NATURE BALANCE {Mathf.RoundToInt(BalancePercent)}%  —  " +
                               (Mathf.Abs(diff) <= 1 ? "<color=#7CE36B>BALANCED</color>"
                                : diff > 0 ? "<color=#FFD23F>ต้องการแสงแดด</color>" : "<color=#3FC3FF>ต้องการน้ำ</color>");

            for (int i = 0; i < 2; i++)
            {
                gaugeFill[i].fillAmount = gauge[i];
                holdFill[i].fillAmount = state[i] == G.Armed ? releaseHold[i].Progress
                                       : state[i] == G.Collect ? Mathf.Clamp01(poseStable[i] / collectStartDelay) : 0f;
                var p = TrackingReceiver.P(i);
                string s;
                if (p == null || !p.Present)
                    s = "<color=#FF5A4E>TRACKING LOST</color> " + (p != null && p.Zone == "near" ? "ถอยหลังอีกนิด"
                        : p != null && p.Zone == "far" ? "เดินเข้ามาใกล้อีกนิด" : "ยืนในพื้นที่เล่น 1.5–2 m");
                else if (i == 0)
                    s = state[i] switch
                    {
                        G.Collect => p.HandsUp ? "กำลังสะสมน้ำ..." : "ยกแขนทั้งสองข้างขึ้นเพื่อสะสมน้ำ",
                        G.Ready => "ลดแขนลงก่อน",
                        G.Armed => p.Push ? "ค้างไว้..." : "ผลักมือทั้งสองไปข้างหน้าเพื่อส่งน้ำ",
                        G.Sending => "กำลังส่งน้ำไปที่ราก!",
                        _ => "ส่งสำเร็จ! ลดมือ แล้วเริ่มรับน้ำใหม่"
                    };
                else
                    s = state[i] switch
                    {
                        G.Collect => p.HandsUp ? "กำลังสะสมแสงอาทิตย์..." : "ยกแขนทั้งสองข้างขึ้นเพื่อสะสมแสง",
                        G.Armed => p.Push ? "ค้างไว้..." : "ผลักมือทั้งสองไปข้างหน้าเพื่อส่งแสง",
                        G.Sending => "กำลังส่งแสงแดด!",
                        _ => "ส่งสำเร็จ! ลดมือ แล้วยกแขนสะสมใหม่"
                    };
                playerStatus[i].text = s;
                bool ready = state[i] == G.Ready || state[i] == G.Armed;
                readyAlert[i].text = ready ? (i == 0 ? "WATER READY!" : "SUNLIGHT READY!") : "";
                readyAlert[i].transform.localScale = Vector3.one * UIKit.Pulse();
            }
            var r = TrackingReceiver.Instance;
            statusText.text = TrackingReceiver.StatusLine();
        }

        void BuildResult()
        {
            var r = new GameObject("Result", typeof(RectTransform)).transform;
            r.SetParent(ui.root, false);
            UIKit.Stretch((RectTransform)r);
            var c = new Vector2(0.5f, 0.5f);
            ui.Label(r, "<b>THE EARTH IS REBOOTING</b>", new Vector2(0.5f, 1f), new Vector2(0, -80), new Vector2(1600, 100), 72, Palette.Cream);
            ui.Panel(r, "Card", new Vector2(0.27f, 0.5f), new Vector2(0, 0), new Vector2(680, 520), Palette.Panel);
            ui.Label(r, "<b>TEAM RESULT</b>", new Vector2(0.27f, 0.5f), new Vector2(0, 210), new Vector2(600, 50), 40, Palette.Gold);
            ui.Label(r,
                $"Growth: <b>{Mathf.RoundToInt(growth * 100)}%</b>\n" +
                $"<color=#3FC3FF>ส่งน้ำสำเร็จ: {waterDelivered} ครั้ง</color>\n" +
                $"<color=#FFD23F>ส่งแสงแดดสำเร็จ: {sunDelivered} ครั้ง</color>\n" +
                $"Nature Balance: {Mathf.RoundToInt(BalancePercent)}%\n" +
                $"Cooperation Bonus: {coopBonuses} ครั้ง\n" +
                $"Air Recovery (ภารกิจ 1): {Mathf.RoundToInt(GameSession.AirRecovery * 100)}%",
                new Vector2(0.27f, 0.5f), new Vector2(0, -20), new Vector2(620, 380), 34, Palette.Cream, TextAnchor.MiddleLeft);
            ui.Label(r, "<b>FINAL OUTCOME</b>\nต้นไม้เติบโตสมบูรณ์\nเมืองเริ่มกลับมามีพื้นที่สีเขียว\n\n" +
                        "<i>การฟื้นฟูธรรมชาติ เริ่มต้นได้จากเมล็ดพันธุ์เพียงเมล็ดเดียว\nและความร่วมมือของคนสองคน</i>",
                     new Vector2(0.75f, 0.62f), Vector2.zero, new Vector2(800, 400), 34, Palette.Cream);
            ui.Label(r, "ยกมือทั้งสองข้างค้างไว้ เพื่อกลับหน้าแรก", new Vector2(0.5f, 0f), new Vector2(0, 50),
                     new Vector2(1400, 50), 28, Palette.Gold);
            fader.transform.SetAsLastSibling();
            _ = c;
        }
    }
}
