using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;



namespace Vocore
{
    public struct ColliderBox2D : ICollider2D
    {
        private readonly ColliderHeader2D _header;
        public ShapeBox2D shape;

        public ColliderHeader2D Header
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _header;
        }

        public ColliderBox2D()
        {
            _header = new ColliderHeader2D
            {
                type = ColliderType2D.Box
            };
        }

        public unsafe bool CollidesWith(ColliderHeader2D* other)
        {
            switch (other->type)
            {
                case ColliderType2D.Box:
                    return UtilsCollision2D.BoxBox(shape, (*(ColliderBox2D*)other).shape);
                case ColliderType2D.Sphere:
                    return UtilsCollision2D.BoxSphere(shape, (*(ColliderSphere2D*)other).shape);
            }
            return false;
        }

        public BoundingBox2D GetBoundingBox()
        {
            return shape.GetBoundingBox();
        }

        public bool IntersectRay(Ray2D ray, out RaycastHit2D hitInfo)
        {
            return UtilsCollision2D.RayBox(ray, shape, out hitInfo);
        }

        public bool IntersectPoint(Vector2 point)
        {
            return UtilsCollision2D.PointBox(point, shape);
        }

        public override string ToString()
        {
            return $"Box Collider: {shape}";
        }
    }

}