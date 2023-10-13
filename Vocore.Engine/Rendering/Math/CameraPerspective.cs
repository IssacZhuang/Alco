using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.Unsafe;

namespace Vocore.Engine
{
    public class CameraPerspective : ICamera
    {
        public const float DefaultFov = 0.83f;
        public const float DefaultNear = 0.1f;
        public const float DefaultFar = 1000f;

        private float _fov;
        private float _near;
        private float _far;

        private Matrix4x4 _projectionMatrix;
        private bool _isProjectionMatrixDirty;

        public Transform tranform;

        public static readonly uint BufferSize = (uint)UtilsMemory.SizeOf<Matrix4x4>();

        public float FieldOfView
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _fov;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                _fov = value;
                _isProjectionMatrixDirty = true;
            }
        }

        public float Near
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _near;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                _near = value;
                _isProjectionMatrixDirty = true;
            }
        }

        public float Far
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _far;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                _far = value;
                _isProjectionMatrixDirty = true;
            }
        }

        public Matrix4x4 ViewMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Matrix4x4.CreateLookAt(tranform.position, tranform.position + Vector3.Transform(Vector3.UnitZ, tranform.rotation), Vector3.UnitY);
                //return Matrix4x4.CreateTranslation(-tranform.position) * Matrix4x4.CreateFromQuaternion(tranform.rotation);
            }
        }

        public Matrix4x4 ProjectionMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (_isProjectionMatrixDirty)
                {
                    UpdateProjectionMatrix();
                    _isProjectionMatrixDirty = false;
                }
                return _projectionMatrix;
            }

        }

        public Matrix4x4 ViewProjectionMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return ViewMatrix * ProjectionMatrix;
            }
        }

        public Vector3 Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return tranform.position;
            }
        }

        public Quaternion Rotation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return tranform.rotation;
            }
        }


        public CameraPerspective(float fov = DefaultFov, float near = DefaultNear, float far = DefaultFar)
        {
            _fov = fov;
            _near = near;
            _far = far;

            tranform = Transform.Default;

            _isProjectionMatrixDirty = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateProjectionMatrix()
        {
            _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(_fov, Window.AspectRatio, _near, _far);
        }
    }
}