using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;



namespace Alco
{
    public struct ColliderBox3D : ICollider3D
    {
        private readonly ColliderHeader3D _header;
        public ShapeBox3D Shape;

        public ColliderHeader3D Header
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _header;
        }

        public ColliderBox3D()
        {
            _header = new ColliderHeader3D
            {
                Type = ColliderType3D.Box
            };
        }

        public unsafe bool CollidesWith(ColliderHeader3D* other)
        {
            switch (other->Type)
            {
                case ColliderType3D.Box:
                    return CollisionUtility3D.BoxBox(Shape, (*(ColliderBox3D*)other).Shape);
                case ColliderType3D.Sphere:
                    return CollisionUtility3D.BoxSphere(Shape, (*(ColliderSphere3D*)other).shape);
            }
            return false;
        }

        public BoundingBox3D GetBoundingBox()
        {
            return Shape.GetBoundingBox();
        }

        public bool IntersectRay(Ray3D ray, out RaycastHit3D hitInfo)
        {
            return CollisionUtility3D.RayBox(ray, Shape, out hitInfo);
        }

        public bool IntersectPoint(Vector3 point)
        {
            return CollisionUtility3D.PointBox(point, Shape);
        }

        public override string ToString()
        {
            return $"Box Collider: {Shape}";
        }

        
    }

}