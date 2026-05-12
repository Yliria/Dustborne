using Project.Health;

namespace Project.DebugUI
{
    /// Shared state for the "click on a body part to damage it" debug mode.
    /// Written by the HealthSkillsDebugPanel UI (toggle, slider, dropdowns)
    /// and read by PlayerInputController to decide whether the next click
    /// should fire a HitZone hit or fall through to the normal MoveOrder /
    /// PickupOrder / HarvestOrder pipeline.
    ///
    /// Static because there's only ever one local player and the state is
    /// pure debug. If the project grows to multi-controller / split-screen
    /// the pattern will need a per-controller instance.
    public static class DamageInputMode
    {
        public static bool IsActive;
        public static float DamageAmount = 20f;
        public static DamageType Type = DamageType.Blunt;
        public static WeaponCategory Weapon = WeaponCategory.Unarmed;
    }
}
