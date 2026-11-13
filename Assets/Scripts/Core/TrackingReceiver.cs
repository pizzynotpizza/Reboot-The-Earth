using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RebootEarth
{
    // ---------------------------------------------------------------------------------
    // JSON packet from Python/body_tracking_sender.py (JsonUtility field names = JSON keys)
    // ---------------------------------------------------------------------------------
    [Serializable]
    public class PlayerPacket
    {
        public bool active;
        public float d;
        public string zone = "none";
        public float lx, ly, rx, ry;
        public bool lt, rt;
        public float bag_x = 0.5f, bag_y = 0.5f, grab_x = 0.5f, grab_y = 0.5f;
        public bool pinch;
        public int pinch_n;
        public bool hand_ok;
        public bool hands_up, hands_down, push, v_pose, triangle;
        public string pose = "none";
        public float sx, sy, sw;
        public float lsx, lsy, rsx, rsy, lex, ley, rex, rey;
    }

    [Serializable]
    public class TrackingPacket
    {
        public double t;
        public float fps;
        public string src;
        public bool depth;
        public bool hands;
        public int player_count;
        public float ar;
        public PlayerPacket P1;
        public PlayerPacket P2;
    }

    /// <summary>Game-facing state of one player (smoothed, with keyboard simulation).</summary>
    public class PlayerControl
    {
        public readonly int index;            // 0 = P1 (left), 1 = P2 (right)
        public bool Present;                   // tracked and inside the play distance
        public string Zone = "none";           // ok / near / far / none
        public float Distance;
        public Vector2 Bag = new Vector2(0.5f, 0.4f);   // 0..1 in the player's reach box
        public Vector2 Grab = new Vector2(0.5f, 0.6f);
        public bool BagTracked, GrabTracked;   // false = tracking lost for that hand
        public bool PinchDown;                 // true on the frame a pinch toggle arrives
        public bool HandsUp, HandsDown, Push, VPose, Triangle;
        public string Pose = "none";
        /// <summary>Live arm joints (image 0..1, y up, mirrored). ArmsValid = shoulders + elbows seen.</summary>
        public Vector2 LSh, RSh, LEl, REl, LWr, RWr;
        public bool ArmsValid;
        public float Aspect = 16f / 9f;

        internal int lastPinchN = -1;
        internal bool injectPinch;

        /// <summary>Test / debug hook: move the hands and/or send one pinch (works in keyboard mode).</summary>
        public void Inject(Vector2? bag, Vector2? grab, bool pinch)
        {
            if (bag.HasValue) { rawBag = bag.Value; Bag = bag.Value; }
            if (grab.HasValue) { rawGrab = grab.Value; Grab = grab.Value; }
            injectPinch |= pinch;
        }

        internal string injectPose;
        internal float injectPoseUntil;

        /// <summary>Test / debug hook: force a pose (hands_up, push, v_pose, triangle, idle) for some seconds.</summary>
        public void InjectPose(string pose, float seconds)
        {
            injectPose = pose;
            injectPoseUntil = Time.unscaledTime + seconds;
        }
        internal Vector2 rawBag = new Vector2(0.5f, 0.4f), rawGrab = new Vector2(0.5f, 0.6f);

        public PlayerControl(int i) { index = i; }
    }

    /// <summary>
    /// Receives UDP packets on a thread, exposes P1 / P2 controls on the main thread.
    /// CAMERA ONLY by default (allowKeyboard = false): no packets for 1 s = nobody is tracked and the
    /// game waits for the camera. Developer keyboard mode (allowKeyboard = true in the Inspector):
    ///   P1  bag WASD   grabber TFGH   pinch Space    water: Z = hands up, X = push
    ///   P2  bag Arrows grabber IJKL   pinch RShift   sun:   C = hands up, V = push
    ///   Enter = continue / skip,  F1 = force keyboard mode
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class TrackingReceiver : MonoBehaviour
    {
        public static TrackingReceiver Instance { get; private set; }

        public int port = 5065;
        [Range(0f, 0.95f)] public float smoothing = 0.55f;
        public float timeoutSeconds = 1.0f;
        [Tooltip("Developer only. Off = the game is played with the camera only.")]
        public bool allowKeyboard = false;

        public readonly PlayerControl[] players = { new PlayerControl(0), new PlayerControl(1) };
        public bool Simulated { get; private set; }
        public string Source { get; private set; } = "none";
        /// <summary>Packets from Python arrived during the last timeoutSeconds.</summary>
        public bool CameraConnected => Time.unscaledTime - lastPacketTime <= timeoutSeconds;
        public float TrackerFps { get; private set; }
        public bool HandsAvailable { get; private set; }

        UdpClient udp;
        Thread thread;
        volatile bool running;
        string latestJson;
        readonly object lockObj = new object();
        float lastPacketTime = -999f;
        bool forceKeyboard;

        public static PlayerControl P(int i) => Instance != null ? Instance.players[i] : null;

        public static TrackingReceiver Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("TrackingReceiver");
            return go.AddComponent<TrackingReceiver>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            StartListening();
        }

        // one socket per domain: closed on stop / script reload so the port is never left busy
        static UdpClient sharedUdp;
        static bool unloadHooked;
        float retryAt;

        static void CloseShared()
        {
            try { sharedUdp?.Close(); } catch { }
            sharedUdp = null;
        }

        void StartListening()
        {
            if (!unloadHooked)
            {
                unloadHooked = true;
                AppDomain.CurrentDomain.DomainUnload += (_, __) => CloseShared();
            }
            CloseShared();
            try
            {
                udp = new UdpClient();
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                udp.Client.ReceiveTimeout = 500;
                sharedUdp = udp;
                running = true;
                thread = new Thread(Listen) { IsBackground = true };
                thread.Start();
                Debug.Log($"[Tracking] listening UDP {port}");
            }
            catch (Exception e)
            {
                udp = null;
                retryAt = Time.unscaledTime + 2f;
                Debug.LogWarning($"[Tracking] cannot open UDP {port}: {e.Message} -> keyboard mode, retrying");
            }
        }

        void Listen()
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    var data = udp.Receive(ref ep);
                    lock (lockObj) latestJson = Encoding.UTF8.GetString(data);
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { break; }
                catch (Exception) { }
            }
        }

        void OnDisable()
        {
            if (Instance != this) return;
            running = false;
            if (sharedUdp == udp) CloseShared();
            udp = null;
        }

        void OnEnable()
        {
            if (Instance == this && udp == null) StartListening();
        }

        void OnApplicationQuit() => CloseShared();

        void OnDestroy()
        {
            if (Instance != this) return;
            running = false;
            if (sharedUdp == udp) CloseShared();
            Instance = null;
        }

        void Update()
        {
            if (udp == null && Instance == this && Time.unscaledTime > retryAt) StartListening();
            var kb = Keyboard.current;
            if (!allowKeyboard) forceKeyboard = false;
            else if (kb != null && kb.f1Key.wasPressedThisFrame) forceKeyboard = !forceKeyboard;

            string json;
            lock (lockObj) { json = latestJson; latestJson = null; }
            if (json != null)
            {
                try
                {
                    var pkt = JsonUtility.FromJson<TrackingPacket>(json);
                    if (pkt != null)
                    {
                        lastPacketTime = Time.unscaledTime;
                        Source = pkt.src;
                        TrackerFps = pkt.fps;
                        HandsAvailable = pkt.hands;
                        if (!forceKeyboard)
                        {
                            Apply(players[0], pkt.P1);
                            Apply(players[1], pkt.P2);
                            if (pkt.ar > 0.1f) players[0].Aspect = players[1].Aspect = pkt.ar;
                        }
                    }
                }
                catch (Exception e) { Debug.LogWarning("[Tracking] bad packet " + e.Message); }
            }
            else
            {
                foreach (var p in players) p.PinchDown = false;
            }

            Simulated = allowKeyboard && (forceKeyboard || !CameraConnected);
            if (Simulated)
            {
                Source = "keyboard";
                Simulate(kb);
            }
            else if (!CameraConnected)
            {
                // camera only: no tracker data = nobody can play until the camera is back
                Source = "none";
                foreach (var p in players) ClearPlayer(p);
            }

            // smoothing in Unity too (UDP frames arrive at camera rate, game runs faster)
            float k = 1f - Mathf.Pow(smoothing, Time.unscaledDeltaTime * 60f);
            foreach (var p in players)
            {
                if (p.injectPinch) { p.PinchDown = true; p.injectPinch = false; }
                if (p.injectPose != null && Time.unscaledTime < p.injectPoseUntil)
                {
                    p.Pose = p.injectPose;
                    p.HandsUp = p.Pose == "hands_up" || p.Pose == "triangle";
                    p.Push = p.Pose == "push";
                    p.VPose = p.Pose == "v_pose";
                    p.Triangle = p.Pose == "triangle";
                    p.HandsDown = p.Pose == "idle" || p.Pose == "hands_down";
                }
                if (p.BagTracked) p.Bag = Vector2.Lerp(p.Bag, p.rawBag, k);
                if (p.GrabTracked) p.Grab = Vector2.Lerp(p.Grab, p.rawGrab, k);
            }
        }

        static void Apply(PlayerControl c, PlayerPacket p)
        {
            if (p == null) return;
            c.Present = p.active;
            c.Zone = p.zone ?? "none";
            c.Distance = p.d;
            c.BagTracked = p.active && p.lt;
            c.GrabTracked = p.active && p.rt;
            if (c.BagTracked) c.rawBag = new Vector2(p.bag_x, p.bag_y);
            if (c.GrabTracked) c.rawGrab = new Vector2(p.grab_x, p.grab_y);
            // pinch toggle = counter increased (robust to lost UDP packets)
            c.PinchDown = c.lastPinchN >= 0 && p.pinch_n > c.lastPinchN && p.active && p.hand_ok;
            c.lastPinchN = p.pinch_n;
            if (!p.hand_ok) c.GrabTracked = false;
            c.HandsUp = p.hands_up;
            c.HandsDown = p.hands_down;
            c.Push = p.push;
            c.VPose = p.v_pose;
            c.Triangle = p.triangle;
            c.Pose = p.pose;
            c.LSh = new Vector2(p.lsx, p.lsy); c.RSh = new Vector2(p.rsx, p.rsy);
            c.LEl = new Vector2(p.lex, p.ley); c.REl = new Vector2(p.rex, p.rey);
            c.LWr = new Vector2(p.lx, p.ly); c.RWr = new Vector2(p.rx, p.ry);
            c.ArmsValid = p.active && p.lsx > 0 && p.rsx > 0 && p.lex > 0 && p.rex > 0 && p.lt && p.rt;
        }

        void Simulate(Keyboard kb)
        {
            foreach (var p in players)
            {
                p.ArmsValid = false;
                p.Present = true; p.Zone = "ok"; p.BagTracked = true; p.GrabTracked = true;
                p.PinchDown = false;
            }
            if (kb == null) return;
            float s = 0.9f * Time.unscaledDeltaTime;
            var a = players[0];
            a.rawBag += new Vector2(Axis(kb.dKey, kb.aKey), Axis(kb.wKey, kb.sKey)) * s;
            a.rawGrab += new Vector2(Axis(kb.hKey, kb.fKey), Axis(kb.tKey, kb.gKey)) * s;
            a.PinchDown = kb.spaceKey.wasPressedThisFrame;
            a.HandsUp = kb.zKey.isPressed;
            a.Push = kb.xKey.isPressed;
            a.HandsDown = !a.HandsUp && !a.Push;
            a.VPose = false; a.Triangle = false;
            var b = players[1];
            b.rawBag += new Vector2(Axis(kb.rightArrowKey, kb.leftArrowKey),
                                    Axis(kb.upArrowKey, kb.downArrowKey)) * s;
            b.rawGrab += new Vector2(Axis(kb.lKey, kb.jKey), Axis(kb.iKey, kb.kKey)) * s;
            b.PinchDown = kb.rightShiftKey.wasPressedThisFrame;
            b.HandsUp = kb.cKey.isPressed;        // sun: raise both arms
            b.Push = kb.vKey.isPressed;           // sun: push both hands
            b.HandsDown = !b.HandsUp && !b.Push;
            b.VPose = false; b.Triangle = false;
            foreach (var p in players)
            {
                p.rawBag = Clamp01(p.rawBag);
                p.rawGrab = Clamp01(p.rawGrab);
                p.Pose = p.Triangle ? "triangle" : p.VPose ? "v_pose" : p.Push ? "push"
                       : p.HandsUp ? "hands_up" : "idle";
            }
        }

        static void ClearPlayer(PlayerControl p)
        {
            p.Present = false; p.Zone = "none"; p.BagTracked = false; p.GrabTracked = false;
            p.PinchDown = false; p.ArmsValid = false;
            p.HandsUp = p.HandsDown = p.Push = p.VPose = p.Triangle = false;
            p.Pose = "none";
        }

        /// <summary>One-line tracking status for the bottom of every screen.</summary>
        public static string StatusLine()
        {
            var r = Instance;
            if (r == null) return "";
            if (r.Simulated) return "DEVELOPER KEYBOARD MODE";
            if (!r.CameraConnected) return "<color=#FF5A4E>ไม่พบกล้อง - เปิด Python/run.bat ก่อนเล่น</color>";
            return $"CAMERA: {r.Source.ToUpper()}  {r.TrackerFps:0} fps" + (r.HandsAvailable ? "" : "   ! hand tracking OFF (pinch disabled)");
        }

        static float Axis(UnityEngine.InputSystem.Controls.KeyControl pos,
                          UnityEngine.InputSystem.Controls.KeyControl neg)
            => (pos.isPressed ? 1f : 0f) - (neg.isPressed ? 1f : 0f);

        static Vector2 Clamp01(Vector2 v) => new Vector2(Mathf.Clamp01(v.x), Mathf.Clamp01(v.y));

        /// <summary>Enter / numpad Enter pressed this frame (continue / skip).</summary>
        public static bool ContinuePressed()
        {
            var kb = Keyboard.current;
            return Instance != null && Instance.allowKeyboard && kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
        }
    }
}
