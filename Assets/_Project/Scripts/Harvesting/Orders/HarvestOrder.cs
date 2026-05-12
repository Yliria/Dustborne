using Project.Items;
using Project.Skills;
using Project.Units;
using UnityEngine;

namespace Project.Harvesting.Orders
{
    /// Navigates the unit to a Harvestable, validates the tool, then chips
    /// away at it once in range. Damage is scaled by SkillSystem's harvest
    /// multiplier; XP Labour gain is proportional to damage dealt (so the
    /// reward tracks actual work, not just time spent in front of the node).
    ///
    /// Drops are not handled here — Harvestable.OnDepleted spawns WorldItems
    /// scattered around its position and the existing pickup pipeline takes
    /// over from there. HarvestOrder never touches Inventory.Add.
    public class HarvestOrder : IOrder, ITargetedOrder
    {
        readonly Harvestable _target;
        Vector3 _lastKnownPosition;
        bool _failedAtStart;
        bool _destinationSet;

        Inventory _inventory;
        SkillSystem _skills;

        public Vector3 TargetPosition => _target != null ? _target.transform.position : _lastKnownPosition;

        public HarvestOrder(Harvestable target)
        {
            _target = target;
            if (target != null) _lastKnownPosition = target.transform.position;
        }

        public void OnStart(Unit unit)
        {
            if (_target == null)
            {
                Debug.LogWarning("[HarvestOrder] Null target.");
                _failedAtStart = true;
                return;
            }
            if (_target.IsDepleted)
            {
                Debug.LogWarning($"[HarvestOrder] Target '{_target.name}' already depleted.");
                _failedAtStart = true;
                return;
            }
            if (_target.Def == null)
            {
                Debug.LogWarning($"[HarvestOrder] Target '{_target.name}' has no HarvestableDefinition.");
                _failedAtStart = true;
                return;
            }

            _inventory = unit.GetComponent<Inventory>();
            _skills = unit.GetComponent<SkillSystem>();

            if (_target.Def.RequiredTool != null)
            {
                if (_inventory == null || !_inventory.Has(_target.Def.RequiredTool, 1))
                {
                    Debug.LogWarning($"[HarvestOrder] Tool required: {_target.Def.RequiredTool.Id} — order failed.");
                    _failedAtStart = true;
                    return;
                }
            }

            if (unit.Agent != null)
            {
                unit.Agent.isStopped = false;
                _destinationSet = unit.Agent.SetDestination(_target.transform.position);
            }
        }

        public OrderStatus Tick(Unit unit, float deltaTime)
        {
            if (_failedAtStart) return OrderStatus.Failed;
            if (_target == null) return OrderStatus.Failed; // destroyed between Start and Tick
            if (unit == null || unit.Agent == null) return OrderStatus.Failed;

            // Cleanly handle the case where another path depleted the target
            // (shared scene, future AI, debug panel, ...).
            if (_target.IsDepleted) return OrderStatus.Complete;

            var agent = unit.Agent;
            float range = _target.Def != null ? _target.Def.InteractionRange : 1.5f;

            // Horizontal distance check — vertical offset from baseOffset
            // would otherwise inflate the range needlessly.
            Vector3 a = unit.transform.position; a.y = 0f;
            Vector3 b = _target.transform.position; b.y = 0f;
            float sqr = (a - b).sqrMagnitude;

            if (sqr > range * range)
            {
                // Approach phase.
                if (agent.isStopped) agent.isStopped = false;
                if (!_destinationSet || (agent.destination - _target.transform.position).sqrMagnitude > 0.04f)
                {
                    _destinationSet = agent.SetDestination(_target.transform.position);
                }
                if (agent.pathPending) return OrderStatus.Running;
                if (agent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid) return OrderStatus.Failed;
                _lastKnownPosition = _target.transform.position;
                return OrderStatus.Running;
            }

            // In range — chop / mine / fish.
            agent.isStopped = true;

            float harvestMult = _skills != null ? _skills.GetHarvestSpeedMult() : 1f;
            float damagePerSec = _target.Def.BaseHarvestSpeed * harvestMult;
            float damage = damagePerSec * deltaTime;

            _target.ApplyDamage(damage);

            // XP Labour proportional to damage actually dealt this frame
            // (pause-safe: GainXP returns early when GameTime.IsPaused).
            if (_skills != null) _skills.GainXP(SkillType.Labour, damage * 0.5f);

            return _target.IsDepleted ? OrderStatus.Complete : OrderStatus.Running;
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
