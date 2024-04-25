using System.Numerics;

namespace Vocore;

public struct Plane3D
{
    public float distance;
    public Vector3 normal;

    public Plane3D(Vector3 normal, float distance)
    {
        this.normal = normal;
        this.distance = distance;
    }

    public Plane3D(Vector3 a, Vector3 b, Vector3 c)
    {
        normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        distance = Vector3.Dot(normal, a);
    }

    public bool IntersectRay(Ray3D ray, out Vector3 hitPoint)
    {
        hitPoint = Vector3.Zero;
        float denominator = Vector3.Dot(normal, ray.displacement);
        if (denominator == 0)
        {
            return false;
        }

        float t = (distance - Vector3.Dot(normal, ray.origin)) / denominator;

        if (t < 0)
        {
            return false;
        }

        hitPoint = ray.origin + t * ray.displacement;
        return true;
    }
}