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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Translate(Vector3 translation)
        {
            position += translation;
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

        public Matrix4x4 Matrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return UtilsTranform.CreateTransform(position, rotation, scale);
            }
        }
    }
}