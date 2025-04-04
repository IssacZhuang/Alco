using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;


/// <summary>
/// The mathmatical representation of a 2D camera. Can be used in 2D scenes and UI.
/// It can also consider as a orthographic camera looking along the Z axis.
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
        get => transform.Scale;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => transform.Scale = value;
    }

    public Matrix4x4 ViewMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Vector3 position = new Vector3(transform.Position, 0);
            return Matrix4x4.CreateLookAtLeftHanded(position, position + Vector3.UnitZ, Vector3.UnitY);
        }
    }

    public Matrix4x4 ProjectionMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Vector2 halfSize = transform.Scale * 0.5f;
            return Matrix4x4.CreateOrthographicOffCenterLeftHanded(
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