using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public struct Transform2D
    {
        /// <summary>
        /// The position of the transform in world space.
        /// </summary>
        public Vector2 position;
        /// <summary>
        /// The rotation of the transform in world space stored as a radian.
        /// </summary>
        public Rotation2D rotation;
        /// <summary>
        /// The scale of the transform in world space.
        /// </summary>
        public Vector2 scale;

        public static readonly Transform2D Identity = new Transform2D(Vector2.Zero, Rotation2D.Identity, Vector2.One);

        public Transform2D(Vector2 pos)
        {
            this.position = pos;
            this.rotation = Rotation2D.Identity;
            this.scale = Vector2.One;
        }

        public Transform2D(Vector2 pos, Rotation2D rot)
        {
            this.position = pos;
            this.rotation = rot;
            this.scale = Vector2.One;
        }

        public Transform2D(Rotation2D rot, Vector2 pos)
        {
            this.position = pos;
            this.rotation = rot;
            this.scale = Vector2.One;
        }

        public Transform2D(Vector2 pos, Rotation2D rot, Vector2 scale)
        {
            this.position = pos;
            this.rotation = rot;
            this.scale = scale;
        }

        public Vector2 Direction
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return math.direction(rotation);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                Vector2 norm = math.normalize(value);
                rotation = new Rotation2D(norm.X, norm.Y);
            }
        }

        public Matrix4x4 Matrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return math.matrix4trs(position, rotation, scale);
            }
        }

        public Matrix4x4 MatrixTranslation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return math.matrix4translation(position);
            }
        }

        public Matrix4x4 MatrixRotation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return math.matrix4rotation(rotation);
            }
        }

        public Matrix4x4 MatrixScale
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return math.matrix4scale(scale);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Translate(Vector2 translation)
        {
            position += math.rotate(translation, rotation);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rotate(float radians)
        {
            math.sincos(radians, out float s, out float c);
            rotation = math.mul(rotation, new Rotation2D(c, s));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rotate(Rotation2D rot)
        {
            rotation = math.mul(rotation, rot);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LookAt(Vector2 point)
        {
            this.Direction = point - position;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rotate(Vector2 center, Rotation2D rot)
        {
            position = math.rotate(position - center, rot) + center;
            rotation = math.mul(rotation, rot);
        }
    }
}