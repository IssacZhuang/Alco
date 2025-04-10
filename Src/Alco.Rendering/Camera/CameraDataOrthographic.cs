using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering
{
    /// <summary>
    /// The mathmatical representation of a 3D Orthographic camera.
    /// </summary>
    public struct CameraDataOrthographic : ICameraData
    {
        /// <summary>
        /// Default width value for the orthographic camera.
        /// </summary>
        public const float DefaultWidth = 16f / 9f;

        /// <summary>
        /// Default height value for the orthographic camera.
        /// </summary>
        public const float DefaultHeight = 1f;

        /// <summary>
        /// Default near plane distance for the orthographic camera.
        /// </summary>
        public const float DefaultNear = 0.1f;

        /// <summary>
        /// Default far plane distance for the orthographic camera.
        /// </summary>
        public const float DefaultFar = 1000f;


        /// <summary>
        /// The transform of the camera in 3D space.
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
        /// The width of the orthographic view.
        /// </summary>
        public float Width;

        /// <summary>
        /// The height of the orthographic view.
        /// </summary>
        public float Height;

        /// <summary>
        /// Gets the view matrix for the camera.
        /// </summary>
        public Matrix4x4 ViewMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Matrix4x4.CreateLookAtLeftHanded(Transform.Position, Transform.Position + Vector3.Transform(Vector3.UnitX, Transform.Rotation), Vector3.Transform(Vector3.UnitZ, Transform.Rotation));
        }


        /// <summary>
        /// Gets the projection matrix for the camera.
        /// </summary>
        public Matrix4x4 ProjectionMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Matrix4x4.CreateOrthographicLeftHanded(Width, Height, Near, Far);
        }

        /// <summary>
        /// Gets the combined view and projection matrix for the camera.
        /// </summary>
        public Matrix4x4 ViewProjectionMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ViewMatrix * ProjectionMatrix;

        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CameraDataOrthographic"/> struct with the specified parameters.
        /// </summary>
        /// <param name="width">The width of the orthographic view. Defaults to <see cref="DefaultWidth"/>.</param>
        /// <param name="height">The height of the orthographic view. Defaults to <see cref="DefaultHeight"/>.</param>
        /// <param name="near">The near clipping plane distance. Defaults to <see cref="DefaultNear"/>.</param>
        /// <param name="far">The far clipping plane distance. Defaults to <see cref="DefaultFar"/>.</param>
        public CameraDataOrthographic(float width = DefaultWidth, float height = DefaultHeight, float near = DefaultNear, float far = DefaultFar)
        {
            Width = width;
            Height = height;
            Near = near;
            Far = far;

            Transform = Transform3D.Identity;
        }
    }
}

