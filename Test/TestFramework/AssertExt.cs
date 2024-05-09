using System.Numerics;
using Vocore;

namespace TestFramework;

public static class AssertExt
{
    public const float Epsilon = 0.0001f;

    public static void AreEqual(Vector2 expected, Vector2 actual)
    {
        if (!IsEqual(expected, actual))
        {
            throw new Exception($"Expected {expected}, but got {actual}");
        }
    }

    public static void AreEqual(Vector3 expected, Vector3 actual)
    {
        if (!IsEqual(expected, actual))
        {
            throw new Exception($"Expected {expected}, but got {actual}");
        }
    }

    public static void AreEqual(Vector4 expected, Vector4 actual)
    {
        if (!IsEqual(expected, actual))
        {
            throw new Exception($"Expected {expected}, but got {actual}");
        }
    }

    public static void AreEqual(Quaternion expected, Quaternion actual)
    {
        if (!IsEqual(expected, actual))
        {
            throw new Exception($"Expected {expected}, but got {actual}");
        }
    }

    public static void AreEqual(Rotation2D expected, Rotation2D actual)
    {
        if (!IsEqual(expected, actual))
        {
            throw new Exception($"Expected {expected}, but got {actual}");
        }
    }

    public static void AreEqual(Transform2D expected, Transform2D actual)
    {
        if (!IsEqual(expected.position, actual.position) ||
        !IsEqual(expected.rotation, actual.rotation) ||
        !IsEqual(expected.scale, actual.scale))
        {
            throw new Exception($"Expected {expected}, but got {actual}");
        }
    }

    public static void AreEqual(Transform3D expected, Transform3D actual)
    {
        if (!IsEqual(expected.position, actual.position) ||
        !IsEqual(expected.rotation, actual.rotation) ||
        !IsEqual(expected.scale, actual.scale))
        {
            throw new Exception($"Expected {expected}, but got {actual}");
        }
    }

    private static bool IsEqual(Vector2 expected, Vector2 actual)
    {
        return IsEqual(expected.X, actual.X) && IsEqual(expected.Y, actual.Y);
    }

    private static bool IsEqual(Vector3 expected, Vector3 actual)
    {
        return IsEqual(expected.X, actual.X) && IsEqual(expected.Y, actual.Y) && IsEqual(expected.Z, actual.Z);
    }

    private static bool IsEqual(Vector4 expected, Vector4 actual)
    {
        return IsEqual(expected.X, actual.X) && IsEqual(expected.Y, actual.Y) && IsEqual(expected.Z, actual.Z) && IsEqual(expected.W, actual.W);
    }

    private static bool IsEqual(Quaternion expected, Quaternion actual)
    {
        return IsEqual(expected.X, actual.X) && IsEqual(expected.Y, actual.Y) && IsEqual(expected.Z, actual.Z) && IsEqual(expected.W, actual.W);
    }

    private static bool IsEqual(Rotation2D expected, Rotation2D actual)
    {
        return IsEqual(expected.s, actual.s) && IsEqual(expected.c, actual.c);
    }



    private static bool IsEqual(float expected, float actual)
    {
        return Math.Abs(expected - actual) < Epsilon;
    }
}