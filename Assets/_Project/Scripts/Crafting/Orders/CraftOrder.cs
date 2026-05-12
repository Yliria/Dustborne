using Project.Items;
using Project.Skills;
using Project.UI;
using Project.Units;
using UnityEngine;

namespace Project.Crafting.Orders
{
    /// Run a RecipeDefinition end-to-end:
    ///  - Validate inputs at OnStart (fast-fail) and again at completion
    ///    (so the player vidaging their inventory mid-craft cancels cleanly).
    ///  - Walk to the station if RequiresStation, hand-craft on the spot
    ///    otherwise.
    ///  - Accumulate progress on GameTime.DeltaTime × GetCraftSpeedMult.
    ///  - On completion: consume inputs, produce outputs, grant XP.
    ///
    /// Strict policy: inputs are consumed only at completion (cancellation
    /// is therefore lossless). Overweight outputs are allowed — Inventory
    /// will simply tip into the red zone, which is consistent with the rest
    /// of the systems.
    ///
    /// CraftOrder does NOT reference RecipeDatabase. Callers pass a
    /// RecipeDefinition directly; the database is for discovery (debug
    /// panel, future UI, save/load).
    public class CraftOrder : IOrder, ITargetedOrder
    {
        readonly RecipeDefinition _recipe;

        Unit _ownerUnit;
        Inventory _inventory;
        SkillSystem _skills;
        CraftingStation _targetStation;

        float _progressTime;
        bool _failedAtStart;
        bool _destinationSet;

        public RecipeDefinition Recipe => _recipe;
        public float ProgressTime => _progressTime;
        public float ProgressRatio => _recipe != null && _recipe.BaseCraftTime > 0f
            ? Mathf.Clamp01(_progressTime / _recipe.BaseCraftTime)
            : 0f;

        /// For the OrderPathRenderer line preview: returns the station's
        /// interaction point when crafting at a workbench, the unit's own
        /// current position for hand-crafts (so the segment is degenerate
        /// and the preview line doesn't draw a phantom waypoint).
        public Vector3 TargetPosition
        {
            get
            {
                if (_targetStation != null) return _targetStation.GetInteractionPosition();
                if (_ownerUnit != null) return _ownerUnit.transform.position;
                return Vector3.zero;
            }
        }

        public CraftOrder(RecipeDefinition recipe)
        {
            _recipe = recipe;
        }

        public void OnStart(Unit unit)
        {
            _ownerUnit = unit;

            if (_recipe == null)
            {
                Debug.LogWarning("[CraftOrder] Null recipe.");
                _failedAtStart = true;
                return;
            }

            _inventory = unit.GetComponent<Inventory>();
            _skills = unit.GetComponent<SkillSystem>();

            if (_inventory == null)
            {
                Debug.LogWarning("[CraftOrder] No Inventory on unit — cannot craft.");
                _failedAtStart = true;
                return;
            }

            // Input gate (fast-fail).
            for (int i = 0; i < _recipe.Inputs.Count; i++)
            {
                var input = _recipe.Inputs[i];
                if (input == null || input.Def == null) continue;
                if (!_inventory.Has(input.Def, input.Quantity))
                {
                    Debug.LogWarning($"[CraftOrder] Missing input for '{_recipe.Id}': {input.Def.Id} ×{input.Quantity}.");
                    FloatingTextService.SpawnError(
                        $"Missing: {input.Def.DisplayName} ×{input.Quantity}",
                        unit.transform.position + Vector3.up * 2f);
                    _failedAtStart = true;
                    return;
                }
            }

            // Station gate.
            if (_recipe.RequiredStation.HasValue)
            {
                var stationType = _recipe.RequiredStation.Value;
                _targetStation = CraftingStation.FindNearest(unit.transform.position, stationType);
                if (_targetStation == null)
                {
                    Debug.LogWarning($"[CraftOrder] No active {stationType} station in scene for '{_recipe.Id}'.");
                    FloatingTextService.SpawnError(
                        $"No {stationType} found",
                        unit.transform.position + Vector3.up * 2f);
                    _failedAtStart = true;
                    return;
                }

                if (unit.Agent != null)
                {
                    unit.Agent.isStopped = false;
                    _destinationSet = unit.Agent.SetDestination(_targetStation.GetInteractionPosition());
                }
            }
            else
            {
                // Hand-craft: stay put.
                _targetStation = null;
                if (unit.Agent != null && unit.Agent.isOnNavMesh)
                {
                    unit.Agent.isStopped = true;
                }
            }

            _progressTime = 0f;
        }

        public OrderStatus Tick(Unit unit, float deltaTime)
        {
            if (_failedAtStart) return OrderStatus.Failed;
            if (_recipe == null) return OrderStatus.Failed;
            if (unit == null) return OrderStatus.Failed;

            if (_recipe.RequiredStation.HasValue)
            {
                if (_targetStation == null)
                {
                    Debug.LogWarning($"[CraftOrder] Station for '{_recipe.Id}' disappeared mid-craft.");
                    return OrderStatus.Failed;
                }

                var agent = unit.Agent;
                if (agent == null) return OrderStatus.Failed;

                Vector3 unitPos = unit.transform.position; unitPos.y = 0f;
                Vector3 stationPos = _targetStation.GetInteractionPosition(); stationPos.y = 0f;
                float range = _targetStation.InteractionRange;

                if ((unitPos - stationPos).sqrMagnitude > range * range)
                {
                    // Approach.
                    if (agent.isStopped) agent.isStopped = false;
                    if (!_destinationSet || (agent.destination - _targetStation.GetInteractionPosition()).sqrMagnitude > 0.04f)
                    {
                        _destinationSet = agent.SetDestination(_targetStation.GetInteractionPosition());
                    }
                    if (agent.pathPending) return OrderStatus.Running;
                    if (agent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid) return OrderStatus.Failed;
                    return OrderStatus.Running;
                }

                // In range — anchor + face the station for cosmetic effect.
                agent.isStopped = true;
                Vector3 lookAt = _targetStation.transform.position;
                lookAt.y = unit.transform.position.y;
                Vector3 toStation = lookAt - unit.transform.position;
                if (toStation.sqrMagnitude > 0.01f)
                {
                    unit.transform.rotation = Quaternion.Slerp(
                        unit.transform.rotation,
                        Quaternion.LookRotation(toStation),
                        10f * deltaTime);
                }
            }

            // Progress (both hand-craft and station-craft).
            float craftMult = _skills != null ? _skills.GetCraftSpeedMult() : 1f;
            _progressTime += deltaTime * craftMult;
            if (_progressTime < _recipe.BaseCraftTime) return OrderStatus.Running;

            // Completion — re-validate inputs (the player might have cleared
            // the inventory while we were chiseling away).
            for (int i = 0; i < _recipe.Inputs.Count; i++)
            {
                var input = _recipe.Inputs[i];
                if (input == null || input.Def == null) continue;
                if (!_inventory.Has(input.Def, input.Quantity))
                {
                    Debug.LogWarning($"[CraftOrder] Inputs vanished during craft of '{_recipe.Id}' — {input.Def.Id} ×{input.Quantity} missing.");
                    FloatingTextService.SpawnError(
                        $"Inputs lost: {input.Def.DisplayName} ×{input.Quantity}",
                        unit.transform.position + Vector3.up * 2f);
                    return OrderStatus.Failed;
                }
            }

            // Consume.
            for (int i = 0; i < _recipe.Inputs.Count; i++)
            {
                var input = _recipe.Inputs[i];
                if (input == null || input.Def == null) continue;
                _inventory.Remove(input.Def, input.Quantity);
            }

            // Produce. Inventory.Add tolerates overweight — consistent with
            // pickup overflow elsewhere; the speed penalty does the policing.
            for (int i = 0; i < _recipe.Outputs.Count; i++)
            {
                var output = _recipe.Outputs[i];
                if (output == null || output.Def == null) continue;
                _inventory.Add(output.Def, output.Quantity);
                FloatingTextService.SpawnPickup(
                    output.Def.DisplayName,
                    output.Quantity,
                    unit.transform.position + Vector3.up * 2f);
            }

            // XP grant once, at completion only (per spec — discourages
            // exploit-y short recipes farmed in a loop).
            if (_skills != null && _recipe.XPGainAmount > 0f)
            {
                _skills.GainXP(_recipe.XPGainSkill, _recipe.XPGainAmount);
            }

            return OrderStatus.Complete;
        }

        public void OnEnd(Unit unit)
        {
            if (unit == null || unit.Agent == null) return;
            if (!unit.Agent.isOnNavMesh) return;
            unit.Agent.isStopped = false;
            unit.Agent.ResetPath();
        }
    }
}
