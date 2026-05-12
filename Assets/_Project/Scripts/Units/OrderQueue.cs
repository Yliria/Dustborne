using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Units
{
    /// FIFO list of IOrder instances driven by Unit.Update. The queue is
    /// strictly mechanical — it does not know how any order works, only how
    /// to start, tick, and end it.
    [DisallowMultipleComponent]
    public class OrderQueue : MonoBehaviour
    {
        readonly Queue<IOrder> _queue = new();

        public IOrder Current { get; private set; }
        public int PendingCount => _queue.Count;
        public int TotalCount => PendingCount + (Current != null ? 1 : 0);

        /// Read-only view of the pending orders, in FIFO order.
        /// Useful for visualizers (waypoint lines, markers) that need to walk
        /// the upcoming orders without mutating the queue.
        public IReadOnlyCollection<IOrder> Pending => _queue;

        public event Action<IOrder> OnOrderStarted;
        public event Action<IOrder, OrderStatus> OnOrderCompleted;

        Unit _unit;

        void Awake()
        {
            _unit = GetComponent<Unit>();
        }

        public void Enqueue(IOrder order)
        {
            if (order == null) return;
            _queue.Enqueue(order);
        }

        public void EnqueueAndClear(IOrder order)
        {
            Clear();
            Enqueue(order);
        }

        public void Clear()
        {
            if (Current != null)
            {
                var ending = Current;
                Current = null;
                ending.OnEnd(_unit);
                OnOrderCompleted?.Invoke(ending, OrderStatus.Failed);
            }
            _queue.Clear();
        }

        public void Tick(float deltaTime)
        {
            if (Current == null)
            {
                if (_queue.Count == 0) return;
                Current = _queue.Dequeue();
                Current.OnStart(_unit);
                OnOrderStarted?.Invoke(Current);
            }

            var status = Current.Tick(_unit, deltaTime);
            if (status == OrderStatus.Running) return;

            var finished = Current;
            Current = null;
            finished.OnEnd(_unit);
            OnOrderCompleted?.Invoke(finished, status);
        }

        void OnDestroy()
        {
            // Best effort cleanup so orders holding spawned visuals get a
            // chance to despawn them.
            if (Current != null)
            {
                Current.OnEnd(_unit);
                Current = null;
            }
            _queue.Clear();
        }
    }
}
