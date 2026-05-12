using Project.Core;
using Project.Skills;
using UnityEngine;
using UnityEngine.AI;

namespace Project.Items
{
    /// Owns the per-frame "trickle XP" hooks that fire while the unit lives
    /// its life: moving (Speed) and moving while overweight (Strength).
    /// These were originally inside SkillModifiersBridge — splitting them out
    /// keeps the bridge focused on event-driven cross-domain wiring and gives
    /// future passive sources (hunger, cold, posture) a clear home to grow in.
    ///
    /// Lives in Project.Items because it needs Inventory to gate the
    /// overweight branch — but it has zero coupling beyond reading public
    /// state from SkillSystem, Inventory, and NavMeshAgent.
    [DisallowMultipleComponent]
    public class PassiveXPHooks : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Below this velocity magnitude the unit is considered idle (no Speed XP, no overweight training).")]
        [SerializeField, Min(0f)] float movementVelocityThreshold = 0.1f;
        [Tooltip("Speed XP per second gained while moving.")]
        [SerializeField, Min(0f)] float speedXPPerSecondMoving = 0.1f;

        [Header("Overweight Strength training")]
        [Tooltip("Strength XP per second gained while moving AND inventory is overweight.")]
        [SerializeField, Min(0f)] float overweightStrengthXPPerSecond = 0.15f;

        SkillSystem _skills;
        Inventory _inventory;
        NavMeshAgent _agent;

        void Awake()
        {
            _skills = GetComponent<SkillSystem>();
            _inventory = GetComponent<Inventory>();
            _agent = GetComponent<NavMeshAgent>();
        }

        void Update()
        {
            if (_skills == null || _agent == null) return;
            float dt = GameTime.DeltaTime;
            if (dt <= 0f) return;

            bool moving = _agent.velocity.sqrMagnitude > movementVelocityThreshold * movementVelocityThreshold;
            if (!moving) return;

            _skills.GainXP(SkillType.Speed, speedXPPerSecondMoving * dt);

            if (_inventory != null && _inventory.IsOverweight)
            {
                _skills.GainXP(SkillType.Strength, overweightStrengthXPPerSecond * dt);
            }
        }
    }
}
