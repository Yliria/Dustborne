using System;
using System.Collections.Generic;
using Project.Core;
using Project.Units;
using UnityEngine;

namespace Project.Health
{
    /// Owns a Unit's body part HP, blood pool, and damage pipeline. Knows
    /// nothing about skills — Vitality multipliers and XP grants are pushed
    /// in from outside (SkillModifiersBridge) via Recompute / event handlers.
    [DisallowMultipleComponent]
    public class HealthSystem : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Order is preserved in the runtime Parts list. Assign one SO per BodyPartId.")]
        [SerializeField] List<BodyPartDefinition> partDefinitions = new();

        [Tooltip("Base maximum blood before Vitality multiplier.")]
        [SerializeField, Min(1f)] float baseMaxBlood = 100f;

        [Header("Regeneration")]
        [Tooltip("HP regenerated per second, per non-Severed part. Scaled by GameTime.DeltaTime.")]
        [SerializeField, Min(0f)] float baseRegenPerSecond = 0.1f;
        [Tooltip("External multiplier for regen — set this from items, skills, etc.")]
        public float RecoveryMultiplier = 1f;

        public List<BodyPartHealth> Parts { get; private set; } = new();
        public BloodSystem Blood { get; private set; } = new();

        public bool IsDead { get; private set; }

        /// Current vitality multiplier. Set by SkillModifiersBridge when
        /// Vitality levels up; HealthSystem itself only reads it.
        public float VitalityMultiplier { get; private set; } = 1f;

        public event Action<DamageInfo> OnDamageTaken;
        public event Action<BodyPartId, BodyPartState, BodyPartState> OnPartStateChanged;
        public event Action OnDeath;
        public event Action OnRevived;

        Unit _unit;

        void Awake()
        {
            _unit = GetComponent<Unit>();
            BuildParts();
            Blood.BaseMaxBlood = baseMaxBlood;
            Blood.Initialize(VitalityMultiplier);
            Blood.OnBloodDepleted += HandleBloodDepleted;
        }

        void OnDestroy()
        {
            if (Blood != null) Blood.OnBloodDepleted -= HandleBloodDepleted;
        }

        void BuildParts()
        {
            Parts.Clear();
            foreach (var def in partDefinitions)
            {
                if (def == null) continue;
                var bph = new BodyPartHealth
                {
                    Def = def,
                    CurrentHP = def.BaseMaxHP
                };
                bph.Recompute(VitalityMultiplier);
                Parts.Add(bph);
            }
        }

        void Update()
        {
            if (IsDead) return;
            float dt = GameTime.DeltaTime;
            if (dt <= 0f) return;

            TickBleeding(dt);
            TickRegen(dt);
        }

        void TickBleeding(float dt)
        {
            float totalBleed = 0f;
            for (int i = 0; i < Parts.Count; i++)
            {
                var p = Parts[i];
                if (p.IsBleeding) totalBleed += p.CurrentBleedRate;
            }
            if (totalBleed > 0f) Blood.Drain(totalBleed * dt);
        }

        void TickRegen(float dt)
        {
            if (baseRegenPerSecond <= 0f || RecoveryMultiplier <= 0f) return;
            float regen = baseRegenPerSecond * RecoveryMultiplier * dt;

            for (int i = 0; i < Parts.Count; i++)
            {
                var p = Parts[i];
                if (p.State == BodyPartState.Severed) continue;
                if (p.CurrentHP >= p.EffectiveMaxHP) continue;

                var oldState = p.State;
                p.CurrentHP = Mathf.Min(p.EffectiveMaxHP, p.CurrentHP + regen);
                p.Recompute(VitalityMultiplier);
                if (oldState != p.State) OnPartStateChanged?.Invoke(p.Def.Id, oldState, p.State);
            }
        }

        // ---- Public API ----

        public BodyPartHealth GetPart(BodyPartId id)
        {
            for (int i = 0; i < Parts.Count; i++)
            {
                if (Parts[i].Def != null && Parts[i].Def.Id == id) return Parts[i];
            }
            return null;
        }

        public void ApplyDamage(DamageInfo info)
        {
            if (IsDead) return;
            var part = GetPart(info.TargetPart);
            if (part == null) return;
            if (part.State == BodyPartState.Severed) return; // already gone

            part.NotifyDamageIncoming();
            var oldState = part.State;
            part.CurrentHP = Mathf.Max(0f, part.CurrentHP - Mathf.Max(0f, info.Amount));
            part.Recompute(VitalityMultiplier);

            if (oldState != part.State)
            {
                OnPartStateChanged?.Invoke(part.Def.Id, oldState, part.State);
                // Anatomical cascade: severing an arm tears off the hand, etc.
                if (part.State == BodyPartState.Severed) CascadeSevered(part);
            }

            OnDamageTaken?.Invoke(info);

            if (part.Def.IsVital && part.CurrentHP <= 0f)
            {
                SetDead();
            }
        }

        /// Walks Def.SeveredChildren and forces each living child to Severed.
        /// Recursive — if a child has its own SeveredChildren they cascade too.
        /// The `state != Severed` guard at every step makes infinite loops
        /// impossible even if someone wires a cycle in the SO graph.
        void CascadeSevered(BodyPartHealth severedPart)
        {
            if (severedPart == null || severedPart.Def == null) return;
            var children = severedPart.Def.SeveredChildren;
            if (children == null) return;

            for (int i = 0; i < children.Count; i++)
            {
                var child = GetPart(children[i]);
                if (child == null) continue;
                if (child.State == BodyPartState.Severed) continue; // already gone, no loop

                var oldChildState = child.State;
                child.CurrentHP = 0f;
                child.Recompute(VitalityMultiplier);

                if (oldChildState != child.State)
                {
                    OnPartStateChanged?.Invoke(child.Def.Id, oldChildState, child.State);
                }

                // If the child itself has SeveredChildren (rare today, planned
                // future-proof), keep cascading.
                if (child.State == BodyPartState.Severed) CascadeSevered(child);
            }
        }

        public void Heal(BodyPartId id, float amount)
        {
            if (IsDead || amount <= 0f) return;
            var part = GetPart(id);
            if (part == null || part.State == BodyPartState.Severed) return;
            var oldState = part.State;
            part.CurrentHP = Mathf.Min(part.EffectiveMaxHP, part.CurrentHP + amount);
            part.Recompute(VitalityMultiplier);
            if (oldState != part.State) OnPartStateChanged?.Invoke(part.Def.Id, oldState, part.State);
        }

        public void Bandage(BodyPartId id)
        {
            if (IsDead) return;
            var part = GetPart(id);
            if (part == null) return;
            part.Bandage();
        }

        /// Resets the unit to a fully healthy state. Severed parts come back
        /// (deliberate choice for the MVP — testing-friendly; if we ever want
        /// permadeath / permanent loss we add a flag here). Keeps the current
        /// Vitality multiplier so capacity reflects skill level.
        public void Revive()
        {
            IsDead = false;
            foreach (var p in Parts)
            {
                if (p.Def == null) continue;
                var oldState = p.State;
                p.IsBandaged = false;
                p.CurrentHP = p.Def.BaseMaxHP * VitalityMultiplier;
                p.Recompute(VitalityMultiplier);
                // Recompute infers Healthy state, which auto-clears bleeding.
                // Fire the transition so visual subscribers (BodyPartVisual,
                // future HUD) can refresh from Severed/Broken back to Healthy.
                if (oldState != p.State)
                {
                    OnPartStateChanged?.Invoke(p.Def.Id, oldState, p.State);
                }
            }
            Blood.Initialize(VitalityMultiplier);
            if (_unit != null && _unit.Agent != null && _unit.Agent.isOnNavMesh) _unit.Agent.isStopped = false;
            OnRevived?.Invoke();
        }

        /// Locomotion penalty: multiplicative product over leg + foot parts.
        /// Each affected part multiplies by (1 - penalty), so two
        /// independently-injured limbs compound naturally and even fully
        /// severed limbs can't drive the multiplier strictly to zero (the
        /// unit becomes glacial, not paralysed — design choice from the
        /// session brief). Hands intentionally have zero penalty in their
        /// definition; they'll matter for equipment slots in Module 6+.
        public float GetMoveSpeedMultiplier()
        {
            if (IsDead) return 0f;
            float mult = 1f;
            for (int i = 0; i < Parts.Count; i++)
            {
                var p = Parts[i];
                if (p.Def == null) continue;
                if (!AffectsMobility(p.Def.Id)) continue;

                switch (p.State)
                {
                    case BodyPartState.Broken:
                        mult *= (1f - p.Def.MoveSpeedPenaltyIfBroken);
                        break;
                    case BodyPartState.Severed:
                        mult *= (1f - p.Def.MoveSpeedPenaltyIfSevered);
                        break;
                }
            }
            return Mathf.Max(mult, 0f);
        }

        static bool AffectsMobility(BodyPartId id)
        {
            return id == BodyPartId.LegLeft || id == BodyPartId.LegRight
                || id == BodyPartId.FootLeft || id == BodyPartId.FootRight;
        }

        // ---- Bridge hooks ----

        /// Called by SkillModifiersBridge when Vitality changes. Updates the
        /// cached multiplier so future Recompute calls (regen, damage) use the
        /// right max. Does NOT rescale CurrentHP — the bridge handles ratio
        /// preservation explicitly to keep the policy in one place.
        public void SetVitalityMultiplier(float multiplier)
        {
            VitalityMultiplier = Mathf.Max(0.01f, multiplier);
        }

        // ---- Internals ----

        void HandleBloodDepleted() => SetDead();

        void SetDead()
        {
            if (IsDead) return;
            IsDead = true;
            // Stop blood ticks from doing anything further.
            // (Update early-returns on IsDead, but Drain might come in from
            // other places later; not strictly required here.)
            if (_unit != null && _unit.Orders != null) _unit.Orders.Clear();
            if (_unit != null && _unit.Agent != null && _unit.Agent.isOnNavMesh) _unit.Agent.isStopped = true;
            OnDeath?.Invoke();
        }

#if UNITY_EDITOR
        // Allow re-building Parts from definitions in the editor without
        // entering play mode (helpful when tweaking the list).
        void OnValidate()
        {
            if (Application.isPlaying) return;
            // Keep base blood non-zero so the inspector preview makes sense.
            if (baseMaxBlood < 1f) baseMaxBlood = 1f;
        }
#endif
    }
}
