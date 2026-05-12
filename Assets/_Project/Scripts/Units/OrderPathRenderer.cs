using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Project.Units
{
    /// Draws a polyline from the unit through its current and pending
    /// targeted orders. Decoupled from concrete order types — any order that
    /// implements ITargetedOrder contributes a waypoint.
    ///
    /// The line is treated as a "queue preview": it is only visible while the
    /// player is holding the queue modifier (Shift). This keeps the world
    /// uncluttered during normal play and gives the player a way to inspect
    /// the planned path on demand. Markers (spawned by each MoveOrder) remain
    /// visible regardless — they are the persistent feedback.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer))]
    public class OrderPathRenderer : MonoBehaviour
    {
        [SerializeField] Unit unit;
        [SerializeField] LineRenderer line;
        [SerializeField, Min(0f)] float groundOffset = 0.05f;

        [Tooltip("If true, the line is only rendered while a queue-modifier key (Shift) is held.")]
        [SerializeField] bool onlyVisibleWhileShiftHeld = true;

        readonly List<Vector3> _buffer = new();

        void Awake()
        {
            if (unit == null) unit = GetComponent<Unit>();
            if (line == null) line = GetComponent<LineRenderer>();
        }

        void LateUpdate()
        {
            if (unit == null || line == null || unit.Orders == null)
            {
                if (line != null) line.positionCount = 0;
                return;
            }

            if (onlyVisibleWhileShiftHeld && !IsShiftHeld())
            {
                line.positionCount = 0;
                return;
            }

            _buffer.Clear();

            // Tentatively add the unit position — we only keep the line if at
            // least one targeted waypoint follows, otherwise we hide it below.
            _buffer.Add(transform.position + Vector3.up * groundOffset);

            if (unit.Orders.Current is ITargetedOrder currentTarget)
                _buffer.Add(currentTarget.TargetPosition + Vector3.up * groundOffset);

            foreach (var order in unit.Orders.Pending)
            {
                if (order is ITargetedOrder t)
                    _buffer.Add(t.TargetPosition + Vector3.up * groundOffset);
            }

            // Hide the line if there's nothing meaningful to draw (e.g. the
            // current order is non-targeted, like a future Wait or Animate).
            if (_buffer.Count < 2)
            {
                line.positionCount = 0;
                return;
            }

            line.positionCount = _buffer.Count;
            for (int i = 0; i < _buffer.Count; i++) line.SetPosition(i, _buffer[i]);
        }

        static bool IsShiftHeld()
        {
            var kb = Keyboard.current;
            return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
        }
    }
}
