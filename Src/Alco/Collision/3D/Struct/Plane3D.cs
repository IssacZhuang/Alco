using System.Numerics;

namespace Alco;

public struct Plane3D
{
    public float Distance;
    public Vector3 Normal;

    public Plane3D(Vector3 normal, float distance)
    {
        this.Normal = Vector3.Normalize(normal);
        this.Distance = distance;
    }

    public Plane3D(Transform3D transform)
    {
        Normal = math.normalize(transform.Direction);
        Distance = Vector3.Dot(Normal, transform.Position);
    }

    public Plane3D(Vector3 normal, Vector3 point)
    {
        this.Normal = Vector3.Normalize(normal);
        Distance = Vector3.Dot(normal, point);
    }

    public Plane3D(Vector3 a, Vector3 b, Vector3 c)
    {
        Normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        Distance = Vector3.Dot(Normal, a);
    }

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