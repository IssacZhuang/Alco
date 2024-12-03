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

        GameEngine engine = new GameEngine(GameEngineSetting.CreateGPUWithoutWindow());
        GPUDevice device = engine.GraphicsDevice;
        int commandCount = 128;
        int drawCallCount = 1000;

        GPUCommandBuffer[] commands = new GPUCommandBuffer[commandCount];

        for (int i = 0; i < commandCount; i++)
        {
            commands[i] = device.CreateCommandBuffer();
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

        RenderTexture renderTarget = rendering.CreateRenderTexture(rendering.PrefferedHDRPass, 1024, 1024);

        Constant constant = new Constant
        {
            Model = transform.Matrix,
            Color = new Vector4(1, 1, 1, 1),
            UvRect = new Rect(0, 0, 1, 1)
        };

        Profiler profiler = new Profiler();

        //jit warm-up
        for (int i = 0; i < commandCount; i++)
        {
            GPUCommandBuffer command = commands[i];
            command.Begin();
            command.SetFrameBuffer(renderTarget.FrameBuffer);
            command.SetGraphicsPipeline(material.GetPipeline(renderTarget.FrameBuffer.RenderPass));
            command.SetVertexBuffer(0, mesh.VertexBuffer, 0, mesh.VertexBuffer.Size);
            command.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt16, 0, mesh.IndexBuffer.Size);
            material.PushResourceToCommandBuffer(command);

            for (int j = 0; j < drawCallCount; j++)
            {
                command.PushConstants(ShaderStage.Vertex | ShaderStage.Fragment, constant);
                command.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
            }

            //the GPUCommandBuffer.End() is most time-consuming operation
            //it will be called in the render thread
            renderThread.SubmitCommandBuffer(command);
        }

        renderThread.WaitForFinish();

        //benchmark
        profiler.Start("Render thread: ");

        profiler.Start("Build Command");
        for (int i = 0; i < commandCount; i++)
        {
            GPUCommandBuffer command = commands[i];
            command.Begin();
            command.SetFrameBuffer(renderTarget.FrameBuffer);
            command.SetGraphicsPipeline(material.GetPipeline(renderTarget.FrameBuffer.RenderPass));
            command.SetVertexBuffer(0, mesh.VertexBuffer, 0, mesh.VertexBuffer.Size);
            command.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt16, 0, mesh.IndexBuffer.Size);
            material.PushResourceToCommandBuffer(command);

            for (int j = 0; j < drawCallCount; j++)
            {
                command.PushConstants(ShaderStage.Vertex | ShaderStage.Fragment, constant);
                command.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
            }

            //the GPUCommandBuffer.End() is most time-consuming operation
            //it will be called in the render thread
            renderThread.SubmitCommandBuffer(command);
        }

        TestContext.WriteLine(profiler.End());

        renderThread.WaitForFinish();

        TestContext.WriteLine(profiler.End());

        renderThread.Dispose();
        engine.Dispose();

    }
}