using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Vocore
{
    public class InstanceBatchRenderFeature : ScriptableRendererFeature
    {
        public class InstanceBatchRenderPass : ScriptableRenderPass
        {
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                CommandBuffer buffer = TestBatchRenderer.instance?.CommandBuffer;
                if (buffer != null)
                {
                    context.ExecuteCommandBuffer(buffer);
                }
            }

            public override void FrameCleanup(CommandBuffer cmd)
            {
                base.FrameCleanup(cmd);
            }
        }

        private InstanceBatchRenderPass _renderPass;

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(_renderPass);
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            base.SetupRenderPasses(renderer, renderingData);
        }

        public override void Create()
        {
            _renderPass = new InstanceBatchRenderPass();
        }

    }
}