using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
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
        public static readonly Transform3D Default = new Transform3D(Vector3.Zero, Quaternion.Identity, Vector3.One);
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
                return Matrix4x4.CreateTranslation(position);
            }
        }

        public Matrix4x4 MatrixRotation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Matrix4x4.CreateFromQuaternion(rotation);
            }
        }

        public Matrix4x4 MatrixScale
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Matrix4x4.CreateScale(scale);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Translate(Vector3 translation)
        {
            position += math.rotate(translation, rotation);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LookAt(Vector3 point)
        {
            rotation = math.direction(point - position);
        } 

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rotate(Quaternion rotation)
        {
            this.rotation = math.mul(this.rotation, rotation);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rotate(Vector3 axis, float angle)
        {
            Rotate(Quaternion.CreateFromAxisAngle(axis, angle));
        }

        
    }
}