using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

namespace Vocore.AssetsLib
{
    public class InstancedRenderQueue
    {
        private readonly InstancedRenderer _renderer;

        private readonly StructuredBuffer<Vector3> _positionBuffer;
        private readonly StructuredBuffer<Quaternion> _rotationBuffer;
        private readonly StructuredBuffer<Vector3> _scaleBuffer;

        private int _count;

        public InstancedRenderQueue(Mesh mesh, Material mat)
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


            _positionBuffer = new StructuredBuffer<Vector3>(InstancedRenderer.MAX_COUNT_IN_BATCH);
            _rotationBuffer = new StructuredBuffer<Quaternion>(InstancedRenderer.MAX_COUNT_IN_BATCH);
            _scaleBuffer = new StructuredBuffer<Vector3>(InstancedRenderer.MAX_COUNT_IN_BATCH);
            _count = 0;
        }

        public void Draw()
        {
            _renderer.DrawWithProperty(_count);
            ResetBuffer();
        }

        public void Push(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if(_count>= InstancedRenderer.MAX_COUNT_IN_BATCH - 1)
            {
                Draw();
                return;
            }

            _positionBuffer[_count] = position;
            _rotationBuffer[_count] = rotation;
            _scaleBuffer[_count] = scale;
        }

        public void ResetBuffer()
        {
            _count = 0;
        }

        private void SetMatriceValue(int start, int length, StructuredBuffer<Matrix4x4> matrices)
        {
            if (start > 0) return; // TODO: warning

        }
    }
}
