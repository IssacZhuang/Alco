using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using Vocore;

namespace Vocore.AssetsLib
{
    public class AnimatedRenderQueue : InstancedRenderQueue
    {
        private readonly StructuredBuffer<float> _frameBuffer;
        private readonly StructuredBuffer<Vector4> _coloreBuffer;

        public AnimatedRenderQueue(Mesh mesh, Material mat) : base(mesh, mat)
        {
            _frameBuffer = new StructuredBuffer<float>(InstancedRenderer.MAX_COUNT_IN_BATCH);
            _coloreBuffer = new StructuredBuffer<Vector4>(InstancedRenderer.MAX_COUNT_IN_BATCH);
        }

        public void AddInstance(Vector3 position, Quaternion rotation, Vector3 scale,Color color, float frame)
        {
            _frameBuffer[_count] = frame;
            _coloreBuffer[_count] = color;
            AddInstance(position, rotation, scale);
        }

        protected override void UpdateMaterialProperty(MaterialPropertyBlock propertyBlock)
        {
            propertyBlock.SetFloatArray(ShaderPropertyID.frame, _frameBuffer.Raw);
            propertyBlock.SetVectorArray(ShaderPropertyID.color, _coloreBuffer.Raw);
        }
    }
}
