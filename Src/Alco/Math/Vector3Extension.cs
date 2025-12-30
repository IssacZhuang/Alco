using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static class Vector3Extension
    {
        extension(Vector3 value)
        {
            public Vector3 YZX
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    return new Vector3(value.Y, value.Z, value.X);
                }
            }

            public Vector3 ZXY
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    return new Vector3(value.Z, value.X, value.Y);
                }
            }

            public Vector3 XZY
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    return new Vector3(value.X, value.Z, value.Y);
                }
            }

            public Vector3 YXZ
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    return new Vector3(value.Y, value.X, value.Z);
                }
            }

            public Vector3 ZYX
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    return new Vector3(value.Z, value.Y, value.X);
                }
            }

            public Vector2 XY
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    return new Vector2(value.X, value.Y);
                }
            }

            public Vector2 XZ
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    return new Vector2(value.X, value.Z);
                }
            }

            public Vector2 YZ
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    return new Vector2(value.Y, value.Z);
                }
            }

            public Vector2 YX
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    return new Vector2(value.Y, value.X);
                }
            }

            public Vector2 ZX
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    return new Vector2(value.Z, value.X);
                }
            }

            public Vector2 ZY
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    return new Vector2(value.Z, value.Y);
                }
            }
        }
    }
}