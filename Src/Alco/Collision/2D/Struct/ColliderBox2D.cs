using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;



namespace Alco
{
    public struct ColliderBox2D : ICollider2D
    {
        private readonly ColliderHeader2D _header;
        public ShapeBox2D Shape;

        public ColliderHeader2D Header
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _header;
        }

        public ColliderBox2D()
        {
            _header = new ColliderHeader2D
            {
                Type = ColliderType2D.Box
            };
        }

        public unsafe bool CollidesWith(ColliderHeader2D* other)
        {
            switch (other->Type)
            {
                case ColliderType2D.Box:
                    return UtilsCollision2D.BoxBox(Shape, (*(ColliderBox2D*)other).Shape);
                case ColliderType2D.Sphere:
                    return UtilsCollision2D.BoxSphere(Shape, (*(ColliderSphere2D*)other).Shape);
            }
            return false;
        }

        public BoundingBox2D GetBoundingBox()
        {
            return Shape.GetBoundingBox();
        }

        public bool IntersectRay(Ray2D ray, out RaycastHit2D hitInfo)
        {
            return UtilsCollision2D.RayBox(ray, Shape, out hitInfo);
        }

        public bool IntersectPoint(Vector2 point)
        {
            return UtilsCollision2D.PointBox(point, Shape);
        }

        public override string ToString()
        {
            return $"Box Collider: {Shape}";
        }
    }

}