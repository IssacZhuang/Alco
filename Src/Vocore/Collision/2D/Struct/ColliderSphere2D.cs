using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;



namespace Vocore
{
    public struct ColliderSphere2D : ICollider2D
    {
        private readonly ColliderHeader2D _header;
        public ShapeSphere2D shape;

        public ColliderHeader2D Header
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _header;
        }

        public ColliderSphere2D()
        {
            _header = new ColliderHeader2D
            {
                type = ColliderType2D.Sphere
            };
        }

        public unsafe bool CollidesWith(ColliderHeader2D* other)
        {
            switch (other->type)
            {
                case ColliderType2D.Box:
                    return UtilsCollision2D.BoxSphere((*(ColliderBox2D*)other).shape, shape);
                case ColliderType2D.Sphere:
                    return UtilsCollision2D.SphereSphere(shape, (*(ColliderSphere2D*)other).shape);
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