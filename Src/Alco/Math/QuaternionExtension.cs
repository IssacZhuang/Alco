
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static class QuaternionExtension
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ToDegrees(this Quaternion q)
        {
            return math.euler(q);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ToRadians(this Quaternion q)
        {
            return math.euler(q) * math.DegToRad;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Normalize(this Quaternion q)
        {
            return Quaternion.Normalize(q);
        }
    }
}