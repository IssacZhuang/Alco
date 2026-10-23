using System;
using System.Collections.Generic;

namespace Alco
{
    /// <summary>
    /// Result of a 2D ray cast, including the hit details and the collider that was hit.
    /// </summary>
    public struct RayCastResult2D
    {
        public bool Hit;
        public RaycastHit2D HitInfo;
        public ColliderRef2D Collider;

        public static readonly RayCastResult2D Default = new RayCastResult2D
        {
            Hit = false,
            HitInfo = new RaycastHit2D(),
            Collider = new ColliderRef2D()
        };

        public static readonly RayCastResult2D none = new RayCastResult2D
        {
            Hit = false,
            HitInfo = new RaycastHit2D(),
            Collider = new ColliderRef2D()
        };
    }
}

