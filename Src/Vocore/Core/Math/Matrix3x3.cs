using System.Runtime.CompilerServices;
using System.Numerics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System;
using Vocore;

/// <summary>
/// Represents a 3x3 matrix.
/// </summary>
public struct Matrix3x3
{
    public static Matrix3x3 Identity => new Matrix3x3(1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f);
    public float M11;
    public float M12;
    public float M13;
    private float M14;// unused, just for memory alignment
    public float M21;
    public float M22;
    public float M23;
    private float M24;// unused, just for memory alignment
    public float M31;
    public float M32;
    public float M33;
    private float M34;// unused, just for memory alignment

    public Matrix3x3(float m11, float m12, float m13,
       float m21, float m22, float m23,
       float m31, float m32, float m33)
    {
        M11 = m11; M12 = m12; M13 = m13;
        M21 = m21; M22 = m22; M23 = m23;
        M31 = m31; M32 = m32; M33 = m33;
    }

    public Matrix3x3(Matrix3x2 matrix)
    {
        M11 = matrix.M11; M12 = matrix.M21; M13 = M31;
        M21 = matrix.M12; M22 = matrix.M22; M23 = M32;
        M31 = 0.0f; M32 = 0.0f; M33 = 1.0f;
    }

    public static Matrix3x3 CreateRotation(float radians)
    {
        float sin = MathF.Sin(radians);
        float cos = MathF.Cos(radians);
        Unsafe.SkipInit(out Matrix3x3 result);
        result.M11 = cos;
        result.M12 = sin;
        result.M13 = 0.0f;
        result.M21 = -sin;
        result.M22 = cos;
        result.M23 = 0.0f;
        result.M31 = 0.0f;
        result.M32 = 0.0f;
        result.M33 = 1.0f;
        return result;
    }


    public static Matrix3x3 CreateRotation(Rotation2D rotation)
    {
        Unsafe.SkipInit(out Matrix3x3 result);
        result.M11 = rotation.c;
        result.M12 = rotation.s;
        result.M13 = 0.0f;
        result.M21 = -rotation.s;
        result.M22 = rotation.c;
        result.M23 = 0.0f;
        result.M31 = 0.0f;
        result.M32 = 0.0f;
        result.M33 = 1.0f;
        return result;
    }

    public static Matrix3x3 CreateTranslation(Vector2 position)
    {
        Unsafe.SkipInit(out Matrix3x3 result);
        result.M11 = 1.0f;
        result.M12 = 0.0f;
        result.M13 = 0.0f;
        result.M21 = 0.0f;
        result.M22 = 1.0f;
        result.M23 = 0.0f;
        result.M31 = position.X;
        result.M32 = position.Y;
        result.M33 = 1.0f;
        return result;
    }

    public static Matrix3x3 CreateScale(Vector2 scales)
    {
        Unsafe.SkipInit(out Matrix3x3 result);
        result.M11 = scales.X;
        result.M12 = 0.0f;
        result.M13 = 0.0f;
        result.M21 = 0.0f;
        result.M22 = scales.Y;
        result.M23 = 0.0f;
        result.M31 = 0.0f;
        result.M32 = 0.0f;
        result.M33 = 1.0f;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Matrix3x3 value1, Matrix3x3 value2)
    {
        return value1.M11 == value2.M11 && value1.M12 == value2.M12 && value1.M13 == value2.M13 &&
               value1.M21 == value2.M21 && value1.M22 == value2.M22 && value1.M23 == value2.M23 &&
               value1.M31 == value2.M31 && value1.M32 == value2.M32 && value1.M33 == value2.M33;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Matrix3x3 value1, Matrix3x3 value2)
    {
        return !(value1 == value2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x3 operator *(Matrix3x3 value1, float value2)
    {
        Unsafe.SkipInit(out Matrix3x3 result);
        result.M11 = value1.M11 * value2;
        result.M12 = value1.M12 * value2;
        result.M13 = value1.M13 * value2;
        result.M21 = value1.M21 * value2;
        result.M22 = value1.M22 * value2;
        result.M23 = value1.M23 * value2;
        result.M31 = value1.M31 * value2;
        result.M32 = value1.M32 * value2;
        result.M33 = value1.M33 * value2;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x3 operator *(Matrix3x3 value1, Matrix3x3 value2)
    {
        Unsafe.SkipInit(out Matrix3x3 result);
        result.M11 = value1.M11 * value2.M11 + value1.M12 * value2.M21 + value1.M13 * value2.M31;
        result.M12 = value1.M11 * value2.M12 + value1.M12 * value2.M22 + value1.M13 * value2.M32;
        result.M13 = value1.M11 * value2.M13 + value1.M12 * value2.M23 + value1.M13 * value2.M33;
        result.M21 = value1.M21 * value2.M11 + value1.M22 * value2.M21 + value1.M23 * value2.M31;
        result.M22 = value1.M21 * value2.M12 + value1.M22 * value2.M22 + value1.M23 * value2.M32;
        result.M23 = value1.M21 * value2.M13 + value1.M22 * value2.M23 + value1.M23 * value2.M33;
        result.M31 = value1.M31 * value2.M11 + value1.M32 * value2.M21 + value1.M33 * value2.M31;
        result.M32 = value1.M31 * value2.M12 + value1.M32 * value2.M22 + value1.M33 * value2.M32;
        result.M33 = value1.M31 * value2.M13 + value1.M32 * value2.M23 + value1.M33 * value2.M33;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x3 operator +(Matrix3x3 value1, Matrix3x3 value2)
    {
        Unsafe.SkipInit(out Matrix3x3 result);
        result.M11 = value1.M11 + value2.M11;
        result.M12 = value1.M12 + value2.M12;
        result.M13 = value1.M13 + value2.M13;
        result.M21 = value1.M21 + value2.M21;
        result.M22 = value1.M22 + value2.M22;
        result.M23 = value1.M23 + value2.M23;
        result.M31 = value1.M31 + value2.M31;
        result.M32 = value1.M32 + value2.M32;
        result.M33 = value1.M33 + value2.M33;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x3 operator -(Matrix3x3 value1, Matrix3x3 value2)
    {
        Unsafe.SkipInit(out Matrix3x3 result);
        result.M11 = value1.M11 - value2.M11;
        result.M12 = value1.M12 - value2.M12;
        result.M13 = value1.M13 - value2.M13;
        result.M21 = value1.M21 - value2.M21;
        result.M22 = value1.M22 - value2.M22;
        result.M23 = value1.M23 - value2.M23;
        result.M31 = value1.M31 - value2.M31;
        result.M32 = value1.M32 - value2.M32;
        result.M33 = value1.M33 - value2.M33;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x3 operator -(Matrix3x3 value1)
    {
        Unsafe.SkipInit(out Matrix3x3 result);
        result.M11 = -value1.M11;
        result.M12 = -value1.M12;
        result.M13 = -value1.M13;
        result.M21 = -value1.M21;
        result.M22 = -value1.M22;
        result.M23 = -value1.M23;
        result.M31 = -value1.M31;
        result.M32 = -value1.M32;
        result.M33 = -value1.M33;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x3 Transpose(Matrix3x3 value)
    {
        Unsafe.SkipInit(out Matrix3x3 result);
        result.M11 = value.M11;
        result.M12 = value.M21;
        result.M13 = value.M31;
        result.M21 = value.M12;
        result.M22 = value.M22;
        result.M23 = value.M32;
        result.M31 = value.M13;
        result.M32 = value.M23;
        result.M33 = value.M33;
        return result;
    }

    public override string ToString()
    {
        return $"[ {M11}, {M12}, {M13} ]\n[ {M21}, {M22}, {M23} ]\n[ {M31}, {M32}, {M33} ]";
    }

    public override bool Equals(object obj)
    {
        return obj is Matrix3x3 matrix && this == matrix;
    }

    public override int GetHashCode()
    {
        HashCode hashCode = default;
        hashCode.Add(M11);
        hashCode.Add(M12);
        hashCode.Add(M13);
        hashCode.Add(M21);
        hashCode.Add(M22);
        hashCode.Add(M23);
        hashCode.Add(M31);
        hashCode.Add(M32);
        hashCode.Add(M33);
        return hashCode.ToHashCode();
    }
}