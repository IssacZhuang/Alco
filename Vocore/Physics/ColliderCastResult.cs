using System;
using System.Collections.Generic;

namespace Vocore
{
    public struct ColliderCastResult
    {
        public bool hit;
        public ColliderRef collider;

        public static readonly ColliderCastResult Default = new ColliderCastResult
        {
            hit = false,
            collider = new ColliderRef()
        };
        
        public static readonly ColliderCastResult None = new ColliderCastResult
        {
            hit = false,
            collider = new ColliderRef()
        };
    }
}

