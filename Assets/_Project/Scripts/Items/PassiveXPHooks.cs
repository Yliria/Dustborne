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
        [Tooltip("Maximum Strength XP per second once the load curve hits 100%. Reached when WeightRatio >= loadStrengthThresholdMax.")]
        [SerializeField, Min(0f)] float loadStrengthXPPerSecondAtFull = 0.15f;
        [Tooltip("WeightRatio below which no Strength XP is granted. Carrying almost nothing trains nothing.")]
        [SerializeField, Range(0f, 1f)] float loadStrengthThresholdMin = 0.10f;
        [Tooltip("WeightRatio at and above which Strength XP gain is at the max rate. The interval [min, max] is a linear ramp; anything beyond max stays at full.")]
        [SerializeField, Range(0f, 1f)] float loadStrengthThresholdMax = 0.90f;

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

            // Strength XP follows a flat-tailed linear ramp on WeightRatio:
            //   0 below loadStrengthThresholdMin (~10%): tiny loads don't train
            //   linear lerp 0→1 across [min, max]
            //   1 at and above loadStrengthThresholdMax (~90%): full reward
            // Overweight stays at the max — the speed penalty already
            // punishes excess load on its own.
            if (_inventory != null)
            {
                float loadFactor = Mathf.InverseLerp(loadStrengthThresholdMin, loadStrengthThresholdMax, _inventory.WeightRatio);
                if (loadFactor > 0f)
                {
                    _skills.GainXP(SkillType.Strength, loadStrengthXPPerSecondAtFull * loadFactor * dt);
                }
            }
        }
    }
}
