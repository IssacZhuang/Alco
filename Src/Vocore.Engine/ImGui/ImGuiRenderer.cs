using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.GUI;
using Vocore.Rendering;

namespace Vocore.Engine;

public class ImGuiRenderer : BaseImGuiRenderer
{
    private readonly InputSystem _input;
    public ImGuiRenderer(InputSystem input, float width, float height, RenderingSystem renderingSystem, Shader shaderText, Shader shaderSprite) : base(width, height, renderingSystem, shaderText, shaderSprite)
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