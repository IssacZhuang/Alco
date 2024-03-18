using Vocore.Graphics;

namespace Vocore.Engine;

public struct GraphicsSetting
{
    public GraphicsBackend Backend { get; set; }
    public bool DebugInfo { get; set; }
    public bool VSync { get; set; }

    public static readonly GraphicsSetting Default = new GraphicsSetting
    {
        Backend = GraphicsBackend.Auto,
        DebugInfo = false,
        VSync = false
    };

    public static readonly GraphicsSetting Debug = new GraphicsSetting
    {
        Backend = GraphicsBackend.Auto,
        DebugInfo = true,
        VSync = false
    };

    public static readonly GraphicsSetting NoGPU = new GraphicsSetting
    {
        Backend = GraphicsBackend.None,
        DebugInfo = false,
        VSync = false
    };
}
