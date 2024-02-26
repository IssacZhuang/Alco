using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore.Engine;

public class Camera2D : ICamera2D
{
    public Transform2D transform;

    public Camera2D()
    {
        transform = Transform2D.Identity;
    }

    public ref Vector2 Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref transform.scale;
    }

    public Matrix4x4 ViewMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => math.matrix4translation(-transform.position) * math.matrix4rotation(math.inverse(transform.rotation));
    }

    public Matrix4x4 ProjectionMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => math.matrix4scale(math.reciprocal(transform.scale));
    }

    public Matrix4x4 ViewProjectionMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ViewMatrix * ProjectionMatrix;
    }
}