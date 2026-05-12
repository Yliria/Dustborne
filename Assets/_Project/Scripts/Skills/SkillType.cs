namespace Project.Skills
{
    /// The five trainable skills. Order matches the canonical Kenshi-style
    /// progression model: physical capability, durability, agility, finesse,
    /// economy. Values are stable identifiers — never re-order, only append.
    public enum SkillType
    {
        Strength,
        Vitality,
        Speed,
        Dexterity,
        Labour
    }
}
