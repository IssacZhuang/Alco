using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore.Engine;

public class Camera2D : ICamera2D
{
    public Transform2D transform;

    public Camera2D()
    {
        transform = Transform2D.Default;
    }

    public ref Vector2 Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref transform.scale;
    }

    public Matrix3x2 ViewMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => math.matrix3translation(-transform.position) * math.matrix3rotation(math.inverse(transform.rotation));
    }

    public Matrix3x2 ProjectionMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => math.matrix3scale(math.reciprocal(transform.scale));
    }

    public Matrix3x2 ViewProjectionMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ViewMatrix * ProjectionMatrix;
    }
}