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
        private readonly StructuredBuffer<float> _frameBuffer;
        private readonly StructuredBuffer<Vector4> _coloreBuffer;
        private readonly static int ShaderID_frame = Shader.PropertyToID("_Frame");
        private readonly static int ShaderID_color = Shader.PropertyToID("_Color");

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
            propertyBlock.SetFloatArray(ShaderID_frame, _frameBuffer.Raw);
            propertyBlock.SetVectorArray(ShaderID_color,_coloreBuffer.Raw);
        }
    }
}
