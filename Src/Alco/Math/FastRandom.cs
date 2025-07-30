using static Alco.math;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct FastRandom
    {
        public uint state;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FastRandom(uint seed)
        {
            state = seed;
            CheckState();
            NextState();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FastRandom CreateFromIndex(uint index)
        {

            // Wang hash will hash 61 to zero but we want uint.MaxValue to hash to zero.  To make this happen
            // we must offset by 62.
            return new FastRandom(WangHash(index + 62u));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint WangHash(uint n)
        {
            // https://gist.github.com/badboy/6267743#hash-function-construction-principles
            // Wang hash: this has the property that none of the outputs will
            // collide with each other, which is important for the purposes of
            // seeding a random number generator.  This was verified empirically
            // by checking all 2^32 uints.
            n = (n ^ 61u) ^ (n >> 16);
            n *= 9u;
            n = n ^ (n >> 4);
            n *= 0x27d4eb2du;
            n = n ^ (n >> 15);

            return n;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint NextState()
        {
            CheckState();
            uint t = state;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return t;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckState()
        {
            if (state == 0)
                throw new System.ArgumentException("Invalid state 0. Random object has not been properly initialized");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint NextUint()
        {
            return NextState();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int NextInt()
        {
            return (int)NextState() ^ -2147483648;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte NextByte()
        {
            return (byte)(NextState() >> 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NextFloat()
        {
            return asfloat(0x3f800000 | (NextState() >> 9)) - 1.0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2 NextVector2()
        {
            return new Vector2(NextFloat(), NextFloat());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 NextVector3()
        {
            return new Vector3(NextFloat(), NextFloat(), NextFloat());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector4 NextVector4()
        {
            return new Vector4(NextFloat(), NextFloat(), NextFloat(), NextFloat());
        }

        /// <summary>
        /// Generates a random int2 vector with each component in the range [int.MinValue, int.MaxValue].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int2 NextInt2()
        {
            return new int2(NextInt(), NextInt());
        }

        /// <summary>
        /// Generates a random int3 vector with each component in the range [int.MinValue, int.MaxValue].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int3 NextInt3()
        {
            return new int3(NextInt(), NextInt(), NextInt());
        }

        /// <summary>
        /// Generates a random int4 vector with each component in the range [int.MinValue, int.MaxValue].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int4 NextInt4()
        {
            return new int4(NextInt(), NextInt(), NextInt(), NextInt());
        }

        /// <summary>
        /// Generates a random uint2 vector with each component in the range [0, uint.MaxValue].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint2 NextUint2()
        {
            return new uint2(NextUint(), NextUint());
        }

        /// <summary>
        /// Generates a random uint3 vector with each component in the range [0, uint.MaxValue].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint3 NextUint3()
        {
            return new uint3(NextUint(), NextUint(), NextUint());
        }

        /// <summary>
        /// Generates a random uint4 vector with each component in the range [0, uint.MaxValue].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint4 NextUint4()
        {
            return new uint4(NextUint(), NextUint(), NextUint(), NextUint());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint NextUint(uint max)
        {
            return NextUint() % max;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int NextInt(int max)
        {
            return (int)((NextState() * (ulong)max) >> 32);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NextFloat(float max)
        {
            return NextFloat() * max;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2 NextVector2(Vector2 max)
        {
            return NextVector2() * max;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2 NextVector2(float max)
        {
            return NextVector2() * max;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 NextVector3(Vector3 max)
        {
            return NextVector3() * max;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 NextVector3(float max)
        {
            return NextVector3() * max;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector4 NextVector4(Vector4 max)
        {
            return NextVector4() * max;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector4 NextVector4(float max)
        {
            return NextVector4() * max;
        }

        /// <summary>
        /// Generates a random int2 vector with each component in the range [0, max.X), [0, max.Y).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int2 NextInt2(int2 max)
        {
            return new int2(NextInt(max.X), NextInt(max.Y));
        }

        /// <summary>
        /// Generates a random int3 vector with each component in the range [0, max.X), [0, max.Y), [0, max.Z).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int3 NextInt3(int3 max)
        {
            return new int3(NextInt(max.X), NextInt(max.Y), NextInt(max.Z));
        }

        /// <summary>
        /// Generates a random int4 vector with each component in the range [0, max.X), [0, max.Y), [0, max.Z), [0, max.W).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int4 NextInt4(int4 max)
        {
            return new int4(NextInt(max.X), NextInt(max.Y), NextInt(max.Z), NextInt(max.W));
        }

        /// <summary>
        /// Generates a random int2 vector with each component in the range [0, max).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int2 NextInt2(int max)
        {
            return new int2(NextInt(max), NextInt(max));
        }

        /// <summary>
        /// Generates a random int3 vector with each component in the range [0, max).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int3 NextInt3(int max)
        {
            return new int3(NextInt(max), NextInt(max), NextInt(max));
        }

        /// <summary>
        /// Generates a random int4 vector with each component in the range [0, max).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int4 NextInt4(int max)
        {
            return new int4(NextInt(max), NextInt(max), NextInt(max), NextInt(max));
        }

        /// <summary>
        /// Generates a random uint2 vector with each component in the range [0, max.X), [0, max.Y).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint2 NextUint2(uint2 max)
        {
            return new uint2(NextUint(max.X), NextUint(max.Y));
        }

        /// <summary>
        /// Generates a random uint3 vector with each component in the range [0, max.X), [0, max.Y), [0, max.Z).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint3 NextUint3(uint3 max)
        {
            return new uint3(NextUint(max.X), NextUint(max.Y), NextUint(max.Z));
        }

        /// <summary>
        /// Generates a random uint4 vector with each component in the range [0, max.X), [0, max.Y), [0, max.Z), [0, max.W).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint4 NextUint4(uint4 max)
        {
            return new uint4(NextUint(max.X), NextUint(max.Y), NextUint(max.Z), NextUint(max.W));
        }

        /// <summary>
        /// Generates a random uint2 vector with each component in the range [0, max).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint2 NextUint2(uint max)
        {
            return new uint2(NextUint(max), NextUint(max));
        }

        /// <summary>
        /// Generates a random uint3 vector with each component in the range [0, max).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint3 NextUint3(uint max)
        {
            return new uint3(NextUint(max), NextUint(max), NextUint(max));
        }

        /// <summary>
        /// Generates a random uint4 vector with each component in the range [0, max).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint4 NextUint4(uint max)
        {
            return new uint4(NextUint(max), NextUint(max), NextUint(max), NextUint(max));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint NextUint(uint min, uint max)
        {
            return NextUint() % (max - min) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int NextInt(int min, int max)
        {
            uint range = (uint)(max - min);
            return (int)(NextState() * (ulong)range >> 32) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NextFloat(float min, float max)
        {
            return NextFloat() * (max - min) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2 NextVector2(Vector2 min, Vector2 max)
        {
            return NextVector2() * (max - min) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2 NextVector2(float min, float max)
        {
            return NextVector2() * (max - min) + min * Vector2.One;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 NextVector3(Vector3 min, Vector3 max)
        {
            return NextVector3() * (max - min) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 NextVector3(float min, float max)
        {
            return NextVector3() * (max - min) + min * Vector3.One;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector4 NextVector4(Vector4 min, Vector4 max)
        {
            return NextVector4() * (max - min) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector4 NextVector4(float min, float max)
        {
            return NextVector4() * (max - min) + min * Vector4.One;
        }

        /// <summary>
        /// Generates a random int2 vector with each component in the range [min.X, max.X), [min.Y, max.Y).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int2 NextInt2(int2 min, int2 max)
        {
            return new int2(NextInt(min.X, max.X), NextInt(min.Y, max.Y));
        }

        /// <summary>
        /// Generates a random int3 vector with each component in the range [min.X, max.X), [min.Y, max.Y), [min.Z, max.Z).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int3 NextInt3(int3 min, int3 max)
        {
            return new int3(NextInt(min.X, max.X), NextInt(min.Y, max.Y), NextInt(min.Z, max.Z));
        }

        /// <summary>
        /// Generates a random int4 vector with each component in the range [min.X, max.X), [min.Y, max.Y), [min.Z, max.Z), [min.W, max.W).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int4 NextInt4(int4 min, int4 max)
        {
            return new int4(NextInt(min.X, max.X), NextInt(min.Y, max.Y), NextInt(min.Z, max.Z), NextInt(min.W, max.W));
        }

        /// <summary>
        /// Generates a random int2 vector with each component in the range [min, max).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int2 NextInt2(int min, int max)
        {
            return new int2(NextInt(min, max), NextInt(min, max));
        }

        /// <summary>
        /// Generates a random int3 vector with each component in the range [min, max).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int3 NextInt3(int min, int max)
        {
            return new int3(NextInt(min, max), NextInt(min, max), NextInt(min, max));
        }

        /// <summary>
        /// Generates a random int4 vector with each component in the range [min, max).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int4 NextInt4(int min, int max)
        {
            return new int4(NextInt(min, max), NextInt(min, max), NextInt(min, max), NextInt(min, max));
        }

        /// <summary>
        /// Generates a random uint2 vector with each component in the range [min.X, max.X), [min.Y, max.Y).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint2 NextUint2(uint2 min, uint2 max)
        {
            return new uint2(NextUint(min.X, max.X), NextUint(min.Y, max.Y));
        }

        /// <summary>
        /// Generates a random uint3 vector with each component in the range [min.X, max.X), [min.Y, max.Y), [min.Z, max.Z).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint3 NextUint3(uint3 min, uint3 max)
        {
            return new uint3(NextUint(min.X, max.X), NextUint(min.Y, max.Y), NextUint(min.Z, max.Z));
        }

        /// <summary>
        /// Generates a random uint4 vector with each component in the range [min.X, max.X), [min.Y, max.Y), [min.Z, max.Z), [min.W, max.W).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint4 NextUint4(uint4 min, uint4 max)
        {
            return new uint4(NextUint(min.X, max.X), NextUint(min.Y, max.Y), NextUint(min.Z, max.Z), NextUint(min.W, max.W));
        }

        /// <summary>
        /// Generates a random uint2 vector with each component in the range [min, max).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint2 NextUint2(uint min, uint max)
        {
            return new uint2(NextUint(min, max), NextUint(min, max));
        }

        /// <summary>
        /// Generates a random uint3 vector with each component in the range [min, max).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint3 NextUint3(uint min, uint max)
        {
            return new uint3(NextUint(min, max), NextUint(min, max), NextUint(min, max));
        }

        /// <summary>
        /// Generates a random uint4 vector with each component in the range [min, max).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint4 NextUint4(uint min, uint max)
        {
            return new uint4(NextUint(min, max), NextUint(min, max), NextUint(min, max), NextUint(min, max));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion NextQuaternionRotation()
        {
            Vector3 rnd = NextVector3(new Vector3(2.0f * PI, 2.0f * PI, 1.0f));
            float u1 = rnd.Z;
            Vector2 theta_rho = rnd.XY();

            float i = sqrt(1.0f - u1);
            float j = sqrt(u1);

            Vector2 sin_theta_rho;
            Vector2 cos_theta_rho;
            sincos(theta_rho, out sin_theta_rho, out cos_theta_rho);

            Vector4 q = new Vector4(i * sin_theta_rho.X, i * cos_theta_rho.X, j * sin_theta_rho.Y, j * cos_theta_rho.Y);
            q = select(q, -q, q.W < 0.0f);
            return new Quaternion(q.X, q.Y, q.Z, q.W);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rotation2D NextRotation2D()
        {
            return Rotation2D.FromRadians(NextFloat(2.0f * PI));
        }
    }
}