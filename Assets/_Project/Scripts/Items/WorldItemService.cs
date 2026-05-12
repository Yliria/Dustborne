using UnityEngine;

namespace Project.Items
{
    /// Tiny bootstrap that exposes the generic WorldItem prefab to the
    /// static WorldItem.Spawn helper. Lives on the GameSystems GameObject
    /// alongside GameTimeService — one per scene.
    [DefaultExecutionOrder(-900)]
    public class WorldItemService : MonoBehaviour
    {
        [SerializeField] GameObject genericPrefab;

        public static GameObject GenericPrefab { get; private set; }

        void Awake()
        {
            GenericPrefab = genericPrefab;
            if (GenericPrefab == null)
            {
                Debug.LogWarning("[WorldItemService] No generic prefab assigned — WorldItem.Spawn will fail for items without a custom WorldPrefab.");
            }
        }

        void OnDestroy()
        {
            // Don't clear the static if a duplicate replaced us already.
            if (GenericPrefab == genericPrefab) GenericPrefab = null;
        }
    }
}
