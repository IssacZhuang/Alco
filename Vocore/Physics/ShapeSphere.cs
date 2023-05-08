using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore
{
    public struct ShapeSphere : IShape
    {
        public Vector3 center;
        public float radius;

        public ShapeSphere(Vector3 center, float radius)
        {
            this.center = center;
            this.radius = radius;
        }

        public BoundingBox GetBoundingBox()
        {
            return new BoundingBox(center - new Vector3(radius, radius, radius), center + new Vector3(radius, radius, radius));
        }
    }
}

