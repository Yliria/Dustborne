using System.Collections.Generic;
using Project.Core;
using Project.Health;
using Project.Units;
using UnityEngine;
using UnityEngine.AI;

namespace Project.Skills
{
    /// The only place that knows about BOTH HealthSystem and SkillSystem.
    /// All cross-domain wiring lives here:
    ///   - Speed/Vitality level-ups → push new agent.speed
    ///   - Vitality level-ups → push new vitality multiplier into Health,
    ///     preserving HP and blood ratios so leveling is "more capacity"
    ///     not "free heal"
    ///   - Health part state changes → recompute move speed
    ///   - Damage taken → grant Vitality XP to defender + attacker XP via SkillSystem
    ///   - Movement → trickle Speed XP
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HealthSystem))]
    [RequireComponent(typeof(SkillSystem))]
    public class SkillModifiersBridge : MonoBehaviour
    {
        [Header("Speed XP from movement")]
        [Tooltip("Speed XP per second gained while the unit is moving (velocity > threshold).")]
        [SerializeField, Min(0f)] float speedXPPerSecondMoving = 0.1f;
        [SerializeField, Min(0f)] float movementVelocityThreshold = 0.1f;

        [Header("Vitality XP from damage taken")]
        [Tooltip("Multiplier applied to DamageInfo.Amount when granting Vitality XP to the defender.")]
        [SerializeField, Min(0f)] float vitalityXPPerDamage = 1.0f;

        Unit _unit;
        HealthSystem _health;
        SkillSystem _skills;
        NavMeshAgent _agent;

        float _baseAgentSpeed;

        // Scratch buffers reused across level-ups to avoid GC.
        readonly Dictionary<BodyPartId, float> _ratioSnapshot = new();

        void Awake()
        {
            _unit = GetComponent<Unit>();
            _health = GetComponent<HealthSystem>();
            _skills = GetComponent<SkillSystem>();
            _agent = GetComponent<NavMeshAgent>();
        }

        void Start()
        {
            if (_agent != null) _baseAgentSpeed = _agent.speed;

            // Push the starting vitality multiplier into Health so Parts and
            // Blood get the right max (still 1.0 at L1 by formula, but this
            // is correct for any starting level — e.g. loaded save).
            ApplyVitalityMultiplier(preserveRatios: false);
            RecomputeMoveSpeed();
        }

        void OnEnable()
        {
            _skills.OnLevelUp += HandleLevelUp;
            _health.OnPartStateChanged += HandlePartStateChanged;
            _health.OnDamageTaken += HandleDamageTaken;
        }

        void OnDisable()
        {
            _skills.OnLevelUp -= HandleLevelUp;
            _health.OnPartStateChanged -= HandlePartStateChanged;
            _health.OnDamageTaken -= HandleDamageTaken;
        }

        void Update()
        {
            // Speed XP trickle while moving. GameTime.DeltaTime gates pause.
            float dt = GameTime.DeltaTime;
            if (dt <= 0f || _agent == null) return;
            if (_agent.velocity.sqrMagnitude > movementVelocityThreshold * movementVelocityThreshold)
            {
                _skills.GainXP(SkillType.Speed, speedXPPerSecondMoving * dt);
            }
        }

        // ---- Event handlers ----

        void HandleLevelUp(SkillType type, int oldLevel, int newLevel)
        {
            if (type == SkillType.Vitality)
            {
                ApplyVitalityMultiplier(preserveRatios: true);
            }
            if (type == SkillType.Speed || type == SkillType.Vitality)
            {
                RecomputeMoveSpeed();
            }
        }

        void HandlePartStateChanged(BodyPartId id, BodyPartState oldState, BodyPartState newState)
        {
            RecomputeMoveSpeed();
        }

        void HandleDamageTaken(DamageInfo info)
        {
            // Defender trains Vitality from absorbed punishment.
            _skills.GainXP(SkillType.Vitality, info.Amount * vitalityXPPerDamage);

            // Attacker trains Strength / Dexterity based on weapon category.
            // Skip if attacker is this unit (shouldn't happen, but cheap to guard).
            if (info.Attacker != null && info.Attacker.gameObject != gameObject)
            {
                SkillSystem.GrantAttackerXP(info.Attacker, info);
            }
        }

        // ---- Speed pipeline ----

        public void RecomputeMoveSpeed()
        {
            if (_agent == null) return;
            float skillMult = _skills.GetMoveSpeedMult();
            float healthMult = _health.GetMoveSpeedMultiplier();
            _agent.speed = _baseAgentSpeed * skillMult * healthMult;
        }

        // ---- Vitality pipeline ----

        /// Pushes the current vitality multiplier into HealthSystem.
        /// If preserveRatios=true (level-up path), HP and blood are rescaled
        /// so current/max stays constant — i.e. leveling Vitality grows the
        /// reservoir without healing. If false (init path), the unit is
        /// filled to the new max.
        public void ApplyVitalityMultiplier(bool preserveRatios)
        {
            float vitMult = _skills.GetVitalityHPMultiplier();

            float bloodRatio = preserveRatios ? _health.Blood.Ratio : 1f;
            if (preserveRatios)
            {
                _ratioSnapshot.Clear();
                foreach (var p in _health.Parts)
                {
                    if (p.Def == null) continue;
                    _ratioSnapshot[p.Def.Id] = p.HPRatio;
                }
            }

            _health.SetVitalityMultiplier(vitMult);
            foreach (var p in _health.Parts)
            {
                if (p.Def == null) continue;
                if (preserveRatios && _ratioSnapshot.TryGetValue(p.Def.Id, out float ratio))
                {
                    p.CurrentHP = ratio * (p.Def.BaseMaxHP * vitMult);
                }
                else
                {
                    p.CurrentHP = p.Def.BaseMaxHP * vitMult;
                }
                p.Recompute(vitMult);
            }

            _health.Blood.SetVitalityMultiplier(vitMult);
            if (preserveRatios)
            {
                _health.Blood.CurrentBlood = bloodRatio * _health.Blood.EffectiveMaxBlood;
            }
            else
            {
                _health.Blood.CurrentBlood = _health.Blood.EffectiveMaxBlood;
            }
        }
    }
}
