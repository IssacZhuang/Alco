using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore.Engine
{
    public class CameraPerspective : BaseCamera
    {
        public const float DefaultFov = 0.83f;
        private float _fov;
        private float _aspectRatio;
        private Matrix4x4 _projectionMatrix;

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


        public override Matrix4x4 ProjectionMatrix
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

        public override Matrix4x4 ViewProjectionMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return ViewMatrix * ProjectionMatrix;
            }
        }

        public CameraPerspective(float fov = DefaultFov, float near = DefaultNear, float far = DefaultFar, float aspectRatio = 16/9f)
        {
            _fov = fov;
            _near = near;
            _far = far;
            _aspectRatio = aspectRatio;

            tranform = Transform3D.Default;

            _isProjectionMatrixDirty = true;
        }

        public void SetAspectRatio(float aspectRatio)
        {
            _aspectRatio = aspectRatio;
            _isProjectionMatrixDirty = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateProjectionMatrix()
        {
            _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(_fov, _aspectRatio, _near, _far);
        }
    }
}