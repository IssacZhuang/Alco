using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    /// <summary>
    /// A 3D transform composed of a position, rotation and scale.
    /// </summary>
    public struct Transform3D
    {
        /// <summary>
        /// The position of the transform in world space.
        /// </summary>
        public Vector3 position;
        /// <summary>
        /// The rotation of the transform in world space stored as a Quaternion.
        /// </summary>
        public Quaternion rotation;
        /// <summary>
        /// The scale of the transform in world space.
        /// </summary>
        public Vector3 scale;
        public static readonly Transform3D Identity = new Transform3D(Vector3.Zero, Quaternion.Identity, Vector3.One);

        public Transform3D()
        {
            this.position = Vector3.Zero;
            this.rotation = Quaternion.Identity;
            this.scale = Vector3.One;
        }

        public Transform3D(Vector3 pos)
        {
            this.position = pos;
            this.rotation = Quaternion.Identity;
            this.scale = Vector3.One;
        }

        public Transform3D(Vector3 pos, Quaternion rot)
        {
            this.position = pos;
            this.rotation = rot;
            this.scale = Vector3.One;
        }

        public Transform3D(Quaternion rot, Vector3 pos)
        {
            this.position = pos;
            this.rotation = rot;
            this.scale = Vector3.One;
        }

        public Transform3D(Vector3 pos, Quaternion rot, Vector3 scale)
        {
            this.position = pos;
            this.rotation = rot;
            this.scale = scale;
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
                return math.direction(rotation);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                rotation = math.direction(value);
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
                return math.matrix4trs(position, rotation, scale);
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
                return Matrix4x4.CreateTranslation(position);
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
                return Matrix4x4.CreateFromQuaternion(rotation);
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
                return Matrix4x4.CreateScale(scale);
            }
        }

        /// <summary>
        /// Translates the transform by the given translation.
        /// </summary>
        /// <param name="translation">The translation to apply.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Translate(Vector3 translation)
        {
            position += math.rotate(translation, rotation);
        }

        /// <summary>
        /// Rotates the transform to look at the given point.
        /// </summary>
        /// <param name="point">The point to look at.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LookAt(Vector3 point)
        {
            rotation = math.direction(point - position);
        } 

        /// <summary>
        /// Rotates the transform by the given rotation.
        /// </summary>
        /// <param name="rotation"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rotate(Quaternion rotation)
        {
            this.rotation = math.mul(this.rotation, rotation);
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

        public override string ToString()
        {
            return $"Position: {position}, Rotation: {rotation}, Scale: {scale}";
        }
    }
}