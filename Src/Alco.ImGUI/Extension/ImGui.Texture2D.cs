
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

    public static bool ImageButton(ReadOnlySpan<char> label, Texture2D texture2D, Vector2 size)
    {
        if (texture2D.IsDisposed || ImGUIRenderer.Instance == null)
        {
            return ImageButton(label, (IntPtr)(-2), size);
        }

        IntPtr textureId = ImGUIRenderer.Instance.AddTexture(texture2D);
        return ImageButton(label, textureId, size);
    }

}