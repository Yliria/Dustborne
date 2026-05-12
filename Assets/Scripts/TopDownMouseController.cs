using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(CharacterController))]
public class TopDownMouseController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 6f;
    public float rotationSpeed = 720f;
    public float stopDistance = 0.1f;

    [Header("Combat")]
    public float attackRange = 1.3f;
    public MeleeWeapon weapon;

    [Header("Mouse Raycast")]
    public LayerMask clickMask = ~0;
    public float rayMaxDistance = 500f;

    private CharacterController controller;
    private Camera cam;
    private Vector3? groundTarget;
    private Enemy enemyTarget;
    private float verticalVelocity;
    private bool wasLeftDown;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        cam = Camera.main;
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        HandleMouseInput();
        Vector3 horizontal = ResolveMovement();
        ApplyMotion(horizontal);
    }

    private void HandleMouseInput()
    {
        if (!TryReadMouse(out bool leftHeld, out Vector2 mousePos)) return;

        bool justPressed = leftHeld && !wasLeftDown;
        wasLeftDown = leftHeld;

        if (!leftHeld) return;

        Ray ray = cam.ScreenPointToRay(mousePos);
        if (!Physics.Raycast(ray, out RaycastHit hit, rayMaxDistance, clickMask, QueryTriggerInteraction.Ignore)) return;

        var enemy = hit.collider.GetComponentInParent<Enemy>();
        if (enemy != null && enemy.IsAlive)
        {
            enemyTarget = enemy;
            groundTarget = null;
        }
        else if (justPressed)
        {
            // Only set a new ground target on the initial click, so holding over
            // an enemy that dies doesn't teleport the target to the ground under the cursor.
            groundTarget = hit.point;
            enemyTarget = null;
        }
    }

    private Vector3 ResolveMovement()
    {
        if (enemyTarget != null && !enemyTarget.IsAlive) enemyTarget = null;

        if (enemyTarget != null)
        {
            Vector3 toEnemy = enemyTarget.transform.position - transform.position;
            toEnemy.y = 0f;
            float dist = toEnemy.magnitude;
            Vector3 dir = dist > 0.001f ? toEnemy / dist : transform.forward;
            FaceDirection(dir);

            if (dist > attackRange)
            {
                return dir * moveSpeed;
            }

            if (weapon != null && !weapon.IsAttacking) weapon.Attack();
            return Vector3.zero;
        }

        if (groundTarget.HasValue)
        {
            Vector3 flatTarget = new Vector3(groundTarget.Value.x, transform.position.y, groundTarget.Value.z);
            Vector3 toTarget = flatTarget - transform.position;
            float dist = toTarget.magnitude;
            if (dist > stopDistance)
            {
                Vector3 dir = toTarget / dist;
                FaceDirection(dir);
                return dir * moveSpeed;
            }
            groundTarget = null;
        }

        return Vector3.zero;
    }

    private void FaceDirection(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion look = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, rotationSpeed * Time.deltaTime);
    }

    private void ApplyMotion(Vector3 horizontal)
    {
        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += Physics.gravity.y * Time.deltaTime;
        Vector3 motion = horizontal + Vector3.up * verticalVelocity;
        controller.Move(motion * Time.deltaTime);
    }

    private bool TryReadMouse(out bool leftHeld, out Vector2 position)
    {
        leftHeld = false;
        position = default;
#if ENABLE_INPUT_SYSTEM
        var mouse = Mouse.current;
        if (mouse != null)
        {
            leftHeld = mouse.leftButton.isPressed;
            position = mouse.position.ReadValue();
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        leftHeld = Input.GetMouseButton(0);
        position = Input.mousePosition;
        return true;
#else
        return false;
#endif
    }
}
