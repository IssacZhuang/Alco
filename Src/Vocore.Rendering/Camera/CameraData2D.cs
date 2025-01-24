using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore.Rendering;


/// <summary>
/// The mathmatical representation of a 2D camera. Can be used in 2D scenes and UI.
/// </summary>
public struct CameraData2D: ICameraData
{
    public Transform2D transform;
    public float near;
    public float far;

    public CameraData2D()
    {
        transform = Transform2D.Identity;
        near = -1;
        far = 1;
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
        get
        {
            Vector2 halfSize = transform.scale * 0.5f;
            return Matrix4x4.CreateOrthographicOffCenter(
                -halfSize.X, halfSize.X,    // left, right
                -halfSize.Y, halfSize.Y,    // bottom, top
                near, far                    // near, far 
            );
        }
    }

    public Matrix4x4 ViewProjectionMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ViewMatrix * ProjectionMatrix;
    }
}