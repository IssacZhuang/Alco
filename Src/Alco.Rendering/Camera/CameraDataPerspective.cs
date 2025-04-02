using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

/// <summary>
/// The mathmatical representation of a 3D perspective camera.
/// </summary>
public struct CameraDataPerspective : ICameraData
{
    public const float DefaultFov = 0.83f;
    public const float DefaultNear = 0.1f;
    public const float DefaultFar = 1000f;

    public Transform3D transform;
    public float near;
    public float far;
    public float fov;
    public float aspectRatio;

    public Matrix4x4 ViewMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Matrix4x4.CreateLookAtLeftHanded(transform.Position, transform.Position + Vector3.Transform(Vector3.UnitX, transform.Rotation), Vector3.UnitZ);
    }


    public Matrix4x4 ProjectionMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(fov, aspectRatio, near, far);
    }

    public Matrix4x4 ViewProjectionMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return ViewMatrix * ProjectionMatrix;
        }
    }

    public CameraDataPerspective(float fov = DefaultFov, float aspectRatio = 16 / 9f, float near = DefaultNear, float far = DefaultFar)
    {
        this.fov = fov;
        this.near = near;
        this.far = far;
        this.aspectRatio = aspectRatio;

        transform = Transform3D.Identity;
    }
}
