using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;



namespace Alco
{
    /// <summary>
    /// A 3D sphere collider carrying the header required by the collision system.
    /// </summary>
    public struct ColliderSphere3D : ICollider3D
    {
        private readonly ColliderHeader3D _header;
        public ShapeSphere3D shape;

        public ColliderHeader3D Header
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _header;
        }

        /// <summary>
        /// Initializes a sphere collider with its type header set.
        /// </summary>
        public ColliderSphere3D()
        {
            _header = new ColliderHeader3D
            {
                Type = ColliderType3D.Sphere
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
                    return CollisionUtility3D.BoxSphere((*(ColliderBox3D*)other).Shape, shape);
                case ColliderType3D.Sphere:
                    return CollisionUtility3D.SphereSphere(shape, (*(ColliderSphere3D*)other).shape);
            }
            return false;
        }

        /// <summary>
        /// Gets the axis-aligned bounds enclosing the collider.
        /// </summary>
        /// <returns>The bounding box of the collider shape.</returns>
        public BoundingBox3D GetBoundingBox()
        {
            return shape.GetBoundingBox();
        }

        /// <summary>
        /// Tests whether the ray intersects the collider.
        /// </summary>
        /// <param name="ray">The ray to test.</param>
        /// <param name="hitInfo">The hit details when an intersection occurs.</param>
        /// <returns>True when the ray intersects the collider.</returns>
        public bool IntersectRay(Ray3D ray, out RaycastHit3D hitInfo)
        {
            return CollisionUtility3D.RaySphere(ray, shape, out hitInfo);
        }

        /// <summary>
        /// Determines whether the collider contains the given point.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>True when the point lies inside the collider.</returns>
        public bool IntersectPoint(Vector3 point)
        {
            return CollisionUtility3D.PointSphere(point, shape);
        }

        public override string ToString()
        {
            return $"Sphere Collider: {shape}";
        }
    }

}