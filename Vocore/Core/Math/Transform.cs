using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public struct Transform//umanaged
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public static readonly Transform Default = new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One);
        public static readonly Vector3 Forward = Vector3.UnitZ;
        public static readonly Vector3 Back = -Vector3.UnitZ;
        public static readonly Vector3 Up = Vector3.UnitY;
        public static readonly Vector3 Down = -Vector3.UnitY;
        public static readonly Vector3 Right = -Vector3.UnitX;
        public static readonly Vector3 Left = Vector3.UnitX;

        public Transform(Vector3 pos)
        {
            this.position = pos;
            this.rotation = Quaternion.Identity;
            this.scale = Vector3.One;
        }

        public Transform(Vector3 pos, Quaternion rot)
        {
            this.position = pos;
            this.rotation = rot;
            this.scale = Vector3.One;
        }

        public Transform(Quaternion rot, Vector3 pos)
        {
            this.position = pos;
            this.rotation = rot;
            this.scale = Vector3.One;
        }

        public Transform(Vector3 pos, Quaternion rot, Vector3 scale)
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
                return Vector3.Transform(Vector3.UnitZ, rotation);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                //SetDirection(value);
            }
        }

        public Matrix4x4 Matrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return UtilsTranform.CreateTransformTRS(position, rotation, scale);
            }
        }

        public Matrix4x4 MatrixRT
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return UtilsTranform.CreateTransformTR(position, rotation);
            }
        }

        public Matrix4x4 MatrixSR
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return UtilsTranform.CreateTransformRS(rotation, scale);
            }
        }

        public Matrix4x4 MatrixST
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return UtilsTranform.CreateTransformTS(position, scale);
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
            position += Vector3.Transform(translation, rotation);
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