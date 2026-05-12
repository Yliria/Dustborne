using UnityEngine;

namespace Project.Skills
{
    /// Shared XP scaling for all skills. Two curves: required XP per level
    /// (steeper at high levels) and a gain multiplier (diminishing returns).
    /// Hand-tuned in the inspector via AnimationCurve.
    [CreateAssetMenu(menuName = "Project/Skills/XP Curve", fileName = "XPCurve_New")]
    public class XPCurve : ScriptableObject
    {
        [Tooltip("Input: current level (float). Output: XP required to reach the next level.")]
        public AnimationCurve XPRequiredPerLevel = AnimationCurve.Linear(1f, 50f, 100f, 50000f);

        [Tooltip("Input: current level. Output: multiplier applied to raw XP gains. Use to model diminishing returns.")]
        public AnimationCurve GainMultiplierByLevel = AnimationCurve.Linear(1f, 1f, 100f, 0.1f);

        public float GetXPForNext(float currentLevel)
        {
            return XPRequiredPerLevel != null ? Mathf.Max(1f, XPRequiredPerLevel.Evaluate(currentLevel)) : 100f;
        }

        public float GetGainMultiplier(float currentLevel)
        {
            return GainMultiplierByLevel != null ? Mathf.Max(0f, GainMultiplierByLevel.Evaluate(currentLevel)) : 1f;
        }
    }
}
