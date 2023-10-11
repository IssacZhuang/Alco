using System;
using System.Collections.Generic;



namespace Vocore
{
    public struct ColliderSphere : ICollider
    {
        public ColliderType type => ColliderType.Sphere;
        public ShapeSphere shape;

        public unsafe bool CollidesWith<T>(T other) where T : unmanaged, ICollider
        {
            T* ptr = &other;
            return CollidesWith(ptr);
        }

        private unsafe bool CollidesWith<T>(T* other) where T : unmanaged, ICollider
        {
            if (other->type == ColliderType.Box)
            {
                return UtilsCollision.BoxSphere(((ColliderBox*)other)->shape, shape);
            }
            
            if (other->type == ColliderType.Sphere)
            {
                return UtilsCollision.SphereSphere(shape, ((ColliderSphere*)other)->shape);
            }

            return false;
        }

        public BoundingBox GetBoundingBox()
        {
            return shape.GetBoundingBox();
        }

        public BoundingBox GetBoundingBox(Tranform transform)
        {
            return shape.GetBoundingBox(transform);
        }

        public bool IntersectRay(Ray ray, out RaycastHit hitInfo)
        {
            return UtilsCollision.RaySphere(ray, shape, out hitInfo);
        }
    }

}