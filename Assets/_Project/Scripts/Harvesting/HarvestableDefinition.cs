using System.Collections.Generic;
using Project.Items;
using UnityEngine;

namespace Project.Harvesting
{
    /// Authoritative tuning data for one harvestable archetype (oak tree,
    /// basic rock, fishing spot, etc.). Hand-authored in the inspector or
    /// seeded by MVPSceneSetup. Hand off to a Harvestable MonoBehaviour
    /// instance via its Def field — that runtime component never duplicates
    /// these numbers in code.
    [CreateAssetMenu(menuName = "Project/Harvesting/Harvestable Definition", fileName = "HV_New")]
    public class HarvestableDefinition : ScriptableObject
    {
        [Header("Identity")]
        public HarvestableType Type = HarvestableType.Tree;
        public string DisplayName = "Oak Tree";

        [Header("Durability")]
        [Tooltip("Hit points before the node depletes and drops its loot.")]
        [Min(1f)] public float MaxHealth = 100f;

        [Header("Tool requirement")]
        [Tooltip("If set, HarvestOrder fails unless the unit's Inventory has this item.")]
        public ItemData RequiredTool;

        [Header("Pace")]
        [Tooltip("HP points removed from the node per second when a unit is harvesting at Labour=1. Skill multiplier scales this.")]
        [Min(0f)] public float BaseHarvestSpeed = 10f;

        [Tooltip("Maximum XZ distance at which the unit can start chipping at the node.")]
        [Min(0.1f)] public float InteractionRange = 1.5f;

        [Header("Loot table (rolled OnDepleted)")]
        public List<HarvestableDrop> Drops = new();

        [Header("Visuals")]
        [Tooltip("Optional bespoke visual. If null, Harvestable spawns a typed fallback (cylinder for Tree, sphere for Rock, etc.).")]
        public GameObject VisualPrefab;

#if UNITY_EDITOR
        void OnValidate()
        {
            for (int i = 0; i < Drops.Count; i++) Drops[i]?.OnValidate();
        }
#endif
    }
}
