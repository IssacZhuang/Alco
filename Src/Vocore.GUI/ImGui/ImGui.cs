using System.Numerics;
using Vocore.Rendering;

namespace Vocore.GUI;

public static class ImGui
{
    private static ImGuiRenderer _renderer = null!;
    private static Vector2 _pivotPosition;

    public static void Initialize(ImGuiRenderer renderer)
    {
        _renderer = renderer;
    }

    public static void Begin(Vector2 pivot)
    {
        _pivotPosition = pivot;
        _renderer.Begin();
    }

    public static void End()
    {
        _renderer.End();
    }
}
