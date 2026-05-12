using System;
using System.Collections.Generic;
using Project.Core;
using Project.Health;
using Project.Units;
using UnityEngine;

namespace Project.Skills
{
    /// Owns a Unit's 5 trainable skills, applies XP, manages level-ups, and
    /// exposes the stat-modifier formulas used by combat, movement, crafting,
    /// etc. Knows nothing about Health — Vitality propagation to body parts
    /// happens via SkillModifiersBridge listening to OnLevelUp.
    [DisallowMultipleComponent]
    public class SkillSystem : MonoBehaviour
    {
        // ---- Tuning constants (per-level deltas) ----
        // Strength
        public const float MeleeDamagePerLevel = 0.01f;
        public const float CarryWeightPerLevel = 2f;
        // Vitality
        public const float VitalityHPPerLevel = 0.015f;
        // Speed
        public const float MoveSpeedPerLevel = 0.005f;
        public const float AttackSpeedFromSpeedPerLevel = 0.003f;
        // Dexterity
        public const float AttackSpeedFromDexPerLevel = 0.005f;
        public const float AccuracyPerLevel = 0.01f;
        public const float DodgeChancePerLevel = 0.003f;
        public const float DodgeChanceCap = 0.5f;
        // Labour
        public const float HarvestSpeedPerLevel = 0.01f;
        public const float CraftSpeedPerLevel = 0.008f;

        [SerializeField] XPCurve curve;
        [SerializeField] List<SkillData> skills = new();

        public XPCurve Curve => curve;
        public IReadOnlyList<SkillData> AllSkills => skills;

        readonly Dictionary<SkillType, SkillData> _bySkill = new();

        public event Action<SkillType, int, int> OnLevelUp;
        public event Action<SkillType, float> OnXPGained;

        void Awake()
        {
            EnsureAllSkillsPresent();
            _bySkill.Clear();
            foreach (var s in skills) _bySkill[s.Type] = s;
        }

        void EnsureAllSkillsPresent()
        {
            foreach (SkillType t in Enum.GetValues(typeof(SkillType)))
            {
                if (skills.Find(s => s.Type == t) != null) continue;
                skills.Add(new SkillData { Type = t, Level = 1f, XPCurrent = 0f });
            }
        }

        // ---- API ----

        public SkillData Get(SkillType type) => _bySkill.TryGetValue(type, out var d) ? d : null;

        public int GetLevel(SkillType type) => Get(type)?.LevelInt ?? 1;
        public float GetValue(SkillType type) => Get(type)?.Level ?? 1f;

        public float GetXPToNext(SkillType type)
        {
            var s = Get(type);
            if (s == null || curve == null) return 100f;
            return curve.GetXPForNext(s.Level);
        }

        /// Adds XP to a skill, applying the curve's gain multiplier and rolling
        /// over level-ups. No-op during pause — XP is gameplay state, the
        /// player should not progress in stopped time.
        public void GainXP(SkillType type, float baseAmount)
        {
            if (GameTime.IsPaused) return;
            if (baseAmount <= 0f) return;
            var s = Get(type);
            if (s == null) return;

            float mult = curve != null ? curve.GetGainMultiplier(s.Level) : 1f;
            float effective = baseAmount * mult;
            if (effective <= 0f) return;

            int oldLevel = s.LevelInt;
            s.XPCurrent += effective;

            // Roll over one or many level boundaries.
            while (true)
            {
                float xpForNext = curve != null ? curve.GetXPForNext(s.Level) : 100f;
                if (s.XPCurrent < xpForNext) break;
                s.XPCurrent -= xpForNext;
                s.Level += 1f;
            }
            int newLevel = s.LevelInt;

            OnXPGained?.Invoke(type, effective);
            if (newLevel > oldLevel) OnLevelUp?.Invoke(type, oldLevel, newLevel);
        }

        /// Diagnostic / debug entry point used by the debug panel. Skips the
        /// pause guard so testers can train skills while looking at the
        /// frozen world.
        public void GainXPIgnoringPause(SkillType type, float baseAmount)
        {
            bool wasPaused = GameTime.IsPaused;
            // Use the same logic without the pause early-return.
            if (baseAmount <= 0f) return;
            var s = Get(type);
            if (s == null) return;

            float mult = curve != null ? curve.GetGainMultiplier(s.Level) : 1f;
            float effective = baseAmount * mult;
            if (effective <= 0f) return;

            int oldLevel = s.LevelInt;
            s.XPCurrent += effective;
            while (true)
            {
                float xpForNext = curve != null ? curve.GetXPForNext(s.Level) : 100f;
                if (s.XPCurrent < xpForNext) break;
                s.XPCurrent -= xpForNext;
                s.Level += 1f;
            }
            int newLevel = s.LevelInt;

            OnXPGained?.Invoke(type, effective);
            if (newLevel > oldLevel) OnLevelUp?.Invoke(type, oldLevel, newLevel);
            _ = wasPaused;
        }

        public void ResetAllSkills()
        {
            foreach (var s in skills)
            {
                s.Level = 1f;
                s.XPCurrent = 0f;
            }
        }

        // ---- Modifier formulas ----

        public float GetMeleeDamageMult() => 1f + (GetValue(SkillType.Strength) - 1f) * MeleeDamagePerLevel;
        public float GetMaxCarryWeightBonus() => (GetValue(SkillType.Strength) - 1f) * CarryWeightPerLevel;
        public float GetVitalityHPMultiplier() => 1f + (GetValue(SkillType.Vitality) - 1f) * VitalityHPPerLevel;
        public float GetMoveSpeedMult() => 1f + (GetValue(SkillType.Speed) - 1f) * MoveSpeedPerLevel;
        public float GetAttackSpeedMult()
            => 1f + (GetValue(SkillType.Speed) - 1f) * AttackSpeedFromSpeedPerLevel
                  + (GetValue(SkillType.Dexterity) - 1f) * AttackSpeedFromDexPerLevel;
        public float GetAccuracyMult() => 1f + (GetValue(SkillType.Dexterity) - 1f) * AccuracyPerLevel;
        public float GetDodgeChance()
            => Mathf.Clamp((GetValue(SkillType.Dexterity) - 1f) * DodgeChancePerLevel, 0f, DodgeChanceCap);
        public float GetHarvestSpeedMult() => 1f + (GetValue(SkillType.Labour) - 1f) * HarvestSpeedPerLevel;
        public float GetCraftSpeedMult() => 1f + (GetValue(SkillType.Labour) - 1f) * CraftSpeedPerLevel;

        // ---- Static helpers ----

        /// Routes XP to the attacker according to their weapon category. Safe
        /// to call with a null attacker (no-op). Called from combat code
        /// (today: SkillModifiersBridge listening to OnDamageTaken on the
        /// defender; tomorrow: AttackOrder when it lands a hit).
        public static void GrantAttackerXP(Unit attacker, DamageInfo info)
        {
            if (attacker == null) return;
            var ss = attacker.GetComponent<SkillSystem>();
            if (ss == null) return;
            float a = Mathf.Max(0f, info.Amount);
            switch (info.Weapon)
            {
                case WeaponCategory.Melee:
                    ss.GainXP(SkillType.Strength, a * 0.5f);
                    break;
                case WeaponCategory.MeleeFast:
                    ss.GainXP(SkillType.Dexterity, a * 0.5f);
                    ss.GainXP(SkillType.Strength, a * 0.2f);
                    break;
                case WeaponCategory.Ranged:
                    ss.GainXP(SkillType.Dexterity, a * 0.6f);
                    break;
                case WeaponCategory.Unarmed:
                    ss.GainXP(SkillType.Strength, a * 0.3f);
                    break;
            }
        }
    }
}
