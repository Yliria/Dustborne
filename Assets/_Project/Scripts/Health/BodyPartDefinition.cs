using UnityEngine;

namespace Project.Health
{
    /// Authoritative configuration for one body part. Hand-tuned per part
    /// via Inspector and shared across all Units (humans, monsters, NPCs)
    /// that have this anatomy. Runtime state lives in BodyPartHealth which
    /// references this asset.
    [CreateAssetMenu(menuName = "Project/Health/Body Part Definition", fileName = "BodyPart_New")]
    public class BodyPartDefinition : ScriptableObject
    {
        [Header("Identity")]
        public BodyPartId Id = BodyPartId.Torso;
        public string DisplayName = "Torso";

        [Header("Classification")]
        [Tooltip("If true, HP <= 0 on this part means death.")]
        public bool IsVital = false;
        [Tooltip("If true, HP <= 0 puts the part in Severed state instead of clamping to Broken.")]
        public bool CanBeSevered = false;

        [Header("HP")]
        [Min(1f)] public float BaseMaxHP = 100f;

        [Header("State thresholds (HP ratio, 0..1)")]
        [Tooltip("Below this ratio, the part is Wounded.")]
        [Range(0f, 1f)] public float WoundedThreshold = 0.7f;
        [Tooltip("Below this ratio, the part is Broken.")]
        [Range(0f, 1f)] public float BrokenThreshold = 0.25f;

        [Header("Bleeding")]
        [Tooltip("Below this HP ratio, the part starts bleeding (unless bandaged).")]
        [Range(0f, 1f)] public float BleedingHPThreshold = 0.5f;
        [Tooltip("Blood drained per second while Wounded.")]
        [Min(0f)] public float BleedRateWounded = 0.5f;
        [Tooltip("Blood drained per second while Broken.")]
        [Min(0f)] public float BleedRateBroken = 1.5f;
        [Tooltip("Blood drained per second while Severed. Only used if CanBeSevered.")]
        [Min(0f)] public float BleedRateSevered = 3f;

        [Header("Locomotion penalties (additive, clamped to [0,1])")]
        [Tooltip("Speed multiplier subtracted when this part is Broken.")]
        [Range(0f, 1f)] public float MoveSpeedPenaltyIfBroken = 0f;
        [Tooltip("Speed multiplier subtracted when this part is Severed.")]
        [Range(0f, 1f)] public float MoveSpeedPenaltyIfSevered = 0f;
    }
}
