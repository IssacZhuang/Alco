using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore.Engine
{
    /// <summary>
    /// The mathmatical representation of a 3D Orthographic camera.
    /// </summary>
    public struct CameraOrthographic
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
            get => Matrix4x4.CreateLookAt(tranform.position, tranform.position + Vector3.Transform(Vector3.UnitZ, tranform.rotation), Vector3.UnitY);
        }


        public Matrix4x4 ProjectionMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Matrix4x4.CreateOrthographic(width, height, near, far);
        }

        public Matrix4x4 ViewProjectionMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ViewMatrix * ProjectionMatrix;

        }

        public CameraOrthographic(float width = DefaultWidth, float height = DefaultHeight, float near = DefaultNear, float far = DefaultFar)
        {
            this.width = width;
            this.height = height;
            this.near = near;
            this.far = far;

            tranform = Transform3D.Default;
        }
    }
}

