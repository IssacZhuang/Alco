using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;



namespace Alco
{
    public struct ColliderSphere3D : ICollider3D
    {
        private readonly ColliderHeader3D _header;
        public ShapeSphere3D shape;

        public ColliderHeader3D Header
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _header;
        }

        public ColliderSphere3D()
        {
            _header = new ColliderHeader3D
            {
                type = ColliderType3D.Sphere
            };
        }

        public unsafe bool CollidesWith(ColliderHeader3D* other)
        {
            switch (other->type)
            {
                case ColliderType3D.Box:
                    return UtilsCollision3D.BoxSphere((*(ColliderBox3D*)other).shape, shape);
                case ColliderType3D.Sphere:
                    return UtilsCollision3D.SphereSphere(shape, (*(ColliderSphere3D*)other).shape);
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

        public bool IntersectPoint(Vector3 point)
        {
            return UtilsCollision3D.PointSphere(point, shape);
        }

        public override string ToString()
        {
            return $"Sphere Collider: {shape}";
        }
    }

}