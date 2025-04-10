using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.GUI;
using Alco.Rendering;

namespace Alco.Engine;

public class DebugGUIRenderer : BaseDebugGUIRenderer
{
    private readonly InputSystem _input;
    private readonly View _window;
    public DebugGUIRenderer(InputSystem input, View window, float width, float height, RenderingSystem renderingSystem, Shader shaderText, Shader shaderSprite, Shader shaderBlit) : base(width, height, renderingSystem, shaderText, shaderSprite, shaderBlit)
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