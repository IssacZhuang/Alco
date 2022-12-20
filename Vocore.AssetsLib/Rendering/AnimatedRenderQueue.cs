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
        private int _count;

        public AnimatedRenderQueue(Mesh mesh, Material mat) : base(mesh, mat)
        {
            _frameBuffer = new StructuredBuffer<float>(InstancedRenderer.MAX_COUNT_IN_BATCH);
        }

        public void PushToQueue()
        {

        }

        protected override void SetMaterialProperty(int start, int length, MaterialPropertyBlock propertyBlock)
        {
            
        }

        private void ResetBuffer()
        {

        }
    }
}
