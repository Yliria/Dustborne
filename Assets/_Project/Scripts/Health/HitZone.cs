using UnityEngine;

namespace Project.Health
{
    /// Marker on a renderable body-part GameObject. Tells the world which
    /// BodyPartId was hit when a Collider belonging to this hierarchy is
    /// returned by a physics query, and routes the damage to the parent
    /// Unit's HealthSystem with the right TargetPart set.
    ///
    /// HitZone owns NO gameplay logic — it doesn't roll damage, doesn't
    /// inspect weapons, doesn't read inventory. It's a pure adapter
    /// between "Collider hit" and "HealthSystem.ApplyDamage(DamageInfo)".
    /// Combat systems (AttackOrder, projectiles, debug click-to-damage)
    /// build the DamageInfo and call TakeHit.
    [RequireComponent(typeof(Collider))]
    public class HitZone : MonoBehaviour
    {
        [SerializeField] BodyPartId partId;
        [SerializeField] HealthSystem health;

        public BodyPartId PartId => partId;
        public HealthSystem Health => health;

        // Editor setters used by MVPSceneSetup to wire freshly-created
        // segments at build time. SerializeField makes the values survive
        // domain reloads / prefab saves.
        public void SetPart(BodyPartId id) => partId = id;
        public void SetHealthSystem(HealthSystem hs) => health = hs;

        void Reset()
        {
            health = GetComponentInParent<HealthSystem>();
        }

        void Awake()
        {
            if (health == null) health = GetComponentInParent<HealthSystem>();
        }

        /// Forces info.TargetPart to this zone's BodyPartId, then forwards
        /// to HealthSystem.ApplyDamage. Safe to call with a freshly-built
        /// DamageInfo (TargetPart can be any default — it gets overridden).
        public void TakeHit(DamageInfo info)
        {
            if (health == null)
            {
                Debug.LogWarning($"[HitZone] '{name}' has no HealthSystem — hit ignored.");
                return;
            }
            info.TargetPart = partId;
            health.ApplyDamage(info);
        }

        /// Convenience: resolve the HitZone from any Collider hit by a
        /// raycast/overlap. Walks the parent chain (children primitives
        /// keep their colliders under the HitZone-bearing GameObject).
        public static HitZone FromCollider(Collider col)
        {
            if (col == null) return null;
            return col.GetComponentInParent<HitZone>();
        }
    }
}
