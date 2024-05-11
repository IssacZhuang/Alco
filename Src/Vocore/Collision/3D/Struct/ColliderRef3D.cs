using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;


namespace Vocore
{
    /// <summary>
    /// The umnanaged reference to a collider data.
    /// </summary>
    public unsafe struct ColliderRef3D
    {
        private ColliderType3D _type;
        private ColliderHeader3D* _ptr;
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
        public ColliderHeader3D* UnsafePointer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _ptr;
        }

        public ColliderType3D Type
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _type;
        }

        public static ColliderRef3D Create<T>(T* collider) where T : unmanaged, ICollider3D
        {

            return new ColliderRef3D
            {
                _ptr = (ColliderHeader3D*)collider,
                _type = collider->Header.type
            };
        }



        public bool CollidesWith(ColliderRef3D other)
        {
            switch (_type)
            {
                case ColliderType3D.Box:
                    return (*(ColliderBox3D*)_ptr).CollidesWith(other.UnsafePointer);
                case ColliderType3D.Sphere:
                    return (*(ColliderSphere3D*)_ptr).CollidesWith(other.UnsafePointer);
            }
            return false;
        }

        public BoundingBox3D GetBoundingBox()
        {
            switch (_type)
            {
                case ColliderType3D.Box:
                    return (*(ColliderBox3D*)_ptr).GetBoundingBox();
                case ColliderType3D.Sphere:
                    return (*(ColliderSphere3D*)_ptr).GetBoundingBox();
            }
            return new BoundingBox3D();
        }

        public bool IntersectRay(Ray3D ray, out RaycastHit3D hitInfo)
        {
            switch (_type)
            {
                case ColliderType3D.Box:
                    return (*(ColliderBox3D*)_ptr).IntersectRay(ray, out hitInfo);
                case ColliderType3D.Sphere:
                    return (*(ColliderSphere3D*)_ptr).IntersectRay(ray, out hitInfo);
            }
            hitInfo = new RaycastHit3D();
            return false;
        }

        public bool IntersectPoint(Vector3 point)
        {
            switch (_type)
            {
                case ColliderType3D.Box:
                    return (*(ColliderBox3D*)_ptr).IntersectPoint(point);
                case ColliderType3D.Sphere:
                    return (*(ColliderSphere3D*)_ptr).IntersectPoint(point);
            }
            return false;
        }

        internal T DebugGetCollder<T>() where T : unmanaged, ICollider3D
        {
            return *(T*)_ptr;
        }

    }
}

