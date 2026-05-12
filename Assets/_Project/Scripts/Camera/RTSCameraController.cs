using UnityEngine;
using UnityEngine.InputSystem;

namespace Project.CameraRig
{
    /// Top-down RTS camera: WASD/edge pan, scroll zoom, optional Q/E yaw.
    /// Reads Time.unscaledDeltaTime by design so the camera stays responsive
    /// while the game is paused — must NOT use GameTime.DeltaTime.
    [DisallowMultipleComponent]
    public class RTSCameraController : MonoBehaviour
    {
        [Header("Pan")]
        [SerializeField] float panSpeed = 18f;
        [SerializeField] bool edgePanEnabled = false;
        [SerializeField, Min(0f)] float edgePanThicknessPx = 12f;

        [Header("Zoom (dolly along camera forward)")]
        [SerializeField] float zoomSpeed = 12f;
        [SerializeField] float minHeight = 6f;
        [SerializeField] float maxHeight = 30f;

        [Header("Rotation")]
        [SerializeField] bool rotationEnabled = false;
        [SerializeField] float rotationSpeedDeg = 90f;

        [Header("Bounds (optional, set max < min to disable)")]
        [SerializeField] Vector2 minWorldXZ = new(-25f, -25f);
        [SerializeField] Vector2 maxWorldXZ = new(25f, 25f);

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            HandlePan(dt);
            HandleZoom();
            if (rotationEnabled) HandleRotate(dt);
        }

        void HandlePan(float dt)
        {
            Vector2 input = ReadPanInput();
            if (input == Vector2.zero) return;

            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            Vector3 delta = (fwd * input.y + right * input.x) * panSpeed * dt;

            Vector3 pos = transform.position + delta;
            if (maxWorldXZ.x > minWorldXZ.x && maxWorldXZ.y > minWorldXZ.y)
            {
                pos.x = Mathf.Clamp(pos.x, minWorldXZ.x, maxWorldXZ.x);
                pos.z = Mathf.Clamp(pos.z, minWorldXZ.y, maxWorldXZ.y);
            }
            transform.position = pos;
        }

        Vector2 ReadPanInput()
        {
            Vector2 axis = Vector2.zero;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    axis.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  axis.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) axis.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  axis.x -= 1f;
            }

            if (edgePanEnabled)
            {
                var mouse = Mouse.current;
                if (mouse != null && Application.isFocused)
                {
                    Vector2 p = mouse.position.ReadValue();
                    int w = Screen.width;
                    int h = Screen.height;
                    if (p.x >= 0 && p.x <= w && p.y >= 0 && p.y <= h)
                    {
                        if (p.x <= edgePanThicknessPx)        axis.x -= 1f;
                        else if (p.x >= w - edgePanThicknessPx) axis.x += 1f;
                        if (p.y <= edgePanThicknessPx)        axis.y -= 1f;
                        else if (p.y >= h - edgePanThicknessPx) axis.y += 1f;
                    }
                }
            }

            if (axis.sqrMagnitude > 1f) axis.Normalize();
            return axis;
        }

        void HandleZoom()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Approximately(scroll, 0f)) return;

            // Scroll units vary across platforms; normalize to a per-notch step
            // and apply zoomSpeed as a multiplier.
            float step = Mathf.Sign(scroll) * zoomSpeed * 0.1f;

            Vector3 nextPos = transform.position + transform.forward * step;
            if (nextPos.y < minHeight && transform.forward.y < 0f) return;
            if (nextPos.y > maxHeight && transform.forward.y > 0f) return;

            transform.position = nextPos;
        }

        void HandleRotate(float dt)
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            float dir = 0f;
            if (kb.qKey.isPressed) dir -= 1f;
            if (kb.eKey.isPressed) dir += 1f;
            if (dir == 0f) return;

            transform.Rotate(Vector3.up, dir * rotationSpeedDeg * dt, Space.World);
        }
    }
}
