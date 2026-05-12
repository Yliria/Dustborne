namespace Project.Harvesting
{
    /// Broad category for a harvest node. Drives the fallback visual when no
    /// VisualPrefab is provided, and is exposed for future UI/AI heuristics
    /// (the lumberjack prefers Trees, the miner prefers Rocks, etc.).
    /// Append-only — order is stable.
    public enum HarvestableType
    {
        Tree,
        Rock,
        FishingSpot,
        Bush,
        Ore,
        Other
    }
}
