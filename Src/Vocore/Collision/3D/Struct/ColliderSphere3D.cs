using System;
using System.Collections.Generic;



namespace Vocore
{
    public struct ColliderSphere3D : ICollider3D
    {
        public ColliderType3D Type => ColliderType3D.Sphere;
        public ShapeSphere3D shape;

        public unsafe bool CollidesWith<T>(T other) where T : unmanaged, ICollider3D
        {
            T* ptr = &other;
            return CollidesWith(ptr);
        }

        private unsafe bool CollidesWith<T>(T* other) where T : unmanaged, ICollider3D
        {
            if (other->Type == ColliderType3D.Box)
            {
                return UtilsCollision3D.BoxSphere(((ColliderBox3D*)other)->shape, shape);
            }

            if (other->Type == ColliderType3D.Sphere)
            {
                return UtilsCollision3D.SphereSphere(shape, ((ColliderSphere3D*)other)->shape);
            }

            return false;
        }

        public BoundingBox3D GetBoundingBox()
        {
            return shape.GetBoundingBox();
        }

        public bool IntersectRay(Ray3D ray, out RaycastHit3D hitInfo)
        {
            return UtilsCollision3D.RaySphere(ray, shape, out hitInfo);
        }

        public override string ToString()
        {
            return $"Sphere Collider: {shape}";
        }
    }

}