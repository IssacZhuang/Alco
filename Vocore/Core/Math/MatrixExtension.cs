using System;
using System.Numerics;
using System.Collections.Generic;

namespace Vocore
{
    public static class MatrixExtension
    {
        public static Matrix4x4 CreateTransform(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
        }
    }
}

