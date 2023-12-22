using System;
using System.Numerics;

namespace Vocore{
    public struct ColliderBox2D : ICollider2D
    {
        public readonly ColliderType type => ColliderType.Box;
        public ShapeBox2D shape;

        public unsafe bool CollidesWith<T>(T other) where T : unmanaged, ICollider2D
        {
            T* ptr = &other;
            return CollidesWith(ptr);
        }

        private unsafe bool CollidesWith<T>(T* other) where T : unmanaged, ICollider2D
        {
            if (other->type == ColliderType.Box)
            {
                return UtilsCollision2D.BoxBox(shape, ((ColliderBox2D*)other)->shape);
            }

            if (other->type == ColliderType.Sphere)
            {
                return UtilsCollision2D.BoxSphere(shape, ((ColliderSphere2D*)other)->shape);
            }

            return false;
        }

        public bool IntersectRay(Ray2D ray, out RaycastHit2D hitInfo)
        {
            return UtilsCollision2D.RayBox(ray, shape, out hitInfo);
        }

        public BoundingBox2D GetBoundingBox()
        {
            return shape.GetBoundingBox();
        }
    }
}