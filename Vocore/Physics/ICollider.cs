using System;
using System.Collections.Generic;

namespace Vocore
{
    //Collider for BVH tree
    public interface ICollider: IShape
    {
        bool CollidesWith(ICollider other);
    }
}