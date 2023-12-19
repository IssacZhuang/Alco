using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore.Engine
{
    public class CameraOrthographic : BaseCamera
    {
        
        public const float DefaultWidth = 16f/9f;
        public const float DefaultHeight = 1f;
        private float _width;
        private float _height;
        
        private Matrix4x4 _projectionMatrix;
        
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

        public CameraOrthographic(float width = DefaultWidth, float height = DefaultHeight, float near = DefaultNear, float far = DefaultFar)
        {
            _width = width;
            _height = height;
            _near = near;
            _far = far;
            _isProjectionMatrixDirty = true;
            tranform = Transform3D.Default;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateProjectionMatrix()
        {
            _projectionMatrix = Matrix4x4.CreateOrthographic(_width, _height, _near, _far);
        }
    }
}

