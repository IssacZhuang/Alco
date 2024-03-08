using System;
using System.Collections.Generic;

namespace Vocore
{
    public struct RayCastResult2D
    {
        public bool hit;
        public RaycastHit2D hitInfo;
        public ColliderRef2D collider;

        public static readonly RayCastResult2D Default = new RayCastResult2D
        {
            hit = false,
            hitInfo = new RaycastHit2D(),
            collider = new ColliderRef2D()
        };

        public static readonly RayCastResult2D none = new RayCastResult2D
        {
            hit = false,
            hitInfo = new RaycastHit2D(),
            collider = new ColliderRef2D()
        };
    }
}

