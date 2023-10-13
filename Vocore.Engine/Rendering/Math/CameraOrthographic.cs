using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore.Engine
{
    public class CameraOrthographic : ICamera
    {
        public const float DefaultNear = 0.1f;
        public const float DefaultFar = 1000f;
        public const float DefaultWidth = 16f/9f;
        public const float DefaultHeight = 1f;
        private float _width;
        private float _height;
        private float _near;
        private float _far;
        private Matrix4x4 _projectionMatrix;
        private bool _isProjectionMatrixDirty;
        public Transform tranform;


        public float Width
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _width;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                _width = value;
                _isProjectionMatrixDirty = true;
            }
        }

        public float Height
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _height;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                _height = value;
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
        public Matrix4x4 ViewMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Matrix4x4.CreateLookAt(tranform.position, tranform.position + Vector3.Transform(Vector3.UnitZ, tranform.rotation), Vector3.UnitY);
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



        public CameraOrthographic(float width = DefaultWidth, float height = DefaultHeight, float near = DefaultNear, float far = DefaultFar)
        {
            _width = width;
            _height = height;
            _near = near;
            _far = far;
            _isProjectionMatrixDirty = true;
            tranform = Transform.Default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetWindowAspectRatio()
        {
            if (Current.Window == null)
            {
                return 16f/9f;
            }

            return (float)Current.Window.Width / Current.Window.Height;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateProjectionMatrix()
        {
            _projectionMatrix = Matrix4x4.CreateOrthographic(_width, _height, _near, _far);
        }
    }
}

