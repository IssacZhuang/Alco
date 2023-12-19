using System;
using System.Collections.Generic;



namespace Vocore
{
    public struct ColliderSphere3D : ICollider3D
    {
        public ColliderType type => ColliderType.Sphere;
        public ShapeSphere3D shape;

        public unsafe bool CollidesWith<T>(T other) where T : unmanaged, ICollider3D
        {
            T* ptr = &other;
            return CollidesWith(ptr);
        }

        private unsafe bool CollidesWith<T>(T* other) where T : unmanaged, ICollider3D
        {
            if (other->type == ColliderType.Box)
            {
                return UtilsCollision.BoxSphere(((ColliderBox3D*)other)->shape, shape);
            }
            
            if (other->type == ColliderType.Sphere)
            {
                return UtilsCollision.SphereSphere(shape, ((ColliderSphere3D*)other)->shape);
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
            return UtilsCollision.RaySphere(ray, shape, out hitInfo);
        }
    }

}