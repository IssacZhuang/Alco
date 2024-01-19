namespace Vocore.Graphics.WebGPU;

internal abstract class WebGPUFrameBufferBase : GPUFrameBuffer
{
    public abstract IReadOnlyList<WebGPUTextureBase> WebGPUColorTextures { get; }
    public abstract WebGPUTextureBase? WebGPUDepthTexture { get; }
}