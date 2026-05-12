using UnityEngine;

namespace Project.Items
{
    /// Authoritative definition for one item. Shared by every system that
    /// references this item (Inventory stacks, WorldItem ground entities,
    /// future recipe inputs/outputs, equipment slots). Hand-authored via the
    /// "Create > Project/Items/Item Data" menu or seeded by MVPSceneSetup.
    [CreateAssetMenu(menuName = "Project/Items/Item Data", fileName = "Item_New")]
    public class ItemData : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable string identifier used by save/load and recipes. Must be unique across the ItemDatabase. Lowercase + underscores.")]
        public string Id = "new_item";
        public string DisplayName = "New Item";
        [TextArea] public string Description = "";
        public Sprite Icon;

        [Header("Classification")]
        public ItemType Type = ItemType.Misc;

        [Header("Physical")]
        [Tooltip("Weight per unit, in kilograms.")]
        [Min(0f)] public float Weight = 1f;

        [Header("Stacking")]
        public bool Stackable = true;
        [Min(1)] public int MaxStackSize = 50;

        [Header("World presentation")]
        [Tooltip("Optional bespoke prefab used when this item drops into the world. If null, WorldItem.Spawn falls back to the generic prefab tinted with FallbackColor.")]
        public GameObject WorldPrefab;
        [Tooltip("Tint applied to the generic world prefab when WorldPrefab is null.")]
        public Color FallbackColor = new(0.7f, 0.7f, 0.7f);

#if UNITY_EDITOR
        void OnValidate()
        {
            // Enforce the stackable/MaxStackSize invariant so authoring
            // mistakes don't leak into runtime branching.
            if (!Stackable) MaxStackSize = 1;
            if (MaxStackSize < 1) MaxStackSize = 1;
            if (Weight < 0f) Weight = 0f;
        }
#endif
    }
}
