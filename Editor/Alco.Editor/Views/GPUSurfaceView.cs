using System;
using System.Threading.Tasks;
using Alco.Engine;
using Alco.Rendering;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;

namespace Alco.Editor.Views;

public class GPUSurfaceView : NativeControlHost, IDisposable
{
    public IntPtr Handle { get; private set; }

    public WindowRenderTarget? RenderTarget { get; private set; }

    

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);
        Handle = handle.Handle;
        Sdl3Window window = Sdl3Window.CreateWin32Window(App.Main.Engine.GraphicsDevice, Handle);
        uint width = (uint)Width;
        uint height = (uint)Height;
        GameEngine engine = App.Main.Engine;
        RenderingSystem renderingSystem = engine.Rendering;
        RenderTarget = engine.CreateWindowRenderTarget(window, renderingSystem.PrefferedSDRPass, engine.BuiltInAssets.Shader_Blit);
        return handle;
    }

    public void Dispose()
    {
        RenderTarget?.Dispose();
    }
}