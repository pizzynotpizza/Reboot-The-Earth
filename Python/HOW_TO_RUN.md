# Reboot The Earth: Body Tracking (Python → Unity)

One Python program (`body_tracking_sender.py`) tracks both players for both mini games
and sends JSON over UDP to Unity on port **5065**.

```
Camera (Orbbec Gemini 2 L, or webcam if no Orbbec is found)
  → YOLOv8-pose (skeletons) → P1 = left half, P2 = right half
  → play distance 1.5 – 2.0 m (Orbbec depth)
  → MediaPipe Hand Landmarker on the right hand (pinch)
  → gestures → UDP → Unity (TrackingReceiver.cs)
```

## 0. After cloning (any OS)

```
cd Python
python setup_env.py          # add --cuda for NVIDIA GPU
python setup_env.py --run    # start tracking (extra args pass through)
```

The `venv/`, `Log/` and `__pycache__/` folders are not in git. Each person creates their own.

## 1. Setup (one time per computer)

1. Install **Python 3.13** (or 3.12) from python.org and tick **Add python.exe to PATH**.
2. Double-click `setup.bat`. For an NVIDIA GPU, run `setup.bat cuda` instead.
   - It creates `venv/`, installs `requirements.txt`, and downloads `hand_landmarker.task`.
3. Orbbec Gemini 2 L: plug it into a **USB 3** port. The pyorbbecsdk2 package includes the driver.

## 2. Run

1. Double-click `run.bat`. A debug window opens. Press `q` or `Esc` to quit.
2. Press Play in Unity (scene `Assets/Scenes/00_Intro.unity`).

| Command | Effect |
|---|---|
| `run.bat` | Uses Orbbec if it is connected, otherwise the webcam (automatic) |
| `run.bat --source webcam --camera 1` | Forces the webcam (index 1) |
| `run.bat --min-dist 1.5 --max-dist 2.0` | Sets the play distance (Orbbec only) |
| `run.bat --no-depth` | Lets everyone in view play (no distance limit) |
| `run.bat --device cuda` | Runs on the GPU |
| `run.bat --print` | Prints every packet |

Test without Unity: `venv\Scripts\python.exe udp_test_receiver.py`

## 3. Gestures

### Game 1: SCRAP SWEEPERS (both players)
| Body | Game |
|---|---|
| Left wrist | Waste bag position (`bag_x`, `bag_y`) |
| Right wrist | Trash grabber position (`grab_x`, `grab_y`) |
| Right hand pinch (thumb + index finger) | Toggle: grab / release (`pinch_n` +1 per pinch) |

- Positions are mapped inside a personal **reach box** around the shoulders, so players
  reach every corner of their half without walking.
- Pinch has hysteresis (`--pinch-on 0.32`, `--pinch-off 0.50`) and a 0.35 s cooldown
  (`--pinch-cooldown`). The hand must open before the next pinch counts.
- If the hand is lost for 0.35 s, `hand_ok` becomes false and the grabber pauses.

### Game 2: SEED OF TOMORROW
| Player | Collect | Send |
|---|---|---|
| P1 Water Guardian | `hands_up`: raise both arms (wrists above the shoulders) | `hands_down`, then `push`: both hands pushed forward, held |
| P2 Sun Guardian | `hands_up`: raise both arms | `push`: both hands pushed forward, held (no hands together, no triangle) |

Both guardians collect at the same time and fill their gauge in the same time (`collectSeconds`).

- Push detection uses Orbbec depth (wrists ≥ 28 cm closer than the chest). With a webcam,
  it uses 2D arm foreshortening instead.
- A pose must hold for 3 frames (`--pose-frames`) before it is sent. Hold times, cooldowns
  and gauges are in Unity (`SeedGame.cs`).

## 4. UDP packet (per camera frame)

```json
{"t":..,"fps":28.4,"src":"orbbec","depth":true,"hands":true,"player_count":2,"ar":1.78,
 "P1":{"active":true,"d":1.72,"zone":"ok",
       "lx":..,"ly":..,"rx":..,"ry":..,"lt":true,"rt":true,
       "bag_x":..,"bag_y":..,"grab_x":..,"grab_y":..,
       "pinch":false,"pinch_n":12,"hand_ok":true,
       "hands_up":false,"hands_down":true,"push":false,"v_pose":false,"triangle":false,
       "pose":"hands_down","sx":..,"sy":..,"sw":.., "lsx":..,"lsy":..,"lex":..,"ley":..},
 "P2":{...}}
```
`zone`: `ok` (inside 1.5–2 m), `near` (step back), `far` (step closer), `none` (no player).
Coordinates are 0..1 with y pointing up (Unity style), and the image is mirrored.

## 5. Camera only (keyboard mode is off)

The game can only be played with the camera. If Unity gets no packets for 1 second, nobody is
tracked: the game pauses at the current step and shows "ไม่พบกล้อง - เปิด Python/run.bat ก่อนเล่น".

Developer testing only: select the `TrackingReceiver` object in a scene and tick `Allow Keyboard`
to turn on the keys below (F1 switches between camera and keyboard).

| | P1 | P2 |
|---|---|---|
| Bag | W A S D | Arrow keys |
| Grabber | T F G H | I J K L |
| Pinch | Space | Right Shift |
| Game 2 | Z = hands up, X = push | C = V pose, V = triangle |

Enter = continue / skip.

## 6. Troubleshooting

- **"Orbbec camera not found"**: the program switches to the webcam on its own. Check the USB 3 cable or port.
- **`hands=OFF` in the debug window**: MediaPipe failed. Run `setup.bat` again.
  The project path contains Thai characters, so the hand model is loaded from memory.
- **Pinch not detected at 2 m**: keep the default 1280x720 (`--width 1280 --height 720`),
  improve the lighting, and face the palm toward the camera.
- **Low FPS**: use `run.bat --device cuda` (NVIDIA) or `--imgsz 384`.
