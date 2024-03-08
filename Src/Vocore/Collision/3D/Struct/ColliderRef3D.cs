using System;
using System.Collections.Generic;


namespace Vocore
{
    public unsafe struct ColliderRef3D : ICollider3D
    {
        private void* _ptr;

        private ColliderType _type;

        public bool HasCollider => _ptr != null;

        public static ColliderRef3D Create<T>(T* collider) where T : unmanaged, ICollider3D
        {

            return new ColliderRef3D
            {
                _ptr = collider,
                _type = (*collider).type
            };
        }

        public ColliderType type => _type;

        public bool CollidesWith<T>(T other) where T : unmanaged, ICollider3D
        {
            switch (_type)
            {
                case ColliderType.Box:
                    return (*(ColliderBox3D*)_ptr).CollidesWith(other);
                case ColliderType.Sphere:
                    return (*(ColliderSphere3D*)_ptr).CollidesWith(other);
            }
            return false;
        }

        public BoundingBox3D GetBoundingBox()
        {
            switch (_type)
            {
                case ColliderType.Box:
                    return (*(ColliderBox3D*)_ptr).GetBoundingBox();
                case ColliderType.Sphere:
                    return (*(ColliderSphere3D*)_ptr).GetBoundingBox();
            }
            return new BoundingBox3D();
        }

        public bool IntersectRay(Ray3D ray, out RaycastHit3D hitInfo)
        {
            switch (_type)
            {
                case ColliderType.Box:
                    return (*(ColliderBox3D*)_ptr).IntersectRay(ray, out hitInfo);
                case ColliderType.Sphere:
                    return (*(ColliderSphere3D*)_ptr).IntersectRay(ray, out hitInfo);
            }
            hitInfo = new RaycastHit3D();
            return false;
        }
    }
}

