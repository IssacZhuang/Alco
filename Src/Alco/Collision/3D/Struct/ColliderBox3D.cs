using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;



namespace Alco
{
    public struct ColliderBox3D : ICollider3D
    {
        private readonly ColliderHeader3D _header;
        public ShapeBox3D shape;

        public ColliderHeader3D Header
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _header;
        }

        public ColliderBox3D()
        {
            _header = new ColliderHeader3D
            {
                type = ColliderType3D.Box
            };
        }

        public unsafe bool CollidesWith(ColliderHeader3D* other)
        {
            switch (other->type)
            {
                case ColliderType3D.Box:
                    return UtilsCollision3D.BoxBox(shape, (*(ColliderBox3D*)other).shape);
                case ColliderType3D.Sphere:
                    return UtilsCollision3D.BoxSphere(shape, (*(ColliderSphere3D*)other).shape);
            }
            return false;
        }

        public BoundingBox3D GetBoundingBox()
        {
            return shape.GetBoundingBox();
        }

        public bool IntersectRay(Ray3D ray, out RaycastHit3D hitInfo)
        {
            return UtilsCollision3D.RayBox(ray, shape, out hitInfo);
        }

        public bool IntersectPoint(Vector3 point)
        {
            return UtilsCollision3D.PointBox(point, shape);
        }

        public override string ToString()
        {
            return $"Box Collider: {shape}";
        }

        
    }

}