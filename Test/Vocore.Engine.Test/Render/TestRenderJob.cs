using System.Numerics;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.Engine.Test;

public class TestRenderJob
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
        public Vector4 Color;
        public Rect UvRect;
    }
    private class TestCommandBuffer : GPUCommandBuffer
    {
        private bool _hasBuffer = false;

        public override bool HasBuffer => _hasBuffer;

        protected override GPUDevice Device { get; }
        private int _drawCallCount = 0;

        public int DrawCallCount => _drawCallCount;

        public TestCommandBuffer(GPUDevice device, in CommandBufferDescriptor? descriptor) : base(descriptor)
        {
            Device = device;
        }

        protected override void BeginCore()
        {
            _hasBuffer = false;
            _drawCallCount = 0;
        }

        protected override void ClearColorCore(ColorFloat color, uint index)
        {

        }

        protected override void ClearDepthCore(float depth)
        {

        }

        protected override void ClearStencilCore(uint stencil)
        {

        }

        protected override void DispatchComputeCore(uint x, uint y, uint z)
        {

        }

        protected override void DispatchComputeIndirectCore(GPUBuffer indirectBuffer, uint offset)
        {

        }

        protected override void Dispose(bool disposing)
        {

        }

        protected override void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
        {
            _drawCallCount++;
        }

        protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
        {
            _drawCallCount++;
        }

        protected override void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset)
        {
            _drawCallCount++;
        }

        protected override void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset)
        {
            _drawCallCount++;
        }

        protected override void EndCore()
        {
            //simulate the most time-consuming operation
            _hasBuffer = true;
        }

        protected override unsafe void PushConstantsCore(ShaderStage stage, uint bufferOffset, byte* data, uint size)
        {

        }

        protected override void SetComputePipelineCore(GPUPipeline pipeline)
        {

        }

        protected override void SetComputeResourcesCore(uint slot, GPUResourceGroup resourceGroup)
        {

        }

        protected override void SetFrameBufferCore(GPUFrameBuffer frameBuffer)
        {

        }

        protected override void SetGraphicsPipelineCore(GPUPipeline pipeline)
        {

        }

        protected override void SetGraphicsResourcesCore(uint slot, GPUResourceGroup resourceGroup)
        {

        }

        protected override void SetIndexBufferCore(GPUBuffer buffer, IndexFormat format, ulong offset, ulong size)
        {

        }

        protected override void SetStencilReferenceCore(uint value)
        {

        }

        protected override void SetVertexBufferCore(uint slot, GPUBuffer buffer, ulong offset, ulong size)
        {

        }
    }

    [Test]
    public void TestRenderJobDrawMesh()
    {
        int drawCount = 1000;

        using GameEngine engine = new GameEngine(GameEngineSetting.CreateGPUWithoutWindow());
        RenderJobDrawMesh renderJob = new RenderJobDrawMesh(engine.MainFrameBuffer);

        RenderingSystem rendering = engine.Rendering;

        Shader shader = engine.BuiltInAssets.Shader_Sprite;
        Material material = rendering.CreateGraphicsMaterial(shader);
        Mesh mesh = rendering.MeshSprite;

        //TestCommandBuffer commandBuffer = new TestCommandBuffer(engine.GraphicsDevice, null);
        GPUCommandBuffer commandBuffer = engine.GraphicsDevice.CreateCommandBuffer(null);

        Constant constant = new Constant
        {
            Model = Transform2D.Identity.Matrix,
            Color = new Vector4(1, 1, 1, 1),
            UvRect = new Rect(0, 0, 1, 1)
        };

        for (int i = 0; i < drawCount; i++)
        {
            renderJob.SetMaterial(material);
            renderJob.SetMesh(mesh);
            renderJob.DrawWithConstant(constant);
        }

        commandBuffer.Begin();
        renderJob.Execute(commandBuffer);
        commandBuffer.End();
        //Assert.That(commandBuffer.DrawCallCount, Is.EqualTo(drawCount));


    }
}