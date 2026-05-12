using System;
using System.Collections;
using Project.Items;
using UnityEngine;
using UnityEngine.AI;

namespace Project.Harvesting
{
    /// A node sitting in the world that HarvestOrder chips at. Owns HP and
    /// drives the depletion → drop pipeline. Pure data + state — the work
    /// (movement, tool check, damage tick) lives in HarvestOrder.
    ///
    /// Auto-adds a NavMeshObstacle with carving=true on Awake so units route
    /// around the node and stop at its boundary. The fallback visual is
    /// instantiated as a child only if no child mesh already exists, so
    /// editor-time pre-built nodes keep their authored visuals.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class Harvestable : MonoBehaviour
    {
        public HarvestableDefinition Def;
        public float CurrentHealth;

        public bool IsDepleted => CurrentHealth <= 0f;
        public event Action<Harvestable> OnDepleted;

        bool _depletedFired;

        void Awake()
        {
            if (Def != null) CurrentHealth = Def.MaxHealth;

            EnsureNavMeshObstacle();
            EnsureFallbackVisual();
        }

        void EnsureNavMeshObstacle()
        {
            var obstacle = GetComponent<NavMeshObstacle>();
            if (obstacle == null) obstacle = gameObject.AddComponent<NavMeshObstacle>();
            obstacle.carving = true;
            obstacle.shape = NavMeshObstacleShape.Box;

            // Default size from the collider's bounds so pathing matches the
            // visual footprint regardless of definition.
            var col = GetComponent<Collider>();
            if (col != null)
            {
                var ext = col.bounds.extents * 2f;
                if (ext.sqrMagnitude > 0.001f) obstacle.size = ext;
            }
        }

        void EnsureFallbackVisual()
        {
            // Only inject a fallback if the GameObject has no child visuals.
            if (transform.childCount > 0) return;
            if (Def == null) return;

            GameObject visual;
            Color color;
            if (Def.VisualPrefab != null)
            {
                visual = Instantiate(Def.VisualPrefab, transform);
                return;
            }

            switch (Def.Type)
            {
                case HarvestableType.Tree:
                    visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    visual.name = "Visual_Tree";
                    visual.transform.SetParent(transform, false);
                    visual.transform.localScale = new Vector3(0.5f, 1.5f, 0.5f); // ~3m tall trunk
                    visual.transform.localPosition = new Vector3(0f, 1.5f, 0f);
                    color = new Color(0.40f, 0.26f, 0.13f);
                    break;
                case HarvestableType.Rock:
                case HarvestableType.Ore:
                    visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    visual.name = "Visual_Rock";
                    visual.transform.SetParent(transform, false);
                    visual.transform.localScale = Vector3.one * 1.2f;
                    visual.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                    color = new Color(0.50f, 0.50f, 0.55f);
                    break;
                case HarvestableType.FishingSpot:
                    visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    visual.name = "Visual_FishingSpot";
                    visual.transform.SetParent(transform, false);
                    visual.transform.localScale = new Vector3(1.5f, 0.1f, 1.5f);
                    visual.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                    color = new Color(0.25f, 0.55f, 0.85f);
                    break;
                case HarvestableType.Bush:
                    visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    visual.name = "Visual_Bush";
                    visual.transform.SetParent(transform, false);
                    visual.transform.localScale = Vector3.one * 0.8f;
                    visual.transform.localPosition = new Vector3(0f, 0.4f, 0f);
                    color = new Color(0.20f, 0.45f, 0.20f);
                    break;
                default:
                    visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    visual.name = "Visual_Other";
                    visual.transform.SetParent(transform, false);
                    visual.transform.localScale = Vector3.one;
                    visual.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                    color = Color.gray;
                    break;
            }

            // Strip the collider from the visual primitive — the Harvestable
            // root owns the click/raycast collider sized appropriately.
            var visualCol = visual.GetComponent<Collider>();
            if (visualCol != null) Destroy(visualCol);

            // Tint via MaterialPropertyBlock to avoid Material clones.
            var renderer = visual.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mpb = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(mpb);
                mpb.SetColor(Shader.PropertyToID("_BaseColor"), color);
                mpb.SetColor(Shader.PropertyToID("_Color"), color);
                renderer.SetPropertyBlock(mpb);
            }
        }

        public void ApplyDamage(float amount)
        {
            if (IsDepleted) return;
            if (Def == null) return;
            CurrentHealth = Mathf.Max(0f, CurrentHealth - Mathf.Max(0f, amount));
            if (CurrentHealth <= 0f && !_depletedFired)
            {
                _depletedFired = true;
                GenerateDrops(transform.position);
                OnDepleted?.Invoke(this);
                StartCoroutine(DestroyAfter(0.1f));
            }
        }

        IEnumerator DestroyAfter(float delay)
        {
            // Wait one tick so HarvestOrder.Tick sees IsDepleted and Completes
            // cleanly before the GameObject is gone.
            yield return new WaitForSeconds(delay);
            if (this != null) Destroy(gameObject);
        }

        /// Rolls every entry in Def.Drops independently and spawns scattered
        /// WorldItems around the drop origin. Each unit produced by the roll
        /// becomes its own pile (qty = 1 per WorldItem) so the player visibly
        /// sees the loot fan out around the felled node. The pickup pipeline
        /// then handles per-pile retrieval. Public so tests / future events
        /// can fire it directly.
        public void GenerateDrops(Vector3 dropOrigin)
        {
            if (Def == null) return;
            for (int i = 0; i < Def.Drops.Count; i++)
            {
                var d = Def.Drops[i];
                if (d == null || d.Item == null) continue;
                if (UnityEngine.Random.value > d.Chance) continue;

                int min = Mathf.Max(1, d.MinQuantity);
                int max = Mathf.Max(min, d.MaxQuantity);
                int qty = UnityEngine.Random.Range(min, max + 1);
                if (qty <= 0) continue;

                for (int u = 0; u < qty; u++)
                {
                    Vector2 offset2D = UnityEngine.Random.insideUnitCircle * 0.7f;
                    Vector3 pos = dropOrigin + new Vector3(offset2D.x, 0f, offset2D.y);
                    pos.y = 0.15f; // sit on the ground
                    WorldItem.Spawn(d.Item, 1, pos);
                }
            }
        }

        /// Used by the debug panel "Reset all" button.
        public void DebugRestoreToFull()
        {
            if (Def == null) return;
            CurrentHealth = Def.MaxHealth;
            _depletedFired = false;
        }
    }
}
