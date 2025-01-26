using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco
{
    //Collider for BVH tree
    public unsafe interface ICollider2D : IShape2D
    {
        ColliderHeader2D Header { get; }
        bool CollidesWith(ColliderHeader2D* other);
        bool IntersectRay(Ray2D ray, out RaycastHit2D hitInfo);
        bool IntersectPoint(Vector2 point);
    }
}