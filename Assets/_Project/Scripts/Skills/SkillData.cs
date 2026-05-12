using System;
using UnityEngine;

namespace Project.Skills
{
    /// Per-skill runtime state. Level is stored as float so that internal
    /// progression (XP towards next, partial training) is visible to systems
    /// that want finer-grained gating; the public integer level is just the
    /// floor and is what UI displays.
    [Serializable]
    public class SkillData
    {
        public SkillType Type;
        [Min(1f)] public float Level = 1f;
        [Min(0f)] public float XPCurrent;

        public int LevelInt => Mathf.FloorToInt(Level);
    }
}
