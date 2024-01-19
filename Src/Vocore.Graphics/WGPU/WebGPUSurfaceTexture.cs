using System.Runtime.CompilerServices;
using WebGPU;

using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal class WebGPUSurfaceTexture : WebGPUTextureBase
{
    #region Properties
    private const string NAME = "WebGPU Surface Texture";
    private readonly WGPUSurface _surface;
    // Update every frame
    private WGPUTexture _texture;
    //Changed when the surface is resized
    private uint _width;
    private uint _height;

    #endregion

    #region Abstract Implementation

    public override uint Width
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _width;
    }

    public override uint Height
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _height;
    }

    public override uint Depth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => 1;
    }

    public override string Name
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => NAME;
    }

    protected override void Dispose(bool disposing)
    {
        //do nothing
    }

    #endregion

    #region WebGPU Implementation

    public override WGPUTexture Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _texture;
    }

    public unsafe WebGPUSurfaceTexture(WGPUSurface surface)
    {
        _surface = surface;

        WGPUSurfaceTexture surfaceTexture = default;
        wgpuSurfaceGetCurrentTexture(_surface, &surfaceTexture);
        _texture = surfaceTexture.texture;
        _width = wgpuTextureGetHeight(_texture);
        _height = wgpuTextureGetWidth(_texture);
    }

    public unsafe void SwapBuffer()
    {
        wgpuSurfacePresent(_surface);
        //release the texture
        wgpuTextureRelease(_texture);
        //get the new texture
        WGPUSurfaceTexture surfaceTexture = default;
        wgpuSurfaceGetCurrentTexture(_surface, &surfaceTexture);
        _texture = surfaceTexture.texture;
        _width = wgpuTextureGetHeight(_texture);
        _height = wgpuTextureGetWidth(_texture);
    }

    //can only be called from the device
    internal void InternalDispose()
    {
        wgpuTextureRelease(_texture);
        wgpuTextureDestroy(_texture);
    }

    #endregion
}