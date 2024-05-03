using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;


namespace Vocore
{
    public unsafe struct ColliderRef3D
    {
        private ColliderType3D _type;
        private ColliderHeader3D* _ptr;
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

        internal T DebugGetCollder<T>() where T : unmanaged, ICollider3D
        {
            return *(T*)_ptr;
        }
    }
}

