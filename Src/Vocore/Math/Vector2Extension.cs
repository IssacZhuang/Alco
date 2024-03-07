using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public static class Vector2Extension
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 YX(this Vector2 v)
        {
            return new Vector2(v.Y, v.X);
        }
    }
}