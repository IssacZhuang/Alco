using System;
using System.Numerics;
using System.Runtime.CompilerServices;




namespace Alco
{
    public static class UtilsCollision3D
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SphereSphere(ShapeSphere3D sphere1, ShapeSphere3D sphere2)
        {
            Vector3 difference = sphere1.Center - sphere2.Center;
            float distanceSquared = math.dot(difference, difference);
            float sumRadius = sphere1.Radius + sphere2.Radius;
            return distanceSquared < sumRadius * sumRadius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoxSphere(ShapeBox3D box, ShapeSphere3D sphere)
        {
            Vector3 sphereCenter = math.mul(sphere.Center - box.Center, math.inverse(box.Rotation));

            Vector3 closestPoint = math.clamp(sphereCenter, -box.Extends, box.Extends);

            Vector3 difference = closestPoint - sphereCenter;
            float distanceSquared = math.dot(difference, difference);
            return distanceSquared < sphere.Radius * sphere.Radius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoxBox(ShapeBox3D box1, ShapeBox3D box2)
        {
            if (box1.Rotation.Equals(Quaternion.Identity) && box2.Rotation.Equals(Quaternion.Identity))
            {
                return BoxBoxAxisAligned(box1, box2);
            }

            return IntersectAABBWorldToLocal(box1, box2) && IntersectAABBWorldToLocal(box2, box1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoxBoxAxisAligned(ShapeBox3D box1, ShapeBox3D box2)
        {
            Vector3 min1 = box1.Center - box1.Extends;
            Vector3 max1 = box1.Center + box1.Extends;
            Vector3 min2 = box2.Center - box2.Extends;
            Vector3 max2 = box2.Center + box2.Extends;

            return min1.X <= max2.X && max1.X >= min2.X &&
                   min1.Y <= max2.Y && max1.Y >= min2.Y &&
                   min1.Z <= max2.Z && max1.Z >= min2.Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IntersectAABBWorldToLocal(ShapeBox3D world, ShapeBox3D toLocal)
        {
            BoundingBox3D worldBox = new BoundingBox3D(-world.Extends, world.Extends);
            Quaternion invRot = math.inverse(world.Rotation);

            toLocal.Center = math.rotate(toLocal.Center - world.Center, invRot);
            toLocal.Rotation = math.mul(toLocal.Rotation, invRot);
            
            BoundingBox3D localBox = toLocal.GetBoundingBox();
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
        public static bool RaySphere(Ray3D ray, ShapeSphere3D sphere, out RaycastHit3D hit)
        {
            hit = new RaycastHit3D();
            float fraction = float.MaxValue;
            Vector3 normal = Vector3.Zero;
            hit = default;

            if (RaySphere(ray.Origin, ray.Displacement, sphere.Center, sphere.Radius, ref fraction, out normal))
            {
                hit.Point = ray.Origin + ray.Displacement * fraction;
                hit.Normal = normal;
                hit.Fraction = fraction;
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RayAABB(Ray3D ray, BoundingBox3D boundingBox, ref float fraction, out Vector3 normal)
        {
            normal = Vector3.Zero;
            Vector3 rayOrigin = ray.Origin;
            Vector3 rayDisplacement = ray.Displacement;
            Vector3 min = boundingBox.Min;
            Vector3 max = boundingBox.Max;

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
        public static bool RayAABB(Ray3D ray, BoundingBox3D boundingBox)
        {
            Vector3 invRayDisplacement = math.reciprocal(ray.Displacement);//1f / ray.displacement;
            Vector3 originToMin = boundingBox.Min - ray.Origin;
            Vector3 originToMax = boundingBox.Max - ray.Origin;

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
        public static bool RayBox(Ray3D ray, ShapeBox3D box, out RaycastHit3D hit)
        {
            hit = default;
            BoundingBox3D localAABB = new BoundingBox3D(-box.Extends, box.Extends);
            Quaternion invRot =math.inverse(box.Rotation);
            //Vector3 rayOriginLocal = math.transform(math.inverse(new Transform3D(box.rotation, box.center)), ray.origin);
            Vector3 rayOriginLocal = math.rotate(invRot, ray.Origin - box.Center);
            Vector3 rayDisplacementLocal = math.rotate(invRot, ray.Displacement);

            Ray3D localRay = new Ray3D(rayOriginLocal, rayDisplacementLocal);

            float fraction = float.MaxValue;

            if (RayAABB(localRay, localAABB, ref fraction, out Vector3 normal))
            {
                hit.Point = ray.Origin + ray.Displacement * fraction;
                hit.Normal = math.rotate(box.Rotation, normal);
                hit.Fraction = fraction;
                return fraction>=0;
            }

            return false;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool PointSphere(Vector3 point, ShapeSphere3D sphere)
        {
            Vector3 difference = point - sphere.Center;
            float distanceSquared = math.dot(difference, difference);
            return distanceSquared < sphere.Radius * sphere.Radius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool PointBox(Vector3 point, ShapeBox3D box)
        {
            Vector3 localPoint = math.rotate(point - box.Center, math.inverse(box.Rotation));
            return math.abs(localPoint.X) <= box.Extends.X &&
                   math.abs(localPoint.Y) <= box.Extends.Y &&
                   math.abs(localPoint.Z) <= box.Extends.Z;
        }
    }
}

