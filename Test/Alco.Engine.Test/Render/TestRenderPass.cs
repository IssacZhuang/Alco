using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine.Test;

public class TestAttachmentLayout
{
    [Test]
    public void TestAttachmentLayoutHash()
    {
        GameEngine engine = new GameEngine(GameEngineSetting.CreateNoGPU());
        RenderingSystem renderingSystem = engine.RenderingSystem;
        GPUDevice device = renderingSystem.GraphicsDevice;
        GPUAttachmentLayout pass1 = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new(PixelFormat.RGBA8Unorm)],
            new(PixelFormat.Depth24PlusStencil8),
            "test"
        ));

        GPUAttachmentLayout pass2 = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new(PixelFormat.RGBA8Unorm)],
            new(PixelFormat.Depth24PlusStencil8),
            "test"
        ));

        GPUAttachmentLayout pass3 = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new(PixelFormat.RGBA8Unorm)],
            null,
            "test"
        ));

        GPUAttachmentLayout pass4 = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
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