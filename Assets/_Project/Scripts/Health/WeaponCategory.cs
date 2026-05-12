namespace Project.Health
{
    /// Categorizes how an attack was delivered. Used by the XP system to
    /// distribute attacker XP across Strength / Dexterity. Unarmed is the
    /// default for environmental damage and fists.
    public enum WeaponCategory
    {
        Unarmed,
        Melee,
        MeleeFast,
        Ranged
    }
}
