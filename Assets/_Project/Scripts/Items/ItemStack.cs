using System;

namespace Project.Items
{
    /// One bundle of a single ItemData in an Inventory. Non-stackable items
    /// always have Quantity = 1 (Inventory enforces this on Add).
    [Serializable]
    public class ItemStack
    {
        public ItemData Def;
        public int Quantity;

        public float TotalWeight => Def != null ? Def.Weight * Quantity : 0f;
        public bool IsEmpty => Def == null || Quantity <= 0;
    }
}
