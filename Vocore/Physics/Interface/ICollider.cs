using System;
using System.Collections.Generic;

namespace Vocore
{
    //Collider for BVH tree
    public interface ICollider: IShape
    {
        bool CollidesWith<T>(T other) where T : unmanaged, ICollider;
        bool IntersectRay(Ray ray, out RaycastHit hitInfo);
        ColliderType type { get; }
    }
}