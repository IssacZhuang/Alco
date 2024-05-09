using System.Numerics;

namespace TestFramework;

public static class AssetExt
{
    public const float Epsilon = 0.0001f;

    public static void AreEqual(Vector2 expected, Vector2 actual)
    {
        if (!IsEqual(expected.X, actual.X) || !IsEqual(expected.Y, actual.Y))
        {
            throw new Exception($"Expected {expected}, but got {actual}");
        }
    }

    public static void AreEqual(Vector3 expected, Vector3 actual)
    {
        if (!IsEqual(expected.X, actual.X) || !IsEqual(expected.Y, actual.Y) || !IsEqual(expected.Z, actual.Z))
        {
            throw new Exception($"Expected {expected}, but got {actual}");
        }
    }

    public static void AreEqual(Vector4 expected, Vector4 actual)
    {
        if (!IsEqual(expected.X, actual.X) || !IsEqual(expected.Y, actual.Y) || !IsEqual(expected.Z, actual.Z) || !IsEqual(expected.W, actual.W))
        {
            throw new Exception($"Expected {expected}, but got {actual}");
        }
    }

    public static void AreEqual(Quaternion expected, Quaternion actual)
    {
        if (!IsEqual(expected.X, actual.X) || !IsEqual(expected.Y, actual.Y) || !IsEqual(expected.Z, actual.Z) || !IsEqual(expected.W, actual.W))
        {
            throw new Exception($"Expected {expected}, but got {actual}");
        }
    }

    private static bool IsEqual(float expected, float actual)
    {
        return Math.Abs(expected - actual) < Epsilon;
    }
}