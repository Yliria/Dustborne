namespace Project.Health
{
    /// Canonical identifiers for the 11 body parts of a humanoid unit.
    /// Order matters for inspector display only; serialized as int —
    /// **append-only**, never re-order existing entries.
    public enum BodyPartId
    {
        Head,
        Torso,
        Abdomen,
        ArmLeft,
        ArmRight,
        LegLeft,
        LegRight,
        // Session 5.5 — hands and feet are first-class severable parts.
        // Severing the parent arm/leg cascades and severs the corresponding
        // hand/foot too (see BodyPartDefinition.SeveredChildren).
        HandLeft,
        HandRight,
        FootLeft,
        FootRight
    }
}
