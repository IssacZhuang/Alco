using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// A 2D transform composed of a position, rotation and scale.
    /// </summary>
    public struct Transform2D
    {
        /// <summary>
        /// The position of the transform in world space.
        /// </summary>
        public Vector2 Position;
        /// <summary>
        /// The rotation of the transform in world space stored as a radian.
        /// </summary>
        public Rotation2D Rotation;
        /// <summary>
        /// The scale of the transform in world space.
        /// </summary>
        public Vector2 Scale;

        public static readonly Transform2D Identity = new Transform2D(Vector2.Zero, Rotation2D.Identity, Vector2.One);

        public Transform2D()
        {
            this.Position = Vector2.Zero;
            this.Rotation = Rotation2D.Identity;
            this.Scale = Vector2.One;
        }

        public Transform2D(Vector2 pos)
        {
            this.Position = pos;
            this.Rotation = Rotation2D.Identity;
            this.Scale = Vector2.One;
        }

        public Transform2D(Vector2 pos, Rotation2D rot)
        {
            this.Position = pos;
            this.Rotation = rot;
            this.Scale = Vector2.One;
        }

        public Transform2D(Rotation2D rot, Vector2 pos)
        {
            this.Position = pos;
            this.Rotation = rot;
            this.Scale = Vector2.One;
        }

        public Transform2D(Vector2 pos, Rotation2D rot, Vector2 scale)
        {
            this.Position = pos;
            this.Rotation = rot;
            this.Scale = scale;
        }

        /// <summary>
        /// The direction of the transform
        /// </summary>
        public Vector2 Direction
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return math.direction(Rotation);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                Vector2 norm = math.normalize(value);
                Rotation = new Rotation2D(norm.X, norm.Y);
            }
        }

        /// <summary>
        /// The matrix representation of the translation, rotation and scale.
        /// </summary>
        /// <value></value>
        public Matrix4x4 Matrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return math.matrix4trs(Position, Rotation, Scale);
            }
        }

        /// <summary>
        /// The matrix representation of the translation.
        /// </summary>
        /// <value></value>
        public Matrix4x4 MatrixTranslation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return math.matrix4translation(Position);
            }
        }

        /// <summary>
        /// The matrix representation of the rotation.
        /// </summary>
        /// <value></value>
        public Matrix4x4 MatrixRotation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return math.matrix4rotation(Rotation);
            }
        }

        /// <summary>
        /// The matrix representation of the scale.
        /// </summary>
        /// <value></value>
        public Matrix4x4 MatrixScale
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return math.matrix4scale(Scale);
            }
        }

        /// <summary>
        /// Translates the transform by the given translation.
        /// </summary>
        /// <param name="translation">The translation to apply.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Translate(Vector2 translation)
        {
            Position += math.rotate(translation, Rotation);
        }

        /// <summary>
        /// Rotates the transform by the given radians.
        /// </summary>
        /// <param name="radians">The radians to rotate by.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rotate(float radians)
        {
            math.sincos(radians, out float s, out float c);
            Rotation = math.mul(Rotation, new Rotation2D(c, s));
        }

        /// <summary>
        /// Rotates the transform by the given degrees.
        /// </summary>
        /// <param name="degree">The degrees to rotate by.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RotateDegree(float degree)
        {
            Rotate(math.radians(degree));
        }

        /// <summary>
        /// Rotates the transform by the given rotation.
        /// </summary>
        /// <param name="rot">The rotation to apply.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rotate(Rotation2D rot)
        {
            Rotation = math.mul(Rotation, rot);
        }

        /// <summary>
        /// Rotates the transform around the given center by the given rotation.
        /// </summary>
        /// <param name="center">The center to rotate around.</param>
        /// <param name="rot">The rotation to apply.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rotate(Vector2 center, Rotation2D rot)
        {
            Position = math.rotate(Position - center, rot) + center;
            Rotation = math.mul(Rotation, rot);
        }

        
        /// <summary>
        /// Set direction of the transform to look at the given point.
        /// </summary>
        /// <param name="point">The point to look at.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LookAt(Vector2 point)
        {
            this.Direction = point - Position;
        }

        
        public override string ToString()
        {
            return $"Position: {Position}, Rotation: {Rotation}, Scale: {Scale}";
        }
    }
}