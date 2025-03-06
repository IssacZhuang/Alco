using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;



namespace Alco
{
    public struct ColliderSphere2D : ICollider2D
    {
        private readonly ColliderHeader2D _header;
        public ShapeSphere2D Shape;

        public ColliderHeader2D Header
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _header;
        }

        public ColliderSphere2D()
        {
            _header = new ColliderHeader2D
            {
                Type = ColliderType2D.Sphere
            };
        }

        public unsafe bool CollidesWith(ColliderHeader2D* other)
        {
            switch (other->Type)
            {
                case ColliderType2D.Box:
                    return UtilsCollision2D.BoxSphere((*(ColliderBox2D*)other).Shape, Shape);
                case ColliderType2D.Sphere:
                    return UtilsCollision2D.SphereSphere(Shape, (*(ColliderSphere2D*)other).Shape);
            }
            return false;
        }

        public BoundingBox2D GetBoundingBox()
        {
            return Shape.GetBoundingBox();
        }

        public bool IntersectRay(Ray2D ray, out RaycastHit2D hitInfo)
        {
            return UtilsCollision2D.RaySphere(ray, Shape, out hitInfo);
        }

        public bool IntersectPoint(Vector2 point)
        {
            return UtilsCollision2D.PointSphere(point, Shape);
        }

        public override string ToString()
        {
            return $"Sphere Collider: {Shape}";
        }
    }

}