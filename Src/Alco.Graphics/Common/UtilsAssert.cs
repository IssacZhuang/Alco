using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Alco.Graphics;

internal static class UtilsAssert
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IsTrue(bool condition, string message = "")
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IsFalse(bool condition, string message = "")
    {
        if (condition)
        {
            throw new Exception(message);
        }
    }
}