using System.Numerics;
using Alco.Graphics;

namespace Alco.ImGUI;

public static unsafe partial class ImGui
{
    /// <summary>Gets the ImGui texture identifier for a GPU texture, or slot 0 (white) when unavailable.</summary>
    /// <param name="texture">The GPU texture, or null.</param>
    /// <returns>The bindless sampled texture identifier usable with ImGui draw commands.</returns>
    public static IntPtr GetTextureId(Texture? texture)
    {
        if (texture == null || texture.IsDestroyed)
        {
            return IntPtr.Zero;
        }

        uint index = texture.SampledIndex;
        return index == Texture.NoIndex ? IntPtr.Zero : (IntPtr)index;
    }

    /// <summary>Draws a texture with the given screen size.</summary>
    /// <param name="texture">The GPU texture to draw.</param>
    /// <param name="size">The drawn size in pixels.</param>
    public static void Image(Texture? texture, Vector2 size)
    {
        Image(GetTextureId(texture), size);
    }

    /// <summary>Draws a texture region with the given screen size.</summary>
    /// <param name="texture">The GPU texture to draw.</param>
    /// <param name="size">The drawn size in pixels.</param>
    /// <param name="uvRect">The texture-space rectangle to sample.</param>
    public static void Image(Texture? texture, Vector2 size, Rect uvRect)
    {
        Vector2 uv0 = uvRect.Origin;
        Vector2 uv1 = uvRect.Origin + uvRect.Size;
        Image(GetTextureId(texture), size, uv0, uv1);
    }

    /// <summary>Draws a texture tinted by the given color.</summary>
    /// <param name="texture">The GPU texture to draw.</param>
    /// <param name="size">The drawn size in pixels.</param>
    /// <param name="color">The tint color.</param>
    public static void Image(Texture? texture, Vector2 size, ColorFloat color)
    {
        Image(GetTextureId(texture), size, Vector2.Zero, Vector2.One, color);
    }

    /// <summary>Draws a tinted texture region with the given screen size.</summary>
    /// <param name="texture">The GPU texture to draw.</param>
    /// <param name="size">The drawn size in pixels.</param>
    /// <param name="uvRect">The texture-space rectangle to sample.</param>
    /// <param name="color">The tint color.</param>
    public static void Image(Texture? texture, Vector2 size, Rect uvRect, ColorFloat color)
    {
        Vector2 uv0 = uvRect.Origin;
        Vector2 uv1 = uvRect.Origin + uvRect.Size;
        Image(GetTextureId(texture), size, uv0, uv1, color);
    }

    /// <summary>Draws a texture button and returns whether it was clicked.</summary>
    /// <param name="label">The button label.</param>
    /// <param name="texture">The GPU texture to draw.</param>
    /// <param name="size">The button size in pixels.</param>
    /// <returns>True when the button was clicked.</returns>
    public static bool ImageButton(ReadOnlySpan<char> label, Texture? texture, Vector2 size)
    {
        return ImageButton(label, GetTextureId(texture), size);
    }

    /// <summary>Draws a texture-region button and returns whether it was clicked.</summary>
    /// <param name="label">The button label.</param>
    /// <param name="texture">The GPU texture to draw.</param>
    /// <param name="size">The button size in pixels.</param>
    /// <param name="uvRect">The texture-space rectangle to sample.</param>
    /// <returns>True when the button was clicked.</returns>
    public static bool ImageButton(ReadOnlySpan<char> label, Texture? texture, Vector2 size, Rect uvRect)
    {
        Vector2 uv0 = uvRect.Origin;
        Vector2 uv1 = uvRect.Origin + uvRect.Size;
        return ImageButton(label, GetTextureId(texture), size, uv0, uv1);
    }

    /// <summary>Draws a tinted texture button and returns whether it was clicked.</summary>
    /// <param name="label">The button label.</param>
    /// <param name="texture">The GPU texture to draw.</param>
    /// <param name="size">The button size in pixels.</param>
    /// <param name="color">The tint color.</param>
    /// <returns>True when the button was clicked.</returns>
    public static bool ImageButton(ReadOnlySpan<char> label, Texture? texture, Vector2 size, ColorFloat color)
    {
        return ImageButton(label, GetTextureId(texture), size, Vector2.Zero, Vector2.One, default, color);
    }

    /// <summary>Draws a tinted texture-region button and returns whether it was clicked.</summary>
    /// <param name="label">The button label.</param>
    /// <param name="texture">The GPU texture to draw.</param>
    /// <param name="size">The button size in pixels.</param>
    /// <param name="uvRect">The texture-space rectangle to sample.</param>
    /// <param name="color">The tint color.</param>
    /// <returns>True when the button was clicked.</returns>
    public static bool ImageButton(ReadOnlySpan<char> label, Texture? texture, Vector2 size, Rect uvRect, ColorFloat color)
    {
        Vector2 uv0 = uvRect.Origin;
        Vector2 uv1 = uvRect.Origin + uvRect.Size;
        return ImageButton(label, GetTextureId(texture), size, uv0, uv1, default, color);
    }
}
