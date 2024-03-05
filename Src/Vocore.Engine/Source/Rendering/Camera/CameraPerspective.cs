using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore.Engine
{
    /// <summary>
    /// The mathmatical representation of a 3D perspective camera.
    /// </summary>
    public struct CameraPerspective
    {
        public const float DefaultFov = 0.83f;
        public const float DefaultNear = 0.1f;
        public const float DefaultFar = 1000f;

        public Transform3D tranform;
        public float near;
        public float far;
        public float fov;
        public float aspectRatio;

        public Matrix4x4 ViewMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>Matrix4x4.CreateLookAt(tranform.position, tranform.position + Vector3.Transform(Vector3.UnitZ, tranform.rotation), Vector3.UnitY);
        }


        public Matrix4x4 ProjectionMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Matrix4x4.CreatePerspectiveFieldOfView(fov, aspectRatio, near, far);
        }

        public Matrix4x4 ViewProjectionMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return ViewMatrix * ProjectionMatrix;
            }
        }

        public CameraPerspective(float fov = DefaultFov, float near = DefaultNear, float far = DefaultFar, float aspectRatio = 16/9f)
        {
            this.fov = fov;
            this.near = near;
            this.far = far;
            this.aspectRatio = aspectRatio;

            tranform = Transform3D.Default;
        }
    }
}