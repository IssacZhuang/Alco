

using System.Numerics;

using static Alco.math;

namespace Alco;

public static class Matrix4x4Extension
{
    public static void DecomposeLeftHanded(this Matrix4x4 matrix, out Vector3 scale, out Quaternion rotation, out Vector3 translation)
    {
        decompose(matrix, out scale, out rotation, out translation);
    }

}


