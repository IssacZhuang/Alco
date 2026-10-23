
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// Extension methods for <see cref="Quaternion"/>.
    /// </summary>
    public static class QuaternionExtension
    {
        /// <summary>
        /// Converts the quaternion to Euler angles in degrees.
        /// </summary>
        /// <param name="q">The rotation to convert.</param>
        /// <returns>The Euler angles in degrees.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ToDegrees(this Quaternion q)
        {
            return math.euler(q);
        }

        /// <summary>
        /// Converts the quaternion to Euler angles in radians.
        /// </summary>
        /// <param name="q">The rotation to convert.</param>
        /// <returns>The Euler angles in radians.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ToRadians(this Quaternion q)
        {
            return math.euler(q) * math.DegToRad;
        }

        /// <summary>
        /// Returns a normalized copy of the quaternion.
        /// </summary>
        /// <param name="q">The quaternion to normalize.</param>
        /// <returns>The normalized quaternion.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Normalize(this Quaternion q)
        {
            return Quaternion.Normalize(q);
        }
    }
}