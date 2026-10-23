using System;
using System.Numerics;
using System.Runtime.CompilerServices;


namespace Alco
{
    /// <summary>
    /// Provides intersection and raycast tests for spheres, boxes, and bounding boxes in 2D.
    /// </summary>
    public static class CollisionUtility2D
    {
        /// <summary>
        /// Determines whether two spheres intersect.
        /// </summary>
        /// <param name="shpere1">The first sphere.</param>
        /// <param name="shpere2">The second sphere.</param>
        /// <returns>True if the spheres overlap.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SphereSphere(ShapeSphere2D shpere1, ShapeSphere2D shpere2)
        {
            Vector2 difference = shpere1.Center - shpere2.Center;
            float distanceSquared = math.dot(difference, difference);
            float radiusSum = shpere1.Radius + shpere2.Radius;
            return distanceSquared <= radiusSum * radiusSum;
        }

        /// <summary>
        /// Determines whether a box intersects a sphere.
        /// </summary>
        /// <param name="box">The box.</param>
        /// <param name="shpere">The sphere.</param>
        /// <returns>True if the box and sphere overlap.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoxSphere(ShapeBox2D box, ShapeSphere2D shpere)
        {
            Vector2 shpereCenter = math.rotate(shpere.Center - box.Center, math.inverse(box.Rotation));

            Vector2 closestPoint = math.clamp(shpereCenter, -box.Extends, box.Extends);

            Vector2 difference = closestPoint - shpereCenter;
            float distanceSquared = math.dot(difference, difference);
            return distanceSquared <= shpere.Radius * shpere.Radius;
        }

        /// <summary>
        /// Determines whether two rotated boxes intersect.
        /// </summary>
        /// <param name="box1">The first box.</param>
        /// <param name="box2D">The second box.</param>
        /// <returns>True if the boxes overlap.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoxBox(ShapeBox2D box1, ShapeBox2D box2D)
        {
            if (box1.Rotation == Rotation2D.Identity && box2D.Rotation == Rotation2D.Identity)
            {
                return BoxBoxAxisAligned(box1, box2D);
            }

            return IntersectAABBWorldToLocal(box1, box2D) && IntersectAABBWorldToLocal(box2D, box1);
        }

        /// <summary>
        /// Determines whether two axis-aligned boxes intersect.
        /// </summary>
        /// <param name="box1">The first box.</param>
        /// <param name="box2">The second box.</param>
        /// <returns>True if the boxes overlap.</returns>
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

        /// <summary>
        /// Determines whether a box intersects another box's axis-aligned bounds expressed in that box's local space.
        /// </summary>
        /// <param name="world">The box whose local space is used for the test.</param>
        /// <param name="toLocal">The box to transform into the local space.</param>
        /// <returns>True if the transformed box overlaps the local bounds.</returns>
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

        /// <summary>
        /// Tests a ray against a sphere and returns the entry fraction and surface normal. A ray that starts inside the sphere does not hit.
        /// </summary>
        /// <param name="rayOrigin">The ray origin.</param>
        /// <param name="rayDisplacement">The ray displacement vector.</param>
        /// <param name="sphereCenter">The sphere center.</param>
        /// <param name="sphereRadius">The sphere radius.</param>
        /// <param name="fraction">The current closest hit fraction; updated when a closer hit is found.</param>
        /// <param name="normal">The surface normal at the hit point.</param>
        /// <returns>True if the ray hits the sphere before the current fraction.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RaySphere(Vector2 rayOrigin, Vector2 rayDisplacement, Vector2 sphereCenter, float sphereRadius, ref float fraction, out Vector2 normal)
        {
            normal = Vector2.Zero;
            Vector2 diff = rayOrigin - sphereCenter;
            float a = math.dot(rayDisplacement, rayDisplacement);
            float b = 2.0f * math.dot(rayDisplacement, diff);
            float c = math.dot(diff, diff) - sphereRadius * sphereRadius;
            float discriminant = b * b - 4.0f * a * c;
            // Ray origin is inside the sphere (c < 0).
            // Consistent with RayBox: no hit when ray starts inside the shape.
            if (c < 0.0f)
            {
                return false;
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

        /// <summary>
        /// Tests a ray against a sphere and returns the hit details.
        /// </summary>
        /// <param name="ray">The ray to test.</param>
        /// <param name="shpere">The sphere to test.</param>
        /// <param name="hit">The hit point, normal, and fraction when a hit occurs.</param>
        /// <returns>True if the ray hits the sphere.</returns>
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

        /// <summary>
        /// Tests a ray against an axis-aligned bounding box and returns the entry fraction and face normal.
        /// </summary>
        /// <param name="ray">The ray to test.</param>
        /// <param name="boundingBox2D">The bounding box to test.</param>
        /// <param name="fraction">The entry fraction along the ray.</param>
        /// <param name="normal">The normal of the face entered.</param>
        /// <returns>True if the ray intersects the box.</returns>
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

        /// <summary>
        /// Determines whether a ray intersects an axis-aligned bounding box.
        /// </summary>
        /// <param name="ray">The ray to test.</param>
        /// <param name="boundingBox">The bounding box to test.</param>
        /// <returns>True if the ray intersects the box.</returns>
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

        /// <summary>
        /// Tests a ray against an oriented box and returns the hit details.
        /// </summary>
        /// <param name="ray">The ray to test.</param>
        /// <param name="box">The box to test.</param>
        /// <param name="hit">The hit point, normal, and fraction when a hit occurs.</param>
        /// <returns>True if the ray hits the box in front of its origin.</returns>
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

        /// <summary>
        /// Determines whether a point lies inside or on a sphere.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <param name="sphere">The sphere to test.</param>
        /// <returns>True if the point lies inside or on the sphere.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool PointSphere(Vector2 point, ShapeSphere2D sphere)
        {
            Vector2 difference = point - sphere.Center;
            float distanceSquared = math.dot(difference, difference);
            return distanceSquared <= sphere.Radius * sphere.Radius;
        }

        /// <summary>
        /// Determines whether a point lies inside or on a box.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <param name="box">The box to test.</param>
        /// <returns>True if the point lies inside or on the box.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool PointBox(Vector2 point, ShapeBox2D box)
        {
            Vector2 localPoint = math.rotate(point - box.Center, math.inverse(box.Rotation));
            return math.abs(localPoint.X) <= box.Extends.X &&
            math.abs(localPoint.Y) <= box.Extends.Y;
        }

    }
}