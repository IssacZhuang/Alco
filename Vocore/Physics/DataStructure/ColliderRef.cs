using System;
using System.Collections.Generic;


namespace Vocore
{
    public unsafe struct ColliderRef: ICollider
    {
        private void* _ptr;

        private ColliderType _type;

        public bool HasCollider => _ptr != null;

        public static ColliderRef Create<T>(T* collider) where T : unmanaged, ICollider
        {

            return new ColliderRef
            {
                _ptr = collider,
                _type = (*collider).type
            };
        }

        public ColliderType type => _type;

        public bool CollidesWith<T>(T other) where T : unmanaged, ICollider
        {
            switch (_type)
            {
                case ColliderType.Box:
                    return (*(ColliderBox*)_ptr).CollidesWith(other);
                case ColliderType.Sphere:
                    return (*(ColliderSphere*)_ptr).CollidesWith(other);
            }
            return false;
        }

        public BoundingBox GetBoundingBox()
        {
            switch (_type)
            {
                case ColliderType.Box:
                    return (*(ColliderBox*)_ptr).GetBoundingBox();
                case ColliderType.Sphere:
                    return (*(ColliderSphere*)_ptr).GetBoundingBox();
            }
            return new BoundingBox();
        }

        public BoundingBox GetBoundingBox(Tranform transform)
        {
            switch (_type)
            {
                case ColliderType.Box:
                    return (*(ColliderBox*)_ptr).GetBoundingBox(transform);
                case ColliderType.Sphere:
                    return (*(ColliderSphere*)_ptr).GetBoundingBox(transform);
            }
            return new BoundingBox();
        }

        public bool IntersectRay(Ray ray, out RaycastHit hitInfo)
        {
            switch (_type)
            {
                case ColliderType.Box:
                    return (*(ColliderBox*)_ptr).IntersectRay(ray, out hitInfo);
                case ColliderType.Sphere:
                    return (*(ColliderSphere*)_ptr).IntersectRay(ray, out hitInfo);
            }
            hitInfo = new RaycastHit();
            return false;
        }
    }
}

