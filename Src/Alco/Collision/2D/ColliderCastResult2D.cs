using System;
using System.Collections.Generic;

namespace Alco
{
    /// <summary>
    /// Result of a 2D collider cast, identifying whether and which collider was hit.
    /// </summary>
    public struct ColliderCastResult2D
    {
        public bool Hit;
        public ColliderRef2D Collider;

        public static readonly ColliderCastResult2D Default = new ColliderCastResult2D
        {
            Hit = false,
            Collider = new ColliderRef2D()
        };

        public static readonly ColliderCastResult2D None = new ColliderCastResult2D
        {
            Hit = false,
            Collider = new ColliderRef2D()
        };
    }
}

