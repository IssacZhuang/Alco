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
        public const float DefaultWidth = 16f / 9f;
        public const float DefaultHeight = 1f;
        public const float DefaultNear = 0.1f;
        public const float DefaultFar = 1000f;


        public Transform3D Transform;
        public float Near;
        public float Far;
        public float Width;
        public float Height;

        public Matrix4x4 ViewMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Matrix4x4.CreateLookAtLeftHanded(Transform.Position, Transform.Position + Vector3.Transform(Vector3.UnitX, Transform.Rotation), Vector3.Transform(Vector3.UnitZ, Transform.Rotation));
        }


        public Matrix4x4 ProjectionMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Matrix4x4.CreateOrthographicLeftHanded(Width, Height, Near, Far);
        }

        public Matrix4x4 ViewProjectionMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ViewMatrix * ProjectionMatrix;

        }

        public CameraDataOrthographic(float width = DefaultWidth, float height = DefaultHeight, float near = DefaultNear, float far = DefaultFar)
        {
            this.Width = width;
            this.Height = height;
            this.Near = near;
            this.Far = far;

            Transform = Transform3D.Identity;
        }
    }
}

