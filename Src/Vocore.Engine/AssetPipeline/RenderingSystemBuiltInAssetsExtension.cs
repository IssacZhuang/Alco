using System.Runtime.CompilerServices;
using Vocore.Rendering;

namespace Vocore.Engine;

public static class RenderingSystemBuiltInAssetsExtension
{
    private static BuiltInAssets BuiltInAssets
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => GameEngine.Instance.BuiltInAssets;
    }

    public static TextRenderer CreateTextRenderer(this RenderingSystem renderingSystem, GraphicsBuffer camera)
    {
        return renderingSystem.CreateTextRenderer(camera, BuiltInAssets.Shader_Text);
    }

    public static SpriteRenderer CreateSpriteRenderer(this RenderingSystem renderingSystem, GraphicsBuffer camera)
    {
        return renderingSystem.CreateSpriteRenderer(camera, BuiltInAssets.Shader_Sprite);
    }

    public static CanvasRenderer CreateCanvasRenderer(this RenderingSystem renderingSystem, GraphicsBuffer camera)
    {
        return renderingSystem.CreateCanvasRenderer(camera, BuiltInAssets.Shader_SpriteMasked, BuiltInAssets.Shader_TextMasked);
    }

    public static BlitRenderer CreateBlitRenderer(this RenderingSystem renderingSystem)
    {
        return renderingSystem.CreateBlitRenderer(BuiltInAssets.Shader_Blit);
    }

    public static WireframeRenderer CreateWireframeRenderer(this RenderingSystem renderingSystem, GraphicsBuffer camera)
    {
        return renderingSystem.CreateWireframeRenderer(camera, BuiltInAssets.Shader_Wireframe);
    }
}