using System;
using System.Collections.Generic;

namespace Alco
{
    /// <summary>
    /// Result of a 3D ray cast, including the hit details and the collider that was hit.
    /// </summary>
    public struct RayCastResult3D
    {
        public bool Hit;
        public RaycastHit3D HitInfo;
        public ColliderRef3D Collider;

        public static readonly RayCastResult3D Default = new RayCastResult3D
        {
            Hit = false,
            HitInfo = new RaycastHit3D(),
            Collider = new ColliderRef3D()
        };

        public static readonly RayCastResult3D none = new RayCastResult3D
        {
            Hit = false,
            HitInfo = new RaycastHit3D(),
            Collider = new ColliderRef3D()
        };
    }
}

