

using System.Numerics;

using static Alco.math;

namespace Alco;

/// <summary>
/// Extends <see cref="Matrix4x4"/> with left-handed transform operations.
/// </summary>
public static class Matrix4x4Extension
{
    /// <summary>
    /// Decomposes a left-handed transform matrix into scale, rotation, and translation components.
    /// </summary>
    /// <param name="matrix">The matrix to decompose.</param>
    /// <param name="scale">Receives the scale component.</param>
    /// <param name="rotation">Receives the rotation component.</param>
    /// <param name="translation">Receives the translation component.</param>
    public static void DecomposeLeftHanded(this Matrix4x4 matrix, out Vector3 scale, out Quaternion rotation, out Vector3 translation)
    {
        decompose(matrix, out scale, out rotation, out translation);
    }

}


