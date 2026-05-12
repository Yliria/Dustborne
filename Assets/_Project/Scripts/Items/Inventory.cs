using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Items
{
    /// Per-Unit weight-based inventory. No slots, no grid. The carry capacity
    /// comes from BaseMaxWeight + an externally-pushed BonusMaxWeight
    /// (SkillModifiersBridge pushes the Strength bonus). Going over the cap
    /// is allowed (Kenshi-style); the speed penalty and Strength XP gain
    /// are handled by the bridge by observing OnWeightChanged + IsOverweight.
    ///
    /// Inventory has no references to SkillSystem or HealthSystem.
    [DisallowMultipleComponent]
    public class Inventory : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Carry capacity before any external bonus. Strength adds on top via SetMaxWeightBonus.")]
        [SerializeField, Min(0f)] float baseMaxWeight = 30f;

        [Header("Drop")]
        [Tooltip("Forward distance from the unit where dropped WorldItems spawn.")]
        [SerializeField, Min(0.2f)] float dropForwardDistance = 1.0f;
        [Tooltip("Vertical position of dropped WorldItems (cube sits with its base at ground).")]
        [SerializeField] float dropHeight = 0.15f;
        [Tooltip("If a WorldItem with the same Def is already within this radius of the drop position, the new drop merges into it instead of spawning a duplicate.")]
        [SerializeField, Min(0f)] float dropMergeRadius = 1.5f;

        [SerializeField] List<ItemStack> stacks = new();

        public IReadOnlyList<ItemStack> Stacks => stacks;
        public float BaseMaxWeight => baseMaxWeight;
        public float BonusMaxWeight { get; private set; }
        public float EffectiveMaxWeight => baseMaxWeight + BonusMaxWeight;

        public float CurrentWeight
        {
            get
            {
                float w = 0f;
                for (int i = 0; i < stacks.Count; i++) w += stacks[i].TotalWeight;
                return w;
            }
        }

        public float WeightRatio => EffectiveMaxWeight > 0f ? CurrentWeight / EffectiveMaxWeight : 0f;
        public bool IsOverweight => WeightRatio > 1f;

        public event Action OnInventoryChanged;
        public event Action<float> OnWeightChanged;

        // ---- Bridge hooks ----

        /// Pushed by SkillModifiersBridge when Strength changes. Inventory
        /// itself never reads SkillSystem.
        public void SetMaxWeightBonus(float bonus)
        {
            float clamped = Mathf.Max(0f, bonus);
            if (Mathf.Approximately(clamped, BonusMaxWeight)) return;
            BonusMaxWeight = clamped;
            // Weight didn't change, but the ratio did → fire so the bridge can
            // recompute the speed multiplier.
            OnWeightChanged?.Invoke(CurrentWeight);
        }

        // ---- Public API ----

        /// Returns the number of units actually added. Today this is always
        /// equal to quantity (overweight is allowed); the return is here for
        /// future per-Unit hard caps (small bags, etc.).
        public int Add(ItemData def, int quantity)
        {
            if (def == null || quantity <= 0) return 0;
            int remaining = quantity;

            if (def.Stackable)
            {
                // Fill existing stacks of the same def first.
                for (int i = 0; i < stacks.Count && remaining > 0; i++)
                {
                    var s = stacks[i];
                    if (s.Def != def) continue;
                    int space = def.MaxStackSize - s.Quantity;
                    if (space <= 0) continue;
                    int add = Mathf.Min(space, remaining);
                    s.Quantity += add;
                    remaining -= add;
                }
                // Overflow → new stack(s) at MaxStackSize each.
                while (remaining > 0)
                {
                    int qty = Mathf.Min(def.MaxStackSize, remaining);
                    stacks.Add(new ItemStack { Def = def, Quantity = qty });
                    remaining -= qty;
                }
            }
            else
            {
                // Non-stackable: one entry per unit.
                for (int i = 0; i < quantity; i++)
                {
                    stacks.Add(new ItemStack { Def = def, Quantity = 1 });
                }
                remaining = 0;
            }

            int added = quantity - remaining;
            if (added > 0) RaiseChanged();
            return added;
        }

        public int Remove(ItemData def, int quantity)
        {
            if (def == null || quantity <= 0) return 0;
            int remaining = quantity;
            // Iterate backwards so RemoveAt is safe.
            for (int i = stacks.Count - 1; i >= 0 && remaining > 0; i--)
            {
                var s = stacks[i];
                if (s.Def != def) continue;
                int take = Mathf.Min(s.Quantity, remaining);
                s.Quantity -= take;
                remaining -= take;
                if (s.Quantity <= 0) stacks.RemoveAt(i);
            }
            int removed = quantity - remaining;
            if (removed > 0) RaiseChanged();
            return removed;
        }

        public int CountOf(ItemData def)
        {
            if (def == null) return 0;
            int total = 0;
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].Def == def) total += stacks[i].Quantity;
            }
            return total;
        }

        public bool Has(ItemData def, int quantity = 1) => CountOf(def) >= quantity;

        public void Clear()
        {
            if (stacks.Count == 0) return;
            stacks.Clear();
            RaiseChanged();
        }

        /// Spawns a WorldItem near the unit with the requested quantity. If a
        /// WorldItem with the same Def already sits within dropMergeRadius
        /// of the drop position, the new quantity is merged into it instead
        /// of spawning a duplicate. Note: the visual cube does not display
        /// its quantity, so merges are only readable in the Inspector or via
        /// the debug panel — acceptable for MVP, addressable with a worldspace
        /// label later.
        public int DropStack(int stackIndex, int quantity)
        {
            if (stackIndex < 0 || stackIndex >= stacks.Count) return 0;
            if (quantity <= 0) return 0;

            var stack = stacks[stackIndex];
            int qtyToDrop = Mathf.Min(quantity, stack.Quantity);
            if (qtyToDrop <= 0) return 0;

            Vector3 pos = ComputeDropPosition();
            if (!TryMergeIntoNearbyWorldItem(pos, stack.Def, qtyToDrop))
            {
                WorldItem.Spawn(stack.Def, qtyToDrop, pos);
            }

            stack.Quantity -= qtyToDrop;
            if (stack.Quantity <= 0) stacks.RemoveAt(stackIndex);
            RaiseChanged();
            return qtyToDrop;
        }

        bool TryMergeIntoNearbyWorldItem(Vector3 pos, ItemData def, int qty)
        {
            if (def == null || qty <= 0 || dropMergeRadius <= 0f) return false;
            var hits = Physics.OverlapSphere(pos, dropMergeRadius, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hits.Length; i++)
            {
                var wi = hits[i].GetComponentInParent<WorldItem>();
                if (wi != null && wi.Def == def)
                {
                    wi.Quantity += qty;
                    wi.name = $"WorldItem_{wi.Def.Id}_x{wi.Quantity}";
                    return true;
                }
            }
            return false;
        }

        Vector3 ComputeDropPosition()
        {
            // Slight random offset so stacked drops don't overlap into the
            // same spot. Y is forced to a small positive value so the cube
            // sits on the ground regardless of unit base offset.
            Vector3 pos = transform.position + transform.forward * dropForwardDistance;
            pos += new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), 0f, UnityEngine.Random.Range(-0.3f, 0.3f));
            pos.y = dropHeight;
            return pos;
        }

        void RaiseChanged()
        {
            OnInventoryChanged?.Invoke();
            OnWeightChanged?.Invoke(CurrentWeight);
        }
    }
}
