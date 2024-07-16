using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public static class RendererExtension
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Begin(IRenderer renderer, IRenderTarget renderTarget)
    {
        renderer.Begin(renderTarget.RenderTexture.FrameBuffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Begin(IRenderer renderer, RenderTexture renderTexture)
    {
        renderer.Begin(renderTexture.FrameBuffer);
    }


    
}