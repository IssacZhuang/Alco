using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        /// <summary>
        /// Creates a Vector3 with all components set to the given value.
        /// </summary>
        /// <param name="v">The value assigned to each component.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 float3(float v)
        {
            return new Vector3(v, v, v);
        }

        /// <summary>
        /// Creates a Vector3 from its component values.
        /// </summary>
        /// <param name="x">The X component.</param>
        /// <param name="y">The Y component.</param>
        /// <param name="z">The Z component.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 float3(float x, float y, float z)
        {
            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Creates a Vector3 from an XY vector and a Z component.
        /// </summary>
        /// <param name="xy">The XY components.</param>
        /// <param name="z">The Z component.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 float3(Vector2 xy, float z)
        {
            return new Vector3(xy.X, xy.Y, z);
        }

        /// <summary>
        /// Creates a Vector3 from an X component and a YZ vector.
        /// </summary>
        /// <param name="x">The X component.</param>
        /// <param name="yz">The YZ components.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 float3(float x, Vector2 yz)
        {
            return new Vector3(x, yz.X, yz.Y);
        }

        /// <summary>
        /// Returns the component-wise minimum of two vectors.
        /// </summary>
        /// <param name="a">The first vector.</param>
        /// <param name="b">The second vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 min(Vector3 a, Vector3 b)
        {
            return Vector3.Min(a, b);
        }

        /// <summary>
        /// Returns the component-wise maximum of two vectors.
        /// </summary>
        /// <param name="a">The first vector.</param>
        /// <param name="b">The second vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 max(Vector3 a, Vector3 b)
        {
            return Vector3.Max(a, b);
        }

        /// <summary>
        /// Returns the component-wise absolute value of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 abs(Vector3 a)
        {
            return Vector3.Abs(a);
        }

        /// <summary>
        /// Selects component-wise between two vectors based on a condition.
        /// </summary>
        /// <param name="a">The vector returned when the condition is false.</param>
        /// <param name="b">The vector returned when the condition is true.</param>
        /// <param name="test">The condition to test.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 select(Vector3 a, Vector3 b, bool test)
        {
            return test ? b : a;
        }

        /// <summary>
        /// Returns the component-wise sign of a vector.
        /// </summary>
        /// <param name="x">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 sign(Vector3 x)
        {
            return new Vector3(sign(x.X), sign(x.Y), sign(x.Z));
        }

        /// <summary>
        /// Clamps each component of a vector to the inclusive bounds of two vectors.
        /// </summary>
        /// <param name="a">The vector to clamp.</param>
        /// <param name="min">The component-wise lower bounds.</param>
        /// <param name="max">The component-wise upper bounds.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 clamp(Vector3 a, Vector3 min, Vector3 max)
        {
            return Vector3.Clamp(a, min, max);
        }

        /// <summary>
        /// Clamps each component of a vector to the inclusive range defined by the given bounds.
        /// </summary>
        /// <param name="a">The vector to clamp.</param>
        /// <param name="min">The lower bound for each component.</param>
        /// <param name="max">The upper bound for each component.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 clamp(Vector3 a, float min, float max)
        {
            return Vector3.Clamp(a, new Vector3(min), new Vector3(max));
        }

        /// <summary>
        /// Returns the component-wise floor of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 floor(Vector3 a)
        {
            return new Vector3(floor(a.X), floor(a.Y), floor(a.Z));
        }

        /// <summary>
        /// Returns the component-wise ceiling of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ceil(Vector3 a)
        {
            return new Vector3(ceil(a.X), ceil(a.Y), ceil(a.Z));
        }

        /// <summary>
        /// Returns the component-wise reciprocal of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 reciprocal(Vector3 a)
        {
            return Vector3.One / a;
        }

        /// <summary>
        /// Returns the dot product of two vectors.
        /// </summary>
        /// <param name="a">The first vector.</param>
        /// <param name="b">The second vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float dot(Vector3 a, Vector3 b)
        {
            return Vector3.Dot(a, b);
        }

        /// <summary>
        /// Raises each component of a vector to the specified power.
        /// </summary>
        /// <param name="a">The base vector.</param>
        /// <param name="b">The exponent.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 pow(Vector3 a, float b)
        {
            return new Vector3(pow(a.X, b), pow(a.Y, b), pow(a.Z, b));
        }

        /// <summary>
        /// Raises each component of a vector to the corresponding power in another vector.
        /// </summary>
        /// <param name="a">The base vector.</param>
        /// <param name="b">The component-wise exponents.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 pow(Vector3 a, Vector3 b)
        {
            return new Vector3(pow(a.X, b.X), pow(a.Y, b.Y), pow(a.Z, b.Z));
        }

        /// <summary>
        /// Returns a unit vector with the same direction as the input.
        /// </summary>
        /// <param name="a">The vector to normalize.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 normalize(Vector3 a)
        {
            return Vector3.Normalize(a);
        }

        /// <summary>
        /// Converts each component of a vector from degrees to radians.
        /// </summary>
        /// <param name="degree">The vector of angles in degrees.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 radians(Vector3 degree)
        {
            return degree * DegToRad;
        }

        /// <summary>
        /// Converts each component of a vector from radians to degrees.
        /// </summary>
        /// <param name="radian">The vector of angles in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 degrees(Vector3 radian)
        {
            return radian * RadToDeg;
        }

        /// <summary>
        /// Linearly interpolates between two vectors.
        /// </summary>
        /// <param name="a">The start vector.</param>
        /// <param name="b">The end vector.</param>
        /// <param name="t">The interpolation factor, typically in the range [0, 1].</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 lerp(Vector3 a, Vector3 b, float t)
        {
            return Vector3.Lerp(a, b, t);
        }

        /// <summary>
        /// Returns the component-wise arccosine of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 acos(Vector3 a)
        {
            return new Vector3(acos(a.X), acos(a.Y), acos(a.Z));
        }

        /// <summary>
        /// Returns the component-wise arcsine of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 asin(Vector3 a)
        {
            return new Vector3(asin(a.X), asin(a.Y), asin(a.Z));
        }

        /// <summary>
        /// Returns the component-wise arctangent of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 atan(Vector3 a)
        {
            return new Vector3(atan(a.X), atan(a.Y), atan(a.Z));
        }

        /// <summary>
        /// Returns the component-wise arctangent of the quotient of two vectors.
        /// </summary>
        /// <param name="a">The numerator vector (y coordinates).</param>
        /// <param name="b">The denominator vector (x coordinates).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 atan2(Vector3 a, Vector3 b)
        {
            return new Vector3(atan2(a.X, b.X), atan2(a.Y, b.Y), atan2(a.Z, b.Z));
        }

        /// <summary>
        /// Returns the component-wise cosine of a vector of angles in radians.
        /// </summary>
        /// <param name="a">The vector of angles in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 cos(Vector3 a)
        {
            //simd
            return Vector3.Cos(a);
        }

        /// <summary>
        /// Returns the component-wise sine of a vector of angles in radians.
        /// </summary>
        /// <param name="a">The vector of angles in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 sin(Vector3 a)
        {
            //simd
            return Vector3.Sin(a);
        }

        /// <summary>
        /// Returns the component-wise tangent of a vector of angles in radians.
        /// </summary>
        /// <param name="a">The vector of angles in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 tan(Vector3 a)
        {
            return new Vector3(tan(a.X), tan(a.Y), tan(a.Z));
        }

        /// <summary>
        /// Returns the component-wise hyperbolic cosine of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 cosh(Vector3 a)
        {
            return new Vector3(cosh(a.X), cosh(a.Y), cosh(a.Z));
        }

        /// <summary>
        /// Returns the component-wise hyperbolic sine of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 sinh(Vector3 a)
        {
            return new Vector3(sinh(a.X), sinh(a.Y), sinh(a.Z));
        }

        /// <summary>
        /// Returns the component-wise hyperbolic tangent of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 tanh(Vector3 a)
        {
            return new Vector3(tanh(a.X), tanh(a.Y), tanh(a.Z));
        }

        /// <summary>
        /// Computes the component-wise sine and cosine of a vector of angles in radians.
        /// </summary>
        /// <param name="a">The vector of angles in radians.</param>
        /// <param name="s">Receives the sine of each angle.</param>
        /// <param name="c">Receives the cosine of each angle.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void sincos(Vector3 a, out Vector3 s, out Vector3 c)
        {
            //simd
            (s, c) = Vector3.SinCos(a);
        }

        /// <summary>
        /// Returns the component-wise rounded value of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 round(Vector3 a)
        {
            return new Vector3(round(a.X), round(a.Y), round(a.Z));
        }

        /// <summary>
        /// Snaps each component of a vector to the nearest multiple of an interval around an offset.
        /// </summary>
        /// <param name="value">The vector to snap.</param>
        /// <param name="interval">The component-wise snapping intervals; zero leaves that component unchanged.</param>
        /// <param name="offset">The component-wise origins of the snapping grid.</param>
        public static Vector3 snap(Vector3 value, Vector3 interval, Vector3 offset)
        {
            return new Vector3(
                snap(value.X, interval.X, offset.X),
                snap(value.Y, interval.Y, offset.Y),
                snap(value.Z, interval.Z, offset.Z)
            );
        }

    }
}

