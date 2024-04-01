using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore.Rendering;


/// <summary>
/// The mathmatical representation of a 2D camera. Can be used in 2D scenes and UI.
/// </summary>
public struct CameraData2D: ICameraData
{
    public Transform2D transform;

    public CameraData2D()
    {
        transform = Transform2D.Identity;
    }

    public Vector2 Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => transform.scale;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => transform.scale = value;
    }

    public Matrix4x4 ViewMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => math.matrix4translation(-transform.position) * math.matrix4rotation(math.inverse(transform.rotation));
    }

    public Matrix4x4 ProjectionMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => math.matrix4scale(math.reciprocal(transform.scale * 0.5f));
    }

    public Matrix4x4 ViewProjectionMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ViewMatrix * ProjectionMatrix;
    }
}