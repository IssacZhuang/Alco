using static Alco.math;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct Random
    {
        public uint state;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Random(uint seed)
        {
            state = seed;
            CheckState();
            NextState();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Random CreateFromIndex(uint index)
        {

            // Wang hash will hash 61 to zero but we want uint.MaxValue to hash to zero.  To make this happen
            // we must offset by 62.
            return new Random(WangHash(index + 62u));
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint NextUint(uint max)
        {
            return NextUint() % max;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint NextUint(uint min, uint max)
        {
            return NextUint() % (max - min) + min;
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