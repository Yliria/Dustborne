namespace Project.Health
{
    /// Discrete health states for a body part. Transitions are HP-driven
    /// (computed from CurrentHP / EffectiveMaxHP ratio in BodyPartHealth.Recompute).
    /// Severed is terminal for parts where Def.CanBeSevered is true.
    public enum BodyPartState
    {
        Healthy,
        Wounded,
        Broken,
        Severed
    }
}
