using UnityEngine;

namespace Project.Units.Orders
{
    /// Move the unit to a world-space destination via NavMeshAgent. Optionally
    /// spawns a visual marker at the destination for the lifetime of the order.
    /// AI-issued moves can pass a null marker prefab; the player input layer
    /// supplies one so player commands stay visible.
    public class MoveOrder : IOrder, ITargetedOrder
    {
        readonly Vector3 _destination;
        readonly GameObject _markerPrefab;

        GameObject _markerInstance;
        bool _destinationSet;

        public Vector3 Destination => _destination;
        public Vector3 TargetPosition => _destination;

        public MoveOrder(Vector3 destination, GameObject markerPrefab = null)
        {
            _destination = destination;
            _markerPrefab = markerPrefab;
        }

        public void OnStart(Unit unit)
        {
            var agent = unit.Agent;
            if (agent == null) return;

            // isStopped resets when SetDestination is called after pause; we
            // re-apply it on resume from Unit, so just set the target here.
            _destinationSet = agent.SetDestination(_destination);

            if (_markerPrefab != null)
            {
                _markerInstance = Object.Instantiate(_markerPrefab, _destination, Quaternion.identity);
                _markerInstance.name = "OrderMarker_Move";
            }
        }

        public OrderStatus Tick(Unit unit, float deltaTime)
        {
            var agent = unit.Agent;
            if (agent == null || !_destinationSet) return OrderStatus.Failed;

            // Wait for path computation before reading remainingDistance.
            if (agent.pathPending) return OrderStatus.Running;

            // If the agent couldn't reach the destination at all, fail rather
            // than spin forever.
            if (agent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid)
                return OrderStatus.Failed;

            if (agent.remainingDistance <= agent.stoppingDistance)
            {
                // velocity check guards against the first frame where the
                // agent has the path but hasn't started moving yet.
                if (!agent.hasPath || agent.velocity.sqrMagnitude < 0.0001f)
                    return OrderStatus.Complete;
            }

            return OrderStatus.Running;
        }

        public void OnEnd(Unit unit)
        {
            var agent = unit.Agent;
            if (agent != null && agent.isOnNavMesh) agent.ResetPath();

            if (_markerInstance != null)
            {
                Object.Destroy(_markerInstance);
                _markerInstance = null;
            }
        }
    }
}
