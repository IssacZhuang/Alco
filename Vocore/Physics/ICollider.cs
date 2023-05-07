using System;
using System.Collections.Generic;

namespace Vocore
{
    //Collider for BVH tree
    public interface ICollider
    {
        BoundingBox GetBoundingBox();
        bool CollidesWith(ICollider other);
    }
}