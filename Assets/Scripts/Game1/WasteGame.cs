using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RebootEarth.Game1
{
    /// <summary>
    /// GAME 1 - SCRAP SWEEPERS: Clean the City. Save the Air.
    /// Flow: title card -> tutorial (equipment + practice) -> 3-2-1 -> 150 s collection -> time out -> result.
    /// </summary>
    public class WasteGame : MonoBehaviour
    {
        public enum Phase { Title, Tutorial, Countdown, Playing, TimeUp, Result }

        [Header("Rules")]
        public float gameDuration = 150f;
        public float spawnIntervalMin = 1.6f, spawnIntervalMax = 2.6f;
        public int maxWastePerSide = 6;
        public float rareIntervalMin = 18f, rareIntervalMax = 28f;
        public float rareLifetime = 9f;
        public float gravity = 16f;

        [Header("Scene refs (optional)")]
        public GameObject player1Prefab;
        public GameObject player2Prefab;

        public Phase phase;
        public int totalImpactSpawned, totalImpactCollected;
        public float AirRecovery => totalImpactSpawned > 0 ? (float)totalImpactCollected / totalImpactSpawned : 0f;

        // ---- scene objects: built by BuildScene() (menu Reboot The Earth/Build Scenes) and saved in the scene
        [SerializeField, HideInInspector] bool built;
        [SerializeField, HideInInspector] Canvas hudCanvas;
        [SerializeField, HideInInspector] WastePlayer[] players = new WastePlayer[2];
        readonly List<WasteItem> items = new List<WasteItem>();
        readonly float[] spawnTimer = new float[2];
        float rareTimer, timeLeft;
        [SerializeField, HideInInspector] CityBackdrop city;
        [SerializeField, HideInInspector] Transform world;
        UIKit ui;

        // HUD
        [SerializeField, HideInInspector] Text timerText, airText, centerText, subText, hintText, statusText;
        [SerializeField, HideInInspector] Image airFill, fader;
        [SerializeField, HideInInspector] Text[] scoreText = new Text[2], comboText = new Text[2], bagText = new Text[2], holdText = new Text[2];
        [SerializeField, HideInInspector] Text[] alertText = new Text[2], readyText = new Text[2];
        [SerializeField, HideInInspector] Image[] readyFill = new Image[2];
        readonly float[] alertUntil = new float[2];
        [SerializeField, HideInInspector] GameObject hud, tutorialPanel;
        GameObject resultPanel;
        float rareAlertUntil;
        readonly ContinueGesture cont = new ContinueGesture();

        void Start()
        {
            TrackingReceiver.Ensure();
            Application.targetFrameRate = 60;
            if (!built) BuildScene();
            else ui = new UIKit(this, hudCanvas);
            foreach (var p in players) p.game = this;
            fader.gameObject.SetActive(true);
            fader.color = Color.black;
            fader.transform.SetAsLastSibling();
            StartCoroutine(Flow());
        }

        /// <summary>Creates every visual (city, players, stations, 3D citizens, HUD). Called in the editor
        /// by Reboot The Earth/Build Scenes so everything is visible and editable in the scene.</summary>
        public void BuildScene()
        {
            world = new GameObject("World").transform;
            city = CityBackdrop.Create(world, 0f, 11);

            // P1 left half, P2 right half (no sharing / stealing)
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Player" + (i + 1));
                go.transform.SetParent(world, false);
                var p = go.AddComponent<WastePlayer>();
                p.index = i;
                p.game = this;
                p.area = i == 0 ? Rect.MinMaxRect(-9.0f, -3.7f, -0.6f, 2.6f) : Rect.MinMaxRect(0.6f, -3.7f, 9.0f, 2.6f);
                p.stationRect = i == 0 ? Rect.MinMaxRect(-9.6f, -4.6f, -6.6f, -1.6f) : Rect.MinMaxRect(6.6f, -4.6f, 9.6f, -1.6f);
                p.Build();
                players[i] = p;
            }
            // divider
            Art.Box(world, "Divider", new Color(1f, 0.95f, 0.85f, 0.35f), new Vector2(0, -0.6f), new Vector2(0.06f, 8.4f), -40, 20);

            // 3D citizens (a little 3D in the 2D world), arms mirror the players
            Astronaut.Create(player1Prefab, world, new Vector3(-3.4f, -5.6f, 4f), 2.8f, 0);
            Astronaut.Create(player2Prefab, world, new Vector3(3.4f, -5.6f, 4f), 2.8f, 1);

            BuildHud();
            if (!Application.isPlaying)
            {
                fader.color = new Color(0, 0, 0, 0);
                fader.gameObject.SetActive(false);
            }
            built = true;
        }

        // =====================================================================  flow
        IEnumerator Flow()
        {
            yield return ui.Fade(fader, 1f, 0f, 0.8f);

            // ---- title card
            phase = Phase.Title;
            ShowCenter("MISSION 1\n<size=110><b>" + GameSession.Game1Title + "</b></size>",
                       GameSession.Game1Tagline + "\nเก็บขยะอิเล็กทรอนิกส์ให้เมืองกลับมาหายใจได้อีกครั้ง");
            yield return new WaitForSeconds(3.5f);

            // ---- STEP 1 intro / tutorial
            phase = Phase.Tutorial;
            ShowCenter("", "");
            tutorialPanel.SetActive(true);
            foreach (var p in players) { p.controlsEnabled = true; p.practiced = false; }
            for (int i = 0; i < 2; i++) SpawnTutorialItem(i);
            float allReady = 0f;
            while (true)
            {
                int present = 0, ready = 0;
                for (int i = 0; i < 2; i++)
                {
                    var p = players[i];
                    bool here = p.Input != null && p.Input.Present;
                    bool isReady = here && p.stableTime > 2.5f && (p.practiced || p.stableTime > 12f);
                    if (here) present++;
                    if (isReady) ready++;
                    readyFill[i].fillAmount = !here ? 0f : isReady ? 1f
                        : Mathf.Clamp01(p.stableTime / 2.5f) * (p.practiced ? 1f : 0.6f);
                    readyText[i].text = !here ? ZoneHint(p.Input)
                        : isReady ? "<color=#7CE36B>READY!</color>"
                        : p.stableTime < 2.5f ? "ยกมือทั้งสองข้าง ให้ระบบจับตำแหน่งมือ"
                        : "ลองคีบขยะตัวอย่าง แล้วปล่อยลงถุง";
                    if (!items.Exists(w => w != null && w.owner == i && w.tutorial && w.state != WasteItem.State.Gone)
                        && p.held == null && !p.practiced)
                        SpawnTutorialItem(i);
                }
                allReady = present > 0 && ready == present ? allReady + Time.deltaTime : 0f;
                if (allReady > 1f || TrackingReceiver.ContinuePressed()) break;
                yield return null;
            }
            tutorialPanel.SetActive(false);
            foreach (var w in items) if (w != null) Destroy(w.gameObject);
            items.Clear();
            foreach (var p in players) { p.ClearForTutorial(); p.controlsEnabled = false; }
            totalImpactSpawned = totalImpactCollected = 0;

            // ---- countdown
            phase = Phase.Countdown;
            for (int n = 3; n > 0; n--)
            {
                ShowCenter("<size=180><b>" + n + "</b></size>", "เตรียมเก็บขยะ!");
                yield return new WaitForSeconds(1f);
            }
            ShowCenter("<size=140><b>GO!</b></size>", "");
            foreach (var p in players) p.controlsEnabled = true;

            // ---- STEP 2 collection (150 s)
            phase = Phase.Playing;
            timeLeft = gameDuration;
            spawnTimer[0] = 0.3f; spawnTimer[1] = 0.3f;
            rareTimer = Random.Range(rareIntervalMin, rareIntervalMax);
            float goHide = 1f;
            while (timeLeft > 0f)
            {
                timeLeft -= Time.deltaTime;
                if ((goHide -= Time.deltaTime) < 0f) ShowCenter("", "");
                Spawning(Time.deltaTime);
                yield return null;
            }
            timeLeft = 0f;

            // ---- STEP 4 time out: only items already in the bag count
            phase = Phase.TimeUp;
            foreach (var p in players)
            {
                p.controlsEnabled = false;
                if (p.held != null) { p.held.Vanish(); p.held = null; }
            }
            foreach (var w in items) if (w != null && w.state != WasteItem.State.Gone) w.Vanish();
            items.Clear();
            ShowCenter("<size=130><b>TIME UP!</b></size>", "หมดเวลา! นับเฉพาะขยะที่ลงถุงสำเร็จ");
            yield return new WaitForSeconds(2.5f);
            ShowCenter("", "");

            // ---- result
            phase = Phase.Result;
            GameSession.AirRecovery = AirRecovery;
            GameSession.TeamScore = players[0].score + players[1].score;
            hud.SetActive(false);
            foreach (var p in players) p.gameObject.SetActive(false);   // clear view of the city
            BuildResult();
            yield return CleanCity(AirRecovery);
            cont.Reset();
            while (!cont.Tick(Time.deltaTime)) yield return null;
            yield return ui.Fade(fader, 0f, 1f, 0.8f);
            GameSession.Load(GameSession.Game2Scene);
        }

        IEnumerator CleanCity(float target)
        {
            // before -> after: the city slowly loses its junk and smog
            yield return new WaitForSeconds(1.0f);
            float shown = Mathf.Max(0.15f, target);
            for (float t = 0; t < 4f; t += Time.deltaTime)
            {
                city.clean = Mathf.SmoothStep(0f, shown, t / 4f);
                yield return null;
            }
            city.clean = shown;
        }

        // ================================================================  spawning
        void Spawning(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                spawnTimer[i] -= dt;
                if (spawnTimer[i] <= 0f)
                {
                    spawnTimer[i] = Random.Range(spawnIntervalMin, spawnIntervalMax);
                    if (CountOnSide(i) < maxWastePerSide) Spawn(i, WasteInfo.Random(), false);
                }
            }
            rareTimer -= dt;
            if (rareTimer <= 0f)
            {
                rareTimer = Random.Range(rareIntervalMin, rareIntervalMax);
                var t = WasteInfo.Random();
                Spawn(0, t, true);
                Spawn(1, t, true);
                rareAlertUntil = Time.time + 2.5f;
            }
        }

        int CountOnSide(int i)
        {
            int n = 0;
            foreach (var w in items) if (w != null && w.owner == i && w.state != WasteItem.State.Gone) n++;
            return n;
        }

        WasteItem Spawn(int side, WasteType type, bool rare, bool tutorial = false)
        {
            var p = players[side];
            Vector2 pos = p.area.center;
            for (int tries = 0; tries < 30; tries++)
            {
                pos = new Vector2(Random.Range(p.area.xMin + 0.6f, p.area.xMax - 0.6f),
                                  Random.Range(p.area.yMin + 0.3f, p.area.yMax - 0.4f));
                bool ok = !p.stationRect.Contains(pos) && Vector2.Distance(pos, p.stationRect.center) > 2.2f;
                foreach (var w in items)
                    if (w != null && w.state == WasteItem.State.Idle && Vector2.Distance(w.Pos, pos) < 1.3f) ok = false;
                if (Vector2.Distance(pos, p.BagMouth) < 1.4f) ok = false;
                if (ok) break;
            }
            var item = WasteItem.Create(world, type, rare, side, pos);
            item.tutorial = tutorial;
            item.rareLifetime = rareLifetime;
            items.Add(item);
            if (!tutorial) totalImpactSpawned += WasteInfo.Impact(type);
            return item;
        }

        void SpawnTutorialItem(int side)
        {
            var w = Spawn(side, WasteType.Cable, false, true);
            var a = players[side].area;
            w.SetPosition(new Vector2(a.center.x + (side == 0 ? 1.8f : -1.8f), a.center.y + 0.6f));
        }

        public WasteItem NearestWaste(int side, Vector2 tip, float radius)
        {
            WasteItem best = null;
            float bestD = radius;
            foreach (var w in items)
            {
                if (w == null || w.owner != side || w.state != WasteItem.State.Idle) continue;
                float d = Vector2.Distance(w.Pos, tip);
                if (d < bestD) { bestD = d; best = w; }
            }
            return best;
        }

        // ================================================================  update
        void Update()
        {
            float dt = Time.deltaTime;
            foreach (var p in players) p.Tick(dt);
            StepFalling(dt);
            UpdateHud();
        }

        void StepFalling(float dt)
        {
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var w = items[i];
                if (w == null) { items.RemoveAt(i); continue; }
                if (w.Expired) { w.Vanish(); items.RemoveAt(i); continue; }
                if (w.state != WasteItem.State.Falling) continue;
                var p = players[w.owner];
                Vector2 prev = w.Pos;
                w.velocity += Vector2.down * gravity * dt;
                Vector2 now = prev + w.velocity * dt;

                if (!w.bouncedOffFullBag && w.velocity.y < 0 && p.CatchCheck(w, prev, now))
                {
                    if (p.BagFull)
                    {
                        // full bag: item bounces off the rim and lands outside
                        w.bouncedOffFullBag = true;
                        w.velocity = new Vector2((now.x < p.BagMouth.x ? -1f : 1f) * 3.5f, 4f);
                        Alert(p.index, "BAG FULL!  ไปเทที่ Recycling Station", Palette.Bad, 2f);
                    }
                    else
                    {
                        items.RemoveAt(i);
                        p.AddToBag(w);
                        if (!w.tutorial) totalImpactCollected += WasteInfo.Impact(w.type);
                        else p.practiced = true;
                        int pts = WasteInfo.Points(w.type) + (w.rare && !w.tutorial ? 30 : 0);
                        ui.Popup(p.BagMouth + Vector2.up * 0.6f, "+" + pts, w.rare ? Palette.Gold : Palette.Good, 52);
                        if (p.BagFull) Alert(p.index, "BAG FULL!  ไปเทที่ Recycling Station", Palette.Bad, 3f);
                        continue;
                    }
                }
                // keep inside the player's own half
                now.x = Mathf.Clamp(now.x, p.area.xMin, p.area.xMax);
                if (now.y <= p.area.yMin)
                {
                    // dropped outside the bag: no points, combo resets, item stays on the ground
                    now.y = p.area.yMin + 0.05f;
                    w.state = WasteItem.State.Idle;
                    w.SetSorting(false);
                    if (!w.tutorial)
                    {
                        p.ResetCombo();
                        ui.Popup(now + Vector2.up * 0.8f, "MISS  (นอกถุง)", Palette.Bad, 38);
                    }
                }
                w.SetPosition(now);
            }
        }

        // ================================================================  events
        public void OnGrab(WastePlayer p, WasteItem w)
        {
            if (w.rare) ui.Popup(w.Pos + Vector2.up * 0.9f, "RARE!", Palette.Gold, 40);
        }

        public void OnMiss(WastePlayer p)
        {
            if (phase == Phase.Playing) ui.Popup(p.GrabTip + Vector2.up * 0.7f, "คีบพลาด", Palette.Bad, 36);
        }

        public void OnRelease(WastePlayer p, WasteItem w) { }

        public void OnComboBonus(WastePlayer p, int extra)
        {
            Alert(p.index, $"COMBO x{p.combo}!  +{extra}", Palette.Gold, 1.6f);
        }

        public void OnDump(WastePlayer p, int count)
        {
            ui.Popup(p.stationRect.center + Vector2.up * 1.8f, $"RECYCLED x{count}", Palette.Good, 46);
            Alert(p.index, "เทขยะสำเร็จ! เก็บต่อได้เลย", Palette.Good, 1.5f);
        }

        void Alert(int side, string text, Color c, float seconds)
        {
            alertText[side].text = text;
            alertText[side].color = c;
            alertUntil[side] = Time.time + seconds;
        }

        // ================================================================  HUD
        void BuildHud()
        {
            ui = new UIKit(this, "HUD");
            hudCanvas = ui.canvas;
            hud = new GameObject("HudRoot", typeof(RectTransform));
            hud.transform.SetParent(ui.root, false);
            UIKit.Stretch((RectTransform)hud.transform);
            var h = hud.transform;

            // timer + air recovery (team)
            ui.Panel(h, "TimerBack", new Vector2(0.5f, 1f), new Vector2(0, -78), new Vector2(380, 140), Palette.Panel);
            timerText = ui.Label(h, "2:30", new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(360, 90), 72, Palette.Cream);
            timerText.fontStyle = FontStyle.Bold;
            airFill = ui.Bar(h, new Vector2(0.5f, 1f), new Vector2(0, -122), new Vector2(330, 26), Palette.Water, new Color(0, 0, 0, 0.6f));
            airText = ui.Label(h, "AIR RECOVERY 0%", new Vector2(0.5f, 1f), new Vector2(0, -160), new Vector2(420, 40), 26, Palette.Water);

            for (int i = 0; i < 2; i++)
            {
                float ax = i == 0 ? 0f : 1f;
                float sx = i == 0 ? 1f : -1f;
                var c = i == 0 ? Palette.P1 : Palette.P2;
                var anchor = new Vector2(ax, 1f);
                ui.Panel(h, "PanelP" + (i + 1), anchor, new Vector2(sx * 300, -100), new Vector2(560, 175), Palette.Panel);
                var name = ui.Label(h, "PLAYER " + (i + 1), anchor, new Vector2(sx * 190, -40), new Vector2(320, 44), 30, c,
                                    TextAnchor.MiddleCenter);
                name.fontStyle = FontStyle.Bold;
                scoreText[i] = ui.Label(h, "0", anchor, new Vector2(sx * 190, -95), new Vector2(320, 70), 64, Palette.Cream);
                scoreText[i].fontStyle = FontStyle.Bold;
                comboText[i] = ui.Label(h, "COMBO 0", anchor, new Vector2(sx * 440, -50), new Vector2(220, 40), 28, Palette.Gold);
                bagText[i] = ui.Label(h, "BAG 0/5", anchor, new Vector2(sx * 440, -95), new Vector2(220, 40), 28, Palette.Good);
                holdText[i] = ui.Label(h, "", anchor, new Vector2(sx * 300, -158), new Vector2(540, 36), 22, Palette.Cream);
                alertText[i] = ui.Label(h, "", new Vector2(i == 0 ? 0.25f : 0.75f, 0.66f), Vector2.zero,
                                        new Vector2(860, 90), 46, Palette.Bad);
                alertText[i].fontStyle = FontStyle.Bold;
            }
            statusText = ui.Label(h, "", new Vector2(0.5f, 0f), new Vector2(0, 22), new Vector2(1400, 34), 20,
                                  new Color(1, 1, 1, 0.7f));

            // center title text
            centerText = ui.Label(ui.root, "", new Vector2(0.5f, 0.55f), Vector2.zero, new Vector2(1600, 400), 72, Palette.Cream);
            subText = ui.Label(ui.root, "", new Vector2(0.5f, 0.38f), Vector2.zero, new Vector2(1500, 160), 36, Palette.Cream);

            // tutorial panel (STEP 1)
            tutorialPanel = new GameObject("Tutorial", typeof(RectTransform));
            tutorialPanel.transform.SetParent(ui.root, false);
            UIKit.Stretch((RectTransform)tutorialPanel.transform);
            var tp = tutorialPanel.transform;
            ui.Panel(tp, "Back", new Vector2(0.5f, 0f), new Vector2(0, 118), new Vector2(1500, 200), Palette.Panel);
            ui.Label(tp, "<b>STEP 1: PREPARE YOUR EQUIPMENT</b>", new Vector2(0.5f, 0f), new Vector2(0, 188),
                     new Vector2(1400, 50), 36, Palette.Gold);
            ui.Label(tp, "มือซ้าย = <color=#7CE36B>ถุงขยะ</color> (ปากถุงเปิดตลอด จุได้ 5 ชิ้น)      " +
                         "มือขวา = <color=#C9CED3>ที่คีบ</color> (จีบนิ้ว 1 ครั้ง = คีบ / จีบอีกครั้ง = ปล่อย)\n" +
                         "ถุงเต็ม → พาถุงไปค้างไว้ที่ RECYCLING STATION 1.2 วินาที เพื่อเทขยะ",
                     new Vector2(0.5f, 0f), new Vector2(0, 118), new Vector2(1440, 90), 26, Palette.Cream);
            hintText = ui.Label(tp, "คีบขยะตัวอย่างแล้วปล่อยลงถุง เพื่อเริ่มภารกิจ",
                                new Vector2(0.5f, 0f), new Vector2(0, 50), new Vector2(1400, 36), 22, new Color(1, 1, 1, 0.75f));
            for (int i = 0; i < 2; i++)
            {
                var a = new Vector2(i == 0 ? 0.25f : 0.75f, 0.76f);
                readyText[i] = ui.Label(tp, "", a, new Vector2(0, 40), new Vector2(820, 60), 34, Palette.Cream);
                readyFill[i] = ui.Bar(tp, a, new Vector2(0, -10), new Vector2(420, 22), i == 0 ? Palette.P1 : Palette.P2,
                                      new Color(0, 0, 0, 0.6f));
            }
            tutorialPanel.SetActive(false);
            statusText.transform.SetParent(ui.root, true);   // camera status always on top
            statusText.transform.SetAsLastSibling();

            fader = ui.Fader();
        }

        static string ZoneHint(PlayerControl p)
        {
            if (p == null) return "";
            return p.Zone == "near" ? "<color=#FF5A4E>ถอยหลังอีกนิด</color> (ระยะเล่น 1.5–2 m)"
                 : p.Zone == "far" ? "<color=#FF5A4E>เดินเข้ามาใกล้อีกนิด</color> (ระยะเล่น 1.5–2 m)"
                 : "ยืนในพื้นที่เล่น (ระยะ 1.5–2 m จากกล้อง)";
        }

        void ShowCenter(string big, string sub)
        {
            centerText.text = big;
            subText.text = sub;
        }

        void UpdateHud()
        {
            if (hud == null || !hud.activeSelf) return;
            float t = phase == Phase.Playing ? timeLeft : phase >= Phase.TimeUp ? 0f : gameDuration;
            int sec = Mathf.CeilToInt(t);
            timerText.text = $"{sec / 60}:{sec % 60:00}";
            timerText.color = sec <= 10 && phase == Phase.Playing && Mathf.Repeat(Time.time * 2f, 1f) > 0.5f ? Palette.Bad : Palette.Cream;
            airFill.fillAmount = Mathf.MoveTowards(airFill.fillAmount, AirRecovery, Time.deltaTime);
            airText.text = $"AIR RECOVERY {Mathf.RoundToInt(AirRecovery * 100)}%";

            for (int i = 0; i < 2; i++)
            {
                var p = players[i];
                scoreText[i].text = p.score.ToString();
                comboText[i].text = "COMBO " + p.combo;
                bagText[i].text = $"BAG {p.bag.Count}/{WastePlayer.BagCapacity}";
                bagText[i].color = p.BagFull ? Palette.Bad : Palette.Good;
                holdText[i].text = p.held != null
                    ? $"กำลังคีบ: {WasteInfo.Name(p.held.type)}  [{WasteInfo.Tier(p.held.type)}]  {WasteInfo.Points(p.held.type)} pts" +
                      (p.held.rare ? "  <color=#FFD54A>+30 RARE</color>" : "")
                    : "";

                // alert priority: tracking lost > bag full > timed alerts > rare
                var inp = p.Input;
                bool playing = phase == Phase.Playing;
                if (playing && (inp == null || !inp.Present))
                    SetAlert(i, "TRACKING LOST\n" + ZoneHint(inp), Palette.Bad, true);
                else if (playing && !inp.GrabTracked)
                    SetAlert(i, "ไม่เห็นมือขวา (ที่คีบหยุดชั่วคราว)", Palette.Bad, true);
                else if (playing && p.BagFull && Time.time > alertUntil[i])
                    SetAlert(i, "BAG FULL!  ไปเทที่ Recycling Station", Palette.Bad, true);
                else if (Time.time < alertUntil[i])
                    alertText[i].transform.localScale = Vector3.one * UIKit.Pulse();
                else if (playing && Time.time < rareAlertUntil)
                    SetAlert(i, ">> RARE E-WASTE! +30 <<", Palette.Gold, true);
                else alertText[i].text = "";
            }
            var r = TrackingReceiver.Instance;
            statusText.text = TrackingReceiver.StatusLine();
        }

        void SetAlert(int i, string text, Color c, bool pulse)
        {
            alertText[i].text = text;
            alertText[i].color = c;
            alertText[i].transform.localScale = Vector3.one * (pulse ? UIKit.Pulse() : 1f);
        }

        // ================================================================  result
        void BuildResult()
        {
            resultPanel = new GameObject("Result", typeof(RectTransform));
            resultPanel.transform.SetParent(ui.root, false);
            UIKit.Stretch((RectTransform)resultPanel.transform);
            var r = resultPanel.transform;
            ui.Label(r, "<b>MISSION COMPLETE</b>", new Vector2(0.5f, 1f), new Vector2(0, -70), new Vector2(1400, 90), 70, Palette.Cream);
            ui.Label(r, "ภาพเมืองก่อน → หลังการทำความสะอาด", new Vector2(0.5f, 1f), new Vector2(0, -130), new Vector2(1400, 50),
                     30, Palette.Cream);

            for (int i = 0; i < 2; i++)
            {
                var p = players[i];
                var a = new Vector2(i == 0 ? 0.2f : 0.8f, 0.5f);
                ui.Panel(r, "Card", a, new Vector2(0, 10), new Vector2(560, 560), Palette.Panel);
                ui.Label(r, "<b>PLAYER " + (i + 1) + "</b>", a, new Vector2(0, 240), new Vector2(520, 60), 40, i == 0 ? Palette.P1 : Palette.P2);
                ui.Label(r, "<b>" + p.score + "</b> pts", a, new Vector2(0, 170), new Vector2(520, 80), 64, Palette.Cream);
                string body =
                    $"ขยะที่เก็บได้: {p.collected} ชิ้น\n" +
                    $"• สายไฟเก่า: {p.perType[0]}\n• โทรศัพท์เก่า: {p.perType[1]}\n" +
                    $"• แผงวงจร/คอมพิวเตอร์: {p.perType[2]}\n• แบตเตอรี่เก่า: {p.perType[3]}\n" +
                    $"Best Combo: {p.bestCombo}  (โบนัส +{p.comboBonusTotal})\n" +
                    $"Rare E-Waste: {p.rareCollected}  (โบนัส +{p.rareBonusTotal})";
                ui.Label(r, body, a, new Vector2(0, -70), new Vector2(500, 360), 28, Palette.Cream, TextAnchor.MiddleLeft);
            }
            var c = new Vector2(0.5f, 0.5f);
            ui.Panel(r, "Team", c, new Vector2(0, -190), new Vector2(560, 300), Palette.Panel);
            ui.Label(r, "<b>TEAM RESULT</b>", c, new Vector2(0, -70), new Vector2(520, 50), 36, Palette.Gold);
            ui.Label(r,
                $"คะแนนรวม: <b>{players[0].score + players[1].score}</b>\n" +
                $"Impact ที่เก็บได้: {totalImpactCollected} / {totalImpactSpawned}\n" +
                $"<size=46><color=#3FC3FF><b>AIR RECOVERY {Mathf.RoundToInt(AirRecovery * 100)}%</b></color></size>",
                c, new Vector2(0, -190), new Vector2(520, 200), 30, Palette.Cream);
            ui.Label(r, "ภารกิจต่อไป: นำเมล็ดพันธุ์สุดท้ายไปปลูก  —  ยกมือทั้งสองข้างค้างไว้ เพื่อไปต่อ",
                     new Vector2(0.5f, 0f), new Vector2(0, 50), new Vector2(1600, 50), 30, Palette.Gold);
            fader.transform.SetAsLastSibling();
        }
    }
}
