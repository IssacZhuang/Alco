using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Vocore.Graphics;
using Vocore.IO;
using Vocore.Rendering;

namespace Vocore.Engine.Test;

public class TestRenderThread
{


    private class TestRenderJob : IRenderJob
    {
        public Material material;
        public Mesh mesh;
        public Constant constant;
        public RenderTexture renderTarget;
        public int drawCallCount;
        public void Execute(GPUCommandBuffer commandBuffer)
        {
            //the usage of renderjobcontext is the same as the command buffer but without the end() method
            ShaderPipelineInfo pipelineInfo = material.GetPipelineInfo(renderTarget.FrameBuffer.RenderPass);
            commandBuffer.SetFrameBuffer(renderTarget.FrameBuffer);
            commandBuffer.SetGraphicsPipeline(pipelineInfo.Pipeline);
            commandBuffer.SetVertexBuffer(0, mesh.VertexBuffer, 0, mesh.VertexBuffer.Size);
            commandBuffer.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt16, 0, mesh.IndexBuffer.Size);
            material.PushResourceToCommandBuffer(commandBuffer);

            for (int i = 0; i < drawCallCount; i++)
            {
                commandBuffer.PushConstants(pipelineInfo.PushConstantsStages, constant);
                commandBuffer.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
            }

        }
    }

    private class TestErrorRenderJob : IRenderJob
    {
        public void Execute(GPUCommandBuffer commandBuffer)
        {
            throw new Exception("Test error");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
        public Vector4 Color;
        public Rect UvRect;
    }

    // [Test]
    // public void TestRenderScheduler()
    // {

    //     if (OperatingSystem.IsLinux())
    //     {
    //         //the linux server of azure pipleine has no vulkan driver
    //         return;
    //     }

    //     GameEngine engine = new GameEngine(GameEngineSetting.CreateGPUWithoutWindow());

    //     GPUDevice device = engine.GraphicsDevice;
    //     int commandCount = 32;
    //     int drawCallCount = 4000;

        

    //     RenderingSystem rendering = engine.Rendering;
    //     ThreadedRenderScheduler renderThread = new ThreadedRenderScheduler(device, 8);//16 threads
    //     Transform3D transform = Transform3D.Identity;

    //     renderThread.OnException += (e) =>
    //     {
    //         Assert.Fail(e.Message);
    //     };


    //     Shader shader = engine.BuiltInAssets.Shader_Sprite;
    //     GraphicsMaterial material = rendering.CreateGraphicsMaterial(shader);
    //     CameraDataPerspective cameraData = new CameraDataPerspective();
    //     material.SetValue("_camera", cameraData);
    //     material.SetTexture("_texture", rendering.TextureWhite);
    //     Mesh mesh = rendering.MeshSprite;

    //     RenderTexture renderTarget = rendering.CreateRenderTexture(rendering.PrefferedHDRPass, 1024, 1024);

    //     Constant constant = new Constant
    //     {
    //         Model = transform.Matrix,
    //         Color = new Vector4(1, 1, 1, 1),
    //         UvRect = new Rect(0, 0, 1, 1)
    //     };

    //     GPUCommandBuffer[] commands = new GPUCommandBuffer[commandCount];
    //     TestRenderJob[] jobs = new TestRenderJob[commandCount];


    //     for (int i = 0; i < commandCount; i++)
    //     {
    //         jobs[i] = new TestRenderJob();
    //         jobs[i].material = material;
    //         jobs[i].mesh = mesh;
    //         jobs[i].constant = constant;
    //         jobs[i].renderTarget = renderTarget;
    //         jobs[i].drawCallCount = drawCallCount;

    //         commands[i] = device.CreateCommandBuffer();
    //     }


    //     Profiler profiler = new Profiler();

    //     //jit warm-up
    //     for (int i = 0; i < commandCount; i++)
    //     {
    //         renderThread.ScheduleRenderJob(jobs[i]);
    //     }

    //     renderThread.WaitForFinish();

    //     Assert.That(renderThread.IsFinished);
    //     renderThread.Reset();

    //     device.OnEndFrame();

    //     //benchmark
    //     profiler.Start("Render thread: ");

    //     profiler.Start("Build Command");
    //     for (int i = 0; i < commandCount; i++)
    //     {
    //         renderThread.ScheduleRenderJob(jobs[i]);
    //     }

    //     TestContext.WriteLine(profiler.End());

    //     renderThread.WaitForFinish();

    //     TestContext.WriteLine(profiler.End());

    //     renderThread.Dispose();
    //     engine.Dispose();

    // }

    // [Test]
    // public void TestRenderThreadError()
    // {

    //     if (OperatingSystem.IsLinux())
    //     {
    //         //the linux server of azure pipleine has no vulkan driver
    //         return;
    //     }

    //     GameEngine engine = new GameEngine(GameEngineSetting.CreateGPUWithoutWindow());
    //     GPUDevice device = engine.GraphicsDevice;
    //     ThreadedRenderScheduler renderThread = new ThreadedRenderScheduler(device, 8);
    //     renderThread.OnException += (e) =>
    //     {
    //         Assert.Pass();
    //     };

    //     Transform3D transform = Transform3D.Identity;

    //     RenderingSystem rendering = engine.Rendering;

    //     Shader shader = engine.BuiltInAssets.Shader_Sprite;
    //     GraphicsMaterial material = rendering.CreateGraphicsMaterial(shader);
    //     CameraDataPerspective cameraData = new CameraDataPerspective();
    //     material.SetValue("_camera", cameraData);
    //     material.SetTexture("_texture", rendering.TextureWhite);
    //     Mesh mesh = rendering.MeshSprite;

    //     RenderTexture renderTarget = rendering.CreateRenderTexture(rendering.PrefferedHDRPass, 1024, 1024);

    //     Constant constant = new Constant
    //     {
    //         Model = transform.Matrix,
    //         Color = new Vector4(1, 1, 1, 1),
    //         UvRect = new Rect(0, 0, 1, 1)
    //     };

    //     TestRenderJob commonJob = new TestRenderJob();
    //     commonJob.material = material;
    //     commonJob.mesh = mesh;
    //     commonJob.constant = constant;
    //     commonJob.renderTarget = renderTarget;
    //     commonJob.drawCallCount = 1000;

    //     TestErrorRenderJob errorJob = new TestErrorRenderJob();

    //     renderThread.ScheduleRenderJob(commonJob);
    //     renderThread.ScheduleRenderJob(errorJob);
    //     renderThread.WaitForFinish();

    //     Assert.That(renderThread.IsFinished);

    //     renderThread.Dispose();
    //     engine.Dispose();
    // }
}