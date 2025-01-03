using NUnit.Framework;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.Engine.Test;

public class TestRenderPass
{
    [Test]
    public void TestRenderPassHash()
    {
        GameEngine engine = new GameEngine(GameEngineSetting.CreateNoGPU());
        RenderingSystem renderingSystem = engine.Rendering;
        GPUDevice device = renderingSystem.GraphicsDevice;
        GPURenderPass pass1 = device.CreateRenderPass(new RenderPassDescriptor(
            [new(PixelFormat.RGBA8Unorm)],
            new(PixelFormat.Depth24PlusStencil8),
            "test"
        ));
        
        GPURenderPass pass2 = device.CreateRenderPass(new RenderPassDescriptor(
            [new(PixelFormat.RGBA8Unorm)],
            new(PixelFormat.Depth24PlusStencil8),
            "test"
        ));

        GPURenderPass pass3 = device.CreateRenderPass(new RenderPassDescriptor(
            [new(PixelFormat.RGBA8Unorm)],
            null,
            "test"
        ));

        GPURenderPass pass4 = device.CreateRenderPass(new RenderPassDescriptor(
            [new(PixelFormat.RGBA8Unorm), new(PixelFormat.R8Unorm)],
            null,
            "test"
        ));


        Assert.IsTrue(pass1.GetHashCode() == pass2.GetHashCode());
        Assert.IsTrue(pass1.Equals(pass2));
        
        Assert.IsFalse(pass1.GetHashCode() == pass3.GetHashCode());
        Assert.IsFalse(pass1.Equals(pass3));

        Assert.IsFalse(pass1.GetHashCode() == pass4.GetHashCode());
        Assert.IsFalse(pass1.Equals(pass4));

        Shader shader = engine.BuiltInAssets.Shader_Sprite;

        GPUPipeline pipeline1 = shader.GetGraphicsPipeline(pass1).Pipeline;
        GPUPipeline pipeline2 = shader.GetGraphicsPipeline(pass2).Pipeline;
        GPUPipeline pipeline3 = shader.GetGraphicsPipeline(pass3).Pipeline;
        GPUPipeline pipeline4 = shader.GetGraphicsPipeline(pass4).Pipeline;

        Assert.IsTrue(pipeline1.GetHashCode() == pipeline2.GetHashCode());
        Assert.IsTrue(pipeline1 == pipeline2);

        Assert.IsFalse(pipeline1.GetHashCode() == pipeline3.GetHashCode());
        Assert.IsFalse(pipeline1 == pipeline3);

        Assert.IsFalse(pipeline1.GetHashCode() == pipeline4.GetHashCode());
        Assert.IsFalse(pipeline1 == pipeline4);
    }
}