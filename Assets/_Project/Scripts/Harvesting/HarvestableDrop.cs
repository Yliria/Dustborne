using System;
using Project.Items;
using UnityEngine;

namespace Project.Harvesting
{
    /// One row of a harvest loot table. Quantity is rolled uniformly in
    /// [MinQuantity, MaxQuantity] and gated by Chance (0..1) — chance=1 means
    /// the row always fires when the node is depleted.
    [Serializable]
    public class HarvestableDrop
    {
        public ItemData Item;
        [Min(1)] public int MinQuantity = 1;
        [Min(1)] public int MaxQuantity = 1;
        [Range(0f, 1f)] public float Chance = 1f;

#if UNITY_EDITOR
        public void OnValidate()
        {
            if (MaxQuantity < MinQuantity) MaxQuantity = MinQuantity;
        }
#endif
    }
}
