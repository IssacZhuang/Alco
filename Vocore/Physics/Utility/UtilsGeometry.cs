using System;
using System.Collections.Generic;

using Unity.Mathematics;

namespace Vocore
{
    public static class UtilsGeometry
    {
        public static bool IntersectsBoxSphere(ShapeBox box, ShapeSphere sphere)
        {
            float3 sphereCenter = math.mul(math.inverse(box.rotation), sphere.center - box.center);

            float3 closestPoint = math.clamp(sphereCenter, -box.extends, box.extends);

            float3 difference = closestPoint - sphereCenter;
            float distanceSquared = math.dot(difference, difference);
            return distanceSquared < sphere.radius * sphere.radius;
        }
    }
}

