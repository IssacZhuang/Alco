using System;
using System.Collections.Generic;



namespace Vocore
{
    public struct ColliderSphere2D : ICollider2D
    {
        public ColliderType2D Type => ColliderType2D.Sphere;
        public ShapeSphere2D shape;

        public unsafe bool CollidesWith<T>(T other) where T : unmanaged, ICollider2D
        {
            T* ptr = &other;
            return CollidesWith(ptr);
        }

        private unsafe bool CollidesWith<T>(T* other) where T : unmanaged, ICollider2D
        {
            if (other->Type == ColliderType2D.Box)
            {
                return UtilsCollision2D.BoxSphere(((ColliderBox2D*)other)->shape, shape);
            }

            if (other->Type == ColliderType2D.Sphere)
            {
                return UtilsCollision2D.SphereSphere(shape, ((ColliderSphere2D*)other)->shape);
            }

            return false;
        }

        public BoundingBox2D GetBoundingBox()
        {
            return shape.GetBoundingBox();
        }

        public bool IntersectRay(Ray2D ray, out RaycastHit2D hitInfo)
        {
            return UtilsCollision2D.RaySphere(ray, shape, out hitInfo);
        }

        public override string ToString()
        {
            return $"Sphere Collider: {shape}";
        }
    }

}