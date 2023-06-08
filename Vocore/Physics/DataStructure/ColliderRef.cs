using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Vocore
{
    public unsafe struct ColliderRef: ICollider
    {
        private void* _ptr;

        private ICollider InnerCollider
        {
            get
            {
                switch (_type)
                {
                    case ColliderType.Box:
                        return *(ColliderBox*)_ptr;
                    case ColliderType.Sphere:
                        return *(ColliderSphere*)_ptr;
                    default:
                        throw new NotImplementedException();
                }
            }
        }

        private ColliderType _type;

        public bool HasCollider => _ptr != null;

        public static ColliderRef Create<T>(ref T collider) where T : unmanaged, ICollider
        {
            fixed (void* ptr = &collider)
            {
                return new ColliderRef
                {
                    _ptr = ptr,
                    _type = collider.type
                };
            }
        }

        public ColliderType type => _type;

        public bool CollidesWith(ICollider other)
        {
            return InnerCollider.CollidesWith(other);
        }

        public BoundingBox GetBoundingBox()
        {
            return InnerCollider.GetBoundingBox();
        }

        public BoundingBox GetBoundingBox(RigidTransform transform)
        {
            return InnerCollider.GetBoundingBox(transform);
        }

        public bool IntersectRay(Ray ray, out RaycastHit hitInfo)
        {
            return InnerCollider.IntersectRay(ray, out hitInfo);
        }
    }
}

