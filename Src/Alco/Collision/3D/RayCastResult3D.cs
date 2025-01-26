using System;
using System.Collections.Generic;

namespace Alco
{
    public struct RayCastResult3D
    {
        public bool hit;
        public RaycastHit3D hitInfo;
        public ColliderRef3D collider;

        public static readonly RayCastResult3D Default = new RayCastResult3D
        {
            hit = false,
            hitInfo = new RaycastHit3D(),
            collider = new ColliderRef3D()
        };

        public static readonly RayCastResult3D none = new RayCastResult3D
        {
            hit = false,
            hitInfo = new RaycastHit3D(),
            collider = new ColliderRef3D()
        };
    }
}

