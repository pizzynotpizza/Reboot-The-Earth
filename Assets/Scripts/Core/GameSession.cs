using UnityEngine;
using UnityEngine.SceneManagement;

namespace RebootEarth
{
    /// <summary>Scene names, game titles and results shared between scenes.</summary>
    public static class GameSession
    {
        public const string IntroScene = "00_Intro";
        public const string Game1Scene = "01_ScrapSweepers";
        public const string Game2Scene = "02_SeedOfTomorrow";

        public const string Game1Title = "SCRAP SWEEPERS";
        public const string Game1Tagline = "Clean the City. Save the Air.";
        public const string Game2Title = "SEED OF TOMORROW";
        public const string Game2Tagline = "Grow the Last Hope";

        /// <summary>Air Recovery from game 1 (0..1). Game 2 starts the city a bit cleaner.</summary>
        public static float AirRecovery = 0f;
        public static int TeamScore = 0;

        public static void Load(string scene) => SceneManager.LoadScene(scene);
    }

    /// <summary>Hold-to-confirm timer: true once the condition stayed true for "duration".</summary>
    public class HoldTimer
    {
        public float duration;
        public float time;
        public float Progress => duration <= 0 ? 1f : Mathf.Clamp01(time / duration);

        public HoldTimer(float duration) { this.duration = duration; }

        /// <summary>Returns true on the frame the hold completes.</summary>
        public bool Tick(bool condition, float dt)
        {
            if (!condition) { time = 0f; return false; }
            bool wasDone = time >= duration;
            time += dt;
            return !wasDone && time >= duration;
        }

        public void Reset() => time = 0f;
    }

    /// <summary>"Raise your hands to continue" helper used on every story / result screen.</summary>
    public class ContinueGesture
    {
        readonly HoldTimer hold = new HoldTimer(1.5f);
        public float Progress => hold.Progress;

        public bool Tick(float dt)
        {
            bool up = false;
            var r = TrackingReceiver.Instance;
            if (r != null && !r.Simulated)
                foreach (var p in r.players) up |= p.Present && p.HandsUp;
            return hold.Tick(up, dt) | TrackingReceiver.ContinuePressed();
        }

        public void Reset() => hold.Reset();
    }
}
