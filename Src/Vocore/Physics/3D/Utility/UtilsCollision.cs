using System;
using System.Numerics;
using System.Runtime.CompilerServices;




namespace Vocore
{
    public static class UtilsCollision
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SphereSphere(ShapeSphere sphere1, ShapeSphere sphere2)
        {
            Vector3 difference = sphere1.center - sphere2.center;
            float distanceSquared = math.dot(difference, difference);
            float sumRadius = sphere1.radius + sphere2.radius;
            return distanceSquared < sumRadius * sumRadius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoxSphere(ShapeBox box, ShapeSphere sphere)
        {
            Vector3 sphereCenter = math.mul(math.inverse(box.rotation), sphere.center - box.center);

            Vector3 closestPoint = math.clamp(sphereCenter, -box.extends, box.extends);

            Vector3 difference = closestPoint - sphereCenter;
            float distanceSquared = math.dot(difference, difference);
            return distanceSquared < sphere.radius * sphere.radius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoxBox(ShapeBox box1, ShapeBox box2)
        {
            if (box1.rotation.Equals(Quaternion.Identity) && box2.rotation.Equals(Quaternion.Identity))
            {
                return BoxBoxAxisAligned(box1, box2);
            }

            return IntersectAABBWorldToLocal(box1, box2) && IntersectAABBWorldToLocal(box2, box1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoxBoxAxisAligned(ShapeBox box1, ShapeBox box2)
        {
            Vector3 min1 = box1.center - box1.extends;
            Vector3 max1 = box1.center + box1.extends;
            Vector3 min2 = box2.center - box2.extends;
            Vector3 max2 = box2.center + box2.extends;

            return min1.X <= max2.X && max1.X >= min2.X &&
                   min1.Y <= max2.Y && max1.Y >= min2.Y &&
                   min1.Z <= max2.Z && max1.Z >= min2.Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IntersectAABBWorldToLocal(ShapeBox world, ShapeBox toLocal)
        {
            BoundingBox worldBox = new BoundingBox(-world.extends, world.extends);
            BoundingBox localBox = toLocal.GetBoundingBox(math.inverse(new Transform(world.rotation, world.center)));
            return worldBox.Intersects(localBox);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RaySphere(Vector3 rayOrigin, Vector3 rayDisplacement, Vector3 sphereCenter, float sphereRadius, ref float fraction, out Vector3 normal)
        {
            normal = Vector3.Zero;
            Vector3 diff = rayOrigin - sphereCenter;
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
            Vector3 normal = Vector3.Zero;
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
        public static bool RayAABB(Ray ray, BoundingBox boundingBox, ref float fraction, out Vector3 normal)
        {
            normal = Vector3.Zero;
            Vector3 rayOrigin = ray.origin;
            Vector3 rayDisplacement = ray.displacement;
            Vector3 min = boundingBox.min;
            Vector3 max = boundingBox.max;

            Vector3 invRayDisplacement = math.reciprocal(rayDisplacement);//1f / rayDisplacement;

            float txmin = (min.X - rayOrigin.X) * invRayDisplacement.X;
            float txmax = (max.X - rayOrigin.X) * invRayDisplacement.X;
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

            float tymin = (min.Y - rayOrigin.Y) * invRayDisplacement.Y;
            float tymax = (max.Y - rayOrigin.Y) * invRayDisplacement.Y;
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

            float tzmin = (min.Z - rayOrigin.Z) * invRayDisplacement.Z;
            float tzmax = (max.Z - rayOrigin.Z) * invRayDisplacement.Z;
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
                normal = new Vector3(-1.0f, 0.0f, 0.0f) * inverseX;
            }
            else if (txmax == tmin)
            {
                normal = new Vector3(1.0f, 0.0f, 0.0f) * inverseX;
            }
            else if (tymin == tmin)
            {
                normal = new Vector3(0.0f, -1.0f, 0.0f) * inverseY;
            }
            else if (tymax == tmin)
            {
                normal = new Vector3(0.0f, 1.0f, 0.0f) * inverseY;
            }
            else if (tzmin == tmin)
            {
                normal = new Vector3(0.0f, 0.0f, -1.0f) * inverseZ;
            }
            else if (tzmax == tmin)
            {
                normal = new Vector3(0.0f, 0.0f, 1.0f) * inverseZ;
            }


            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RayAABB(Ray ray, BoundingBox boundingBox)
        {
            Vector3 invRayDisplacement = math.reciprocal(ray.displacement);//1f / ray.displacement;
            Vector3 originToMin = boundingBox.min - ray.origin;
            Vector3 originToMax = boundingBox.max - ray.origin;

            float txmin = originToMin.X * invRayDisplacement.X;
            float txmax = originToMax.X * invRayDisplacement.X;

            float temp;

            if (txmin > txmax)
            {
                temp = txmin;
                txmin = txmax;
                txmax = temp;
            }

            float tmin = txmin;
            float tmax = txmax;

            float tymin = originToMin.Y * invRayDisplacement.Y;
            float tymax = originToMax.Y * invRayDisplacement.Y;

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

            float tzmin = originToMin.Z * invRayDisplacement.Z;
            float tzmax = originToMax.Z * invRayDisplacement.Z;

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

            Vector3 rayOriginLocal = math.transform(math.inverse(new Transform(box.rotation, box.center)), ray.origin);
            Vector3 rayDisplacementLocal = math.rotate(math.inverse(box.rotation), ray.displacement);

            Ray localRay = new Ray(rayOriginLocal, rayDisplacementLocal);

            float fraction = float.MaxValue;

            if (RayAABB(localRay, localAABB, ref fraction, out Vector3 normal))
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

