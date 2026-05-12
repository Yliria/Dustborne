using Project.Core;
using Project.Harvesting;
using Project.Harvesting.Orders;
using Project.Items;
using Project.Items.Orders;
using Project.Units;
using Project.Units.Orders;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Project.PlayerInput
{
    /// Translates raw input (mouse, keys) into Unit orders and global pause
    /// toggles. The controller is the only place that knows about the player's
    /// selected unit and the marker prefab to use for visual feedback.
    [DisallowMultipleComponent]
    public class PlayerInputController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] Unit unit;
        [SerializeField] Camera worldCamera;

        [Header("Order visuals")]
        [Tooltip("Marker spawned for an immediate (non-shift) move order.")]
        [SerializeField] GameObject moveMarkerPrefab;
        [Tooltip("Marker spawned for a queued (shift) move order. Falls back to moveMarkerPrefab if null.")]
        [SerializeField] GameObject queuedMoveMarkerPrefab;

        [Header("Raycast")]
        [SerializeField] LayerMask groundMask = ~0;
        [SerializeField] float maxRayDistance = 500f;

        void Awake()
        {
            if (worldCamera == null) worldCamera = Camera.main;
            if (unit == null) unit = FindFirstObjectByType<Unit>();
        }

        void Update()
        {
            HandlePauseToggle();
            HandleMoveClick();
        }

        void HandlePauseToggle()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.spaceKey.wasPressedThisFrame) GameTime.TogglePause();
        }

        void HandleMoveClick()
        {
            if (unit == null || worldCamera == null) return;

            var mouse = Mouse.current;
            if (mouse == null) return;
            if (!mouse.leftButton.wasPressedThisFrame) return;

            // Don't issue orders when the click started over UI.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Vector2 screenPos = mouse.position.ReadValue();
            Ray ray = worldCamera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, groundMask, QueryTriggerInteraction.Ignore))
                return;

            bool append = false;
            var kb = Keyboard.current;
            if (kb != null) append = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;

            // Click priority: WorldItem → Harvestable → ground. Each level
            // uses GetComponentInParent so a click on a child collider
            // (visual mesh, hitbox) still resolves to the gameplay component
            // on the root.
            if (hit.collider != null)
            {
                var worldItem = hit.collider.GetComponentInParent<WorldItem>();
                if (worldItem != null)
                {
                    unit.IssueOrder(new PickupOrder(worldItem), append);
                    return;
                }

                var harvestable = hit.collider.GetComponentInParent<Harvestable>();
                if (harvestable != null && !harvestable.IsDepleted)
                {
                    unit.IssueOrder(new HarvestOrder(harvestable), append);
                    return;
                }
            }

            GameObject prefab = append && queuedMoveMarkerPrefab != null
                ? queuedMoveMarkerPrefab
                : moveMarkerPrefab;

            unit.IssueOrder(new MoveOrder(hit.point, prefab), append);
        }

        public static bool IsShiftHeld()
        {
            var kb = Keyboard.current;
            return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
        }
    }
}
