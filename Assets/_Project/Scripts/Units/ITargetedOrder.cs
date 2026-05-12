using UnityEngine;

namespace Project.Units
{
    /// Implemented by orders that have a single world-space target position
    /// (move, attack, harvest, interact, ...). Used by visualizers to draw
    /// waypoint lines and markers without coupling to concrete order types.
    public interface ITargetedOrder
    {
        Vector3 TargetPosition { get; }
    }
}
