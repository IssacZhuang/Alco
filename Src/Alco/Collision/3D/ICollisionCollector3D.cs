using System;

namespace Alco
{
    public interface IBvhCollisionCollector3D
    {
        bool OnHit(ColliderCastResult3D result);
    }

    public interface IBvhRayCastCollector3D
    {
        bool OnHit(RayCastResult3D result);
    }

    public interface IRayCastCollector3D
    {
        bool OnHit(object target, RaycastHit3D hit);
    }
}
