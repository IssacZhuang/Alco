using System;
using System.Collections.Generic;

namespace Alco
{
    public struct ColliderCastResult3D
    {
        public bool hit;
        public ColliderRef3D collider;

        public static readonly ColliderCastResult3D Default = new ColliderCastResult3D
        {
            hit = false,
            collider = new ColliderRef3D()
        };

        public static readonly ColliderCastResult3D None = new ColliderCastResult3D
        {
            hit = false,
            collider = new ColliderRef3D()
        };
    }
}

