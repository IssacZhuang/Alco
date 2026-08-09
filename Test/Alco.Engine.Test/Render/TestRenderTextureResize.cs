using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine.Test;

/// <summary>
/// Tests for <see cref="RenderTexture.Resize"/>: the wrapper keeps its object identity
/// while the internal GPU resources are recreated in place, and the material system
/// rebuilds the affected bind groups automatically through the version check.
/// </summary>
public class TestRenderTextureResize
{
    // Group 0: sampled texture + sampler companion; group 1: a storage output so the
    // sampled texture survives compilation.
    private const string ResizeTestShader = @"
[[vk::binding(0, 0)]] Texture2D _texture;
[[vk::binding(1, 0)]] SamplerState _textureSampler;
[[vk::binding(0, 1)]] RWTexture2D<float4> _output;

[shader(""compute"")]
[numthreads(1, 1, 1)]
void MainCS(uint3 id : SV_DispatchThreadID)
{
    _output[id.xy] = _texture.SampleLevel(_textureSampler, float2(0, 0), 0);
}
";

    [Test]
    public void TestResizeKeepsIdentityAndBumpsVersion()
    {
        GameEngine engine = new GameEngine(TestEngineSettings.CreateNoGPUWithShaderCache());
        RenderingSystem renderingSystem = engine.RenderingSystem;
        RenderTexture rt = renderingSystem.CreateRenderTexture(CreateColorLayout(renderingSystem.GraphicsDevice), 64, 32, "test_rt");

        GPUFrameBuffer frameBefore = rt.FrameBuffer;
        Texture2D colorBefore = rt.ColorTextures[0];
        Assert.That(rt.Version, Is.EqualTo(0));

        rt.Resize(128, 64);

        Assert.That(rt.Width, Is.EqualTo(128));
        Assert.That(rt.Height, Is.EqualTo(64));
        Assert.That(rt.Version, Is.EqualTo(1));
        Assert.AreNotSame(frameBefore, rt.FrameBuffer);
        Assert.AreNotSame(colorBefore, rt.ColorTextures[0]);
    }

    [Test]
    public void TestResizeSameSizeIsNoOp()
    {
        GameEngine engine = new GameEngine(TestEngineSettings.CreateNoGPUWithShaderCache());
        RenderingSystem renderingSystem = engine.RenderingSystem;
        RenderTexture rt = renderingSystem.CreateRenderTexture(CreateColorLayout(renderingSystem.GraphicsDevice), 64, 32, "test_rt");

        GPUFrameBuffer frameBefore = rt.FrameBuffer;
        rt.Resize(64, 32);

        Assert.That(rt.Version, Is.EqualTo(0));
        Assert.AreSame(frameBefore, rt.FrameBuffer);
    }

    [Test]
    public void TestResizeDisposedThrows()
    {
        GameEngine engine = new GameEngine(TestEngineSettings.CreateNoGPUWithShaderCache());
        RenderingSystem renderingSystem = engine.RenderingSystem;
        RenderTexture rt = renderingSystem.CreateRenderTexture(CreateColorLayout(renderingSystem.GraphicsDevice), 64, 32, "test_rt");

        rt.Dispose();

        Assert.Throws<System.ObjectDisposedException>(() => rt.Resize(16, 16));
    }

    [Test]
    public void TestDepthEntriesRecreatedOnResize()
    {
        GameEngine engine = new GameEngine(TestEngineSettings.CreateNoGPUWithShaderCache());
        RenderingSystem renderingSystem = engine.RenderingSystem;
        GPUAttachmentLayout layout = renderingSystem.GraphicsDevice.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)],
            new DepthAttachment(PixelFormat.Depth32Float),
            "test_depth_layout"));
        RenderTexture rt = renderingSystem.CreateRenderTexture(layout, 64, 64, "test_rt_depth");

        Assert.IsTrue(rt.HasDepth);
        GPUResourceGroup? depthRead = rt.EntryDepthRead;
        Assert.NotNull(depthRead);

        rt.Resize(128, 128);

        // The cached depth sample group referenced the old depth view and must be
        // recreated lazily from the new frame buffer.
        Assert.AreNotSame(depthRead, rt.EntryDepthRead);
    }

    [Test]
    public void TestMaterialGroupRebuiltOnResize()
    {
        GameEngine engine = new GameEngine(TestEngineSettings.CreateNoGPUWithShaderCache());
        RenderingSystem renderingSystem = engine.RenderingSystem;
        Shader shader = renderingSystem.CreateShader(ResizeTestShader, "rt_resize_shader");
        ComputeMaterial material = renderingSystem.CreateComputeMaterial(shader);
        RenderTexture rt = renderingSystem.CreateRenderTexture(CreateColorLayout(renderingSystem.GraphicsDevice), 64, 64, "test_rt");

        material.SetRenderTexture("_texture", rt);

        GPUResourceGroup? before = material[0];
        Assert.NotNull(before);

        // Steady state: repeated access is served from the content cache.
        Assert.AreSame(before, material[0]);

        // Re-setting the same reference is a no-op for the slot (identity check), so
        // the in-place resize is detected only through the version check.
        rt.Resize(128, 128);
        material.SetRenderTexture("_texture", rt);

        GPUResourceGroup? after = material[0];
        Assert.NotNull(after);
        Assert.AreNotSame(before, after);

        // Steady state again after the rebuild.
        Assert.AreSame(after, material[0]);
    }

    private static GPUAttachmentLayout CreateColorLayout(GPUDevice device)
    {
        return device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)],
            null,
            "test_color_layout"));
    }
}
