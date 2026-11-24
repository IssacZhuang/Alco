using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>Renders the debug stats overlay with batched immediate-mode draw calls.</summary>
public sealed class DebugStatsRenderer : IDisposable
{
    private readonly Input _input;
    private readonly View _window;
    private readonly RenderContext _renderContext;
    private readonly List<DrawCommand> _commands = new(64);
    private uint2 _resolution;

    /// <summary>Gets the cursor position in window coordinates.</summary>
    public Vector2 MousePosition
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _window.MousePosition;
    }

    /// <summary>Gets whether the left mouse button was pressed this frame.</summary>
    public bool IsMouseClicked
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsMouseDown(Mouse.Left);
    }

    /// <summary>Gets whether the left mouse button is currently pressed.</summary>
    public bool IsMousePressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsMousePressing(Mouse.Left);
    }

    /// <summary>Creates a debug stats renderer and its overlay context.</summary>
    /// <param name="input">The input system used for mouse state.</param>
    /// <param name="window">The window used for cursor coordinates.</param>
    /// <param name="width">The initial render width in pixels.</param>
    /// <param name="height">The initial render height in pixels.</param>
    /// <param name="renderingSystem">The rendering system that owns GPU resources.</param>
    public DebugStatsRenderer(Input input, View window, uint width, uint height, RenderingSystem renderingSystem)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(renderingSystem);

        _input = input;
        _window = window;
        _resolution = new uint2(width, height);
        _renderContext = new RenderContext(renderingSystem);
    }

    /// <summary>Sets the render resolution in pixels.</summary>
    /// <param name="width">The render width in pixels.</param>
    /// <param name="height">The render height in pixels.</param>
    public void SetResolution(uint width, uint height)
    {
        _resolution = new uint2(width, height);
    }

    /// <summary>Begins collecting debug overlay draw commands for the current frame.</summary>
    public void Begin()
    {
        _commands.Clear();
    }

    /// <summary>Queues a solid quad draw command.</summary>
    /// <param name="position">The quad position.</param>
    /// <param name="size">The quad size.</param>
    /// <param name="color">The quad color.</param>
    public void DrawQuad(Vector2 position, Vector2 size, ColorFloat color)
    {
        _commands.Add(new DrawCommand
        {
            Position = position,
            Size = size,
            Color = color,
        });
    }

    /// <summary>Queues a text draw command and returns its estimated advance width.</summary>
    /// <param name="str">The text to draw.</param>
    /// <param name="position">The text position.</param>
    /// <param name="font">The font to use.</param>
    /// <param name="fontSize">The font size in pixels.</param>
    /// <param name="color">The text color.</param>
    /// <param name="pivot">The text pivot.</param>
    /// <returns>The advance width in normalized font-size units.</returns>
    public float DrawText(ReadOnlySpan<char> str, Vector2 position, Font font, float fontSize, ColorFloat color, Pivot pivot)
    {
        _commands.Add(new DrawCommand
        {
            Position = position,
            Size = new Vector2(fontSize, fontSize),
            Color = color,
        });

        return str.Length * 0.5f;
    }

    /// <summary>Queues a textured quad draw command.</summary>
    /// <param name="position">The texture position.</param>
    /// <param name="size">The texture size.</param>
    /// <param name="texture">The texture to draw.</param>
    /// <param name="color">The texture tint.</param>
    public void DrawTexture(Vector2 position, Vector2 size, Texture texture, ColorFloat color)
    {
        _commands.Add(new DrawCommand
        {
            Position = position,
            Size = size,
            Color = color,
        });
    }

    /// <summary>Flushes queued overlay commands into <paramref name="presenter"/> before present.</summary>
    /// <param name="presenter">The presenter holding the target swapchain frame.</param>
    public void End(ViewPresenter presenter)
    {
        ArgumentNullException.ThrowIfNull(presenter);

        if (_commands.Count == 0 || !presenter.HasValidFrame)
        {
            _commands.Clear();
            return;
        }

        // Phase 2 will record bindless Sprite/Text overlay draws here on top of CurrentFrame.RenderView.
        _ = _renderContext;
        _ = _resolution;
        _commands.Clear();
    }

    /// <summary>Releases the overlay rendering context.</summary>
    public void Dispose()
    {
        _commands.Clear();
        _renderContext.Dispose();
    }

    private readonly struct DrawCommand
    {
        public Vector2 Position { get; init; }
        public Vector2 Size { get; init; }
        public ColorFloat Color { get; init; }
    }
}
