using System;
using System.Collections.Generic;

using Unity.Mathematics;

namespace Vocore
{
    public struct ColliderSphere : ICollider
    {
        public ColliderType type => ColliderType.Sphere;
        public ShapeSphere shape;

        public bool CollidesWith(ICollider other)
        {
            if (other.type == ColliderType.Box)
            {
                return UtilsCollision.BoxSphere(((ColliderBox)other).shape, shape);
            }
            
            if (other.type == ColliderType.Sphere)
            {
                return UtilsCollision.SphereSphere(shape, ((ColliderSphere)other).shape);
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

        public bool IntersectRay(Ray ray, out RaycastHit hitInfo)
        {
            return UtilsCollision.RaySphere(ray, shape, out hitInfo);
        }
    }

}