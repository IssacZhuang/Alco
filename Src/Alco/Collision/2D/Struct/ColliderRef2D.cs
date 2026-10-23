using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;


namespace Alco
{
    /// <summary>
    /// The umnanaged reference to a collider data.
    /// </summary>
    public unsafe struct ColliderRef2D
    {
        private ColliderType2D _type;
        private ColliderHeader2D* _ptr;
        /// <summary>
        /// Used for index the target collider in the <see cref="CollisionWorld3D"/> 
        /// <br/> Also can be the custom data for the caster collider.
        /// </summary>
        public int UserData;

        public bool HasCollider
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _ptr != null;
        }
        public ColliderHeader2D* UnsafePointer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _ptr;
        }

        public ColliderType2D Type
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _type;
        }

        /// <summary>
        /// Creates a reference from a pointer to an unmanaged collider.
        /// </summary>
        /// <typeparam name="T">The collider type.</typeparam>
        /// <param name="collider">The collider to reference; it must stay alive while the reference is used.</param>
        /// <returns>A reference to the collider.</returns>
        public static ColliderRef2D Create<T>(T* collider) where T : unmanaged, ICollider2D
        {
            return new ColliderRef2D
            {
                _ptr = (ColliderHeader2D*)collider,
                _type = collider->Header.Type
            };
        }

        /// <summary>
        /// Determines whether this collider overlaps another collider.
        /// </summary>
        /// <param name="other">The other collider reference.</param>
        /// <returns>True when the colliders overlap.</returns>
        public bool CollidesWith(ColliderRef2D other)
        {
            switch (_type)
            {
                case ColliderType2D.Box:
                    return (*(ColliderBox2D*)_ptr).CollidesWith(other.UnsafePointer);
                case ColliderType2D.Sphere:
                    return (*(ColliderSphere2D*)_ptr).CollidesWith(other.UnsafePointer);
            }
            return false;
        }

        /// <summary>
        /// Gets the axis-aligned bounds enclosing the collider.
        /// </summary>
        /// <returns>The bounding box of the collider; empty when no collider is set.</returns>
        public BoundingBox2D GetBoundingBox()
        {
            switch (_type)
            {
                case ColliderType2D.Box:
                    return (*(ColliderBox2D*)_ptr).GetBoundingBox();
                case ColliderType2D.Sphere:
                    return (*(ColliderSphere2D*)_ptr).GetBoundingBox();
            }
            return new BoundingBox2D();
        }

        /// <summary>
        /// Tests whether the ray intersects the collider.
        /// </summary>
        /// <param name="ray">The ray to test.</param>
        /// <param name="hitInfo">The hit details when an intersection occurs.</param>
        /// <returns>True when the ray intersects the collider.</returns>
        public bool IntersectRay(Ray2D ray, out RaycastHit2D hitInfo)
        {
            switch (_type)
            {
                case ColliderType2D.Box:
                    return (*(ColliderBox2D*)_ptr).IntersectRay(ray, out hitInfo);
                case ColliderType2D.Sphere:
                    return (*(ColliderSphere2D*)_ptr).IntersectRay(ray, out hitInfo);
            }
            hitInfo = new RaycastHit2D();
            return false;
        }

        /// <summary>
        /// Determines whether the collider contains the given point.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>True when the point lies inside the collider.</returns>
        public bool IntersectPoint(Vector2 point)
        {
            switch (_type)
            {
                case ColliderType2D.Box:
                    return (*(ColliderBox2D*)_ptr).IntersectPoint(point);
                case ColliderType2D.Sphere:
                    return (*(ColliderSphere2D*)_ptr).IntersectPoint(point);
            }
            return false;
        }

        internal T DebugGetCollder<T>() where T : unmanaged, ICollider2D
        {
            return *(T*)_ptr;
        }
    }
}

