using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Vocore
{
    public unsafe struct ColliderRef: ICollider
    {
        private void* _ptr;

#pragma warning disable CS8500
        private ICollider* InnerCollider => (ICollider*)_ptr;
#pragma warning restore CS8500

        public static ColliderRef Create<T>(T* collider) where T : unmanaged, ICollider
        {
            return new ColliderRef
            {
                _ptr = collider
            };
        }

        public ColliderType type => InnerCollider->type;

        public bool CollidesWith(ICollider other)
        {
            return InnerCollider->CollidesWith(other);
        }

        public BoundingBox GetBoundingBox()
        {
            return InnerCollider->GetBoundingBox();
        }

        public BoundingBox GetBoundingBox(RigidTransform transform)
        {
            return InnerCollider->GetBoundingBox(transform);
        }
    }
}

