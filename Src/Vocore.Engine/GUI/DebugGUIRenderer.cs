using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.GUI;
using Vocore.Rendering;

namespace Vocore.Engine;

public class DebugGUIRenderer : BaseDebugGUIRenderer
{
    private readonly InputSystem _input;
    public DebugGUIRenderer(InputSystem input, float width, float height, RenderingSystem renderingSystem, Shader shaderText, Shader shaderSprite, Shader shaderBlit) : base(width, height, renderingSystem, shaderText, shaderSprite, shaderBlit)
    {
        _input = input;
    }

    public override Vector2 MousePosition
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.MousePosition;
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