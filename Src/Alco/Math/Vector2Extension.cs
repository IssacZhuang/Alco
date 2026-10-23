using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// Provides extension members for <see cref="Vector2"/>.
    /// </summary>
    public static class Vector2Extension
    {
        extension(Vector2 value)
        {
            public Vector2 YX
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    return new Vector2(value.Y, value.X);
                }
            }
        }
    }
}