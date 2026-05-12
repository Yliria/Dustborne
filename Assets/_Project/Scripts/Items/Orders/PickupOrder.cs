using Project.UI;
using Project.Units;
using UnityEngine;

namespace Project.Items.Orders
{
    /// Drives the unit to a WorldItem and absorbs it into the unit's
    /// Inventory. The WorldItem itself doubles as the visual marker (no
    /// separate spawn), and the order targets its world position so the
    /// OrderPathRenderer can include it in the shift-preview line.
    public class PickupOrder : IOrder, ITargetedOrder
    {
        readonly WorldItem _target;
        Vector3 _lastKnownPosition;
        bool _destinationSet;

        [Tooltip("Pickup engages when the unit gets within this distance of the target.")]
        public float PickupRange = 1.5f;

        public Vector3 TargetPosition => _target != null ? _target.transform.position : _lastKnownPosition;

        public PickupOrder(WorldItem target)
        {
            _target = target;
            if (target != null) _lastKnownPosition = target.transform.position;
        }

        public void OnStart(Unit unit)
        {
            if (_target == null || unit == null || unit.Agent == null) return;
            _destinationSet = unit.Agent.SetDestination(_target.transform.position);
        }

        public OrderStatus Tick(Unit unit, float deltaTime)
        {
            if (_target == null) return OrderStatus.Failed; // someone destroyed it (other pickup, despawn)
            if (unit == null || unit.Agent == null) return OrderStatus.Failed;

            var agent = unit.Agent;

            // If the target slid (e.g. physics knock), follow.
            if (!_destinationSet || (agent.destination - _target.transform.position).sqrMagnitude > 0.04f)
            {
                _destinationSet = agent.SetDestination(_target.transform.position);
            }

            if (agent.pathPending) return OrderStatus.Running;
            if (agent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid) return OrderStatus.Failed;

            // Distance check happens in horizontal plane so the unit base
            // offset (Y) doesn't push the threshold around.
            Vector3 a = unit.transform.position; a.y = 0f;
            Vector3 b = _target.transform.position; b.y = 0f;
            if ((a - b).sqrMagnitude <= PickupRange * PickupRange)
            {
                TryAbsorb(unit);
                return OrderStatus.Complete;
            }

            _lastKnownPosition = _target.transform.position;
            return OrderStatus.Running;
        }

        public void OnEnd(Unit unit)
        {
            if (unit == null || unit.Agent == null) return;
            if (unit.Agent.isOnNavMesh) unit.Agent.ResetPath();
        }

        void TryAbsorb(Unit unit)
        {
            if (_target == null) return;
            var inv = unit.GetComponent<Inventory>();
            if (inv != null && _target.Def != null)
            {
                inv.Add(_target.Def, _target.Quantity);
                FloatingTextService.SpawnPickup(
                    _target.Def.DisplayName,
                    _target.Quantity,
                    unit.transform.position + Vector3.up * 2f);
            }
            else if (inv == null)
            {
                Debug.LogWarning($"[PickupOrder] {unit.name} has no Inventory — WorldItem destroyed without storage.");
                FloatingTextService.SpawnError(
                    "No inventory",
                    unit.transform.position + Vector3.up * 2f);
            }
            Object.Destroy(_target.gameObject);
        }
    }
}
