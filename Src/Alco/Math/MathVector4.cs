using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        /// <summary>
        /// Creates a vector with all four components set to the given value.
        /// </summary>
        /// <param name="v">The value assigned to every component.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 float4(float v)
        {
            return new Vector4(v, v, v, v);
        }

        /// <summary>
        /// Creates a vector from four component values.
        /// </summary>
        /// <param name="x">The X component.</param>
        /// <param name="y">The Y component.</param>
        /// <param name="z">The Z component.</param>
        /// <param name="w">The W component.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 float4(float x, float y, float z, float w)
        {
            return new Vector4(x, y, z, w);
        }

        /// <summary>
        /// Creates a vector from an XY pair and separate Z and W values.
        /// </summary>
        /// <param name="xy">The XY components.</param>
        /// <param name="z">The Z component.</param>
        /// <param name="w">The W component.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 float4(Vector2 xy, float z, float w)
        {
            return new Vector4(xy.X, xy.Y, z, w);
        }

        /// <summary>
        /// Creates a vector from a separate X, a YZ pair and a W value.
        /// </summary>
        /// <param name="x">The X component.</param>
        /// <param name="yz">The YZ components.</param>
        /// <param name="w">The W component.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 float4(float x, Vector2 yz, float w)
        {
            return new Vector4(x, yz.X, yz.Y, w);
        }

        /// <summary>
        /// Creates a vector from separate X and Y values and a ZW pair.
        /// </summary>
        /// <param name="x">The X component.</param>
        /// <param name="y">The Y component.</param>
        /// <param name="zw">The ZW components.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 float4(float x, float y, Vector2 zw)
        {
            return new Vector4(x, y, zw.X, zw.Y);
        }

        /// <summary>
        /// Creates a vector from an XYZ triple and a W value.
        /// </summary>
        /// <param name="xyz">The XYZ components.</param>
        /// <param name="w">The W component.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 float4(Vector3 xyz, float w)
        {
            return new Vector4(xyz.X, xyz.Y, xyz.Z, w);
        }

        /// <summary>
        /// Creates a vector from an X value and a YZW triple.
        /// </summary>
        /// <param name="x">The X component.</param>
        /// <param name="yzw">The YZW components.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 float4(float x, Vector3 yzw)
        {
            return new Vector4(x, yzw.X, yzw.Y, yzw.Z);
        }

        /// <summary>
        /// Returns the component-wise minimum of two vectors.
        /// </summary>
        /// <param name="a">The first vector.</param>
        /// <param name="b">The second vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 min(Vector4 a, Vector4 b)
        {
            return Vector4.Min(a, b);
        }

        /// <summary>
        /// Returns the component-wise maximum of two vectors.
        /// </summary>
        /// <param name="a">The first vector.</param>
        /// <param name="b">The second vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 max(Vector4 a, Vector4 b)
        {
            return Vector4.Max(a, b);
        }

        /// <summary>
        /// Returns the component-wise absolute value of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 abs(Vector4 a)
        {
            return Vector4.Abs(a);
        }

        /// <summary>
        /// Returns b when test is true, otherwise a.
        /// </summary>
        /// <param name="a">The value returned when test is false.</param>
        /// <param name="b">The value returned when test is true.</param>
        /// <param name="test">The selection condition.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 select(Vector4 a, Vector4 b, bool test)
        {
            return test ? b : a;
        }

        /// <summary>
        /// Returns the component-wise sign of a vector.
        /// </summary>
        /// <param name="x">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 sign(Vector4 x)
        {
            return new Vector4(sign(x.X), sign(x.Y), sign(x.Z), sign(x.W));
        }

        /// <summary>
        /// Clamps each component of a vector to the given bounds.
        /// </summary>
        /// <param name="a">The vector to clamp.</param>
        /// <param name="min">The lower bounds.</param>
        /// <param name="max">The upper bounds.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 clamp(Vector4 a, Vector4 min, Vector4 max)
        {
            return Vector4.Clamp(a, min, max);
        }

        /// <summary>
        /// Clamps each component of a vector to the given scalar bounds.
        /// </summary>
        /// <param name="a">The vector to clamp.</param>
        /// <param name="min">The lower bound.</param>
        /// <param name="max">The upper bound.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 clamp(Vector4 a, float min, float max)
        {
            return Vector4.Clamp(a, new Vector4(min), new Vector4(max));
        }

        /// <summary>
        /// Returns the component-wise floor of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 floor(Vector4 a)
        {
            return new Vector4(floor(a.X), floor(a.Y), floor(a.Z), floor(a.W));
        }

        /// <summary>
        /// Returns the component-wise ceiling of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 ceil(Vector4 a)
        {
            return new Vector4(ceil(a.X), ceil(a.Y), ceil(a.Z), ceil(a.W));
        }

        /// <summary>
        /// Returns the component-wise reciprocal of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 reciprocal(Vector4 a)
        {
            return Vector4.One / a;
        }

        /// <summary>
        /// Returns the dot product of two vectors.
        /// </summary>
        /// <param name="a">The first vector.</param>
        /// <param name="b">The second vector.</param>
        /// <returns>The dot product.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float dot(Vector4 a, Vector4 b)
        {
            return Vector4.Dot(a, b);
        }

        /// <summary>
        /// Raises each component of a vector to the given power.
        /// </summary>
        /// <param name="a">The base vector.</param>
        /// <param name="b">The exponent.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 pow(Vector4 a, float b)
        {
            return new Vector4(pow(a.X, b), pow(a.Y, b), pow(a.Z, b), pow(a.W, b));
        }

        /// <summary>
        /// Raises each component of a vector to the corresponding component of another.
        /// </summary>
        /// <param name="a">The base vector.</param>
        /// <param name="b">The exponent vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 pow(Vector4 a, Vector4 b)
        {
            return new Vector4(pow(a.X, b.X), pow(a.Y, b.Y), pow(a.Z, b.Z), pow(a.W, b.W));
        }

        /// <summary>
        /// Returns a vector with the same direction and unit length.
        /// </summary>
        /// <param name="a">The vector to normalize.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 normalize(Vector4 a)
        {
            return Vector4.Normalize(a);
        }

        /// <summary>
        /// Converts each component of a vector from degrees to radians.
        /// </summary>
        /// <param name="a">The vector in degrees.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 radians(Vector4 a)
        {
            return a * DegToRad;
        }

        /// <summary>
        /// Linearly interpolates between two vectors.
        /// </summary>
        /// <param name="a">The start vector.</param>
        /// <param name="b">The end vector.</param>
        /// <param name="t">The interpolation factor, where 0 returns a and 1 returns b.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 lerp(Vector4 a, Vector4 b, float t)
        {
            return Vector4.Lerp(a, b, t);
        }

        /// <summary>
        /// Returns the component-wise arccosine of a vector, in radians.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 acos(Vector4 a)
        {
            return new Vector4(acos(a.X), acos(a.Y), acos(a.Z), acos(a.W));
        }

        /// <summary>
        /// Returns the component-wise arcsine of a vector, in radians.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 asin(Vector4 a)
        {
            return new Vector4(asin(a.X), asin(a.Y), asin(a.Z), asin(a.W));
        }

        /// <summary>
        /// Returns the component-wise arctangent of a vector, in radians.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 atan(Vector4 a)
        {
            return new Vector4(atan(a.X), atan(a.Y), atan(a.Z), atan(a.W));
        }

        /// <summary>
        /// Returns the component-wise arctangent of a divided by b, in radians.
        /// </summary>
        /// <param name="a">The numerator components.</param>
        /// <param name="b">The denominator components.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 atan2(Vector4 a, Vector4 b)
        {
            return new Vector4(atan2(a.X, b.X), atan2(a.Y, b.Y), atan2(a.Z, b.Z), atan2(a.W, b.W));
        }

        /// <summary>
        /// Returns the component-wise cosine of a vector.
        /// </summary>
        /// <param name="a">The input vector in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 cos(Vector4 a)
        {
            return new Vector4(cos(a.X), cos(a.Y), cos(a.Z), cos(a.W));
        }

        /// <summary>
        /// Returns the component-wise sine of a vector.
        /// </summary>
        /// <param name="a">The input vector in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 sin(Vector4 a)
        {
            return new Vector4(sin(a.X), sin(a.Y), sin(a.Z), sin(a.W));
        }

        /// <summary>
        /// Returns the component-wise tangent of a vector.
        /// </summary>
        /// <param name="a">The input vector in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 tan(Vector4 a)
        {
            return new Vector4(tan(a.X), tan(a.Y), tan(a.Z), tan(a.W));
        }

        /// <summary>
        /// Returns the component-wise hyperbolic cosine of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 cosh(Vector4 a)
        {
            return new Vector4(cosh(a.X), cosh(a.Y), cosh(a.Z), cosh(a.W));
        }

        /// <summary>
        /// Returns the component-wise hyperbolic sine of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 sinh(Vector4 a)
        {
            return new Vector4(sinh(a.X), sinh(a.Y), sinh(a.Z), sinh(a.W));
        }

        /// <summary>
        /// Returns the component-wise hyperbolic tangent of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 tanh(Vector4 a)
        {
            return new Vector4(tanh(a.X), tanh(a.Y), tanh(a.Z), tanh(a.W));
        }

        /// <summary>
        /// Computes the component-wise sine and cosine of a vector.
        /// </summary>
        /// <param name="a">The input vector in radians.</param>
        /// <param name="s">The computed sine values.</param>
        /// <param name="c">The computed cosine values.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void sincos(Vector4 a, out Vector4 s, out Vector4 c)
        {
            s = sin(a);
            c = cos(a);
        }

        /// <summary>
        /// Returns the component-wise value rounded to the nearest integer.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 round(Vector4 a)
        {
            return new Vector4(round(a.X), round(a.Y), round(a.Z), round(a.W));
        }

        /// <summary>
        /// Snaps each component to the nearest multiple of interval plus offset.
        /// </summary>
        /// <param name="value">The value to snap.</param>
        /// <param name="interval">The interval between snap points.</param>
        /// <param name="offset">The offset of the snap grid.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 snap(Vector4 value, Vector4 interval, Vector4 offset)
        {
            return new Vector4(
                snap(value.X, interval.X, offset.X),
                snap(value.Y, interval.Y, offset.Y),
                snap(value.Z, interval.Z, offset.Z),
                snap(value.W, interval.W, offset.W)
            );
        }
    }
}

