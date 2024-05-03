using System;
using System.Collections.Generic;

namespace Vocore
{
    //Collider for BVH tree
    public unsafe interface ICollider3D : IShape3D
    {
        ColliderHeader3D Header { get; }
        bool CollidesWith(ColliderHeader3D* other);
        bool IntersectRay(Ray3D ray, out RaycastHit3D hitInfo);
    }
}