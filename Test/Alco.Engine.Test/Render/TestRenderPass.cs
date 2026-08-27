using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine.Test;

public class TestAttachmentLayout
{
    [Test]
    public void TestAttachmentLayoutHash()
    {
        GameEngine engine = new GameEngine(TestEngineSettings.CreateNoGPUWithShaderCache());
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
        // The sprite module is generic (MainPS<let Repeated>): pin the default.

        GPUPipeline? pipeline1 = shader.GetGraphicsPipeline(pass1, false).Pipeline;
        GPUPipeline? pipeline2 = shader.GetGraphicsPipeline(pass2, false).Pipeline;
        GPUPipeline? pipeline3 = shader.GetGraphicsPipeline(pass3, false).Pipeline;
        GPUPipeline? pipeline4 = shader.GetGraphicsPipeline(pass4, false).Pipeline;

        // Every layout must yield a usable pipeline.
        Assert.That(pipeline1, Is.Not.Null);
        Assert.That(pipeline2, Is.Not.Null);
        Assert.That(pipeline3, Is.Not.Null);
        Assert.That(pipeline4, Is.Not.Null);

        Assert.IsTrue(pipeline1!.GetHashCode() == pipeline2!.GetHashCode());
        Assert.IsTrue(pipeline1 == pipeline2);

        Assert.IsFalse(pipeline1.GetHashCode() == pipeline3!.GetHashCode());
        Assert.IsFalse(pipeline1 == pipeline3);

        Assert.IsFalse(pipeline1.GetHashCode() == pipeline4!.GetHashCode());
        Assert.IsFalse(pipeline1 == pipeline4);
    }
}