using Project.Core;
using Project.PlayerInput;
using Project.Units;
using UnityEngine;

namespace Project.DebugUI
{
    /// Top-left HUD showing pause state and order count. OnGUI keeps it
    /// zero-setup — no Canvas or TMP required.
    [DisallowMultipleComponent]
    public class GameTimeDebugUI : MonoBehaviour
    {
        [SerializeField] Unit watchedUnit;
        [SerializeField] int fontSize = 18;

        GUIStyle _style;

        void Awake()
        {
            if (watchedUnit == null) watchedUnit = FindFirstObjectByType<Unit>();
        }

        void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold
            };

            const float pad = 10f;
            float y = pad;

            bool paused = GameTime.IsPaused;
            _style.normal.textColor = paused ? new Color(1f, 0.3f, 0.3f) : new Color(0.4f, 1f, 0.4f);
            GUI.Label(new Rect(pad, y, 240f, fontSize + 6f), paused ? "PAUSED" : "RUNNING", _style);
            y += fontSize + 6f;

            _style.normal.textColor = Color.white;
            float scale = GameTime.TimeScale;
            GUI.Label(new Rect(pad, y, 240f, fontSize + 6f), $"timeScale: {scale:0.00}", _style);
            y += fontSize + 6f;

            if (watchedUnit != null && watchedUnit.Orders != null)
            {
                int pending = watchedUnit.Orders.PendingCount;
                int total = watchedUnit.Orders.TotalCount;
                string current = watchedUnit.Orders.Current != null
                    ? watchedUnit.Orders.Current.GetType().Name
                    : "—";
                GUI.Label(new Rect(pad, y, 320f, fontSize + 6f), $"orders: {total}  (current: {current}, pending: {pending})", _style);
                y += fontSize + 6f;
            }
            else
            {
                GUI.Label(new Rect(pad, y, 320f, fontSize + 6f), "orders: no unit", _style);
                y += fontSize + 6f;
            }

            if (PlayerInputController.IsShiftHeld())
            {
                _style.normal.textColor = new Color(1f, 0.75f, 0.2f);
                GUI.Label(new Rect(pad, y, 320f, fontSize + 6f), "[Shift] QUEUE +", _style);
            }
        }
    }
}
