namespace Project.Items
{
    /// Coarse categorization used for filtering, hauling priorities, AI hints,
    /// and future equipment slot validation. Order is stable — never re-order,
    /// only append (serialized as int in ItemData).
    public enum ItemType
    {
        Resource,
        Tool,
        Weapon,
        Consumable,
        Misc
    }
}
