using System;
using UnityEngine;

namespace Project.Health
{
    /// Global blood pool for a Unit. Drained continuously by bleeding body
    /// parts; reaching zero fires OnBloodDepleted (HealthSystem listens and
    /// kills the unit). Not a MonoBehaviour — owned by HealthSystem.
    [Serializable]
    public class BloodSystem
    {
        [Min(1f)] public float BaseMaxBlood = 100f;
        public float CurrentBlood;

        [SerializeField] float _vitalityMultiplier = 1f;

        public float EffectiveMaxBlood => BaseMaxBlood * _vitalityMultiplier;
        public float Ratio => EffectiveMaxBlood > 0f ? Mathf.Clamp01(CurrentBlood / EffectiveMaxBlood) : 0f;

        public event Action<float, float> OnBloodChanged;
        public event Action OnBloodDepleted;

        /// Sets the multiplier and fills the pool to its new effective max.
        /// Use this at start-of-life or after a full heal. To change capacity
        /// without resetting current (e.g. on Vitality level up), prefer
        /// SetVitalityMultiplier + manual rescale.
        public void Initialize(float vitalityMultiplier)
        {
            _vitalityMultiplier = Mathf.Max(0.01f, vitalityMultiplier);
            CurrentBlood = EffectiveMaxBlood;
            OnBloodChanged?.Invoke(CurrentBlood, EffectiveMaxBlood);
        }

        /// Updates only the capacity multiplier. CurrentBlood is preserved
        /// (and clamped to the new max). The caller is responsible for any
        /// ratio-preserving rescale before/after this call.
        public void SetVitalityMultiplier(float vitalityMultiplier)
        {
            _vitalityMultiplier = Mathf.Max(0.01f, vitalityMultiplier);
            if (CurrentBlood > EffectiveMaxBlood) CurrentBlood = EffectiveMaxBlood;
            OnBloodChanged?.Invoke(CurrentBlood, EffectiveMaxBlood);
        }

        public void Drain(float amount)
        {
            if (amount <= 0f || CurrentBlood <= 0f) return;
            float prev = CurrentBlood;
            CurrentBlood = Mathf.Max(0f, CurrentBlood - amount);
            if (!Mathf.Approximately(prev, CurrentBlood)) OnBloodChanged?.Invoke(CurrentBlood, EffectiveMaxBlood);
            if (CurrentBlood <= 0f && prev > 0f) OnBloodDepleted?.Invoke();
        }

        public void Restore(float amount)
        {
            if (amount <= 0f) return;
            float prev = CurrentBlood;
            CurrentBlood = Mathf.Min(EffectiveMaxBlood, CurrentBlood + amount);
            if (!Mathf.Approximately(prev, CurrentBlood)) OnBloodChanged?.Invoke(CurrentBlood, EffectiveMaxBlood);
        }
    }
}
