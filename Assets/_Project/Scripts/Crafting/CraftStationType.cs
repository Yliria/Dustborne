namespace Project.Crafting
{
    /// Categorizes a CraftingStation so recipes can declare what surface they
    /// need (Workbench MVP today, Forge for metalwork later, etc.). Stable —
    /// only append, never re-order. Serialized as int.
    public enum CraftStationType
    {
        Workbench,
        Forge
    }
}
