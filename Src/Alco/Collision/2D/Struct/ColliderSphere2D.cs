using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;



namespace Alco
{
    /// <summary>
    /// A 2D sphere collider carrying the header required by the collision system.
    /// </summary>
    public struct ColliderSphere2D : ICollider2D
    {
        private readonly ColliderHeader2D _header;
        public ShapeSphere2D Shape;

        public ColliderHeader2D Header
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _header;
        }

        /// <summary>
        /// Initializes a sphere collider with its type header set.
        /// </summary>
        public ColliderSphere2D()
        {
            _header = new ColliderHeader2D
            {
                Type = ColliderType2D.Sphere
            };
        }

        /// <summary>
        /// Determines whether this collider overlaps another collider.
        /// </summary>
        /// <param name="other">The header of the other collider.</param>
        /// <returns>True when the colliders overlap.</returns>
        public unsafe bool CollidesWith(ColliderHeader2D* other)
        {
            switch (other->Type)
            {
                case ColliderType2D.Box:
                    return CollisionUtility2D.BoxSphere((*(ColliderBox2D*)other).Shape, Shape);
                case ColliderType2D.Sphere:
                    return CollisionUtility2D.SphereSphere(Shape, (*(ColliderSphere2D*)other).Shape);
            }
            return false;
        }

        /// <summary>
        /// Gets the axis-aligned bounds enclosing the collider.
        /// </summary>
        /// <returns>The bounding box of the collider shape.</returns>
        public BoundingBox2D GetBoundingBox()
        {
            return Shape.GetBoundingBox();
        }

        /// <summary>
        /// Tests whether the ray intersects the collider.
        /// </summary>
        /// <param name="ray">The ray to test.</param>
        /// <param name="hitInfo">The hit details when an intersection occurs.</param>
        /// <returns>True when the ray intersects the collider.</returns>
        public bool IntersectRay(Ray2D ray, out RaycastHit2D hitInfo)
        {
            return CollisionUtility2D.RaySphere(ray, Shape, out hitInfo);
        }

        /// <summary>
        /// Determines whether the collider contains the given point.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>True when the point lies inside the collider.</returns>
        public bool IntersectPoint(Vector2 point)
        {
            return CollisionUtility2D.PointSphere(point, Shape);
        }

        public override string ToString()
        {
            return $"Sphere Collider: {Shape}";
        }
    }

}