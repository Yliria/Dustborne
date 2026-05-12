using Project.Skills;
using UnityEngine;

namespace Project.UI
{
    /// Bridge between unit-state events and FloatingTextService. Lives on
    /// the Unit GameObject next to PassiveXPHooks and the bridge. Today it
    /// listens to SkillSystem.OnLevelUp and spawns a gold toast above the
    /// unit's head; the file is named "Toasts" plural because the natural
    /// home for future feedback hooks (OnDeath, OnRevived, OnPartStateChanged,
    /// OnDamageTaken floaters, etc.) is exactly here — same wiring pattern.
    ///
    /// Decoupled from PassiveXPHooks intentionally: that one is about
    /// generating XP, this one is about reflecting state changes back to
    /// the player. Separate concerns, separate components.
    [DisallowMultipleComponent]
    public class UnitFeedbackToasts : MonoBehaviour
    {
        [Tooltip("Vertical offset above the unit's transform where toasts spawn.")]
        [SerializeField, Min(0f)] float headHeight = 2f;

        SkillSystem _skills;

        void Awake()
        {
            _skills = GetComponent<SkillSystem>();
        }

        void OnEnable()
        {
            if (_skills != null) _skills.OnLevelUp += HandleLevelUp;
        }

        void OnDisable()
        {
            if (_skills != null) _skills.OnLevelUp -= HandleLevelUp;
        }

        void HandleLevelUp(SkillType type, int oldLevel, int newLevel)
        {
            FloatingTextService.SpawnLevelUp(
                type.ToString(),
                newLevel,
                transform.position + Vector3.up * headHeight);
        }
    }
}
