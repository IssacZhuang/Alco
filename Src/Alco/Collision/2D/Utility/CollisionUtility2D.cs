using System;
using System.Numerics;
using System.Runtime.CompilerServices;


namespace Alco
{
    public static class CollisionUtility2D
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SphereSphere(ShapeSphere2D shpere1, ShapeSphere2D shpere2)
        {
            Vector2 difference = shpere1.Center - shpere2.Center;
            float distanceSquared = math.dot(difference, difference);
            float radiusSum = shpere1.Radius + shpere2.Radius;
            return distanceSquared <= radiusSum * radiusSum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoxSphere(ShapeBox2D box, ShapeSphere2D shpere)
        {
            Vector2 shpereCenter = math.rotate(shpere.Center - box.Center, math.inverse(box.Rotation));

            Vector2 closestPoint = math.clamp(shpereCenter, -box.Extends, box.Extends);

            Vector2 difference = closestPoint - shpereCenter;
            float distanceSquared = math.dot(difference, difference);
            return distanceSquared <= shpere.Radius * shpere.Radius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoxBox(ShapeBox2D box1, ShapeBox2D box2D)
        {
            if (box1.Rotation == Rotation2D.Identity && box2D.Rotation == Rotation2D.Identity)
            {
                return BoxBoxAxisAligned(box1, box2D);
            }

            return IntersectAABBWorldToLocal(box1, box2D) && IntersectAABBWorldToLocal(box2D, box1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoxBoxAxisAligned(ShapeBox2D box1, ShapeBox2D box2)
        {
            Vector2 min1 = box1.Center - box1.Extends;
            Vector2 max1 = box1.Center + box1.Extends;
            Vector2 min2 = box2.Center - box2.Extends;
            Vector2 max2 = box2.Center + box2.Extends;

            return min1.X <= max2.X && max1.X >= min2.X &&
                   min1.Y <= max2.Y && max1.Y >= min2.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IntersectAABBWorldToLocal(ShapeBox2D world, ShapeBox2D toLocal)
        {
            BoundingBox2D worldBox = new BoundingBox2D(-world.Extends, world.Extends);

            Rotation2D invRot = math.inverse(world.Rotation);
            toLocal.Center = math.rotate(toLocal.Center - world.Center, invRot);
            //toLocal.rotation -= world.rotation;
            toLocal.Rotation = math.mul(toLocal.Rotation, invRot);

            BoundingBox2D localBox = toLocal.GetBoundingBox();
            return worldBox.Intersects(localBox);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RaySphere(Vector2 rayOrigin, Vector2 rayDisplacement, Vector2 sphereCenter, float sphereRadius, ref float fraction, out Vector2 normal)
        {
            normal = Vector2.Zero;
            Vector2 diff = rayOrigin - sphereCenter;
            float a = math.dot(rayDisplacement, rayDisplacement);
            float b = 2.0f * math.dot(rayDisplacement, diff);
            float c = math.dot(diff, diff) - sphereRadius * sphereRadius;
            float discriminant = b * b - 4.0f * a * c;
            if (c < 0.0f)
            {
                fraction = 0.0f;
                normal = math.normalize(-rayDisplacement);
                return true;
            }
            if (discriminant < 0.0f)
            {
                return false;
            }
            float sqrtDiscriminant = math.sqrt(discriminant);
            float inv2a = 0.5f / a;
            float t0 = (sqrtDiscriminant - b) * inv2a;
            float t1 = (-sqrtDiscriminant - b) * inv2a;
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
        public static bool RaySphere(Ray2D ray, ShapeSphere2D shpere, out RaycastHit2D hit)
        {
            hit = new RaycastHit2D();
            float fraction = float.MaxValue;
            Vector2 normal = Vector2.Zero;
            hit = default;

            if (RaySphere(ray.Origin, ray.Displacement, shpere.Center, shpere.Radius, ref fraction, out normal))
            {
                hit.Fraction = fraction;
                hit.Normal = normal;
                hit.Point = ray.Origin + ray.Displacement * fraction;
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RayAABB(Ray2D ray, BoundingBox2D boundingBox2D, ref float fraction, out Vector2 normal)
        {
            normal = Vector2.One;
            Vector2 rayOrigin = ray.Origin;
            Vector2 rayDisplacement = ray.Displacement;
            Vector2 min = boundingBox2D.Min;
            Vector2 max = boundingBox2D.Max;

            Vector2 invRayDisplacement = math.reciprocal(rayDisplacement);

            float txmin = (min.X - rayOrigin.X) * invRayDisplacement.X;
            float txmax = (max.X - rayOrigin.X) * invRayDisplacement.X;
            float inverseX = 1f;

            if (txmin > txmax)
            {
                float temp = txmin;
                txmin = txmax;
                txmax = temp;
                inverseX = -1f;
            }

            float tmin = txmin;
            float tmax = txmax;

            float tymin = (min.Y - rayOrigin.Y) * invRayDisplacement.Y;
            float tymax = (max.Y - rayOrigin.Y) * invRayDisplacement.Y;
            float inverseY = 1f;

            if (tymin > tymax)
            {
                float temp = tymin;
                tymin = tymax;
                tymax = temp;
                inverseY = -1f;
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

            fraction = tmin;
            if (txmin == tmin)
            {
                normal = new Vector2(-1.0f, 0.0f) * inverseX;
            }
            else if (txmax == tmin)
            {
                normal = new Vector2(1.0f, 0.0f) * inverseX;
            }
            else if (tymin == tmin)
            {
                normal = new Vector2(0.0f, -1.0f) * inverseY;
            }
            else if (tymax == tmin)
            {
                normal = new Vector2(0.0f, 1.0f) * inverseY;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RayAABB(Ray2D ray, BoundingBox2D boundingBox)
        {
            Vector2 invRayDisplacement = math.reciprocal(ray.Displacement);
            Vector2 originToMin = boundingBox.Min - ray.Origin;
            Vector2 originToMax = boundingBox.Max - ray.Origin;

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

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RayBox(Ray2D ray, ShapeBox2D box, out RaycastHit2D hit)
        {
            hit = default;
            BoundingBox2D localAABB = new BoundingBox2D(-box.Extends, box.Extends);

            Rotation2D invRot = math.inverse(box.Rotation);
            //Vector3 rayOriginLocal = math.transform(math.inverse(new Transform3D(box.rotation, box.center)), ray.origin);
            Vector2 rayOriginLocal = math.rotate(ray.Origin - box.Center, invRot);
            Vector2 rayDisplacementLocal = math.rotate(ray.Displacement, invRot);

            Ray2D localRay = new Ray2D(rayOriginLocal, rayDisplacementLocal);

            float fraction = float.MaxValue;

            if (RayAABB(localRay, localAABB, ref fraction, out Vector2 normal))
            {
                hit.Point = ray.Origin + ray.Displacement * fraction;
                hit.Normal = math.rotate(normal, box.Rotation);
                hit.Fraction = fraction;
                return fraction >= 0;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool PointSphere(Vector2 point, ShapeSphere2D sphere)
        {
            Vector2 difference = point - sphere.Center;
            float distanceSquared = math.dot(difference, difference);
            return distanceSquared <= sphere.Radius * sphere.Radius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool PointBox(Vector2 point, ShapeBox2D box)
        {
            Vector2 localPoint = math.rotate(point - box.Center, math.inverse(box.Rotation));
            return math.abs(localPoint.X) <= box.Extends.X &&
            math.abs(localPoint.Y) <= box.Extends.Y;
        }

    }
}