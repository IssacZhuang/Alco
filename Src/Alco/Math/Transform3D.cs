using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// A 3D transform composed of a position, rotation and scale.
    /// </summary>
    public struct Transform3D
    {
        /// <summary>
        /// The position of the transform in world space.
        /// </summary>
        public Vector3 Position;
        /// <summary>
        /// The rotation of the transform in world space stored as a Quaternion.
        /// </summary>
        public Quaternion Rotation;
        /// <summary>
        /// The scale of the transform in world space.
        /// </summary>
        public Vector3 Scale;
        /// <summary>
        /// The identity transform with zero position, no rotation, and unit scale.
        /// </summary>
        public static readonly Transform3D Identity = new Transform3D(Vector3.Zero, Quaternion.Identity, Vector3.One);

        /// <summary>
        /// Initializes a transform at the origin with no rotation and unit scale.
        /// </summary>
        public Transform3D()
        {
            Position = Vector3.Zero;
            Rotation = Quaternion.Identity;
            Scale = Vector3.One;
        }

        /// <summary>
        /// Initializes a transform at the specified position with no rotation and unit scale.
        /// </summary>
        /// <param name="pos">The world-space position.</param>
        public Transform3D(Vector3 pos)
        {
            Position = pos;
            Rotation = Quaternion.Identity;
            Scale = Vector3.One;
        }

        /// <summary>
        /// Initializes a transform with the specified position and rotation, and unit scale.
        /// </summary>
        /// <param name="pos">The world-space position.</param>
        /// <param name="rot">The world-space rotation.</param>
        public Transform3D(Vector3 pos, Quaternion rot)
        {
            Position = pos;
            Rotation = rot;
            Scale = Vector3.One;
        }

        /// <summary>
        /// Initializes a transform with the specified rotation and position, and unit scale.
        /// </summary>
        /// <param name="rot">The world-space rotation.</param>
        /// <param name="pos">The world-space position.</param>
        public Transform3D(Quaternion rot, Vector3 pos)
        {
            Position = pos;
            Rotation = rot;
            Scale = Vector3.One;
        }

        /// <summary>
        /// Initializes a transform with the specified position, rotation, and scale.
        /// </summary>
        /// <param name="pos">The world-space position.</param>
        /// <param name="rot">The world-space rotation.</param>
        /// <param name="scale">The world-space scale.</param>
        public Transform3D(Vector3 pos, Quaternion rot, Vector3 scale)
        {
            Position = pos;
            Rotation = rot;
            Scale = scale;
        }

        /// <summary>
        /// Initializes a 3D transform from a 2D transform placed on the XY plane.
        /// </summary>
        /// <param name="transform">The 2D transform to convert.</param>
        public Transform3D(Transform2D transform)
        {
            Position = new Vector3(transform.Position, 0);
            Rotation = math.quaternion(0, 0, transform.Rotation.ToDegree());
            Scale = new Vector3(transform.Scale, 1);
        }

        /// <summary>
        /// Initializes a transform from a 4x4 matrix representation.
        /// </summary>
        /// <param name="matrix">The matrix to decompose.</param>
        public Transform3D(Matrix4x4 matrix)
        {

        }

        /// <summary>
        /// The forward direction of the transform.
        /// </summary>
        /// <value></value>
        public Vector3 Direction
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return math.direction(Rotation);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                Rotation = math.direction(value);
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
                return Matrix4x4.CreateTranslation(Position);
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
                return Matrix4x4.CreateFromQuaternion(Rotation);
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
                return Matrix4x4.CreateScale(Scale);
            }
        }

        /// <summary>
        /// Translates the transform by the given translation.
        /// </summary>
        /// <param name="translation">The translation to apply.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Translate(Vector3 translation)
        {
            Position += math.rotate(translation, Rotation);
        }

        /// <summary>
        /// Rotates the transform to look at the given point.
        /// </summary>
        /// <param name="point">The point to look at.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LookAt(Vector3 point)
        {
            Rotation = math.direction(point - Position);
        } 

        /// <summary>
        /// Rotates the transform by the given rotation.
        /// </summary>
        /// <param name="rotation"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rotate(Quaternion rotation)
        {
            this.Rotation = math.mul(this.Rotation, rotation);
        }

        /// <summary>
        /// Rotates the transform by the given axis and angle.
        /// </summary>
        /// <param name="axis">The axis to rotate around.</param>
        /// <param name="angle">The angle to rotate by.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rotate(Vector3 axis, float angle)
        {
            Rotate(Quaternion.CreateFromAxisAngle(axis, angle));
        }

        /// <summary>
        /// Returns a string containing the position, rotation, and scale.
        /// </summary>
        /// <returns>A string representation of the transform.</returns>
        public override string ToString()
        {
            return $"Position: {Position}, Rotation: {Rotation}, Scale: {Scale}";
        }
    }
}