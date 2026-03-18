using System;

namespace Alco
{
    public interface IBvhCollisionCastCollector2D
    {
        bool OnHit(ColliderCastResult2D result);
    }

    public interface IBvhRayCastCollector2D
    {
        bool OnHit(RayCastResult2D result);
    }

    public interface IRayCastCollector2D
    {
        bool OnHit(object target, RaycastHit2D hit);
    }
}
