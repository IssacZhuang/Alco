using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Vocore
{
    public struct TransformData
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;

        public Matrix4x4 Matrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Matrix4x4.TRS(position, rotation, scale);
            }
        }
    }
}