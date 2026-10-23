using System;

namespace Alco
{
    /// <summary>
    /// Receives collider cast hits from a 2D BVH.
    /// </summary>
    public interface IBvhCollisionCastCollector2D
    {
        bool OnHit(ColliderCastResult2D result);
    }

    /// <summary>
    /// Receives ray cast hits from a 2D BVH.
    /// </summary>
    public interface IBvhRayCastCollector2D
    {
        bool OnHit(RayCastResult2D result);
    }

    /// <summary>
    /// Receives high-level ray cast hits against a 2D target.
    /// </summary>
    public interface IRayCastCollector2D
    {
        bool OnHit(object target, RaycastHit2D hit);
    }
}
