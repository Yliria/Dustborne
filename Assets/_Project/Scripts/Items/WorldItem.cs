using UnityEngine;

namespace Project.Items
{
    /// A loot pile sitting in the world. Carries the ItemData reference and a
    /// quantity, and is destroyed by PickupOrder once an Inventory absorbs it.
    /// The collider is required so PlayerInputController's raycast can detect
    /// the click on it.
    [DisallowMultipleComponent]
    public class WorldItem : MonoBehaviour
    {
        public ItemData Def;
        [Min(1)] public int Quantity = 1;

        /// Factory: instantiates a custom WorldPrefab when the ItemData
        /// provides one, otherwise the generic prefab tinted with FallbackColor.
        /// Returns the WorldItem component on the spawned GameObject.
        public static WorldItem Spawn(ItemData def, int qty, Vector3 position)
        {
            if (def == null)
            {
                Debug.LogWarning("[WorldItem.Spawn] Null ItemData — nothing spawned.");
                return null;
            }

            GameObject go;
            bool tintFromFallback = false;
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
                tintFromFallback = true;
            }

            go.name = $"WorldItem_{def.Id}_x{qty}";

            var wi = go.GetComponent<WorldItem>();
            if (wi == null) wi = go.AddComponent<WorldItem>();
            wi.Def = def;
            wi.Quantity = Mathf.Max(1, qty);

            if (tintFromFallback) ApplyFallbackTint(go, def.FallbackColor);

            return wi;
        }

        static void ApplyFallbackTint(GameObject go, Color color)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                // Use a per-instance material so the shared asset isn't
                // permanently recoloured.
                var mat = new Material(r.sharedMaterial);
                mat.color = color;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                r.sharedMaterial = mat;
            }
        }
    }
}
