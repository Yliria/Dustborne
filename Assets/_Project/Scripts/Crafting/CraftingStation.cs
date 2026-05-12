using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Project.Crafting
{
    /// A static placeable that CraftOrder uses as an anchor for workbench-
    /// style recipes. Auto-registers itself in a static list on enable so
    /// FindNearest is a single sweep — fine for the handful of stations we
    /// expect to have in scene.
    ///
    /// Auto-adds a NavMeshObstacle (carving = true) on Awake so units route
    /// around it. The InteractionPoint defines where the unit lands when
    /// crafting; falls back to a fixed offset in front of the station if
    /// left unassigned.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class CraftingStation : MonoBehaviour
    {
        [Header("Identity")]
        public CraftStationType Type = CraftStationType.Workbench;
        public string DisplayName = "Workbench";

        [Header("Interaction")]
        [Tooltip("Optional. World-space anchor where units stop to craft. If null, falls back to (station position − transform.forward × 1.0f).")]
        public Transform InteractionPoint;
        [Tooltip("Maximum XZ distance from the InteractionPoint at which crafting can start.")]
        [Min(0.1f)] public float InteractionRange = 1.5f;

        // Registry — a CraftingStation appears here while enabled.
        static readonly List<CraftingStation> _active = new();
        public static IReadOnlyList<CraftingStation> ActiveStations => _active;

        void OnEnable() { if (!_active.Contains(this)) _active.Add(this); }
        void OnDisable() { _active.Remove(this); }

        void Awake()
        {
            EnsureNavMeshObstacle();
        }

        void EnsureNavMeshObstacle()
        {
            var obstacle = GetComponent<NavMeshObstacle>();
            if (obstacle == null) obstacle = gameObject.AddComponent<NavMeshObstacle>();
            obstacle.carving = true;
            obstacle.shape = NavMeshObstacleShape.Box;
            var col = GetComponent<Collider>();
            if (col != null)
            {
                var ext = col.bounds.extents * 2f;
                if (ext.sqrMagnitude > 0.001f) obstacle.size = ext;
            }
        }

        public Vector3 GetInteractionPosition()
        {
            if (InteractionPoint != null) return InteractionPoint.position;
            // Default: a metre in front of the station along its forward.
            return transform.position - transform.forward * 1.0f;
        }

        /// Linear sweep of the registry for the closest active station of
        /// the requested type. Returns null if none match. O(n) on the
        /// number of stations — negligible for the scale we plan for.
        public static CraftingStation FindNearest(Vector3 origin, CraftStationType type)
        {
            CraftingStation best = null;
            float bestSqr = float.PositiveInfinity;
            for (int i = 0; i < _active.Count; i++)
            {
                var s = _active[i];
                if (s == null || s.Type != type) continue;
                float sqr = (s.transform.position - origin).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = s;
                }
            }
            return best;
        }

        /// True if there is at least one active station of the requested type
        /// somewhere in the scene. Used by the debug panel to grey out
        /// recipes whose station has been destroyed.
        public static bool AnyAvailable(CraftStationType type)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i] != null && _active[i].Type == type) return true;
            }
            return false;
        }
    }
}
