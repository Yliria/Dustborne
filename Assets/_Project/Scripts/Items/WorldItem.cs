using UnityEngine;

namespace Project.Items
{
    /// A loot pile sitting in the world. Carries the ItemData reference and a
    /// quantity, and is destroyed by PickupOrder once an Inventory absorbs it.
    /// The collider is required so PlayerInputController's raycast can detect
    /// the click on it.
    ///
    /// Tinting (for items without a bespoke WorldPrefab) goes through a cached
    /// MaterialPropertyBlock so we never clone the source material. Awake
    /// applies the tint based on Def.FallbackColor, which means scene-baked
    /// WorldItems (placed by MVPSceneSetup) get coloured at play start without
    /// the editor having to create per-instance material assets.
    [DisallowMultipleComponent]
    public class WorldItem : MonoBehaviour
    {
        public ItemData Def;
        [Min(1)] public int Quantity = 1;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        MaterialPropertyBlock _mpb;

        void Awake()
        {
            ApplyVisualTint();
        }

        /// Applies Def.FallbackColor via MaterialPropertyBlock on every
        /// renderer in this hierarchy. No-op if Def is null or if the item
        /// uses a bespoke WorldPrefab (which is expected to have authored
        /// materials and doesn't need fallback tinting).
        public void ApplyVisualTint()
        {
            if (Def == null) return;
            if (Def.WorldPrefab != null) return;

            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            var renderers = GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, Def.FallbackColor);
                _mpb.SetColor(LegacyColorId, Def.FallbackColor);
                r.SetPropertyBlock(_mpb);
            }
        }

        /// Factory: instantiates a custom WorldPrefab when the ItemData
        /// provides one, otherwise the generic prefab. Tinting goes through
        /// MaterialPropertyBlock — the source material is never cloned.
        public static WorldItem Spawn(ItemData def, int qty, Vector3 position)
        {
            if (def == null)
            {
                Debug.LogWarning("[WorldItem.Spawn] Null ItemData — nothing spawned.");
                return null;
            }

            GameObject go;
            if (def.WorldPrefab != null)
            {
                go = Object.Instantiate(def.WorldPrefab, position, Quaternion.identity);
            }
            else
            {
                if (WorldItemService.GenericPrefab == null)
                {
                    Debug.LogError("[WorldItem.Spawn] No generic prefab registered. Add a WorldItemService on a scene GameObject with the generic prefab assigned.");
                    return null;
                }
                go = Object.Instantiate(WorldItemService.GenericPrefab, position, Quaternion.identity);
            }

            go.name = $"WorldItem_{def.Id}_x{qty}";

            var wi = go.GetComponent<WorldItem>();
            if (wi == null) wi = go.AddComponent<WorldItem>();
            wi.Def = def;
            wi.Quantity = Mathf.Max(1, qty);

            // Awake already ran during Instantiate, so re-tint explicitly with
            // the now-assigned Def.
            wi.ApplyVisualTint();

            return wi;
        }
    }
}
