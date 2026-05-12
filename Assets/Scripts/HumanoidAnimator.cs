using UnityEngine;

public class HumanoidAnimator : MonoBehaviour
{
    [Header("Limb Joints")]
    public Transform leftArm;
    public Transform rightArm;
    public Transform leftLeg;
    public Transform rightLeg;

    [Header("Walk Cycle")]
    [Tooltip("Speed at which the character is considered fully moving.")]
    public float referenceSpeed = 6f;
    public float cycleSpeed = 9f;
    public float maxSwingDeg = 45f;
    public float smoothing = 10f;

    [Header("External Overrides")]
    [Tooltip("When true, the walk cycle leaves the right arm alone so another system (e.g. attack) can animate it.")]
    public bool SuppressRightArm;

    private Vector3 lastPosition;
    private float moveAmount01;
    private float phase;

    void OnEnable()
    {
        lastPosition = transform.position;
        moveAmount01 = 0f;
        phase = 0f;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt < 1e-5f) return;

        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;
        lastPosition = transform.position;

        float speed = delta.magnitude / dt;
        float target = Mathf.Clamp01(speed / Mathf.Max(referenceSpeed, 0.01f));
        moveAmount01 = Mathf.Lerp(moveAmount01, target, 1f - Mathf.Exp(-smoothing * dt));

        phase += cycleSpeed * moveAmount01 * dt;
        float swing = Mathf.Sin(phase) * maxSwingDeg * moveAmount01;

        if (leftLeg)  leftLeg.localRotation  = Quaternion.Euler( swing, 0f, 0f);
        if (rightLeg) rightLeg.localRotation = Quaternion.Euler(-swing, 0f, 0f);
        if (leftArm)  leftArm.localRotation  = Quaternion.Euler(-swing, 0f, 0f);
        if (rightArm && !SuppressRightArm) rightArm.localRotation = Quaternion.Euler( swing, 0f, 0f);
    }
}
