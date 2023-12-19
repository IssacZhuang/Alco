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
        public float rotation;
        /// <summary>
        /// The scale of the transform in world space.
        /// </summary>
        public Vector2 scale;

        public static readonly Transform2D Default = new Transform2D(Vector2.Zero, 0, Vector2.One);

        public Transform2D(Vector2 pos)
        {
            this.position = pos;
            this.rotation = 0;
            this.scale = Vector2.One;
        }

        public Transform2D(Vector2 pos, float rot)
        {
            this.position = pos;
            this.rotation = rot;
            this.scale = Vector2.One;
        }

        public Transform2D(float rot, Vector2 pos)
        {
            this.position = pos;
            this.rotation = rot;
            this.scale = Vector2.One;
        }

        public Transform2D(Vector2 pos, float rot, Vector2 scale)
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
                rotation = math.direction(value);
            }
        }

        public Matrix3x2 Matrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Matrix3x2.CreateScale(scale) * Matrix3x2.CreateRotation(rotation) * Matrix3x2.CreateTranslation(position);
            }
        }

        public Matrix3x2 MatrixTranslation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Matrix3x2.CreateTranslation(position);
            }
        }

        public Matrix3x2 MatrixRotation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Matrix3x2.CreateRotation(rotation);
            }
        }

        public Matrix3x2 MatrixScale
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Matrix3x2.CreateScale(scale);
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
            rotation += radians;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LookAt(Vector2 point)
        {
            rotation = math.direction(point - position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rotate(Vector2 center, float radians)
        {
            position = math.rotate(position - center, radians) + center;
            rotation += radians;
        }
    }
}