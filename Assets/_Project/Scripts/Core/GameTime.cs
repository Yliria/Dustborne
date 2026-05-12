using System;
using UnityEngine;

namespace Project.Core
{
    /// Static facade for game-clock state. All gameplay code reads time
    /// through this — never Time.deltaTime — so pause and time-scale are
    /// honored uniformly.
    public static class GameTime
    {
        internal static GameTimeService Instance;

        static bool _warnedNoInstance;

        public static event Action<bool> OnPauseChanged;

        public static bool IsPaused
        {
            get
            {
                if (Instance == null) { WarnOnce(); return false; }
                return Instance.IsPaused;
            }
        }

        public static float TimeScale
        {
            get
            {
                if (Instance == null) { WarnOnce(); return 1f; }
                return Instance.TimeScale;
            }
            set
            {
                if (Instance == null) { WarnOnce(); return; }
                Instance.TimeScale = value;
            }
        }

        public static float DeltaTime
        {
            get
            {
                if (Instance == null) { WarnOnce(); return 0f; }
                return Instance.IsPaused ? 0f : Time.deltaTime * Instance.TimeScale;
            }
        }

        public static void TogglePause()
        {
            if (Instance == null) { WarnOnce(); return; }
            Instance.TogglePause();
        }

        public static void Pause()
        {
            if (Instance == null) { WarnOnce(); return; }
            Instance.Pause();
        }

        public static void Resume()
        {
            if (Instance == null) { WarnOnce(); return; }
            Instance.Resume();
        }

        internal static void RaisePauseChanged(bool paused) => OnPauseChanged?.Invoke(paused);

        static void WarnOnce()
        {
            if (_warnedNoInstance) return;
            _warnedNoInstance = true;
            Debug.LogWarning("[GameTime] No GameTimeService in scene — using safe defaults. Add a GameSystems GameObject with GameTimeService.");
        }
    }
}
