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

    public Transform3D Transform;
    public float Near;
    public float Far;
    public float Fov;
    public float AspectRatio;

    public Matrix4x4 ViewMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Matrix4x4.CreateLookAtLeftHanded(
            Transform.Position,
            Transform.Position + Vector3.Transform(Vector3.UnitX, Transform.Rotation),
            Vector3.Transform(Vector3.UnitZ, Transform.Rotation));
    }


    public Matrix4x4 ProjectionMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(Fov, AspectRatio, Near, Far);
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
        this.Fov = fov;
        this.Near = near;
        this.Far = far;
        this.AspectRatio = aspectRatio;

        Transform = Transform3D.Identity;
    }
}
