using System;
using System.Collections.Generic;
using System.Numerics;

namespace Vocore
{
    //Collider for BVH tree
    public unsafe interface ICollider3D : IShape3D
    {
        ColliderHeader3D Header { get; }
        bool CollidesWith(ColliderHeader3D* other);
        bool IntersectRay(Ray3D ray, out RaycastHit3D hitInfo);
        bool IntersectPoint(Vector3 point);
    }
}