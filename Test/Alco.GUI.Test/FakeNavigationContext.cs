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

    /// <summary>Initializes a new instance with the given input tracker.</summary>
    /// <param name="inputTracker">The input tracker exposed to navigation logic.</param>
    public FakeNavigationContext(FakeInputTracker inputTracker)
    {
        _inputTracker = inputTracker;
    }

    /// <summary>Gets or sets the currently hovered node.</summary>
    public UINode? Hovered { get; set; }

    /// <summary>Gets the input tracker.</summary>
    public IUIInputTracker InputTracker => _inputTracker;

    /// <summary>Gets or sets the current navigation focus owner.</summary>
    public INavigationFocusable? NavigationFocus { get; set; }

    /// <summary>Gets the last node passed to <see cref="SetHovered"/>.</summary>
    public UINode? LastSetHovered { get; private set; }
    /// <summary>Gets the number of calls to <see cref="SetHovered"/>.</summary>
    public int SetHoveredCallCount { get; private set; }

    /// <summary>Records the hovered node and increments the call count.</summary>
    /// <param name="node">The node to record as hovered.</param>
    public void SetHovered(UINode? node)
    {
        LastSetHovered = node;
        SetHoveredCallCount++;
    }

    /// <summary>Does nothing, since tests set <see cref="Hovered"/> explicitly.</summary>
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
    /// <summary>Gets or sets the cursor position.</summary>
    public Vector2 CursorPosition { get; set; }
    /// <summary>Gets or sets the window size.</summary>
    public Vector2 WindowSize { get; set; } = new(1920, 1080);
    /// <summary>Gets or sets a value indicating whether the left mouse button is pressed.</summary>
    public bool IsMouseLeftPressing { get; set; }
    /// <summary>Gets or sets a value indicating whether the confirm input is pressed.</summary>
    public bool IsConfirmPressing { get; set; }
    /// <summary>Gets or sets a value indicating whether the delete key is pressed.</summary>
    public bool IsKeyDeletePressing { get; set; }
    /// <summary>Gets or sets a value indicating whether the backspace key is pressed.</summary>
    public bool IsKeyBackspacePressing { get; set; }
    /// <summary>Gets or sets a value indicating whether the enter key is pressed.</summary>
    public bool IsKeyEnterPressing { get; set; }
    /// <summary>Gets or sets a value indicating whether the tab key is pressed.</summary>
    public bool IsKeyTabPressing { get; set; }
    /// <summary>Gets or sets a value indicating whether the left arrow key is pressed.</summary>
    public bool IsKeyLeftPressing { get; set; }
    /// <summary>Gets or sets a value indicating whether the right arrow key is pressed.</summary>
    public bool IsKeyRightPressing { get; set; }
    /// <summary>Gets or sets a value indicating whether the up arrow key is pressed.</summary>
    public bool IsKeyUpPressing { get; set; }
    /// <summary>Gets or sets a value indicating whether the down arrow key is pressed.</summary>
    public bool IsKeyDownPressing { get; set; }
    /// <summary>Gets or sets a value indicating whether the select-all shortcut is pressed.</summary>
    public bool IsKeySelectAllPressing { get; set; }
    /// <summary>Gets or sets a value indicating whether the copy shortcut is pressed.</summary>
    public bool IsKeyCopyPressing { get; set; }
    /// <summary>Gets or sets a value indicating whether the paste shortcut is pressed.</summary>
    public bool IsKeyPastePressing { get; set; }
    /// <summary>Gets or sets a value indicating whether gamepad input is active.</summary>
    public bool IsGamepadInputting { get; set; }

    /// <summary>Reports that no scrolling occurred.</summary>
    /// <param name="delta">Receives a zero scroll delta.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool IsScrolling(out Vector2 delta)
    {
        delta = Vector2.Zero;
        return false;
    }

    /// <summary>Does nothing; text input is not simulated.</summary>
    public void SetTextInput(float xNorm, float yNorm, float widthNorm, float heightNorm, int cursor) { }
    /// <summary>Does nothing; clipboard writes are not simulated.</summary>
    public void CopyToClipboard(ReadOnlySpan<char> text) { }
    /// <summary>Returns an empty clipboard value.</summary>
    /// <returns>An empty character span.</returns>
    public ReadOnlySpan<char> GetClipboardText() => ReadOnlySpan<char>.Empty;
    /// <summary>Does nothing; text input registration is not simulated.</summary>
    public void RegisterTextInput(Action<ReadOnlySpan<char>> action) { }
    /// <summary>Does nothing; text input registration is not simulated.</summary>
    public void UnregisterTextInput(Action<ReadOnlySpan<char>> action) { }
    /// <summary>Gets the number of text-input requests.</summary>
    public int TextInputRequestCount { get; private set; }

    /// <summary>Gets the number of released text-input requests.</summary>
    public int TextInputReleaseCount { get; private set; }

    /// <summary>Records a text-input request.</summary>
    public void RequestTextInput() => TextInputRequestCount++;

    /// <summary>Records the release of a text-input request.</summary>
    public void ReleaseTextInput() => TextInputReleaseCount++;
}
