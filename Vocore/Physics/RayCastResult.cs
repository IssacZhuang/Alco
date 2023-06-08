using System;
using System.Collections.Generic;

namespace Vocore
{
    public struct RayCastResult
    {
        public bool hit;
        public RaycastHit hitInfo;
        public ColliderRef collider;

        public static RayCastResult none = new RayCastResult
        {
            hit = false,
            hitInfo = new RaycastHit(),
            collider = new ColliderRef()
        };
    }
}

