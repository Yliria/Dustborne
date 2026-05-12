using System.Collections.Generic;
using UnityEngine;

// Attach to a humanoid arm joint. Animates a forward swing and damages any Enemy
// the arm volume sweeps through during the "active" window of the swing.
public class MeleeWeapon : MonoBehaviour
{
    [Header("Refs")]
    public HumanoidAnimator animator;

    [Header("Damage")]
    public float damage = 10f;
    public LayerMask hitMask = ~0;

    [Header("Swing Timing (seconds)")]
    public float swingDuration = 0.45f;
    [Range(0f, 1f)] public float activeStart = 0.2f;
    [Range(0f, 1f)] public float activeEnd = 0.6f;

    [Header("Swing Geometry")]
    [Tooltip("Peak forward rotation of the arm around its local X axis.")]
    public float maxSwingAngle = 110f;
    [Tooltip("Length of the arm from the joint to the fist (matches limb length in HumanoidBuilder).")]
    public float armLength = 0.75f;
    [Tooltip("Radius of the swept capsule that represents the arm's hit volume.")]
    public float hitRadius = 0.3f;

    private float timer = -1f;
    private readonly HashSet<Enemy> hitThisSwing = new HashSet<Enemy>();
    private Transform selfEnemyRoot; // cached so we don't damage the weapon's own owner

    public bool IsAttacking => timer >= 0f;

    void Awake()
    {
        var ownEnemy = GetComponentInParent<Enemy>();
        if (ownEnemy != null) selfEnemyRoot = ownEnemy.transform;
    }

    public void Attack()
    {
        if (IsAttacking) return;
        timer = 0f;
        hitThisSwing.Clear();
        if (animator != null) animator.SuppressRightArm = true;
    }

    void Update()
    {
        if (!IsAttacking) return;

        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / swingDuration);

        // Sin-shaped swing: 0 → peak → 0
        float curve = Mathf.Sin(t * Mathf.PI);
        float angle = curve * maxSwingAngle;
        transform.localRotation = Quaternion.Euler(-angle, 0f, 0f);

        if (t >= activeStart && t <= activeEnd) CheckHits();

        if (t >= 1f) EndSwing();
    }

    private void CheckHits()
    {
        Vector3 shoulder = transform.position;
        Vector3 tip = transform.TransformPoint(Vector3.down * armLength);
        Collider[] hits = Physics.OverlapCapsule(shoulder, tip, hitRadius, hitMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            var enemy = hits[i].GetComponentInParent<Enemy>();
            if (enemy == null || !enemy.IsAlive) continue;
            if (selfEnemyRoot != null && enemy.transform == selfEnemyRoot) continue;
            if (hitThisSwing.Add(enemy)) enemy.TakeDamage(damage);
        }
    }

    private void EndSwing()
    {
        timer = -1f;
        transform.localRotation = Quaternion.identity;
        if (animator != null) animator.SuppressRightArm = false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = IsAttacking ? Color.red : new Color(1f, 0.6f, 0f, 0.5f);
        Vector3 shoulder = transform.position;
        Vector3 tip = transform.TransformPoint(Vector3.down * armLength);
        Gizmos.DrawWireSphere(shoulder, hitRadius);
        Gizmos.DrawWireSphere(tip, hitRadius);
        Gizmos.DrawLine(shoulder, tip);
    }
}
