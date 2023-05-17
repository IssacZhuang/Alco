using System;
using System.Collections.Generic;

using Unity.Mathematics;

namespace Vocore
{
    public struct ColliderBox : ICollider
    {
        public ColliderType type => ColliderType.Box;
        public ShapeBox shape;

        public bool CollidesWith(ICollider other)
        {
            if (other.type == ColliderType.Box)
            {
                return UtilsCollision.BoxBox(shape, ((ColliderBox)other).shape);
            }
            
            if (other.type == ColliderType.Sphere)
            {
                return UtilsCollision.BoxSphere(shape, ((ColliderSphere)other).shape);
            }

            return false;
        }

        public BoundingBox GetBoundingBox()
        {
            return shape.GetBoundingBox();
        }

        public BoundingBox GetBoundingBox(RigidTransform transform)
        {
            return shape.GetBoundingBox(transform);
        }
    }

}