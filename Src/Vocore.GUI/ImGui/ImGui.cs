using System.Numerics;
using Vocore.Rendering;

namespace Vocore.GUI;

public static class ImGui
{
    private static IImGuiRenderer _renderer = new NoImGuiRenderer();
    private static bool _isBegin = false;

    public static void Initialize(IImGuiRenderer renderer)
    {
        _renderer = renderer;
    }


    private static void CheckStart()
    {
        if (!_isBegin)
        {
            _renderer.Begin();
            _isBegin = true;
        }
    }

    internal static bool CheckAndSubmit()
    {
        if (_isBegin)
        {
            _renderer.End();
            return true;
        }

        return false;
    }
}
