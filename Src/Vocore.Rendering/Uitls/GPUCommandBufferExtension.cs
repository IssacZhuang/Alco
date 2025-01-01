using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public static class GPUCommandBufferExtension
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetFrameBuffer(this GPUCommandBuffer command, IRenderTarget renderTarget)
    {
        command.SetFrameBuffer(renderTarget.RenderTexture.FrameBuffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetFrameBuffer(this GPUCommandBuffer command, RenderTexture renderTexture)
    {
        command.SetFrameBuffer(renderTexture.FrameBuffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetGraphicsPipeline(this GPUCommandBuffer command, GraphicsPipelineContext pipelineInfo)
    {
        command.SetGraphicsPipeline(pipelineInfo.Pipeline!);
    }
}