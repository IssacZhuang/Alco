using System;
using System.Collections.Generic;

using Unity.Mathematics;

namespace Vocore
{
    public static class UtilsCollision
    {
        public static bool SphereSphere(ShapeSphere sphere1, ShapeSphere sphere2)
        {
            float3 difference = sphere1.center - sphere2.center;
            float distanceSquared = math.dot(difference, difference);
            float sumRadius = sphere1.radius + sphere2.radius;
            return distanceSquared < sumRadius * sumRadius;
        }

        public static bool BoxSphere(ShapeBox box, ShapeSphere sphere)
        {
            float3 sphereCenter = math.mul(math.inverse(box.rotation), sphere.center - box.center);

            float3 closestPoint = math.clamp(sphereCenter, -box.extends, box.extends);

            float3 difference = closestPoint - sphereCenter;
            float distanceSquared = math.dot(difference, difference);
            return distanceSquared < sphere.radius * sphere.radius;
        }

        public static bool BoxBox(ShapeBox box1, ShapeBox box2)
        {
            if (box1.rotation.Equals(quaternion.identity) && box2.rotation.Equals(quaternion.identity))
            {
                return BoxBoxAxisAligned(box1, box2);
            }

            return IntersectAABBWorldToLocal(box1, box2) && IntersectAABBWorldToLocal(box2, box1);
        }


        public static bool BoxBoxAxisAligned(ShapeBox box1, ShapeBox box2)
        {
            float3 min1 = box1.center - box1.extends;
            float3 max1 = box1.center + box1.extends;
            float3 min2 = box2.center - box2.extends;
            float3 max2 = box2.center + box2.extends;

            return min1.x <= max2.x && max1.x >= min2.x &&
                   min1.y <= max2.y && max1.y >= min2.y &&
                   min1.z <= max2.z && max1.z >= min2.z;
        }

        public static bool IntersectAABBWorldToLocal(ShapeBox world, ShapeBox toLocal)
        {
            BoundingBox worldBox = new BoundingBox(-world.extends, world.extends);
            BoundingBox localBox = toLocal.GetBoundingBox(math.inverse(new RigidTransform(world.rotation, world.center)));
            return worldBox.Intersects(localBox);
        }
    }
}

