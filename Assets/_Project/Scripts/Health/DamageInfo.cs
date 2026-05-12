using Project.Units;

namespace Project.Health
{
    /// Carries one damage event from emitter to receiver. A value type so
    /// it can be passed cheaply through events without GC. Attacker is null
    /// for environmental damage (falls, fire, traps, debug button presses).
    public struct DamageInfo
    {
        public float Amount;
        public DamageType Type;
        public BodyPartId TargetPart;
        public Unit Attacker;
        public WeaponCategory Weapon;

        public static DamageInfo Environmental(float amount, DamageType type, BodyPartId part)
            => new DamageInfo { Amount = amount, Type = type, TargetPart = part, Attacker = null, Weapon = WeaponCategory.Unarmed };
    }
}
