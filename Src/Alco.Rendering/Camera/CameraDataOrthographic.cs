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


        public Transform3D tranform;
        public float near;
        public float far;
        public float width;
        public float height;

        public Matrix4x4 ViewMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Matrix4x4.CreateLookAtLeftHanded(tranform.Position, tranform.Position + Vector3.Transform(Vector3.UnitX, tranform.Rotation), Vector3.UnitZ);
        }


        public Matrix4x4 ProjectionMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Matrix4x4.CreateOrthographicLeftHanded(width, height, near, far);
        }

        public Matrix4x4 ViewProjectionMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ViewMatrix * ProjectionMatrix;

        }

        public CameraDataOrthographic(float width = DefaultWidth, float height = DefaultHeight, float near = DefaultNear, float far = DefaultFar)
        {
            this.width = width;
            this.height = height;
            this.near = near;
            this.far = far;

            tranform = Transform3D.Identity;
        }
    }
}

