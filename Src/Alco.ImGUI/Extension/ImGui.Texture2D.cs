using System.Numerics;
using Alco.Rendering;

namespace Alco.ImGUI;

public static unsafe partial class ImGui
{
    public static void Image(Texture2D texture2D, Vector2 size)
    {
        if (texture2D.IsDisposed || ImGUIRenderer.Instance == null)
        {
            Image((IntPtr)(-2), size);//it will render a white texture
            return;
        }

        IntPtr textureId = ImGUIRenderer.Instance.AddTexture(texture2D);
        Image(textureId, size);
    }

    public static void Image(IntPtr textureId, Vector2 size, Rect uvRect)
    {
        // Convert Rect to UV coordinates
        // Rect.Origin represents the top-left corner (uv0)
        // Rect.Origin + Rect.Size represents the bottom-right corner (uv1)
        Vector2 uv0 = uvRect.Origin;
        Vector2 uv1 = uvRect.Origin + uvRect.Size;

        Image(textureId, size, uv0, uv1);
    }

    public static bool ImageButton(ReadOnlySpan<char> label, Texture2D texture2D, Vector2 size)
    {
        if (texture2D.IsDisposed || ImGUIRenderer.Instance == null)
        {
            return ImageButton(label, (IntPtr)(-2), size);
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
            return ImageButton(label, (IntPtr)(-2), size, uv0, uv1);
        }

        IntPtr textureId = ImGUIRenderer.Instance.AddTexture(texture2D);        
        return ImageButton(label, textureId, size, uv0, uv1);
    }
}