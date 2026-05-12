using Project.Core;
using UnityEngine;
using UnityEngine.AI;

namespace Project.Units
{
    /// Composition hub for an actor. Holds component references and
    /// dispatches per-frame ticks through GameTime so pause and time-scale
    /// flow uniformly. Future modules (HealthSystem, SkillSystem, Inventory,
    /// Equipment) attach as sibling components; do NOT add gameplay logic here.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(OrderQueue))]
    public class Unit : MonoBehaviour
    {
        public NavMeshAgent Agent { get; private set; }
        public OrderQueue Orders { get; private set; }

        float _baseAgentSpeed;

        void Awake()
        {
            Agent = GetComponent<NavMeshAgent>();
            Orders = GetComponent<OrderQueue>();
            _baseAgentSpeed = Agent.speed;
        }

        void OnEnable()
        {
            GameTime.OnPauseChanged += HandlePauseChanged;
            // In case we enable while already paused.
            if (GameTime.IsPaused) ApplyPause(true);
        }

        void OnDisable()
        {
            GameTime.OnPauseChanged -= HandlePauseChanged;
        }

        void Update()
        {
            Orders.Tick(GameTime.DeltaTime);
        }

        public void IssueOrder(IOrder order, bool append)
        {
            if (order == null) return;
            if (append) Orders.Enqueue(order);
            else Orders.EnqueueAndClear(order);
        }

        void HandlePauseChanged(bool paused) => ApplyPause(paused);

        void ApplyPause(bool paused)
        {
            if (Agent == null || !Agent.isOnNavMesh) return;
            Agent.isStopped = paused;
        }
    }
}
