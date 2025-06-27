using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.GUI;
using Alco.Rendering;

namespace Alco.Engine;

public class DebugStatsRenderer : BaseDebugStatsRenderer
{
    private readonly Input _input;
    private readonly View _window;
    public DebugStatsRenderer(Input input, View window, float width, float height, IRenderTarget renderTarget,RenderingSystem renderingSystem, Shader shaderText, Shader shaderSprite) : base(width, height, renderTarget, renderingSystem, shaderText, shaderSprite)
    {
        _input = input;
        _window = window;
    }

    public override Vector2 MousePosition
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _window.MousePosition;
    }

    public override bool IsMouseClicked
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsMouseDown(Mouse.Left);
    }

    public override bool IsMousePressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsMousePressing(Mouse.Left);
    }
}