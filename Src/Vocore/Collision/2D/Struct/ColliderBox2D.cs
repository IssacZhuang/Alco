using System;
using System.Numerics;

namespace Vocore{
    public struct ColliderBox2D : ICollider2D
    {
        public readonly ColliderType2D Type => ColliderType2D.Box;
        public ShapeBox2D shape;

        public unsafe bool CollidesWith<T>(T other) where T : unmanaged, ICollider2D
        {
            T* ptr = &other;
            return CollidesWith(ptr);
        }

        private unsafe bool CollidesWith<T>(T* other) where T : unmanaged, ICollider2D
        {
            if (other->Type == ColliderType2D.Box)
            {
                return UtilsCollision2D.BoxBox(shape, ((ColliderBox2D*)other)->shape);
            }

            if (other->Type == ColliderType2D.Sphere)
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

        public override string ToString()
        {
            return $"Box Collider: {shape}";
        }
    }
}