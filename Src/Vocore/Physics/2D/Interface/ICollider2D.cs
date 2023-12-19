using System;
using System.Collections.Generic;

namespace Vocore
{
    //Collider for BVH tree
    public interface ICollider2D : IShape2D
    {
        bool CollidesWith<T>(T other) where T : unmanaged, ICollider2D;
        bool IntersectRay(Ray3D ray, out RaycastHit3D hitInfo);
        ColliderType type { get; }
    }
}