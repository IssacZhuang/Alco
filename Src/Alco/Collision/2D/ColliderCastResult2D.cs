using System;
using System.Collections.Generic;

namespace Alco
{
    public struct ColliderCastResult2D
    {
        public bool hit;
        public ColliderRef2D collider;

        public static readonly ColliderCastResult2D Default = new ColliderCastResult2D
        {
            hit = false,
            collider = new ColliderRef2D()
        };

        public static readonly ColliderCastResult2D None = new ColliderCastResult2D
        {
            hit = false,
            collider = new ColliderRef2D()
        };
    }
}

