using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

/// <summary>
/// The mathmatical representation of a 3D perspective camera.
/// </summary>
public struct CameraDataPerspective : ICamera
{
    public const float DefaultFov = 0.83f;
    public const float DefaultNear = 0.1f;
    public const float DefaultFar = 1000f;

    /// <summary>
    /// The transformation data of the camera in 3D space.
    /// </summary>
    public Transform3D Transform;

    /// <summary>
    /// The near clipping plane distance.
    /// </summary>
    public float Near;

    /// <summary>
    /// The far clipping plane distance.
    /// </summary>
    public float Far;

    /// <summary>
    /// The field of view in radians.
    /// </summary>
    public float Fov;

    /// <summary>
    /// The aspect ratio (width/height) of the viewport.
    /// </summary>
    public float AspectRatio;

    /// <summary>
    /// Whether to build a reversed infinite-far projection: NDC z maps the near
    /// plane to 1.0 and the far plane (at infinity) to 0.0, so depth precision
    /// stays uniform in relative terms and nothing is ever far-clipped. Requires
    /// a float depth buffer cleared to 0 with GreaterEqual depth tests
    /// (<c>DepthStencilState.WriteReverseZ</c> / <c>DepthStencilState.ReadReverseZ</c>);
    /// <see cref="Far"/> is unused for the projection itself.
    /// </summary>
    public bool ReverseInfiniteDepth;

    /// <summary>
    /// Gets the view matrix representing the camera's orientation and position in the world.
    /// </summary>
    public Matrix4x4 ViewMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Matrix4x4.CreateLookAtLeftHanded(
            Transform.Position,
            Transform.Position + Vector3.Transform(Vector3.UnitX, Transform.Rotation),
            Vector3.Transform(Vector3.UnitZ, Transform.Rotation));
    }


    /// <summary>
    /// Gets the projection matrix used to transform from view space to clip space.
    /// </summary>
    public Matrix4x4 ProjectionMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ReverseInfiniteDepth
            ? CreatePerspectiveFieldOfViewReversedInfinite(Fov, AspectRatio, Near)
            : Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(Fov, AspectRatio, Near, Far);
    }

    /// <summary>
    /// Reversed infinite-far perspective projection, row-vector left-handed convention
    /// matching <see cref="Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded"/> (z mapped
    /// to [0, 1]): z_ndc = near / z_view, i.e. near maps to 1.0 and the far plane at
    /// infinity to 0.0, which keeps float depth precision uniform in relative terms.
    /// With M33 = 0 and M43 = near, z_clip = near and w = z_view.
    /// </summary>
    private static Matrix4x4 CreatePerspectiveFieldOfViewReversedInfinite(float fieldOfView, float aspectRatio, float nearPlaneDistance)
    {
        float yScale = 1f / MathF.Tan(fieldOfView * 0.5f);
        Matrix4x4 matrix = default;
        matrix.M11 = yScale / aspectRatio;
        matrix.M22 = yScale;
        matrix.M33 = 0f;
        matrix.M34 = 1f;
        matrix.M43 = nearPlaneDistance;
        return matrix;
    }

    /// <summary>
    /// Gets the combined view-projection matrix.
    /// </summary>
    public Matrix4x4 ViewProjectionMatrix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return ViewMatrix * ProjectionMatrix;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CameraDataPerspective"/> struct with specified parameters.
    /// </summary>
    /// <param name="fov">The field of view in radians. Defaults to <see cref="DefaultFov"/>.</param>
    /// <param name="aspectRatio">The aspect ratio (width/height) of the viewport. Defaults to 16/9.</param>
    /// <param name="near">The near clipping plane distance. Defaults to <see cref="DefaultNear"/>.</param>
    /// <param name="far">The far clipping plane distance. Defaults to <see cref="DefaultFar"/>.</param>
    public CameraDataPerspective(float fov = DefaultFov, float aspectRatio = 16 / 9f, float near = DefaultNear, float far = DefaultFar)
    {
        this.Fov = fov;
        this.Near = near;
        this.Far = far;
        this.AspectRatio = aspectRatio;

        Transform = Transform3D.Identity;
    }

    public Ray3D ScreenPointToRay(Vector2 screenPosition, Vector2 screenSize)
    {
        return CameraMathUtility.ScreenPointToRayPerspective(screenPosition, screenSize, ViewProjectionMatrix, Transform.Position);
    }
}
