using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        /// <summary>
        /// Creates a Vector2 from its component values.
        /// </summary>
        /// <param name="x">The X component.</param>
        /// <param name="y">The Y component.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 float2(float x, float y)
        {
            return new Vector2(x, y);
        }

        /// <summary>
        /// Returns the component-wise minimum of two vectors.
        /// </summary>
        /// <param name="a">The first vector.</param>
        /// <param name="b">The second vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 min(Vector2 a, Vector2 b)
        {
            return Vector2.Min(a, b);
        }

        /// <summary>
        /// Returns the component-wise maximum of two vectors.
        /// </summary>
        /// <param name="a">The first vector.</param>
        /// <param name="b">The second vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 max(Vector2 a, Vector2 b)
        {
            return Vector2.Max(a, b);
        }

        /// <summary>
        /// Returns the component-wise absolute value of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 abs(Vector2 a)
        {
            return Vector2.Abs(a);
        }

        /// <summary>
        /// Selects component-wise between two vectors based on a condition.
        /// </summary>
        /// <param name="a">The vector returned when the condition is false.</param>
        /// <param name="b">The vector returned when the condition is true.</param>
        /// <param name="test">The condition to test.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 select(Vector2 a, Vector2 b, bool test)
        {
            return test ? b : a;
        }

        /// <summary>
        /// Returns the component-wise sign of a vector.
        /// </summary>
        /// <param name="x">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 sign(Vector2 x)
        {
            return new Vector2(sign(x.X), sign(x.Y));
        }

        /// <summary>
        /// Clamps each component of a vector to the inclusive bounds of two vectors.
        /// </summary>
        /// <param name="a">The vector to clamp.</param>
        /// <param name="min">The component-wise lower bounds.</param>
        /// <param name="max">The component-wise upper bounds.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 clamp(Vector2 a, Vector2 min, Vector2 max)
        {
            return Vector2.Clamp(a, min, max);
        }

        /// <summary>
        /// Clamps each component of a vector to the inclusive range defined by the given bounds.
        /// </summary>
        /// <param name="a">The vector to clamp.</param>
        /// <param name="min">The lower bound for each component.</param>
        /// <param name="max">The upper bound for each component.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 clamp(Vector2 a, float min, float max)
        {
            return Vector2.Clamp(a, new Vector2(min), new Vector2(max));
        }

        /// <summary>
        /// Returns the component-wise floor of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 floor(Vector2 a)
        {
            return new Vector2(floor(a.X), floor(a.Y));
        }

        /// <summary>
        /// Returns the component-wise ceiling of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ceil(Vector2 a)
        {
            return new Vector2(ceil(a.X), ceil(a.Y));
        }

        /// <summary>
        /// Returns the component-wise reciprocal of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 reciprocal(Vector2 a)
        {
            return Vector2.One / a;
        }

        /// <summary>
        /// Returns the dot product of two vectors.
        /// </summary>
        /// <param name="a">The first vector.</param>
        /// <param name="b">The second vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float dot(Vector2 a, Vector2 b)
        {
            return Vector2.Dot(a, b);
        }

        /// <summary>
        /// Raises each component of a vector to the specified power.
        /// </summary>
        /// <param name="a">The base vector.</param>
        /// <param name="b">The exponent.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 pow(Vector2 a, float b)
        {
            return new Vector2(pow(a.X, b), pow(a.Y, b));
        }

        /// <summary>
        /// Raises each component of a vector to the corresponding power in another vector.
        /// </summary>
        /// <param name="a">The base vector.</param>
        /// <param name="b">The component-wise exponents.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 pow(Vector2 a, Vector2 b)
        {
            return new Vector2(pow(a.X, b.X), pow(a.Y, b.Y));
        }

        /// <summary>
        /// Returns a unit vector with the same direction as the input.
        /// </summary>
        /// <param name="a">The vector to normalize.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 normalize(Vector2 a)
        {
            return Vector2.Normalize(a);
        }

        /// <summary>
        /// Converts each component of a vector from degrees to radians.
        /// </summary>
        /// <param name="a">The vector of angles in degrees.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 radians(Vector2 a)
        {
            return a * DegToRad;
        }

        /// <summary>
        /// Linearly interpolates between two vectors.
        /// </summary>
        /// <param name="a">The start vector.</param>
        /// <param name="b">The end vector.</param>
        /// <param name="t">The interpolation factor, typically in the range [0, 1].</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 lerp(Vector2 a, Vector2 b, float t)
        {
            return Vector2.Lerp(a, b, t);
        }

        /// <summary>
        /// Returns the component-wise arccosine of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 acos(Vector2 a)
        {
            return new Vector2(acos(a.X), acos(a.Y));
        }

        /// <summary>
        /// Returns the component-wise arcsine of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 asin(Vector2 a)
        {
            return new Vector2(asin(a.X), asin(a.Y));
        }

        /// <summary>
        /// Returns the component-wise arctangent of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 atan(Vector2 a)
        {
            return new Vector2(atan(a.X), atan(a.Y));
        }

        /// <summary>
        /// Returns the component-wise arctangent of the quotient of two vectors.
        /// </summary>
        /// <param name="a">The numerator vector (y coordinates).</param>
        /// <param name="b">The denominator vector (x coordinates).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 atan2(Vector2 a, Vector2 b)
        {
            return new Vector2(atan2(a.X, b.X), atan2(a.Y, b.Y));
        }

        /// <summary>
        /// Returns the component-wise cosine of a vector of angles in radians.
        /// </summary>
        /// <param name="a">The vector of angles in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 cos(Vector2 a)
        {
            return new Vector2(cos(a.X), cos(a.Y));
        }

        /// <summary>
        /// Returns the component-wise sine of a vector of angles in radians.
        /// </summary>
        /// <param name="a">The vector of angles in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 sin(Vector2 a)
        {
            return new Vector2(sin(a.X), sin(a.Y));
        }

        /// <summary>
        /// Returns the component-wise tangent of a vector of angles in radians.
        /// </summary>
        /// <param name="a">The vector of angles in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 tan(Vector2 a)
        {
            return new Vector2(tan(a.X), tan(a.Y));
        }

        /// <summary>
        /// Returns the component-wise hyperbolic cosine of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 cosh(Vector2 a)
        {
            return new Vector2(cosh(a.X), cosh(a.Y));
        }

        /// <summary>
        /// Returns the component-wise hyperbolic sine of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 sinh(Vector2 a)
        {
            return new Vector2(sinh(a.X), sinh(a.Y));
        }

        /// <summary>
        /// Returns the component-wise hyperbolic tangent of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 tanh(Vector2 a)
        {
            return new Vector2(tanh(a.X), tanh(a.Y));
        }

        /// <summary>
        /// Computes the component-wise sine and cosine of a vector of angles in radians.
        /// </summary>
        /// <param name="a">The vector of angles in radians.</param>
        /// <param name="s">Receives the sine of each angle.</param>
        /// <param name="c">Receives the cosine of each angle.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void sincos(Vector2 a, out Vector2 s, out Vector2 c)
        {
            s = sin(a);
            c = cos(a);
        }

        /// <summary>
        /// Returns the component-wise rounded value of a vector.
        /// </summary>
        /// <param name="a">The input vector.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 round(Vector2 a)
        {
            return new Vector2(round(a.X), round(a.Y));
        }

        /// <summary>
        /// Snaps each component of a vector to the nearest multiple of an interval around an offset.
        /// </summary>
        /// <param name="value">The vector to snap.</param>
        /// <param name="interval">The component-wise snapping intervals; zero leaves that component unchanged.</param>
        /// <param name="offset">The component-wise origins of the snapping grid.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 snap(Vector2 value, Vector2 interval, Vector2 offset)
        {
            return new Vector2(
                snap(value.X, interval.X, offset.X),
                snap(value.Y, interval.Y, offset.Y)
            );
        }

    }
}

