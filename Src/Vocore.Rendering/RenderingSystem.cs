namespace Vocore.Rendering;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Vocore.Graphics;

/// <summary>
/// The facility to manage rendering resource and perform rendering.
/// <br/>It is a high-level encapsulation of <see cref="GPUDevice"/>.
/// </summary>
public partial class RenderingSystem
{
    private readonly GPUDevice _device;
    private readonly Dictionary<string, GPURenderPass> _renderPasses;


    public GPUDevice GraphicsDevice
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _device;
    }

    public GPURenderPass DefaultRenderPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return _device.SwapChainRenderPass;
        }
    }

    public GPUFrameBuffer DefaultFrameBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return _device.SwapChainFrameBuffer;
        }
    }

    public RenderingSystem(GPUDevice device)
    {
        _device = device;
        _renderPasses = new Dictionary<string, GPURenderPass>();
    }

    public void RegisterRenderPass(string name, GPURenderPass renderPass)
    {
        if (_renderPasses.ContainsKey(name))
        {
            throw new ArgumentException($"The render pass with name '{name}' has already been registered.");
        }

        _renderPasses.Add(name, renderPass);
    }

    public GPURenderPass RegisterRenderPass(string name, PixelFormat[] colors, PixelFormat? depthStencil)
    {
        if (_renderPasses.ContainsKey(name))
        {
            throw new ArgumentException($"The render pass with name '{name}' has already been registered.");
        }

        if (colors.Length == 0)
        {
            throw new ArgumentException("The color attachment count must be greater than 0.");
        }

        ColorAttachment[] colorAttachments = new ColorAttachment[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            colorAttachments[i] = new ColorAttachment(colors[i]);
        }

        DepthAttachment? depthAttachment = depthStencil.HasValue ? new DepthAttachment(depthStencil.Value) : null;
        RenderPassDescriptor descriptor = new RenderPassDescriptor(colorAttachments, depthAttachment, name);
        GPURenderPass renderPass = _device.CreateRenderPass(descriptor);
        RegisterRenderPass(name, renderPass);
        return renderPass;
    }

    public bool HasRenderPass(string name)
    {
        return _renderPasses.ContainsKey(name);
    }

    public GPURenderPass GetRenderPass(string name)
    {
        if (!_renderPasses.TryGetValue(name, out GPURenderPass? renderPass))
        {
            throw new ArgumentException($"The render pass with name '{name}' has not been registered.");
        }

        return renderPass;
    }

    public bool TryGetRenderPass(string name, [NotNullWhen(true)] out GPURenderPass? renderPass)
    {
        return _renderPasses.TryGetValue(name, out renderPass);
    }

    public void UnregisterRenderPass(string name)
    {
        if (!_renderPasses.ContainsKey(name))
        {
            throw new ArgumentException($"The render pass with name '{name}' has not been registered.");
        }

        _renderPasses.Remove(name);
    }
}