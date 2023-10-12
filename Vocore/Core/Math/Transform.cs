using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public struct Tranform
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;

        public Tranform(Vector3 pos, Quaternion rot)
        {
            this.position = pos;
            this.rotation = rot;
            this.scale = Vector3.One;
        }

        public Tranform(Quaternion rot, Vector3 pos)
        {
            this.position = pos;
            this.rotation = rot;
            this.scale = Vector3.One;
        }

        public Tranform(Vector3 pos, Quaternion rot, Vector3 scale)
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
                SetDirection(value);
            }
        }

        public Matrix4x4 MatrixSRT
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return UtilsTranform.CreateTransform(position, rotation, scale);
            }
        }

        public Matrix4x4 MatrixRT
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return UtilsTranform.CreateTransform(position, rotation);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Translate(Vector3 translation)
        {
            position += translation;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LookAt(Vector3 point)
        {
            SetDirection(point - position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetDirection(Vector3 direction)
        {
            Vector3 forward = Vector3.Normalize(direction);
            Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
            Vector3 up = Vector3.Normalize(Vector3.Cross(forward, right));
            //rotation = Quaternion.CreateFromRotationMatrix(new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, forward.X, forward.Y, forward.Z, 0, 0, 0, 0, 1));
            rotation = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateLookAt(Vector3.Zero, forward, up));
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