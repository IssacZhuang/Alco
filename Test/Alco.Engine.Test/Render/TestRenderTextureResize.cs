#nullable enable

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
    private const string ResizeTestShader = """
        module rt_resize_shader;

        cbuffer _pass : register(b0, space0)
        {
            Texture2D _texture;
            SamplerState _textureSampler;
        };

        cbuffer _output : register(b0, space1)
        {
            [[vk::image_format("rgba16f")]] RWTexture2D<float4> _output;
        };

        [shader("compute")]
        [numthreads(1, 1, 1)]
        void MainCS(uint3 id : SV_DispatchThreadID)
        {
            _output[id.xy] = _texture.SampleLevel(_textureSampler, float2(0, 0), 0);
        }
        """;

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
        Assert.That(rt.FrameBuffer, Is.Not.SameAs(frameBefore));
        Assert.That(rt.ColorTextures[0], Is.Not.SameAs(colorBefore));
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
        Assert.That(rt.FrameBuffer, Is.SameAs(frameBefore));
    }

    [Test]
    public void TestResizeDisposedThrows()
    {
        GameEngine engine = new GameEngine(TestEngineSettings.CreateNoGPUWithShaderCache());
        RenderingSystem renderingSystem = engine.RenderingSystem;
        RenderTexture rt = renderingSystem.CreateRenderTexture(CreateColorLayout(renderingSystem.GraphicsDevice), 64, 32, "test_rt");

        rt.Dispose();

        Assert.That(() => rt.Resize(16, 16), Throws.TypeOf<System.ObjectDisposedException>());
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

        Assert.That(rt.HasDepth, Is.True);
        GPUResourceGroup? depthRead = rt.EntryDepthRead;
        Assert.That(depthRead, Is.Not.Null);

        rt.Resize(128, 128);

        // The cached depth sample group referenced the old depth view and must be
        // recreated lazily from the new frame buffer.
        Assert.That(rt.EntryDepthRead, Is.Not.SameAs(depthRead));
    }

    [Test]
    public void TestMaterialGroupRebuiltOnResize()
    {
        GameEngine engine = new GameEngine(TestEngineSettings.CreateNoGPUWithShaderCache());
        RenderingSystem renderingSystem = engine.RenderingSystem;
        Shader shader = renderingSystem.ShaderSystem.GetShaderFromModule(
            "rt_resize_shader", "rt_resize_shader.slang", ResizeTestShader);
        ComputeMaterial material = renderingSystem.CreateComputeMaterial(shader);
        RenderTexture rt = renderingSystem.CreateRenderTexture(CreateColorLayout(renderingSystem.GraphicsDevice), 64, 64, "test_rt");

        material.SetRenderTexture("_texture", rt);

        GPUResourceGroup? before = material[0];
        Assert.That(before, Is.Not.Null);

        // Steady state: repeated access is served from the content cache.
        Assert.That(material[0], Is.SameAs(before));

        // Re-setting the same reference is a no-op for the slot (identity check), so
        // the in-place resize is detected only through the version check.
        rt.Resize(128, 128);
        material.SetRenderTexture("_texture", rt);

        GPUResourceGroup? after = material[0];
        Assert.That(after, Is.Not.Null);
        Assert.That(after, Is.Not.SameAs(before));

        // Steady state again after the rebuild.
        Assert.That(material[0], Is.SameAs(after));
    }

    private static GPUAttachmentLayout CreateColorLayout(GPUDevice device)
    {
        return device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)],
            null,
            "test_color_layout"));
    }
}
