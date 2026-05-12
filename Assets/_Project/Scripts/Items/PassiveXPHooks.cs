using Project.Core;
using Project.Skills;
using UnityEngine;
using UnityEngine.AI;

namespace Project.Items
{
    /// Owns the per-frame "trickle XP" hooks that fire while the unit lives
    /// its life: moving (Speed) and moving under a load (Strength, scaled by
    /// inventory fill ratio). These were originally inside SkillModifiersBridge
    /// — splitting them out keeps the bridge focused on event-driven
    /// cross-domain wiring and gives future passive sources (hunger, cold,
    /// posture) a clear home to grow in.
    ///
    /// Lives in Project.Items because it reads Inventory state to scale
    /// Strength gain — but it has zero coupling beyond reading public state
    /// from SkillSystem, Inventory, and NavMeshAgent.
    [DisallowMultipleComponent]
    public class PassiveXPHooks : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Below this velocity magnitude the unit is considered idle (no Speed XP, no Strength training).")]
        [SerializeField, Min(0f)] float movementVelocityThreshold = 0.1f;
        [Tooltip("Speed XP per second gained while moving.")]
        [SerializeField, Min(0f)] float speedXPPerSecondMoving = 0.1f;

        [Header("Strength training while carrying")]
        [Tooltip("Maximum Strength XP per second when moving with a 100%-full inventory. Scales linearly down to 0 at an empty inventory. Overweight loads (ratio > 1) are clamped to this max — the speed penalty already punishes them on its own.")]
        [SerializeField, Min(0f)] float loadStrengthXPPerSecondAtFull = 0.15f;

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

            // Strength XP scales linearly with inventory fill: 0 at empty,
            // max at full. Overweight loads stay capped at max (the speed
            // penalty + the red bar already carry the "you carry too much"
            // signal — no need to double-dip XP gain on top).
            if (_inventory != null)
            {
                float loadFactor = Mathf.Clamp01(_inventory.WeightRatio);
                if (loadFactor > 0f)
                {
                    _skills.GainXP(SkillType.Strength, loadStrengthXPPerSecondAtFull * loadFactor * dt);
                }
            }
        }
    }
}
