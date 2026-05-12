using System.Collections.Generic;
using Project.Core;
using Project.Health;
using Project.Items;
using Project.Units;
using UnityEngine;
using UnityEngine.AI;

namespace Project.Skills
{
    /// The only place that knows about HealthSystem + SkillSystem + Inventory.
    /// All cross-domain wiring lives here:
    ///   - Speed/Vitality level-ups → push new agent.speed
    ///   - Strength level-ups       → push new BonusMaxWeight into Inventory
    ///   - Vitality level-ups       → push new vitality multiplier into Health,
    ///                                preserving HP and blood ratios so leveling
    ///                                is "more capacity" not "free heal"
    ///   - Health part state change → recompute move speed
    ///   - Inventory weight change  → recompute move speed (load penalty)
    ///   - Damage taken             → Vitality XP for defender, Str/Dex for attacker
    ///   - Movement                 → Speed XP (trickle)
    ///   - Movement + overweight    → Strength XP (trickle)
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

        [Header("Strength XP from carrying overweight")]
        [Tooltip("Strength XP per second gained while moving with an overweight inventory.")]
        [SerializeField, Min(0f)] float overweightStrengthXPPerSecond = 0.15f;

        [Header("Weight speed penalty curve")]
        [Tooltip("No penalty below this weight ratio.")]
        [SerializeField, Range(0f, 1f)] float weightPenaltyStartRatio = 0.75f;
        [Tooltip("Speed multiplier at full load (ratio = 1.0).")]
        [SerializeField, Range(0f, 1f)] float weightPenaltyAtFullLoad = 0.7f;
        [Tooltip("Slope of additional penalty per unit of overweight beyond ratio 1.0.")]
        [SerializeField, Min(0f)] float weightPenaltyOverloadSlope = 0.4f;
        [Tooltip("Minimum speed multiplier no matter how overweight.")]
        [SerializeField, Range(0f, 1f)] float weightPenaltyFloor = 0.15f;

        Unit _unit;
        HealthSystem _health;
        SkillSystem _skills;
        Inventory _inventory;
        NavMeshAgent _agent;

        float _baseAgentSpeed;

        // Scratch buffers reused across level-ups to avoid GC.
        readonly Dictionary<BodyPartId, float> _ratioSnapshot = new();

        void Awake()
        {
            _unit = GetComponent<Unit>();
            _health = GetComponent<HealthSystem>();
            _skills = GetComponent<SkillSystem>();
            _inventory = GetComponent<Inventory>();
            _agent = GetComponent<NavMeshAgent>();
        }

        void Start()
        {
            if (_agent != null) _baseAgentSpeed = _agent.speed;

            // Initial propagation: push starting Strength bonus into Inventory,
            // starting Vitality multiplier into Health, then compute speed.
            if (_inventory != null) _inventory.SetMaxWeightBonus(_skills.GetMaxCarryWeightBonus());
            ApplyVitalityMultiplier(preserveRatios: false);
            RecomputeMoveSpeed();
        }

        void OnEnable()
        {
            _skills.OnLevelUp += HandleLevelUp;
            _health.OnPartStateChanged += HandlePartStateChanged;
            _health.OnDamageTaken += HandleDamageTaken;
            if (_inventory != null) _inventory.OnWeightChanged += HandleWeightChanged;
        }

        void OnDisable()
        {
            _skills.OnLevelUp -= HandleLevelUp;
            _health.OnPartStateChanged -= HandlePartStateChanged;
            _health.OnDamageTaken -= HandleDamageTaken;
            if (_inventory != null) _inventory.OnWeightChanged -= HandleWeightChanged;
        }

        void Update()
        {
            float dt = GameTime.DeltaTime;
            if (dt <= 0f || _agent == null) return;

            bool moving = _agent.velocity.sqrMagnitude > movementVelocityThreshold * movementVelocityThreshold;
            if (!moving) return;

            _skills.GainXP(SkillType.Speed, speedXPPerSecondMoving * dt);

            // Overweight Strength training — only while actually moving under
            // the load, not while standing still.
            if (_inventory != null && _inventory.IsOverweight)
            {
                _skills.GainXP(SkillType.Strength, overweightStrengthXPPerSecond * dt);
            }
        }

        // ---- Event handlers ----

        void HandleLevelUp(SkillType type, int oldLevel, int newLevel)
        {
            if (type == SkillType.Vitality)
            {
                ApplyVitalityMultiplier(preserveRatios: true);
            }
            if (type == SkillType.Strength && _inventory != null)
            {
                _inventory.SetMaxWeightBonus(_skills.GetMaxCarryWeightBonus());
                // SetMaxWeightBonus fires OnWeightChanged which already calls
                // RecomputeMoveSpeed. No need to call it again here.
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

        void HandleWeightChanged(float currentWeight)
        {
            RecomputeMoveSpeed();
        }

        void HandleDamageTaken(DamageInfo info)
        {
            // Defender trains Vitality from absorbed punishment.
            _skills.GainXP(SkillType.Vitality, info.Amount * vitalityXPPerDamage);

            // Attacker trains Strength / Dexterity based on weapon category.
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
            float weightMult = GetWeightSpeedMultiplier();
            _agent.speed = _baseAgentSpeed * skillMult * healthMult * weightMult;
        }

        public float GetWeightSpeedMultiplier()
        {
            if (_inventory == null) return 1f;
            float ratio = _inventory.WeightRatio;

            if (ratio <= weightPenaltyStartRatio) return 1f;

            if (ratio <= 1f)
            {
                // Lerp from 1.0 at weightPenaltyStartRatio to weightPenaltyAtFullLoad at 1.0.
                float span = 1f - weightPenaltyStartRatio;
                float t = span > 0f ? (ratio - weightPenaltyStartRatio) / span : 1f;
                return Mathf.Lerp(1f, weightPenaltyAtFullLoad, t);
            }

            // Over capacity: linear punishment past full load, clamped.
            return Mathf.Max(weightPenaltyFloor,
                             weightPenaltyAtFullLoad - (ratio - 1f) * weightPenaltyOverloadSlope);
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
