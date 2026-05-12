using System;
using UnityEngine;

namespace Project.Health
{
    /// Runtime state of one body part. References a BodyPartDefinition for
    /// its tuning data and tracks HP, derived state, and bleeding. Not a
    /// MonoBehaviour — HealthSystem owns a List of these.
    [Serializable]
    public class BodyPartHealth
    {
        public BodyPartDefinition Def;
        public float CurrentHP;
        public BodyPartState State;
        public bool IsBleeding;
        public float CurrentBleedRate;

        [Tooltip("Latched by Bandage(). Cleared automatically when the part returns to Healthy.")]
        public bool IsBandaged;

        [SerializeField] float _vitalityMultiplier = 1f;

        public float EffectiveMaxHP => Def != null ? Def.BaseMaxHP * _vitalityMultiplier : 0f;
        public float HPRatio => EffectiveMaxHP > 0f ? Mathf.Clamp01(CurrentHP / EffectiveMaxHP) : 0f;

        /// Updates the cached vitality multiplier and refreshes State + IsBleeding
        /// from the current HP value. Does not mutate CurrentHP — callers that
        /// want to preserve ratios after a multiplier change must rescale HP
        /// themselves before calling this.
        public void Recompute(float vitalityMultiplier)
        {
            _vitalityMultiplier = Mathf.Max(0.01f, vitalityMultiplier);
            if (Def == null)
            {
                State = BodyPartState.Healthy;
                IsBleeding = false;
                CurrentBleedRate = 0f;
                return;
            }

            float maxHP = EffectiveMaxHP;
            float ratio = maxHP > 0f ? CurrentHP / maxHP : 0f;

            // State: HP=0 + severable goes terminal; otherwise the threshold ladder.
            if (CurrentHP <= 0f && Def.CanBeSevered)
            {
                State = BodyPartState.Severed;
            }
            else if (ratio <= Def.BrokenThreshold)
            {
                State = BodyPartState.Broken;
            }
            else if (ratio <= Def.WoundedThreshold)
            {
                State = BodyPartState.Wounded;
            }
            else
            {
                State = BodyPartState.Healthy;
            }

            // Cleared bandage when the part fully recovers (so re-injury will
            // re-bleed naturally without manual intervention).
            if (State == BodyPartState.Healthy) IsBandaged = false;

            // Bleeding: HP below threshold AND not currently bandaged.
            bool eligibleToBleed = !IsBandaged && ratio <= Def.BleedingHPThreshold && State != BodyPartState.Healthy;
            if (eligibleToBleed)
            {
                IsBleeding = true;
                CurrentBleedRate = State switch
                {
                    BodyPartState.Wounded => Def.BleedRateWounded,
                    BodyPartState.Broken => Def.BleedRateBroken,
                    BodyPartState.Severed => Def.BleedRateSevered,
                    _ => 0f
                };
            }
            else
            {
                IsBleeding = false;
                CurrentBleedRate = 0f;
            }
        }

        /// Apply a bandage: stops bleeding until the part takes more damage or
        /// fully recovers. The flag is reset when the part returns to Healthy.
        public void Bandage()
        {
            IsBandaged = true;
            IsBleeding = false;
            CurrentBleedRate = 0f;
        }

        /// Called by HealthSystem before applying new damage so that fresh
        /// wounds re-enable bleeding even on previously bandaged parts.
        public void NotifyDamageIncoming()
        {
            IsBandaged = false;
        }
    }
}
