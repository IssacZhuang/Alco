using System;
using System.Collections.Generic;

namespace Alco
{
    /// <summary>
    /// Result of a 3D collider cast, identifying whether and which collider was hit.
    /// </summary>
    public struct ColliderCastResult3D
    {
        public bool Hit;
        public ColliderRef3D Collider;

        public static readonly ColliderCastResult3D Default = new ColliderCastResult3D
        {
            Hit = false,
            Collider = new ColliderRef3D()
        };

        public static readonly ColliderCastResult3D None = new ColliderCastResult3D
        {
            Hit = false,
            Collider = new ColliderRef3D()
        };
    }
}

