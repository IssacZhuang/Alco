using System.Runtime.CompilerServices;
using Vocore.Graphics.WebGPU;
using WebGPU;

namespace Vocore.Graphics;

//on a reference to WGPUTextureView
//no control of the lifecycle of the wgpuTextureView
//only used by framebuffers to prevent new managed GPUTextureView object at render loop
internal sealed class WebGPUTextureViewWrapper : WebGPUTextureViewBase
{
    private WebGPUTextureBase _texture;
    private WGPUTextureView _view;

    public override WGPUTextureView Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _view;
    }

    public override GPUTexture Texture
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _texture;
    }

    public override TextureViewDimension Dimension
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => TextureViewDimension.Texture2D; // framebuffers are always 2D

    }

    public override string Name
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Texture.Name;
    }

    protected override GPUDevice Device { get; }


    public WebGPUTextureViewWrapper(WebGPUDevice device,WebGPUTextureBase texture, WGPUTextureView view)
    {
        Device = device;
        _texture = texture;
        _view = view;
    }

    public void UpdateTextureAndView(WebGPUTextureBase texture, WGPUTextureView view)
    {
        _texture = texture;
        _view = view;
    }

    protected override void Dispose(bool disposing)
    {

    }
}