using System;
using System.Collections.Generic;



namespace Vocore
{
    public struct ColliderBox : ICollider
    {
        public ColliderType type => ColliderType.Box;
        public ShapeBox shape;

        public unsafe bool CollidesWith<T>(T other) where T : unmanaged, ICollider
        {
            T* ptr = &other;
            return CollidesWith(ptr);
        }

        private unsafe bool CollidesWith<T>(T* other) where T : unmanaged, ICollider
        {
            if (other->type == ColliderType.Box)
            {
                return UtilsCollision.BoxBox(shape, ((ColliderBox*)other)->shape);
            }
            
            if (other->type == ColliderType.Sphere)
            {
                return UtilsCollision.BoxSphere(shape, ((ColliderSphere*)other)->shape);
            }

            return false;
        }

        public BoundingBox GetBoundingBox()
        {
            return shape.GetBoundingBox();
        }

        public BoundingBox GetBoundingBox(Transform transform)
        {
            return shape.GetBoundingBox(transform);
        }

        public bool IntersectRay(Ray ray, out RaycastHit hitInfo)
        {
            return UtilsCollision.RayBox(ray, shape, out hitInfo);
        }
    }

}