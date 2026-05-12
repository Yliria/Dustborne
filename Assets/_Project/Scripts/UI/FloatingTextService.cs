using System.Collections.Generic;
using UnityEngine;

namespace Project.UI
{
    /// World-anchored toast text that drifts up and fades out. Spawned from
    /// anywhere via FloatingTextService.Spawn(...). One service per scene,
    /// lives on the GameSystems GameObject.
    ///
    /// Rendered through OnGUI: each entry's world position is projected to
    /// screen space every Repaint, with a small text shadow for legibility
    /// over arbitrary backgrounds. No prefab, no TMP asset dependency, no
    /// Canvas setup — keeps the feature trivially commitable.
    ///
    /// Uses Time.unscaledTime so toasts animate even during gameplay pause.
    /// They are pure feedback UI and shouldn't freeze with the world.
    [DisallowMultipleComponent]
    public class FloatingTextService : MonoBehaviour
    {
        public static readonly Color PickupColor = new(0.45f, 0.95f, 0.45f);
        public static readonly Color ErrorColor  = new(0.95f, 0.35f, 0.35f);
        public static readonly Color InfoColor   = new(0.95f, 0.95f, 0.95f);

        [Header("Defaults")]
        [SerializeField, Min(0.2f)] float defaultDuration = 1.5f;
        [Tooltip("World units per second the toast drifts upward.")]
        [SerializeField, Min(0f)] float riseSpeed = 1.5f;
        [Tooltip("Random XZ offset (m) applied at spawn so simultaneous toasts don't perfectly overlap.")]
        [SerializeField, Min(0f)] float jitterRadius = 0.25f;
        [SerializeField, Range(8, 64)] int fontSize = 18;
        [Tooltip("Cap on simultaneous toasts. Oldest are dropped past this.")]
        [SerializeField, Min(1)] int maxEntries = 32;

        static FloatingTextService _instance;

        struct Entry
        {
            public string Text;
            public Vector3 StartPos;
            public Color Color;
            public float StartTime;
            public float Duration;
            public Vector3 JitterOffset;
        }

        readonly List<Entry> _entries = new();
        GUIStyle _style;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[FloatingTextService] Duplicate instance, destroying.");
                Destroy(this);
                return;
            }
            _instance = this;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ---- Public API ----

        /// Spawns a toast at worldPos with the given color. Returns silently
        /// when no service is in scene (safe to call from anywhere without
        /// guard checks).
        public static void Spawn(string text, Vector3 worldPos, Color color, float duration = -1f)
        {
            if (_instance == null) return;
            if (string.IsNullOrEmpty(text)) return;

            float d = duration > 0f ? duration : _instance.defaultDuration;
            float j = _instance.jitterRadius;
            var jitter = new Vector3(
                Random.Range(-j, j), 0f, Random.Range(-j, j));

            _instance._entries.Add(new Entry
            {
                Text = text,
                StartPos = worldPos,
                Color = color,
                StartTime = Time.unscaledTime,
                Duration = d,
                JitterOffset = jitter
            });

            // Cap. Drop oldest first.
            while (_instance._entries.Count > _instance.maxEntries)
                _instance._entries.RemoveAt(0);
        }

        /// Convenience for "I just picked up / produced N of X". Renders in
        /// green like "+3 Wood Log".
        public static void SpawnPickup(string itemName, int quantity, Vector3 worldPos)
        {
            string qty = quantity > 1 ? $"+{quantity} " : "+";
            Spawn($"{qty}{itemName}", worldPos, PickupColor);
        }

        /// Red error toast — tool missing, recipe blocked, etc.
        public static void SpawnError(string message, Vector3 worldPos)
        {
            Spawn(message, worldPos, ErrorColor);
        }

        public static void SpawnInfo(string message, Vector3 worldPos)
        {
            Spawn(message, worldPos, InfoColor);
        }

        // ---- Tick & render ----

        void Update()
        {
            float now = Time.unscaledTime;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (now - _entries[i].StartTime >= _entries[i].Duration)
                    _entries.RemoveAt(i);
            }
        }

        void OnGUI()
        {
            if (_entries.Count == 0) return;
            var cam = Camera.main;
            if (cam == null) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = fontSize,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    richText = true
                };
            }

            float now = Time.unscaledTime;
            Color prevColor = GUI.color;

            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                float elapsed = now - e.StartTime;
                float t = Mathf.Clamp01(elapsed / e.Duration);

                Vector3 world = e.StartPos + e.JitterOffset + Vector3.up * (riseSpeed * elapsed);
                Vector3 screen = cam.WorldToScreenPoint(world);
                if (screen.z <= 0f) continue; // behind camera

                float guiX = screen.x;
                float guiY = Screen.height - screen.y;

                // Hold full alpha for the first half, fade out across the
                // second half — feels more readable than a linear ramp.
                float alpha = t < 0.5f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.5f) * 2f);

                Color color = e.Color;
                color.a *= alpha;

                var size = _style.CalcSize(new GUIContent(e.Text));
                var rect = new Rect(guiX - size.x * 0.5f, guiY - size.y * 0.5f, size.x, size.y);

                // Drop shadow for legibility on bright/dark grounds alike.
                GUI.color = new Color(0f, 0f, 0f, color.a * 0.65f);
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), e.Text, _style);

                // Main glyph.
                GUI.color = color;
                GUI.Label(rect, e.Text, _style);
            }

            GUI.color = prevColor;
        }
    }
}
