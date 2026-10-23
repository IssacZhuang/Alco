using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;



namespace Alco
{
    /// <summary>
    /// A 3D box collider carrying the header required by the collision system.
    /// </summary>
    public struct ColliderBox3D : ICollider3D
    {
        private readonly ColliderHeader3D _header;
        public ShapeBox3D Shape;

        public ColliderHeader3D Header
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _header;
        }

        /// <summary>
        /// Initializes a box collider with its type header set.
        /// </summary>
        public ColliderBox3D()
        {
            _header = new ColliderHeader3D
            {
                Type = ColliderType3D.Box
            };
        }

        /// <summary>
        /// Determines whether this collider overlaps another collider.
        /// </summary>
        /// <param name="other">The header of the other collider.</param>
        /// <returns>True when the colliders overlap.</returns>
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

        /// <summary>
        /// Gets the axis-aligned bounds enclosing the collider.
        /// </summary>
        /// <returns>The bounding box of the collider shape.</returns>
        public BoundingBox3D GetBoundingBox()
        {
            return Shape.GetBoundingBox();
        }

        /// <summary>
        /// Tests whether the ray intersects the collider.
        /// </summary>
        /// <param name="ray">The ray to test.</param>
        /// <param name="hitInfo">The hit details when an intersection occurs.</param>
        /// <returns>True when the ray intersects the collider.</returns>
        public bool IntersectRay(Ray3D ray, out RaycastHit3D hitInfo)
        {
            return CollisionUtility3D.RayBox(ray, Shape, out hitInfo);
        }

        /// <summary>
        /// Determines whether the collider contains the given point.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>True when the point lies inside the collider.</returns>
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