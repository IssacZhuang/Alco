using System.Numerics;
using Alco.Rendering;

namespace Alco.ImGUI;

public static unsafe partial class ImGui
{
    private static readonly IntPtr WhiteTexture = (IntPtr)(-2);

    public static void Image(Texture2D texture2D, Vector2 size)
    {
        if (texture2D.IsDisposed || ImGUIRenderer.Instance == null)
        {
            Image(WhiteTexture, size);//it will render a white texture
            return;
        }

        IntPtr textureId = ImGUIRenderer.Instance.AddTexture(texture2D);
        Image(textureId, size);
    }

    public static void Image(Texture2D texture2D, Vector2 size, Rect uvRect)
    {
        if (texture2D.IsDisposed || ImGUIRenderer.Instance == null)
        {
            Image(WhiteTexture, size);//it will render a white texture
            return;
        }

        IntPtr textureId = ImGUIRenderer.Instance.AddTexture(texture2D);
        
        Vector2 uv0 = uvRect.Origin;
        Vector2 uv1 = uvRect.Origin + uvRect.Size;

        Image(textureId, size, uv0, uv1);
    }

    public static void Image(Texture2D texture2D, Vector2 size, ColorFloat color)
    {
        Vector4 tint = color;
        if (texture2D.IsDisposed || ImGUIRenderer.Instance == null)
        {
            Image(WhiteTexture, size, Vector2.Zero, Vector2.One, tint);
            return;
        }

        IntPtr textureId = ImGUIRenderer.Instance.AddTexture(texture2D);
        Image(textureId, size, Vector2.Zero, Vector2.One, tint);
    }

    public static void Image(Texture2D texture2D, Vector2 size, Rect uvRect, ColorFloat color)
    {
        Vector2 uv0 = uvRect.Origin;
        Vector2 uv1 = uvRect.Origin + uvRect.Size;
        Vector4 tint = color;
        if (texture2D.IsDisposed || ImGUIRenderer.Instance == null)
        {
            Image(WhiteTexture, size, uv0, uv1, tint);
            return;
        }

        IntPtr textureId = ImGUIRenderer.Instance.AddTexture(texture2D);
        Image(textureId, size, uv0, uv1, tint);
    }

    public static bool ImageButton(ReadOnlySpan<char> label, Texture2D texture2D, Vector2 size)
    {
        if (texture2D.IsDisposed || ImGUIRenderer.Instance == null)
        {
            return ImageButton(label, WhiteTexture, size);
        }

        IntPtr textureId = ImGUIRenderer.Instance.AddTexture(texture2D);
        return ImageButton(label, textureId, size);
    }

    public static bool ImageButton(ReadOnlySpan<char> label, Texture2D texture2D, Vector2 size, Rect uvRect)
    {
        Vector2 uv0 = uvRect.Origin;
        Vector2 uv1 = uvRect.Origin + uvRect.Size;

        if (texture2D.IsDisposed || ImGUIRenderer.Instance == null)
        {
            return ImageButton(label, WhiteTexture, size, uv0, uv1);
        }

        IntPtr textureId = ImGUIRenderer.Instance.AddTexture(texture2D);        
        return ImageButton(label, textureId, size, uv0, uv1);
    }

    public static bool ImageButton(ReadOnlySpan<char> label, Texture2D texture2D, Vector2 size, ColorFloat color)
    {
        if (texture2D.IsDisposed || ImGUIRenderer.Instance == null)
        {
            return ImageButton(label, WhiteTexture, size, Vector2.Zero, Vector2.One, default, color);
        }

        IntPtr textureId = ImGUIRenderer.Instance.AddTexture(texture2D);
        return ImageButton(label, textureId, size, Vector2.Zero, Vector2.One, default, color);
    }

    public static bool ImageButton(ReadOnlySpan<char> label, Texture2D texture2D, Vector2 size, Rect uvRect, ColorFloat color)
    {
        Vector2 uv0 = uvRect.Origin;
        Vector2 uv1 = uvRect.Origin + uvRect.Size;

        if (texture2D.IsDisposed || ImGUIRenderer.Instance == null)
        {
            return ImageButton(label, WhiteTexture, size, uv0, uv1, default, color);
        }

        IntPtr textureId = ImGUIRenderer.Instance.AddTexture(texture2D);
        return ImageButton(label, textureId, size, uv0, uv1, default, color);
    }
}