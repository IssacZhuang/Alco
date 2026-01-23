using System;

namespace Alco
{
    public interface IBvhCollisionCollector2D
    {
        bool OnHit(ColliderCastResult2D result);
    }

    /// <summary>
    /// Interface for collecting collision results from CollisionWorld2D with the target object.
    /// </summary>
    public interface ICollisionCollector2D
    {
        /// <summary>
        /// Called when a target is hit.
        /// </summary>
        /// <param name="target">The target object that was hit.</param>
        /// <returns>True to continue the query, false to stop.</returns>
        bool OnHit(object target);
    }
}
