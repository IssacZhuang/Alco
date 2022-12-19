using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;
using Unity.Jobs;

namespace Vocore.AssetsLib
{
    public class InstancedRenderQueue: IDisposable
    {
        private readonly InstancedRenderer _renderer;

        private NativeBuffer<Vector3> _positionBuffer;
        private NativeBuffer<Quaternion> _rotationBuffer;
        private NativeBuffer<Vector3> _scaleBuffer;

        private JobCalcMatricesUnsafe _jobMatrices;

        private int _count;

        public unsafe InstancedRenderQueue(Mesh mesh, Material mat)
        {
            if (mesh == null) throw ExceptionRendering.MeshIsMissing;
            if (mat == null) throw ExceptionRendering.MaterialIsMissing;
            if (!mat.enableInstancing) throw ExceptionRendering.MaterialNotInstanced;

            _renderer = new InstancedRenderer
            {
                Mesh = mesh,
                Material = mat,
                onUpdateMatriceValues = SetMatriceValue
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
            _renderer.DrawWithProperty(_count);
            ResetBuffer();
        }

        public void PushToQueue(Vector3 position, Quaternion rotation, Vector3 scale)
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

        public void ResetBuffer()
        {
            _count = 0;
        }

        public void Dispose()
        {
            ClearBuffer();
            GC.SuppressFinalize(this);
        }

        protected void SetMatriceValue(int start, int length, StructuredBuffer<Matrix4x4> matrices)
        {
            if (start > 0) return; // TODO: warning
            _jobMatrices.Schedule(length, 1).Complete();
        }

        private unsafe void InitializeJob()
        {
            _jobMatrices = new JobCalcMatricesUnsafe
            {
                positions = _positionBuffer.Raw,
                rotations = _rotationBuffer.Raw,
                scales = _scaleBuffer.Raw,
                matrices = _renderer.MatrixBuffer.PtrHead
            };
        }

        private void ClearBuffer()
        {
            _positionBuffer.Dispose();
            _rotationBuffer.Dispose();
            _scaleBuffer.Dispose();
        }
    }
}
