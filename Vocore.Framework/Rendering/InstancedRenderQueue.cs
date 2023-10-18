using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;
using Unity.Jobs;

namespace Vocore
{
    public class InstancedRenderQueue: IDisposable
    {
        private readonly InstancedRenderer _renderer;

        private NativeBuffer<Vector3> _positionBuffer;
        private NativeBuffer<Quaternion> _rotationBuffer;
        private NativeBuffer<Vector3> _scaleBuffer;

        private JobCalcMatricesUnsafe _jobMatrices;

        protected int _count;

        public Mesh Mesh => _renderer.Mesh;

        public Material Material => _renderer.Material;

        public unsafe InstancedRenderQueue(Mesh mesh, Material mat)
        {
            if (mesh == null) throw ExceptionRendering.MeshIsMissing;
            if (mat == null) throw ExceptionRendering.MaterialIsMissing;
            if (!mat.enableInstancing) throw ExceptionRendering.MaterialNotInstanced;

            _renderer = new InstancedRenderer
            {
                Mesh = mesh,
                Material = mat,
                onUpdateData = UpdateData
            };

            _positionBuffer = new NativeBuffer<Vector3>(InstancedRenderer.MAX_COUNT_IN_BATCH);
            _rotationBuffer = new NativeBuffer<Quaternion>(InstancedRenderer.MAX_COUNT_IN_BATCH);
            _scaleBuffer = new NativeBuffer<Vector3>(InstancedRenderer.MAX_COUNT_IN_BATCH);

            InitializeJob();

            _count = 0;
        }

        ~InstancedRenderQueue()
        {
            ClearBuffer();
        }

        public void Draw()
        {
            _renderer.Draw(_count);
        }

        public void AddInstance(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if(_count>= InstancedRenderer.MAX_COUNT_IN_BATCH - 1)
            {
                Draw();
                return;
            }

            _positionBuffer[_count] = position;
            _rotationBuffer[_count] = rotation;
            _scaleBuffer[_count] = scale;
            _count++;
        }

        protected void UpdateData(int start, int length, StructuredBuffer<Matrix4x4> matrices, MaterialPropertyBlock propertyBlock)
        {
            if (start > 0) return; // TODO: warning
            JobHandle handle = _jobMatrices.Schedule(length, 1);
            UpdateMaterialProperty(propertyBlock);
            handle.Complete();
            ResetBuffer();
        }

        protected virtual void UpdateMaterialProperty(MaterialPropertyBlock propertyBlock)
        {

        }

        private unsafe void InitializeJob()
        {
            _jobMatrices = new JobCalcMatricesUnsafe
            {
                positions = _positionBuffer.DataPtr,
                rotations = _rotationBuffer.DataPtr,
                scales = _scaleBuffer.DataPtr,
                matrices = _renderer.MatrixBuffer.UnsafePtr
            };
        }

        public void Dispose()
        {
            ClearBuffer();
            GC.SuppressFinalize(this);
        }

        private void ResetBuffer()
        {
            _count = 0;
        }


        private void ClearBuffer()
        {
            _positionBuffer.Dispose();
            _rotationBuffer.Dispose();
            _scaleBuffer.Dispose();
        }

    }
}
