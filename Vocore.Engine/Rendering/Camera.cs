using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Veldrid;
using Vocore.Unsafe;

namespace Vocore.Engine
{
    public class Camera
    {
        public const float DefaultFov = 1.0472f;
        public const float DefaultNear = 0.1f;
        public const float DefaultFar = 1000f;

        private float _fov;
        private float _near;
        private float _far;

        private Matrix4x4 _viewMatrix;
        private Matrix4x4 _projectionMatrix;

        private Vector3 _position;
        private Quaternion _rotation;

        private bool _isViewMatrixDirty;
        private bool _isProjectionMatrixDirty;

        

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

        public Vector3 Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _position;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                _position = value;
                _isViewMatrixDirty = true;
            }
        }

        public Quaternion Rotation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _rotation;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                _rotation = value;
                _isViewMatrixDirty = true;
            }
        }

        public Matrix4x4 ViewMatrix
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (_isViewMatrixDirty)
                {
                    UpdateViewMatrix();
                    _isViewMatrixDirty = false;
                }
                return _viewMatrix;
            }
            set
            {
                _viewMatrix = value;
                _isViewMatrixDirty = false;
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

        public Camera(float fov = DefaultFov, float near = DefaultNear, float far = DefaultFar) :
            this(Vector3.Zero, fov, near, far)
        {

        }

        public Camera(Vector3 position, float fov = DefaultFov, float near = DefaultNear, float far = DefaultFar) :
            this(position, Quaternion.Identity, fov, near, far)
        {

        }

        public Camera(Vector3 position, Quaternion rotation, float fov = DefaultFov, float near = DefaultNear, float far = DefaultFar)
        {
            _position = position;
            _rotation = rotation;
            _fov = fov;
            _near = near;
            _far = far;

            _isViewMatrixDirty = true;
            _isProjectionMatrixDirty = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateViewMatrix()
        {
            _viewMatrix = Matrix4x4.CreateFromQuaternion(_rotation) * Matrix4x4.CreateTranslation(_position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateProjectionMatrix()
        {
            _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(_fov, Window.AspectRatio, _near, _far);
        }

        public void UpdateBuffer(GraphicsDevice device, DeviceBuffer buffer)
        {
            device.UpdateBuffer(buffer, 0, ViewProjectionMatrix);
        }
    }
}