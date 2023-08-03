using System;
using UnityEngine;
using UnityEngine.Rendering;

using Unity.Jobs;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;

namespace Vocore
{
    public class InstanceBatcher : BaseInstanceBatcher
    {
        [BurstCompile]
        private unsafe struct JobCalcMatrices : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction]
            public TransformData* _transformData;
            [NativeDisableUnsafePtrRestriction]
            public Matrix4x4* _matrices;
            public void Execute(int index)
            {
                _matrices[index] = _transformData[index].Matrix;
            }
        }
        private NativeBuffer<TransformData> _transformBuffer;
        public InstanceBatcher(CommandBuffer renderTarget, Material material, Mesh mesh, int layer = 0) : base(renderTarget, material, mesh, layer)
        {
            _transformBuffer = new NativeBuffer<TransformData>(MaxCountPerBatch);
        }

        protected unsafe override void UpdateData(int count, Matrix4x4[] matrices, MaterialPropertyBlock propertyBlock)
        {
            JobCalcMatrices job = new JobCalcMatrices();
            job._transformData = (TransformData*)_transformBuffer.Ptr;
            fixed (Matrix4x4* ptr = matrices)
            {
                job._matrices = ptr;
            }

            job.Schedule(count, UtilsJob.GetOptimizedBatchCountByLength(count)).Complete();
        }

        public void AddInstance(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (Count >= MaxCountPerBatch)
            {
                PushToBuffer();
            }

            _transformBuffer[Count] = new TransformData
            {
                position = position,
                rotation = rotation,
                scale = scale
            };

            Count++;
        }


    }
}