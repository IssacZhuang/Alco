using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Vocore.Graphics;
using Vocore.IO;
using Vocore.Rendering;

namespace Vocore.Engine.Test;

public class TestRender
{
    private class TestCommandBuffer : GPUCommandBuffer
    {
        private bool _hasBuffer = false;

        public override bool HasBuffer => _hasBuffer;

        protected override GPUDevice Device => throw new NotImplementedException();

        public TestCommandBuffer(in CommandBufferDescriptor? descriptor) : base(descriptor)
        {
        }

        protected override void BeginCore()
        {
            _hasBuffer = false;
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

        }

        protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
        {

        }

        protected override void DrawIndexedIndirectCore(GPUBuffer indirectBuffer, uint offset)
        {

        }

        protected override void DrawIndirectCore(GPUBuffer indirectBuffer, uint offset)
        {

        }

        protected override void EndCore()
        {
            //simulate the most time-consuming operation
            Thread.Sleep(6);
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
        public Vector4 Color;
        public Rect UvRect;
    }

    [Test]
    public void TestRenderThread()
    {

        GameEngine engine = new GameEngine(GameEngineSetting.CreateNoGPU());
        GPUDevice device = engine.GraphicsDevice;
        int commandCount = 128;
        int drawCallCount = 1000;

        GPUCommandBuffer[] commands = new GPUCommandBuffer[commandCount];

        for (int i = 0; i < commandCount; i++)
        {
            commands[i] = new TestCommandBuffer(null);
        }

        RenderingSystem rendering = engine.Rendering;
        RenderThread renderThread = new RenderThread(device, 16);//16 threads
        Transform3D transform = Transform3D.Identity;


        Shader shader = engine.BuiltInAssets.Shader_Sprite;
        GraphicsMaterial material = rendering.CreateGraphicsMaterial(shader);
        CameraDataPerspective cameraData = new CameraDataPerspective();
        material.SetValue("_camera", cameraData);
        material.SetTexture("_texture", rendering.TextureWhite);
        Mesh mesh = rendering.MeshSprite;

        Constant constant = new Constant
        {
            Model = transform.Matrix,
            Color = new Vector4(1, 1, 1, 1),
            UvRect = new Rect(0, 0, 1, 1)
        };

        Profiler profiler = new Profiler();

        profiler.Start("Render thread: ");
        for (int i = 0; i < commandCount; i++)
        {
            GPUCommandBuffer command = commands[i];
            command.Begin();
            command.SetVertexBuffer(0, mesh.VertexBuffer, 0, mesh.VertexBuffer.Size);
            command.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt16, 0, mesh.IndexBuffer.Size);
            command.SetGraphicsPipeline(material.GetPipeline(engine.MainFrameBuffer.RenderPass));
            material.PushResourceToCommandBuffer(command);

            for (int j = 0; j < drawCallCount; j++)
            {
                command.PushConstants(ShaderStage.Vertex, constant);
                command.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
            }

            //the GPUCommandBuffer.End() is most time-consuming operation
            //it will be called in the render thread
            renderThread.SubmitCommandBuffer(command);
        }

        renderThread.WaitForFinish();

        var result = profiler.End();

        TestContext.WriteLine(result);

        renderThread.Dispose();
        engine.Dispose();

    }
}