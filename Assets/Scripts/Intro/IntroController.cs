using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace RebootEarth
{
    /// <summary>Title screen + short story, then Mission 1.</summary>
    public class IntroController : MonoBehaviour
    {
        public GameObject player1Prefab;
        public GameObject player2Prefab;
        public float slideSeconds = 6f;

        // ---- scene objects: built by BuildScene() (menu Reboot The Earth/Build Scenes) and saved in the scene
        [SerializeField, HideInInspector] bool built;
        [SerializeField, HideInInspector] Canvas uiCanvas;
        UIKit ui;
        [SerializeField, HideInInspector] Image fader, holdBar;
        [SerializeField, HideInInspector] Text title, sub, story, prompt, status;
        [SerializeField, HideInInspector] Text[] slot = new Text[2];
        [SerializeField, HideInInspector] CityBackdrop city;
        [SerializeField, HideInInspector] Transform world;
        readonly ContinueGesture cont = new ContinueGesture();

        void Start()
        {
            TrackingReceiver.Ensure();
            Application.targetFrameRate = 60;
            GameSession.AirRecovery = 0f;
            GameSession.TeamScore = 0;
            if (!built) BuildScene();
            else ui = new UIKit(this, uiCanvas);
            city.clean = 0f;
            fader.gameObject.SetActive(true);
            fader.color = Color.black;
            fader.transform.SetAsLastSibling();
            StartCoroutine(Flow());
        }

        /// <summary>Creates every visual (city, characters, title UI). Called in the editor by
        /// Reboot The Earth/Build Scenes so everything is visible and editable in the scene.</summary>
        public void BuildScene()
        {
            world = new GameObject("World").transform;
            city = CityBackdrop.Create(world, 0f, 5);
            Astronaut.Create(player1Prefab, world, new Vector3(-5.5f, -5.2f, 2f), 3.4f, 0);
            Astronaut.Create(player2Prefab, world, new Vector3(5.5f, -5.2f, 2f), 3.4f, 1);

            ui = new UIKit(this);
            uiCanvas = ui.canvas;
            ui.Panel(null, "Shade", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920, 1080), new Color(0, 0, 0, 0.15f), false);
            title = ui.Label(null, "<b>REBOOT THE EARTH</b>", new Vector2(0.5f, 0.68f), Vector2.zero, new Vector2(1800, 200), 130, Palette.Cream);
            sub = ui.Label(null, "เก็บขยะ  •  ปลูกเมล็ดพันธุ์สุดท้าย  •  รีบูตโลกใบนี้ไปด้วยกัน",
                           new Vector2(0.5f, 0.56f), Vector2.zero, new Vector2(1600, 60), 38, Palette.Cream);
            story = ui.Label(null, "", new Vector2(0.5f, 0.78f), Vector2.zero, new Vector2(1500, 220), 44, Palette.Cream);
            prompt = ui.Label(null, "", new Vector2(0.5f, 0.2f), Vector2.zero, new Vector2(1600, 120), 36, Palette.Gold);
            holdBar = ui.Bar(null, new Vector2(0.5f, 0.12f), Vector2.zero, new Vector2(420, 18), Palette.Gold, new Color(0, 0, 0, 0.5f));
            for (int i = 0; i < 2; i++)
                slot[i] = ui.Label(null, "", new Vector2(i == 0 ? 0.17f : 0.83f, 0.45f), Vector2.zero, new Vector2(560, 120), 30,
                                   i == 0 ? Palette.P1 : Palette.P2);
            status = ui.Label(null, "", new Vector2(0.5f, 0f), new Vector2(0, 22), new Vector2(1500, 34), 20, new Color(1, 1, 1, 0.7f));
            fader = ui.Fader();
            if (!Application.isPlaying)
            {
                fader.color = new Color(0, 0, 0, 0);
                fader.gameObject.SetActive(false);
            }
            built = true;
        }

        IEnumerator Flow()
        {
            yield return ui.Fade(fader, 1f, 0f, 1f);
            // ---- title: wait for a player
            prompt.text = "ยืนห่างกล้อง 1.5–2 เมตร  (Player 1 ฝั่งซ้าย / Player 2 ฝั่งขวา)\nยกมือทั้งสองข้างค้างไว้เพื่อเริ่ม";
            yield return WaitContinue(float.MaxValue);
            title.text = ""; sub.text = "";

            string[] slides =
            {
                "ในอนาคต... โลกเต็มไปด้วยตึกสูงและขยะอิเล็กทรอนิกส์เกลื่อนพื้น\nไม่มีต้นไม้หลงเหลืออยู่อีกแล้ว",
                "ผู้คนติดโทรศัพท์ ไม่มีใครสนใจธรรมชาติ\nทุกคนต้องสวมหมวกและสะพายถังออกซิเจนเพื่อหายใจตลอดเวลา",
                "วันหนึ่ง เพื่อนซี้ 2 คนไปเจอหนังสือเก่าเล่มหนึ่งในห้องสมุด...",
                "หนังสือเล่าถึงโลกที่เคยเต็มไปด้วยป่าสีเขียว ให้ออกซิเจนฟรี\nและบอกถึงข้อดีของการเก็บขยะ  ท้ายเล่มแนบ <color=#7CE36B>เมล็ดพันธุ์สุดท้าย</color> ไว้",
                "ทั้งสองยืมหนังสือออกมา แล้วแยกกันไปช่วยเก็บขยะในเมือง\nก่อนนำเมล็ดพันธุ์ไปปลูกด้วยกัน",
            };
            StoryBook book = null;
            for (int s = 0; s < slides.Length; s++)
            {
                story.text = slides[s];
                if (s == 2) book = StoryBook.Create(world, new Vector2(0, -0.6f), 1.1f);
                if (s == 3 && book != null) StartCoroutine(OpenBook(book));
                prompt.text = "ยกมือทั้งสองข้างค้างไว้ = ข้าม";
                yield return WaitContinue(slideSeconds);
            }
            story.text = "";
            yield return ui.Fade(fader, 0f, 1f, 0.8f);
            GameSession.Load(GameSession.Game1Scene);
        }

        IEnumerator OpenBook(StoryBook b)
        {
            for (float t = 0; t < 1.5f; t += Time.deltaTime) { b.open = t / 1.5f; yield return null; }
            b.open = 1f;
        }

        IEnumerator WaitContinue(float maxSeconds)
        {
            cont.Reset();
            float t = 0f;
            yield return null;
            while (t < maxSeconds)
            {
                t += Time.deltaTime;
                if (cont.Tick(Time.deltaTime)) break;
                holdBar.fillAmount = cont.Progress;
                yield return null;
            }
            holdBar.fillAmount = 0f;
        }

        void Update()
        {
            var r = TrackingReceiver.Instance;
            if (r == null) return;
            for (int i = 0; i < 2; i++)
            {
                var p = r.players[i];
                slot[i].text = r.Simulated || !r.CameraConnected ? "" :
                    "PLAYER " + (i + 1) + "\n" + (p.Present ? "<color=#7CE36B>READY</color>"
                    : p.Zone == "near" ? "ถอยหลังอีกนิด" : p.Zone == "far" ? "เดินเข้ามาใกล้อีกนิด" : "รอผู้เล่น...");
            }
            status.text = TrackingReceiver.StatusLine();
        }
    }
}
