using UnityEngine;

namespace Project.Core
{
    /// Scene-bound singleton that owns the authoritative pause and time-scale
    /// state. Place on a single "GameSystems" GameObject in each playable scene.
    [DefaultExecutionOrder(-1000)]
    public class GameTimeService : MonoBehaviour
    {
        [SerializeField, Min(0f)] float timeScale = 1f;
        [SerializeField] bool startPaused;

        public bool IsPaused { get; private set; }

        public float TimeScale
        {
            get => timeScale;
            set => timeScale = Mathf.Max(0f, value);
        }

        void Awake()
        {
            if (GameTime.Instance != null && GameTime.Instance != this)
            {
                Debug.LogWarning($"[GameTimeService] Duplicate instance on '{name}', destroying.");
                Destroy(this);
                return;
            }
            GameTime.Instance = this;
            IsPaused = startPaused;
        }

        void OnDestroy()
        {
            if (GameTime.Instance == this) GameTime.Instance = null;
        }

        public void TogglePause()
        {
            if (IsPaused) Resume(); else Pause();
        }

        public void Pause()
        {
            if (IsPaused) return;
            IsPaused = true;
            GameTime.RaisePauseChanged(true);
        }

        public void Resume()
        {
            if (!IsPaused) return;
            IsPaused = false;
            GameTime.RaisePauseChanged(false);
        }
    }
}
