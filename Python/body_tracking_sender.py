"""
body_tracking_sender.py  -  "Reboot The Earth" (2-player body tracking for Unity)
-------------------------------------------------------------------------------
One tracking system for both mini games:

  Camera (Orbbec Gemini 2 L, auto fallback to webcam)
    -> YOLOv8-pose (body skeleton, up to 6 people)
    -> P1 (left half) / P2 (right half), play distance 1.5 - 2.0 m (Orbbec depth)
    -> MediaPipe Hand Landmarker on a crop around each RIGHT hand (pinch)
    -> gestures -> JSON over UDP -> Unity (port 5065)

Game 1  SCRAP SWEEPERS (waste hunting)
  left wrist  -> waste bag position    (lx, ly / bag_x, bag_y)
  right wrist -> trash grabber position (rx, ry / grab_x, grab_y)
  right hand pinch -> toggle grab / release (pinch_n counts debounced pinches)

Game 2  SEED OF TOMORROW (grow the last seed)
  P1 Water Guardian : hands_up (raise both arms, collect water) -> hands_down -> push = send water
  P2 Sun Guardian   : hands_up (raise both arms, collect sun)   -> push = send sunlight
  (v_pose / triangle are still sent but no longer used by the game)

Hands: MediaPipe 21-point skeleton on BOTH hands of each player (4 hands), drawn in the
  debug window. Right hand = pinch, left hand = skeleton + lhand_ok.
  Every flag is computed for both players; Unity uses the one that fits the role.

UDP packet (one per camera frame):
{
  "t": 1731045600.123, "fps": 28.4, "src": "orbbec", "depth": true, "hands": true,
  "player_count": 2, "ar": 1.7778,
  "P1": {
    "active": true,         # skeleton tracked AND inside the play distance
    "d": 1.72,              # distance to the camera in meters (0 = unknown / webcam)
    "zone": "ok",           # ok / near / far / none   (for the "step back" hint)
    "lx": .., "ly": .., "rx": .., "ry": ..,   # wrists, whole frame 0..1 (Unity y up)
    "lt": true, "rt": true,                   # wrist tracked this frame
    "bag_x": .., "bag_y": .., "grab_x": .., "grab_y": ..,  # wrists in the player's
                                              # personal reach box 0..1 (easy to reach)
    "pinch": false,         # right hand is pinching now
    "pinch_n": 12,          # counter, +1 for every debounced pinch (toggle grab)
    "hand_ok": true,        # right hand landmarks found (false = grabber tracking lost)
    "lhand_ok": true,       # left hand landmarks found
    "hands_up": false, "hands_down": true, "push": false,
    "v_pose": false, "triangle": false,
    "pose": "idle",         # main pose label for debug / UI
    "sx": .., "sy": .., "sw": ..,             # shoulder center + width
    "lsx" "lsy" "rsx" "rsy" "lex" "ley" "rex" "rey" "hx" "hy" "hw" "nx" "ny"
  },
  "P2": { same }
}

Run:  run.bat            (Orbbec if plugged in, else webcam)
      run.bat --source webcam --camera 1
"""

import argparse
import json
import math
import os
import socket
import threading
import time
import urllib.request

import cv2
import numpy as np
import torch
from ultralytics import YOLO

HERE = os.path.dirname(os.path.abspath(__file__))

# COCO keypoint indices (YOLOv8-pose)
KP_NOSE = 0
KP_LSH, KP_RSH = 5, 6
KP_LEL, KP_REL = 7, 8
KP_LW, KP_RW = 9, 10
KP_LHIP, KP_RHIP = 11, 12

KP_MIN_CONF = 0.45
COLOR_P1 = (255, 140, 0)     # BGR blue-ish
COLOR_P2 = (0, 140, 255)     # BGR orange

HAND_MODEL_URL = ("https://storage.googleapis.com/mediapipe-models/hand_landmarker/"
                  "hand_landmarker/float16/latest/hand_landmarker.task")
HAND_MODEL_FILE = os.path.join(HERE, "hand_landmarker.task")


# ----------------------------------------------------------------------------
# args
# ----------------------------------------------------------------------------
def parse_args():
    p = argparse.ArgumentParser(description="Reboot The Earth body tracking -> Unity UDP")
    p.add_argument("--source", choices=("auto", "orbbec", "webcam"), default="auto",
                   help="auto = Orbbec Gemini 2 L if connected, else webcam")
    p.add_argument("--camera", type=int, default=0, help="webcam index")
    p.add_argument("--model", default=os.path.join(HERE, "yolov8n-pose.pt"))
    p.add_argument("--imgsz", type=int, default=480, help="YOLO inference size")
    p.add_argument("--device", default="auto", help="auto / cuda / cpu")
    p.add_argument("--ip", default="127.0.0.1", help="Unity IP")
    p.add_argument("--port", type=int, default=5065, help="Unity UDP port")
    p.add_argument("--width", type=int, default=1280, help="camera width (hands need detail)")
    p.add_argument("--height", type=int, default=720)
    p.add_argument("--cam-fps", type=int, default=30)
    p.add_argument("--conf", type=float, default=0.45, help="person detection confidence")
    p.add_argument("--smooth", type=float, default=0.45,
                   help="position smoothing 0 (none) - 0.9 (very smooth, laggy)")
    # play zone (Orbbec depth)
    p.add_argument("--min-dist", type=float, default=1.5, help="play zone near edge (m)")
    p.add_argument("--max-dist", type=float, default=2.0, help="play zone far edge (m)")
    p.add_argument("--dist-hyst", type=float, default=0.15, help="zone hysteresis (m)")
    p.add_argument("--no-depth", action="store_true", help="ignore distance zone")
    # pinch
    p.add_argument("--pinch-on", type=float, default=0.32,
                   help="thumb-index gap / palm size below this = pinch")
    p.add_argument("--pinch-off", type=float, default=0.50,
                   help="gap above this = open hand (hysteresis)")
    p.add_argument("--pinch-cooldown", type=float, default=0.35,
                   help="seconds between two counted pinches (debounce)")
    p.add_argument("--no-hands", action="store_true", help="disable MediaPipe pinch")
    # reach box for game 1 (in shoulder widths around the shoulder center)
    p.add_argument("--reach-x", type=float, default=1.9, help="half width of reach box (x sw)")
    p.add_argument("--reach-y", type=float, default=1.6, help="half height of reach box (x sw)")
    p.add_argument("--pose-frames", type=int, default=3,
                   help="frames a pose must be seen before it is reported (anti flicker)")
    p.add_argument("--no-mirror", action="store_true", help="do not mirror the image")
    p.add_argument("--no-window", action="store_true", help="no preview window")
    p.add_argument("--print", action="store_true", help="print every packet")
    return p.parse_args()


# ----------------------------------------------------------------------------
# cameras (reader threads keep only the newest frame)
# ----------------------------------------------------------------------------
class CameraStream:
    def __init__(self, index, width, height, fps):
        self.cap = cv2.VideoCapture(index, cv2.CAP_DSHOW)
        if not self.cap.isOpened():
            self.cap = cv2.VideoCapture(index)
        if not self.cap.isOpened():
            raise RuntimeError(f"cannot open camera {index}")
        self.cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))
        self.cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
        self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
        self.cap.set(cv2.CAP_PROP_FPS, fps)
        self.cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
        self.frame, self.frame_id = None, 0
        self.lock = threading.Lock()
        self.running = True
        self.thread = threading.Thread(target=self._loop, daemon=True)
        self.thread.start()

    def _loop(self):
        while self.running:
            ok, f = self.cap.read()
            if not ok:
                time.sleep(0.005)
                continue
            with self.lock:
                self.frame = f
                self.frame_id += 1

    def read(self):
        with self.lock:
            return self.frame_id, self.frame

    def read_depth(self):
        return None

    def info(self):
        return (int(self.cap.get(cv2.CAP_PROP_FRAME_WIDTH)),
                int(self.cap.get(cv2.CAP_PROP_FRAME_HEIGHT)),
                self.cap.get(cv2.CAP_PROP_FPS))

    def release(self):
        self.running = False
        self.thread.join(timeout=1)
        self.cap.release()


def orbbec_connected():
    try:
        import pyorbbecsdk as ob
        return ob.Context().query_devices().get_count() > 0
    except Exception as e:
        print(f"[INFO] Orbbec SDK check: {e}")
        return False


class OrbbecStream:
    """Orbbec Gemini 2 L color + depth (aligned to color, meters)."""

    def __init__(self, width, height, fps, depth=True):
        import pyorbbecsdk as ob
        self.ob = ob
        self.want_depth = depth
        self.pipeline = ob.Pipeline()
        config = ob.Config()
        profiles = self.pipeline.get_stream_profile_list(ob.OBSensorType.COLOR_SENSOR)
        profile = None
        for w, h in ((width, height), (1280, 720), (1280, 800), (640, 400)):
            for fmt in (ob.OBFormat.RGB, ob.OBFormat.MJPG, ob.OBFormat.BGR, ob.OBFormat.YUYV):
                try:
                    profile = profiles.get_video_stream_profile(w, h, fmt, fps)
                    break
                except Exception:
                    continue
            if profile is not None:
                break
        if profile is None:
            profile = profiles.get_default_video_stream_profile()
            print("[WARN] Orbbec: requested color mode not supported, using default")
        config.enable_stream(profile)

        self.depth, self.align = None, None
        if self.want_depth:
            try:
                dprofiles = self.pipeline.get_stream_profile_list(ob.OBSensorType.DEPTH_SENSOR)
                config.enable_stream(dprofiles.get_default_video_stream_profile())
                self.align = ob.AlignFilter(align_to_stream=ob.OBStreamType.COLOR_STREAM)
            except Exception as e:
                print(f"[WARN] Orbbec depth not available ({e}) -> no distance zone")
                self.want_depth = False
        self.pipeline.start(config)
        self._info = (profile.get_width(), profile.get_height(), profile.get_fps())

        self.frame, self.frame_id = None, 0
        self.lock = threading.Lock()
        self.running = True
        self.thread = threading.Thread(target=self._loop, daemon=True)
        self.thread.start()

    def _to_bgr(self, cf):
        ob = self.ob
        w, h, fmt = cf.get_width(), cf.get_height(), cf.get_format()
        data = np.asanyarray(cf.get_data())
        if fmt == ob.OBFormat.RGB:
            return cv2.cvtColor(data.reshape(h, w, 3), cv2.COLOR_RGB2BGR)
        if fmt == ob.OBFormat.BGR:
            return data.reshape(h, w, 3).copy()
        if fmt == ob.OBFormat.MJPG:
            return cv2.imdecode(data, cv2.IMREAD_COLOR)
        if fmt == ob.OBFormat.YUYV:
            return cv2.cvtColor(data.reshape(h, w, 2), cv2.COLOR_YUV2BGR_YUYV)
        return None

    def _loop(self):
        while self.running:
            try:
                frames = self.pipeline.wait_for_frames(100)
            except Exception:
                continue
            if frames is None:
                continue
            depth_m = None
            if self.want_depth:
                try:
                    aligned = self.align.process(frames)
                    fs = aligned.as_frame_set() if aligned is not None else None
                    df = fs.get_depth_frame() if fs is not None else None
                    if df is not None:
                        d = np.frombuffer(df.get_data(), dtype=np.uint16)
                        d = d.reshape(df.get_height(), df.get_width())
                        depth_m = d.astype(np.float32) * (df.get_depth_scale() / 1000.0)
                        frames = fs
                except Exception:
                    depth_m = None
            cf = frames.get_color_frame()
            if cf is None:
                continue
            img = self._to_bgr(cf)
            if img is None:
                continue
            with self.lock:
                self.frame = img
                if depth_m is not None:
                    self.depth = depth_m
                self.frame_id += 1

    def read(self):
        with self.lock:
            return self.frame_id, self.frame

    def read_depth(self):
        with self.lock:
            return self.depth

    def info(self):
        return self._info

    def release(self):
        self.running = False
        self.thread.join(timeout=1)
        try:
            self.pipeline.stop()
        except Exception:
            pass


def wait_first_frame(cam, timeout=5.0):
    end = time.time() + timeout
    while time.time() < end:
        if cam.read()[1] is not None:
            return True
        time.sleep(0.05)
    return False


def open_webcam(args):
    order = [args.camera] + [i for i in range(4) if i != args.camera]
    for idx in order:
        try:
            cam = CameraStream(idx, args.width, args.height, args.cam_fps)
        except RuntimeError:
            continue
        if wait_first_frame(cam, 3.0):
            if idx != args.camera:
                print(f"[INFO] camera {args.camera} not working -> using camera {idx}")
            return cam
        cam.release()
    raise SystemExit("[ERROR] no working camera found (tried 0-3). "
                     "Close Zoom/Teams/OBS, check Windows camera privacy, replug USB.")


def open_camera(args):
    """Orbbec Gemini 2 L first (auto / orbbec), webcam fallback. Returns (cam, name)."""
    if args.source in ("auto", "orbbec"):
        if orbbec_connected():
            cam = None
            try:
                cam = OrbbecStream(args.width, args.height, args.cam_fps, depth=not args.no_depth)
                if not wait_first_frame(cam):
                    raise RuntimeError("no frames from Orbbec in 5 s")
                print("[INFO] Orbbec Gemini 2 L connected")
                return cam, "orbbec"
            except Exception as e:
                print(f"[WARN] Orbbec failed ({e}) -> switching to webcam")
                if cam is not None:
                    cam.release()
        else:
            print("[INFO] Orbbec camera not found -> switching to webcam")
    return open_webcam(args), "webcam"


# ----------------------------------------------------------------------------
# hand landmarks (pinch) - MediaPipe Tasks, legacy solutions as fallback
# ----------------------------------------------------------------------------
class HandDetector:
    """Runs on a small square crop around one wrist. detect(crop_rgb) -> 21 (x, y) or None."""

    def __init__(self):
        self.mode = None
        try:
            import mediapipe as mp
        except Exception as e:
            print(f"[WARN] mediapipe not installed ({e}) -> pinch disabled")
            return
        self.mp = mp
        try:
            if not os.path.exists(HAND_MODEL_FILE):
                print("[INFO] downloading hand_landmarker.task (one time, ~8 MB)")
                urllib.request.urlretrieve(HAND_MODEL_URL, HAND_MODEL_FILE)
            from mediapipe.tasks.python import BaseOptions, vision
            # buffer, not path: MediaPipe cannot open paths with Thai (non-ASCII) characters
            with open(HAND_MODEL_FILE, "rb") as f:
                model_bytes = f.read()
            opts = vision.HandLandmarkerOptions(
                base_options=BaseOptions(model_asset_buffer=model_bytes),
                running_mode=vision.RunningMode.IMAGE,
                num_hands=1,
                min_hand_detection_confidence=0.35,
                min_hand_presence_confidence=0.35,
                min_tracking_confidence=0.35)
            self.det = vision.HandLandmarker.create_from_options(opts)
            self.mode = "tasks"
        except Exception as e:
            print(f"[WARN] MediaPipe Tasks failed ({e}) -> trying legacy solutions")
            try:
                self.det = mp.solutions.hands.Hands(static_image_mode=True, max_num_hands=1,
                                                    min_detection_confidence=0.35)
                self.mode = "legacy"
            except Exception as e2:
                print(f"[WARN] MediaPipe hands not available ({e2}) -> pinch disabled")

    @property
    def ok(self):
        return self.mode is not None

    def detect(self, crop_rgb):
        if self.mode == "tasks":
            img = self.mp.Image(image_format=self.mp.ImageFormat.SRGB,
                                data=np.ascontiguousarray(crop_rgb))
            res = self.det.detect(img)
            if not res.hand_landmarks:
                return None
            return [(lm.x, lm.y) for lm in res.hand_landmarks[0]]
        if self.mode == "legacy":
            res = self.det.process(crop_rgb)
            if not res.multi_hand_landmarks:
                return None
            return [(lm.x, lm.y) for lm in res.multi_hand_landmarks[0].landmark]
        return None


# MediaPipe hand bones (21 landmarks): wrist, thumb, index, middle, ring, pinky + palm
HAND_BONES = ((0, 1), (1, 2), (2, 3), (3, 4), (0, 5), (5, 6), (6, 7), (7, 8),
              (5, 9), (9, 10), (10, 11), (11, 12), (9, 13), (13, 14), (14, 15), (15, 16),
              (13, 17), (0, 17), (17, 18), (18, 19), (19, 20))


def draw_hand(frame, pts, color):
    """Draw one MediaPipe hand skeleton (pts = 21 pixel points)."""
    p = [(int(x), int(y)) for x, y in pts]
    for a, b in HAND_BONES:
        cv2.line(frame, p[a], p[b], color, 2)
    for q in p:
        cv2.circle(frame, q, 3, (255, 255, 255), -1)


def pinch_ratio(lms):
    """thumb tip - index tip gap divided by palm size (wrist - middle MCP)."""
    gap = math.dist(lms[4], lms[8])
    palm = max(math.dist(lms[0], lms[9]), 1e-4)
    return gap / palm


# ----------------------------------------------------------------------------
# people -> zones -> distance
# ----------------------------------------------------------------------------
def kp(kxy, kconf, i, w, h):
    return (float(kxy[i][0]) / w, float(kxy[i][1]) / h, float(kconf[i]))


def assign_zones(result, w, h, conf_thr):
    """Biggest person whose shoulder center is in the left half = P1, right half = P2."""
    zones = {"P1": None, "P2": None}
    if result.boxes is None or len(result.boxes) == 0 or result.keypoints is None:
        return zones, 0
    boxes = result.boxes.xyxy.cpu().numpy()
    bconf = result.boxes.conf.cpu().numpy()
    kxy = result.keypoints.xy.cpu().numpy()
    kconf = result.keypoints.conf.cpu().numpy()
    count = 0
    best = {"P1": -1.0, "P2": -1.0}
    for i in range(len(boxes)):
        if bconf[i] < conf_thr:
            continue
        count += 1
        x1, y1, x2, y2 = boxes[i]
        area = float((x2 - x1) * (y2 - y1))
        if kconf[i][KP_LSH] >= KP_MIN_CONF and kconf[i][KP_RSH] >= KP_MIN_CONF:
            cx = float(kxy[i][KP_LSH][0] + kxy[i][KP_RSH][0]) / 2.0 / w
        else:
            cx = float(x1 + x2) / 2.0 / w
        zone = "P1" if cx < 0.5 else "P2"
        if area > best[zone]:
            best[zone] = area
            zones[zone] = {
                "center_x": cx,
                "nose": kp(kxy[i], kconf[i], KP_NOSE, w, h),
                "lsh": kp(kxy[i], kconf[i], KP_LSH, w, h),
                "rsh": kp(kxy[i], kconf[i], KP_RSH, w, h),
                "le": kp(kxy[i], kconf[i], KP_LEL, w, h),
                "re": kp(kxy[i], kconf[i], KP_REL, w, h),
                "lw": kp(kxy[i], kconf[i], KP_LW, w, h),
                "rw": kp(kxy[i], kconf[i], KP_RW, w, h),
                "lh": kp(kxy[i], kconf[i], KP_LHIP, w, h),
                "rh": kp(kxy[i], kconf[i], KP_RHIP, w, h),
                "box": (x1 / w, y1 / h, x2 / w, y2 / h),
            }
    return zones, count


def depth_at(depth, x, y, half, mirrored, y_span=None):
    """Median depth (m) in a small box around normalized (x, y) of the (mirrored) image."""
    if depth is None:
        return None
    dh, dw = depth.shape[:2]
    if mirrored:
        x = 1.0 - x
    ys = y_span if y_span else (y - half, y + half)
    xa, xb = int(max(0, (x - half) * dw)), int(min(dw, (x + half) * dw))
    ya, yb = int(max(0, ys[0] * dh)), int(min(dh, ys[1] * dh))
    if xb - xa < 2 or yb - ya < 2:
        return None
    patch = depth[ya:yb, xa:xb]
    valid = patch[(patch > 0.2) & (patch < 8.0)]
    if valid.size < 8:
        return None
    return float(np.median(valid))


def torso_distance(person, depth, mirrored):
    lsh, rsh = person["lsh"], person["rsh"]
    if lsh[2] >= KP_MIN_CONF and rsh[2] >= KP_MIN_CONF:
        cx, cy = (lsh[0] + rsh[0]) / 2.0, (lsh[1] + rsh[1]) / 2.0
        half = max(abs(lsh[0] - rsh[0]) * 0.35, 0.015)
        return depth_at(depth, cx, cy, half, mirrored, (cy, cy + half * 2.5))
    x1, y1, x2, y2 = person["box"]
    return depth_at(depth, (x1 + x2) / 2.0, y1 + (y2 - y1) * 0.33,
                    max((x2 - x1) * 0.2, 0.015), mirrored)


# ----------------------------------------------------------------------------
# per-player tracker: smoothing, play zone, pinch, poses
# ----------------------------------------------------------------------------
def clamp01(v):
    return min(max(float(v), 0.0), 1.0)


class PlayerTracker:
    POSES = ("hands_up", "hands_down", "push", "v_pose", "triangle")

    def __init__(self, name, args):
        self.name, self.args = name, args
        self.in_zone = False
        self.zone = "none"
        self.distance = None
        self.pinch_n = 0
        self.reset()

    def reset(self):
        self.person = None
        self.pos = {}                  # smoothed joints (x, y) image coords (y down)
        self.wrist_seen = {"lw": False, "rw": False}
        self.pinch = False
        self.pinch_armed = True        # must see an open hand before the next pinch counts
        self.pinch_frames = 0
        self.last_pinch_t = 0.0
        self.hand_ok = False
        self.hand_seen_t = 0.0
        self.ratio = None
        self.hand_box = None           # right hand crop (pinch)
        self.hand_lms = None           # right hand 21 landmarks (pixels)
        self.lhand_box = None          # left hand crop (skeleton only)
        self.lhand_lms = None
        self.lhand_ok = False
        self.lhand_seen_t = 0.0
        self.pose_count = {k: 0 for k in self.POSES}
        self.poses = {k: False for k in self.POSES}
        self.wrist_depth = {"lw": None, "rw": None}

    # ---- smoothing ----
    def _smooth(self, key, xy):
        a = self.args.smooth
        old = self.pos.get(key)
        if old is None or a <= 0:
            self.pos[key] = xy
        else:
            # faster follow on big moves, smoother when still (adaptive smoothing)
            dist = math.dist(old, xy)
            k = a * (0.35 if dist > 0.08 else 1.0)
            self.pos[key] = (k * old[0] + (1 - k) * xy[0], k * old[1] + (1 - k) * xy[1])

    def update_person(self, person, t):
        if person is None:
            keep_count = self.pinch_n
            self.reset()
            self.pinch_n = keep_count
            return
        self.person = person
        for key in ("lsh", "rsh", "le", "re", "lw", "rw", "lh", "rh", "nose"):
            x, y, c = person[key]
            if c >= KP_MIN_CONF:
                self._smooth(key, (x, y))
                if key in self.wrist_seen:
                    self.wrist_seen[key] = True
            elif key in self.wrist_seen:
                self.wrist_seen[key] = False

    # ---- hands: MediaPipe skeleton on BOTH hands (4 hands for 2 players), pinch on the right ----
    def _detect_hand(self, frame_rgb, hands, wrist, elbow):
        """Crop around one hand and run MediaPipe. Returns (box, landmarks 0..1 in crop, pixels)."""
        if hands is None or not hands.ok or self.person is None or wrist not in self.pos:
            return None, None, None
        h, w = frame_rgb.shape[:2]
        wx, wy = self.pos[wrist]
        # the hand sits beyond the wrist along the forearm
        if elbow in self.pos:
            ex, ey = self.pos[elbow]
            wx, wy = wx + (wx - ex) * 0.45, wy + (wy - ey) * 0.45
        sw = self.shoulder_width() or 0.08
        side = int(max(sw * w * 1.25, 90))
        cx, cy = int(wx * w), int(wy * h)
        x0, y0 = max(0, cx - side // 2), max(0, cy - side // 2)
        x1, y1 = min(w, x0 + side), min(h, y0 + side)
        if x1 - x0 < 30 or y1 - y0 < 30:
            return None, None, None
        crop = cv2.resize(frame_rgb[y0:y1, x0:x1], (256, 256), interpolation=cv2.INTER_LINEAR)
        lms = hands.detect(crop)
        box = (x0, y0, x1, y1)
        if lms is None:
            return box, None, None
        px = [(x0 + lx * (x1 - x0), y0 + ly * (y1 - y0)) for lx, ly in lms]
        return box, lms, px

    def update_hands(self, frame_rgb, hands, t):
        # left hand: skeleton only (bag hand)
        self.lhand_box, _, self.lhand_lms = self._detect_hand(frame_rgb, hands, "lw", "le")
        if self.lhand_lms is not None:
            self.lhand_ok, self.lhand_seen_t = True, t
        elif t - self.lhand_seen_t > 0.35:
            self.lhand_ok = False
        self.update_pinch(frame_rgb, hands, t)

    # ---- pinch (right hand) ----
    def update_pinch(self, frame_rgb, hands, t):
        a = self.args
        self.hand_box, lms, self.hand_lms = self._detect_hand(frame_rgb, hands, "rw", "re")
        if lms is None:
            self._hand_lost(t)
            return
        self.hand_ok = True
        self.hand_seen_t = t
        r = pinch_ratio(lms)
        self.ratio = r if self.ratio is None else 0.5 * self.ratio + 0.5 * r
        if self.pinch:
            if self.ratio > a.pinch_off:
                self.pinch = False
                self.pinch_frames = 0
        else:
            if self.ratio > a.pinch_off:
                self.pinch_armed = True
            if self.ratio < a.pinch_on:
                self.pinch_frames += 1
                if self.pinch_frames >= 2:            # 2 frames = not a single-frame glitch
                    self.pinch = True
                    if self.pinch_armed and t - self.last_pinch_t >= a.pinch_cooldown:
                        self.pinch_n += 1              # one toggle for Unity
                        self.last_pinch_t = t
                        self.pinch_armed = False
            else:
                self.pinch_frames = 0

    def _hand_lost(self, t):
        # short gaps (motion blur) keep the state; long gaps = tracking lost
        if t - self.hand_seen_t > 0.35:
            self.hand_ok = False
            self.pinch = False
            self.pinch_frames = 0
            self.pinch_armed = False      # avoid a fake pinch right after the hand returns
            self.ratio = None

    # ---- body poses ----
    def shoulder_width(self):
        if "lsh" in self.pos and "rsh" in self.pos:
            return max(abs(self.pos["lsh"][0] - self.pos["rsh"][0]), 0.03)
        return None

    def update_poses(self, depth, mirrored):
        raw = {k: False for k in self.POSES}
        p = self.pos
        sw = self.shoulder_width()
        if self.person is not None and sw and all(k in p for k in ("lw", "rw")) \
                and all(self.wrist_seen.values()):
            sx = (p["lsh"][0] + p["rsh"][0]) / 2.0
            sy = (p["lsh"][1] + p["rsh"][1]) / 2.0            # image y (down)
            head_y = p["nose"][1] if "nose" in p else sy - 0.8 * sw
            lw, rw = p["lw"], p["rw"]
            up_l, up_r = sy - lw[1], sy - rw[1]               # >0 = above the shoulders
            spread = abs(lw[0] - rw[0])

            # Water + Sun: collect = BOTH arms raised (wrists well above the shoulders)
            raw["hands_up"] = up_l > 0.5 * sw and up_r > 0.5 * sw
            # hands down = both wrists clearly below the shoulders
            raw["hands_down"] = up_l < -0.5 * sw and up_r < -0.5 * sw
            # Sun: V = both wrists above the shoulders and spread wide outside the body
            raw["v_pose"] = (up_l > 0.35 * sw and up_r > 0.35 * sw and spread > 2.4 * sw
                             and lw[0] < p["lsh"][0] and rw[0] > p["rsh"][0])
            # Sun: triangle roof = both wrists above the head, touching,
            # elbows out to the sides and lower than the hands
            tri = lw[1] < head_y and rw[1] < head_y and spread < 0.8 * sw
            if tri and "le" in p and "re" in p:
                tri = (abs(p["le"][0] - p["re"][0]) > spread + 0.5 * sw
                       and p["le"][1] > lw[1] and p["re"][1] > rw[1])
            raw["triangle"] = tri
            # Water + Sun: send = both hands at chest/shoulder height, pushed forward to the camera
            chest = all(-0.9 * sw < u < 0.6 * sw for u in (up_l, up_r)) and spread < 2.0 * sw
            push = False
            if chest:
                d_body = self.distance
                if depth is not None and d_body:
                    dl = depth_at(depth, lw[0], lw[1], 0.012, mirrored)
                    dr = depth_at(depth, rw[0], rw[1], 0.012, mirrored)
                    self.wrist_depth = {"lw": dl, "rw": dr}
                    if dl and dr:
                        push = (d_body - dl) > 0.28 and (d_body - dr) > 0.28
                if not push:
                    # no depth (webcam): arms toward the camera look short in 2D
                    reach_l = math.dist(lw, p["lsh"])
                    reach_r = math.dist(rw, p["rsh"])
                    push = (reach_l < 0.85 * sw and reach_r < 0.85 * sw
                            and abs(lw[0] - sx) < 1.3 * sw and abs(rw[0] - sx) < 1.3 * sw)
            raw["push"] = push

        # anti flicker: a pose must hold N frames before it is reported
        for k in self.POSES:
            if raw[k]:
                self.pose_count[k] = min(self.pose_count[k] + 1, 1000)
            else:
                self.pose_count[k] = 0
            self.poses[k] = self.pose_count[k] >= self.args.pose_frames

    def pose_label(self):
        for k in ("triangle", "v_pose", "push", "hands_up", "hands_down"):
            if self.poses[k]:
                return k
        return "idle"

    # ---- packet ----
    def reach(self, key):
        """Wrist inside the personal reach box (0..1, y up) around the shoulders."""
        p, sw = self.pos, self.shoulder_width()
        if key not in p or not sw:
            return 0.5, 0.5
        sx = (p["lsh"][0] + p["rsh"][0]) / 2.0
        sy = (p["lsh"][1] + p["rsh"][1]) / 2.0
        rx, ry = self.args.reach_x * sw, self.args.reach_y * sw
        x = (p[key][0] - (sx - rx)) / (2 * rx)
        y = 1.0 - (p[key][1] - (sy - ry)) / (2 * ry)
        return clamp01(x), clamp01(y)

    def packet(self):
        active = self.person is not None and self.in_zone
        out = {"active": active,
               "d": round(self.distance, 3) if self.distance else 0.0,
               "zone": self.zone,
               "pinch_n": self.pinch_n}
        if not active:
            out.update({"lx": 0.0, "ly": 0.0, "rx": 0.0, "ry": 0.0, "lt": False, "rt": False,
                        "bag_x": 0.5, "bag_y": 0.5, "grab_x": 0.5, "grab_y": 0.5,
                        "pinch": False, "hand_ok": False, "lhand_ok": False, "pose": "none",
                        **{k: False for k in self.POSES}})
            return out
        p = self.pos
        lw, rw = p.get("lw", (0.0, 1.0)), p.get("rw", (0.0, 1.0))
        bx, by = self.reach("lw")
        gx, gy = self.reach("rw")
        out.update({
            "lx": round(clamp01(lw[0]), 4), "ly": round(clamp01(1 - lw[1]), 4),
            "rx": round(clamp01(rw[0]), 4), "ry": round(clamp01(1 - rw[1]), 4),
            "lt": self.wrist_seen["lw"], "rt": self.wrist_seen["rw"],
            "bag_x": round(bx, 4), "bag_y": round(by, 4),
            "grab_x": round(gx, 4), "grab_y": round(gy, 4),
            "pinch": self.pinch, "hand_ok": self.hand_ok, "lhand_ok": self.lhand_ok,
            **{k: bool(v) for k, v in self.poses.items()},
            "pose": self.pose_label(),
        })
        sw = self.shoulder_width() or 0.0
        if "lsh" in p and "rsh" in p:
            out.update({"sx": round((p["lsh"][0] + p["rsh"][0]) / 2, 4),
                        "sy": round(1 - (p["lsh"][1] + p["rsh"][1]) / 2, 4),
                        "sw": round(sw, 4)})
        for key, fx, fy in (("lsh", "lsx", "lsy"), ("rsh", "rsx", "rsy"), ("le", "lex", "ley"),
                            ("re", "rex", "rey"), ("nose", "nx", "ny")):
            if key in p:
                out[fx] = round(clamp01(p[key][0]), 4)
                out[fy] = round(clamp01(1 - p[key][1]), 4)
        if "lh" in p and "rh" in p:
            out["hx"] = round(clamp01((p["lh"][0] + p["rh"][0]) / 2), 4)
            out["hy"] = round(clamp01(1 - (p["lh"][1] + p["rh"][1]) / 2), 4)
            out["hw"] = round(clamp01(abs(p["lh"][0] - p["rh"][0])), 4)
        return out


def apply_play_zone(zones, trackers, depth, args, mirrored):
    """Players outside min-dist .. max-dist are not active (hysteresis at both edges)."""
    for name, tr in trackers.items():
        person = zones[name]
        if person is None:
            tr.in_zone, tr.zone, tr.distance = False, "none", None
            continue
        if args.no_depth or depth is None:
            tr.in_zone, tr.zone, tr.distance = True, "ok", None
            continue
        d = torso_distance(person, depth, mirrored)
        if d is None:
            continue                     # no depth this frame: keep the last decision
        tr.distance = d if tr.distance is None else 0.7 * tr.distance + 0.3 * d
        hy = args.dist_hyst if tr.in_zone else 0.0
        if tr.distance < args.min_dist - hy:
            tr.in_zone, tr.zone = False, "near"
        elif tr.distance > args.max_dist + hy:
            tr.in_zone, tr.zone = False, "far"
        else:
            tr.in_zone, tr.zone = True, "ok"


# ----------------------------------------------------------------------------
# debug window
# ----------------------------------------------------------------------------
def draw_debug(frame, trackers, fps, device, src, people, hands_ok):
    h, w = frame.shape[:2]
    cv2.line(frame, (w // 2, 0), (w // 2, h), (255, 255, 255), 2)
    for name, color in (("P1", COLOR_P1), ("P2", COLOR_P2)):
        tr = trackers[name]
        x_label = w // 4 if name == "P1" else 3 * w // 4
        zone_txt = {"ok": "READY", "near": "STEP BACK", "far": "STEP CLOSER",
                    "none": "NO PLAYER"}[tr.zone]
        dist_txt = f" {tr.distance:.2f}m" if tr.distance else ""
        cv2.putText(frame, f"{name} {zone_txt}{dist_txt}", (x_label - 110, h - 20),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.8, color, 2)
        if tr.person is None:
            continue
        p = tr.pos

        def px(key):
            return int(p[key][0] * w), int(p[key][1] * h)

        for a, b in (("lsh", "rsh"), ("lsh", "le"), ("le", "lw"), ("rsh", "re"), ("re", "rw"),
                     ("lsh", "lh"), ("rsh", "rh"), ("lh", "rh")):
            if a in p and b in p:
                cv2.line(frame, px(a), px(b), color, 3)
        if "lw" in p:
            cv2.circle(frame, px("lw"), 14, (60, 200, 60), -1)
            cv2.putText(frame, "BAG", (px("lw")[0] + 16, px("lw")[1]),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.55, (60, 200, 60), 2)
        if "rw" in p:
            cv2.circle(frame, px("rw"), 14, (0, 220, 255) if tr.pinch else (200, 200, 200), -1)
            cv2.putText(frame, f"GRAB {'PINCH' if tr.pinch else 'open'} #{tr.pinch_n}",
                        (px("rw")[0] + 16, px("rw")[1]),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 220, 255), 2)
        # MediaPipe hand skeleton on both hands (left = bag hand, right = grabber + pinch)
        for box, lms, ok, hc in ((tr.lhand_box, tr.lhand_lms, tr.lhand_ok, (60, 200, 60)),
                                 (tr.hand_box, tr.hand_lms, tr.hand_ok, (0, 220, 255))):
            if box:
                x0, y0, x1, y1 = box
                cv2.rectangle(frame, (x0, y0), (x1, y1), (0, 255, 0) if ok else (0, 0, 255), 1)
            if lms:
                draw_hand(frame, lms, hc)
        if tr.hand_lms:
            for i in (4, 8):     # thumb + index tips (pinch)
                cv2.circle(frame, (int(tr.hand_lms[i][0]), int(tr.hand_lms[i][1])), 6,
                           (255, 0, 255), -1)
        top = int(tr.person["box"][1] * h) + 25
        label = f"{name}: {tr.pose_label()}" + (f" r={tr.ratio:.2f}" if tr.ratio else "")
        cv2.rectangle(frame, (int(tr.person["box"][0] * w), top - 22),
                      (int(tr.person["box"][0] * w) + 300, top + 6), (0, 0, 0), -1)
        cv2.putText(frame, label, (int(tr.person["box"][0] * w) + 4, top),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.6, color, 2)
    cv2.rectangle(frame, (0, 0), (520, 34), (0, 0, 0), -1)
    cv2.putText(frame, f"FPS {fps:4.1f} [{device}] cam={src} people={people} "
                       f"hands={'on' if hands_ok else 'OFF'}",
                (8, 24), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
    return frame


# ----------------------------------------------------------------------------
# main
# ----------------------------------------------------------------------------
def pick_device(choice):
    if choice == "auto":
        return "cuda" if torch.cuda.is_available() else "cpu"
    if choice == "cuda" and not torch.cuda.is_available():
        print("[WARN] CUDA not available -> CPU")
        return "cpu"
    return choice


def main():
    args = parse_args()
    device = pick_device(args.device)
    half = device == "cuda"
    print(f"[INFO] device: {torch.cuda.get_device_name(0) if half else 'CPU'}")
    model = YOLO(args.model)
    model.to(device)
    hands = None if args.no_hands else HandDetector()

    cam, src = open_camera(args)
    cw, ch, cfps = cam.info()
    print(f"[INFO] camera {src} {cw}x{ch} @ {cfps:.0f}fps")
    mirrored = not args.no_mirror
    if src != "orbbec" or args.no_depth:
        print("[INFO] play zone: off (no depth) -> everybody in view can play")
    else:
        print(f"[INFO] play zone: {args.min_dist:.2f} - {args.max_dist:.2f} m")

    extra = {"half": True} if half else {}

    def infer(img):
        return model.predict(img, imgsz=args.imgsz, device=device, conf=args.conf,
                             max_det=6, verbose=False, **extra)[0]

    for _ in range(3):
        infer(cam.read()[1])

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (args.ip, args.port)
    print(f"[INFO] sending UDP to {target[0]}:{target[1]}  (q / Esc = quit)")

    trackers = {"P1": PlayerTracker("P1", args), "P2": PlayerTracker("P2", args)}
    last_id = -1
    fps, last_t = 0.0, time.perf_counter()
    try:
        while True:
            fid, frame = cam.read()
            if fid == last_id or frame is None:
                time.sleep(0.001)
                continue
            last_id = fid
            if mirrored:
                frame = cv2.flip(frame, 1)
            h, w = frame.shape[:2]
            now = time.perf_counter()

            result = infer(frame)
            zones, people = assign_zones(result, w, h, args.conf)
            depth = cam.read_depth()
            apply_play_zone(zones, trackers, depth, args, mirrored)

            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB) if hands and hands.ok else None
            for name, tr in trackers.items():
                tr.update_person(zones[name] if tr.in_zone else None, now)
                tr.update_hands(rgb, hands, now)
                tr.update_poses(depth, mirrored)

            packet = {
                "t": round(time.time(), 3),
                "fps": round(fps, 1),
                "src": src,
                "depth": depth is not None,
                "hands": bool(hands and hands.ok),
                "player_count": sum(1 for t in trackers.values() if t.in_zone),
                "ar": round(w / float(h), 4),
                "P1": trackers["P1"].packet(),
                "P2": trackers["P2"].packet(),
            }
            data = json.dumps(packet, separators=(",", ":"))
            sock.sendto(data.encode("utf-8"), target)
            if args.print:
                print(data)

            fps = 0.9 * fps + 0.1 * (1.0 / max(now - last_t, 1e-6))
            last_t = now
            if not args.no_window:
                cv2.imshow("Reboot The Earth - Tracking (q = quit)",
                           draw_debug(frame, trackers, fps, device, src, people,
                                      bool(hands and hands.ok)))
                if cv2.waitKey(1) & 0xFF in (ord("q"), 27):
                    break
    except KeyboardInterrupt:
        pass
    finally:
        cam.release()
        sock.close()
        cv2.destroyAllWindows()
        print("[INFO] stopped")


if __name__ == "__main__":
    main()
