using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore.Engine
{
    public abstract class BaseCamera3D : ICamera
    {
        public const float DefaultNear = 0.1f;
        public const float DefaultFar = 1000f;
        protected float _near;
        protected float _far;
        protected bool _isProjectionMatrixDirty;
        public Transform3D tranform;

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
            }
        }

        public virtual Matrix4x4 ProjectionMatrix => throw new NotImplementedException();

        public virtual Matrix4x4 ViewProjectionMatrix => throw new NotImplementedException();

        public BaseCamera3D(float near = DefaultNear, float far = DefaultFar)
        {
            _near = near;
            _far = far;
            _isProjectionMatrixDirty = true;
            tranform = Transform3D.Default;
        }
    }
}

