using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Vocore.AssetsLib
{
    public class AnimatedRenderQueue : InstancedRenderQueue
    {
        private StructuredBuffer<float> _frameBuffer;
        private readonly static int ShaderID_frame = Shader.PropertyToID("_Frame");

        public AnimatedRenderQueue(Mesh mesh, Material mat) : base(mesh, mat)
        {
            _frameBuffer = new StructuredBuffer<float>(InstancedRenderer.MAX_COUNT_IN_BATCH);
        }

        public void AddInstance(Vector3 position, Quaternion rotation, Vector3 scale, float frame)
        {
            _frameBuffer[_count] = frame;
            AddInstance(position, rotation, scale);
        }

        protected override void UpdateMaterialProperty(MaterialPropertyBlock propertyBlock)
        {
            propertyBlock.SetFloatArray(ShaderID_frame, _frameBuffer.Raw);
        }
    }
}
