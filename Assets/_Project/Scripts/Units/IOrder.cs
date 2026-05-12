namespace Project.Units
{
    /// Atomic command executed by a Unit's OrderQueue. New order types
    /// (Attack, Harvest, Interact, ...) implement this without touching the
    /// queue or the Unit hub.
    public interface IOrder
    {
        void OnStart(Unit unit);
        OrderStatus Tick(Unit unit, float deltaTime);
        void OnEnd(Unit unit);
    }
}
