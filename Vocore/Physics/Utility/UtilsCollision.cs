using System;
using System.Runtime.CompilerServices;


using Unity.Mathematics;

namespace Vocore
{
    public static class UtilsCollision
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SphereSphere(ShapeSphere sphere1, ShapeSphere sphere2)
        {
            float3 difference = sphere1.center - sphere2.center;
            float distanceSquared = math.dot(difference, difference);
            float sumRadius = sphere1.radius + sphere2.radius;
            return distanceSquared < sumRadius * sumRadius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoxSphere(ShapeBox box, ShapeSphere sphere)
        {
            float3 sphereCenter = math.mul(math.inverse(box.rotation), sphere.center - box.center);

            float3 closestPoint = math.clamp(sphereCenter, -box.extends, box.extends);

            float3 difference = closestPoint - sphereCenter;
            float distanceSquared = math.dot(difference, difference);
            return distanceSquared < sphere.radius * sphere.radius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoxBox(ShapeBox box1, ShapeBox box2)
        {
            if (box1.rotation.Equals(quaternion.identity) && box2.rotation.Equals(quaternion.identity))
            {
                return BoxBoxAxisAligned(box1, box2);
            }

            return IntersectAABBWorldToLocal(box1, box2) && IntersectAABBWorldToLocal(box2, box1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IntersectAABBWorldToLocal(ShapeBox world, ShapeBox toLocal)
        {
            BoundingBox worldBox = new BoundingBox(-world.extends, world.extends);
            BoundingBox localBox = toLocal.GetBoundingBox(math.inverse(new RigidTransform(world.rotation, world.center)));
            return worldBox.Intersects(localBox);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RaySphere(float3 rayOrigin, float3 rayDisplacement, float3 sphereCenter, float sphereRadius, ref float fraction, out float3 normal)
        {
            normal = float3.zero;
            float3 diff = rayOrigin - sphereCenter;
            float a = math.dot(rayDisplacement, rayDisplacement);
            float b = 2f * math.dot(rayDisplacement, diff);
            float c = math.dot(diff, diff) - sphereRadius * sphereRadius;
            float discriminant = b * b - 4f * a * c;
            if (c < 0f)
            {
                fraction = 0f;
                normal = math.normalize(-rayDisplacement);
                return true;
            }
            if (discriminant < 0f)
            {
                return false;
            }
            float sqrtDiscriminant = math.sqrt(discriminant);
            float invDenom = 0.5f / a;
            float t0 = (sqrtDiscriminant - b) * invDenom;
            float t1 = (0f - sqrtDiscriminant - b) * invDenom;
            float tMin = math.min(t0, t1);
            if (tMin >= 0f && tMin < fraction)
            {
                fraction = tMin;
                normal = (rayOrigin + rayDisplacement * fraction - sphereCenter) / sphereRadius;
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RaySphere(Ray ray, ShapeSphere sphere, out RaycastHit hit)
        {
            hit = new RaycastHit();
            float fraction = float.MaxValue;
            float3 normal = float3.zero;
            hit = default;

            if (RaySphere(ray.origin, ray.displacement, sphere.center, sphere.radius, ref fraction, out normal))
            {
                hit.point = ray.origin + ray.displacement * fraction;
                hit.normal = normal;
                hit.fraction = fraction;
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RayAABB(Ray ray, BoundingBox boundingBox, ref float fraction, out float3 normal)
        {
            normal = float3.zero;
            float3 rayOrigin = ray.origin;
            float3 rayDisplacement = ray.displacement;
            float3 min = boundingBox.min;
            float3 max = boundingBox.max;

            float3 invRayDisplacement = 1f / rayDisplacement;

            float txmin = (min.x - rayOrigin.x) * invRayDisplacement.x;
            float txmax = (max.x - rayOrigin.x) * invRayDisplacement.x;
            float inverseX = 1f;

            if (txmin > txmax)
            {
                inverseX = -1f;
                float temp = txmin;
                txmin = txmax;
                txmax = temp;
            }

            float tmin = txmin;
            float tmax = txmax;

            float tymin = (min.y - rayOrigin.y) * invRayDisplacement.y;
            float tymax = (max.y - rayOrigin.y) * invRayDisplacement.y;
            float inverseY = 1f;

            if (tymin > tymax)
            {
                inverseY = -1f;
                float temp = tymin;
                tymin = tymax;
                tymax = temp;
            }

            if ((tmin > tymax) || (tymin > tmax))
            {
                return false;
            }

            if (tymin > tmin)
            {
                tmin = tymin;
            }

            if (tymax < tmax)
            {
                tmax = tymax;
            }

            float tzmin = (min.z - rayOrigin.z) * invRayDisplacement.z;
            float tzmax = (max.z - rayOrigin.z) * invRayDisplacement.z;
            float inverseZ = 1f;

            if (tzmin > tzmax)
            {
                inverseZ = -1f;
                float temp = tzmin;
                tzmin = tzmax;
                tzmax = temp;
            }

            if ((tmin > tzmax) || (tzmin > tmax))
            {
                return false;
            }

            if (tzmin > tmin)
            {
                tmin = tzmin;
            }

            if (tzmax < tmax)
            {
                tmax = tzmax;
            }

            fraction = tmin;
            //todo: calculate normal of hit face
            if (txmin == tmin)
            {
                normal = new float3(-1.0f, 0.0f, 0.0f) * inverseX;
            }
            else if (txmax == tmin)
            {
                normal = new float3(1.0f, 0.0f, 0.0f) * inverseX;
            }
            else if (tymin == tmin)
            {
                normal = new float3(0.0f, -1.0f, 0.0f) * inverseY;
            }
            else if (tymax == tmin)
            {
                normal = new float3(0.0f, 1.0f, 0.0f) * inverseY;
            }
            else if (tzmin == tmin)
            {
                normal = new float3(0.0f, 0.0f, -1.0f) * inverseZ;
            }
            else if (tzmax == tmin)
            {
                normal = new float3(0.0f, 0.0f, 1.0f) *inverseZ;
            }


            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RayAABB(Ray ray, BoundingBox boundingBox)
        {
            float3 invRayDisplacement = 1f / ray.displacement;
            float3 originToMin = boundingBox.min - ray.origin;
            float3 originToMax = boundingBox.max - ray.origin;

            float txmin = originToMin.x * invRayDisplacement.x;
            float txmax = originToMax.x * invRayDisplacement.x;

            float temp;

            if (txmin > txmax)
            {
                temp = txmin;
                txmin = txmax;
                txmax = temp;
            }

            float tmin = txmin;
            float tmax = txmax;

            float tymin = originToMin.y * invRayDisplacement.y;
            float tymax = originToMax.y * invRayDisplacement.y;

            if (tymin > tymax)
            {
                temp = tymin;
                tymin = tymax;
                tymax = temp;
            }

            if ((tmin > tymax) || (tymin > tmax))
            {
                return false;
            }

            if (tymin > tmin)
            {
                tmin = tymin;
            }

            if (tymax < tmax)
            {
                tmax = tymax;
            }

            float tzmin = originToMin.z * invRayDisplacement.z;
            float tzmax = originToMax.z * invRayDisplacement.z;

            if (tzmin > tzmax)
            {
                temp = tzmin;
                tzmin = tzmax;
                tzmax = temp;
            }

            if ((tmin > tzmax) || (tzmin > tmax))
            {
                return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RayBox(Ray ray, ShapeBox box, out RaycastHit hit)
        {
            hit = default;
            BoundingBox localAABB = new BoundingBox(-box.extends, box.extends);

            float3 rayOriginLocal = math.transform(math.inverse(new RigidTransform(box.rotation, box.center)), ray.origin);
            float3 rayDisplacementLocal = math.rotate(math.inverse(box.rotation), ray.displacement);

            Ray localRay = new Ray(rayOriginLocal, rayDisplacementLocal);

            float fraction = float.MaxValue;

            if (RayAABB(localRay, localAABB, ref fraction, out float3 normal))
            {
                hit.point = ray.origin + ray.displacement * fraction;
                hit.normal = math.rotate(box.rotation, normal);
                hit.fraction = fraction;
                return fraction>=0;
            }

            return false;
        }
    }
}

