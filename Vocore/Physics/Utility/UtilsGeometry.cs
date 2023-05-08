using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore
{
    public static class UtilsGeometry
    {
        public static bool IntersectsBoxSphere(ShapeBox box, ShapeSphere sphere)
        {
            Vector3 localCenter = Quaternion.Inverse(box.rotation) * (sphere.center - box.center);

            Vector3 closestPoint = new Vector3(
                Mathf.Clamp(localCenter.x, -box.extends.x, box.extends.x),
                Mathf.Clamp(localCenter.y, -box.extends.y, box.extends.y),
                Mathf.Clamp(localCenter.z, -box.extends.z, box.extends.z));

            float distance = localCenter.SqrMagnitude(closestPoint);

            if (distance <= sphere.radius * sphere.radius)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}

