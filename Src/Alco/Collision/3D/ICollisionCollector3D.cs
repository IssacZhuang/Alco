using System;

namespace Alco
{
    /// <summary>
    /// Receives collider cast hits from a 3D BVH.
    /// </summary>
    public interface IBvhCollisionCollector3D
    {
        bool OnHit(ColliderCastResult3D result);
    }

    /// <summary>
    /// Receives ray cast hits from a 3D BVH.
    /// </summary>
    public interface IBvhRayCastCollector3D
    {
        bool OnHit(RayCastResult3D result);
    }

    /// <summary>
    /// Receives high-level ray cast hits against a 3D target.
    /// </summary>
    public interface IRayCastCollector3D
    {
        bool OnHit(object target, RaycastHit3D hit);
    }
}
