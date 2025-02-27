using System.Numerics;
using NUnit.Framework;
using Alco;

namespace TestFramework;

public static class AssertExt
{
    public const float Epsilon = 0.0001f;

    public static void AreEqual(Vector2 expected, Vector2 actual)
    {
        if (!IsEqual(expected, actual))
        {
            Assert.Fail($"Expected {expected}, but got {actual}");
            //throw new Exception($"Expected {expected}, but got {actual}");
        }
    }

    public static void AreEqual(Vector3 expected, Vector3 actual)
    {
        if (!IsEqual(expected, actual))
        {
            Assert.Fail($"Expected {expected}, but got {actual}");
            //throw new Exception($"Expected {expected}, but got {actual}");
        }
    }

    public static void AreEqual(Vector4 expected, Vector4 actual)
    {
        if (!IsEqual(expected, actual))
        {
            Assert.Fail($"Expected {expected}, but got {actual}");
            //throw new Exception($"Expected {expected}, but got {actual}");
        }
    }

    public static void AreEqual(Quaternion expected, Quaternion actual)
    {
        if (!IsEqual(expected, actual))
        {
            Assert.Fail($"Expected {expected}, but got {actual}");
            //throw new Exception($"Expected {expected}, but got {actual}");
        }
    }

    public static void AreEqual(Rotation2D expected, Rotation2D actual)
    {
        if (!IsEqual(expected, actual))
        {
            Assert.Fail($"Expected {expected}, but got {actual}");
            //throw new Exception($"Expected {expected}, but got {actual}");
        }
    }

    public static void AreEqual(Transform2D expected, Transform2D actual)
    {
        if (!IsEqual(expected.Position, actual.Position) ||
        !IsEqual(expected.Rotation, actual.Rotation) ||
        !IsEqual(expected.Scale, actual.Scale))
        {
            Assert.Fail($"Expected {expected}, but got {actual}");
            //throw new Exception($"Expected {expected}, but got {actual}");
        }
    }

    public static void AreEqual(Transform3D expected, Transform3D actual)
    {
        if (!IsEqual(expected.Position, actual.Position) ||
        !IsEqual(expected.Rotation, actual.Rotation) ||
        !IsEqual(expected.Scale, actual.Scale))
        {
            Assert.Fail($"Expected {expected}, but got {actual}");
            //throw new Exception($"Expected {expected}, but got {actual}");
        }
    }

    public static void AreEqual(Matrix4x4 expected, Matrix4x4 actual)
    {
        if (!IsEqual(expected.M11, actual.M11) ||
        !IsEqual(expected.M12, actual.M12) ||
        !IsEqual(expected.M13, actual.M13) ||
        !IsEqual(expected.M14, actual.M14) ||
        !IsEqual(expected.M21, actual.M21) ||
        !IsEqual(expected.M22, actual.M22) ||
        !IsEqual(expected.M23, actual.M23) ||
        !IsEqual(expected.M24, actual.M24) ||
        !IsEqual(expected.M31, actual.M31) ||
        !IsEqual(expected.M32, actual.M32) ||
        !IsEqual(expected.M33, actual.M33) ||
        !IsEqual(expected.M34, actual.M34) ||
        !IsEqual(expected.M41, actual.M41) ||
        !IsEqual(expected.M42, actual.M42) ||
        !IsEqual(expected.M43, actual.M43) ||
        !IsEqual(expected.M44, actual.M44))
        {
            Assert.Fail($"Expected {expected}, but got {actual}");
            //throw new Exception($"Expected {expected}, but got {actual}");
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
        return IsEqual(expected.S, actual.S) && IsEqual(expected.C, actual.C);
    }



    private static bool IsEqual(float expected, float actual)
    {
        return Math.Abs(expected - actual) < Epsilon;
    }
}