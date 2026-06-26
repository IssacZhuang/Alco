using System.Numerics;
using Alco.GUI;

namespace Alco.GUI.Test;

/// <summary>
/// A fake <see cref="INavigationContext"/> for unit testing navigation logic
/// without a graphics-backed canvas. Exposes settable hover, focus owner, and
/// input state, and records hover changes.
/// </summary>
public sealed class FakeNavigationContext : INavigationContext
{
    private readonly FakeInputTracker _inputTracker;

    public FakeNavigationContext(FakeInputTracker inputTracker)
    {
        _inputTracker = inputTracker;
    }

    public UINode? Hovered { get; set; }

    public IUIInputTracker InputTracker => _inputTracker;

    public INavigationFocusable? NavigationFocus { get; set; }

    public UINode? LastSetHovered { get; private set; }
    public int SetHoveredCallCount { get; private set; }

    public void SetHovered(UINode? node)
    {
        LastSetHovered = node;
        SetHoveredCallCount++;
    }

    public void RecomputeHoverFromCursor()
    {
        // No-op in tests: the fake does not perform hit-testing. Hovered is set explicitly.
    }
}

/// <summary>
/// A fake <see cref="IUIInputTracker"/> exposing settable directional button state.
/// </summary>
public sealed class FakeInputTracker : IUIInputTracker
{
    public Vector2 CursorPosition { get; set; }
    public Vector2 WindowSize { get; set; } = new(1920, 1080);
    public bool IsMouseLeftPressing { get; set; }
    public bool IsConfirmPressing { get; set; }
    public bool IsKeyDeletePressing { get; set; }
    public bool IsKeyBackspacePressing { get; set; }
    public bool IsKeyEnterPressing { get; set; }
    public bool IsKeyTabPressing { get; set; }
    public bool IsKeyLeftPressing { get; set; }
    public bool IsKeyRightPressing { get; set; }
    public bool IsKeyUpPressing { get; set; }
    public bool IsKeyDownPressing { get; set; }
    public bool IsKeySelectAllPressing { get; set; }
    public bool IsKeyCopyPressing { get; set; }
    public bool IsKeyPastePressing { get; set; }
    public bool IsGamepadInputting { get; set; }

    public bool IsScrolling(out Vector2 delta)
    {
        delta = Vector2.Zero;
        return false;
    }

    public void SetTextInput(float xNorm, float yNorm, float widthNorm, float heightNorm, int cursor) { }
    public void CopyToClipboard(ReadOnlySpan<char> text) { }
    public ReadOnlySpan<char> GetClipboardText() => ReadOnlySpan<char>.Empty;
    public void RegisterTextInput(Action<ReadOnlySpan<char>> action) { }
    public void UnregisterTextInput(Action<ReadOnlySpan<char>> action) { }
    public void RequestTextInput() { }
    public void ReleaseTextInput() { }
}
