using System;
using System.Collections.Generic;



namespace Vocore
{
    public struct ColliderBox3D : ICollider3D
    {
        public ColliderType type => ColliderType.Box;
        public ShapeBox3D shape;

        public unsafe bool CollidesWith<T>(T other) where T : unmanaged, ICollider3D
        {
            T* ptr = &other;
            return CollidesWith(ptr);
        }

        private unsafe bool CollidesWith<T>(T* other) where T : unmanaged, ICollider3D
        {
            if (other->type == ColliderType.Box)
            {
                return UtilsCollision.BoxBox(shape, ((ColliderBox3D*)other)->shape);
            }
            
            if (other->type == ColliderType.Sphere)
            {
                return UtilsCollision.BoxSphere(shape, ((ColliderSphere3D*)other)->shape);
            }

            return false;
        }

        public BoundingBox3D GetBoundingBox()
        {
            return shape.GetBoundingBox();
        }

        public BoundingBox3D GetBoundingBox(Transform transform)
        {
            return shape.GetBoundingBox(transform);
        }

        public bool IntersectRay(Ray3D ray, out RaycastHit3D hitInfo)
        {
            return UtilsCollision.RayBox(ray, shape, out hitInfo);
        }
    }

}