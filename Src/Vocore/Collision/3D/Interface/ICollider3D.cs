using System;
using System.Collections.Generic;

namespace Vocore
{
    //Collider for BVH tree
    public interface ICollider3D : IShape3D
    {
        bool CollidesWith<T>(T other) where T : unmanaged, ICollider3D;
        bool IntersectRay(Ray3D ray, out RaycastHit3D hitInfo);
        ColliderType Type { get; }
    }
}