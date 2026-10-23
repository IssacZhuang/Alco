using System.Numerics;

namespace Alco;

/// <summary>
/// A plane defined by a unit normal and a signed distance from the origin.
/// </summary>
public struct Plane3D
{
    public float Distance;
    public Vector3 Normal;

    /// <summary>
    /// Initializes a plane from a normal and a distance from the origin; the normal is normalized.
    /// </summary>
    /// <param name="normal">The plane normal before normalization.</param>
    /// <param name="distance">The signed distance from the origin along the normal.</param>
    public Plane3D(Vector3 normal, float distance)
    {
        this.Normal = Vector3.Normalize(normal);
        this.Distance = distance;
    }

    /// <summary>
    /// Initializes a plane facing the transform's direction and passing through its position.
    /// </summary>
    /// <param name="transform">The transform defining the plane orientation and position.</param>
    public Plane3D(Transform3D transform)
    {
        Normal = math.normalize(transform.Direction);
        Distance = Vector3.Dot(Normal, transform.Position);
    }

    /// <summary>
    /// Initializes a plane with the given normal that passes through the given point.
    /// </summary>
    /// <param name="normal">The plane normal before normalization.</param>
    /// <param name="point">A point on the plane.</param>
    public Plane3D(Vector3 normal, Vector3 point)
    {
        this.Normal = Vector3.Normalize(normal);
        Distance = Vector3.Dot(normal, point);
    }

    /// <summary>
    /// Initializes a plane that passes through three points.
    /// </summary>
    /// <param name="a">The first point on the plane.</param>
    /// <param name="b">The second point on the plane.</param>
    /// <param name="c">The third point on the plane.</param>
    public Plane3D(Vector3 a, Vector3 b, Vector3 c)
    {
        Normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        Distance = Vector3.Dot(Normal, a);
    }

    /// <summary>
    /// Tests whether the ray intersects the plane in the forward direction.
    /// </summary>
    /// <param name="ray">The ray to test.</param>
    /// <param name="hitPoint">The intersection point when a hit occurs.</param>
    /// <returns>True when the ray intersects the plane.</returns>
    public bool IntersectRay(Ray3D ray, out Vector3 hitPoint)
    {
        hitPoint = Vector3.Zero;
        float denominator = Vector3.Dot(Normal, ray.Displacement);
        if (denominator == 0)
        {
            return false;
        }

        float t = (Distance - Vector3.Dot(Normal, ray.Origin)) / denominator;

        if (t < 0)
        {
            return false;
        }

        hitPoint = ray.Origin + t * ray.Displacement;
        return true;
    }
}