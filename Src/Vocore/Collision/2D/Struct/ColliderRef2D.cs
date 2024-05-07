using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;


namespace Vocore
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
        public int userData;

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

        public static ColliderRef2D Create<T>(T* collider) where T : unmanaged, ICollider2D
        {
            return new ColliderRef2D
            {
                _ptr = (ColliderHeader2D*)collider,
                _type = collider->Header.type
            };
        }

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

        internal T DebugGetCollder<T>() where T : unmanaged, ICollider2D
        {
            return *(T*)_ptr;
        }
    }
}

