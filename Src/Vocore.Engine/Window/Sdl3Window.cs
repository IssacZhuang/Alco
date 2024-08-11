
using SDL3;
using Vocore.Graphics;

using static SDL3.SDL3;

namespace Vocore.Engine;

public unsafe class Sdl3Window : Window
{
    private readonly SDL_Window _window;

    public override WindowMode WindowMode { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public override int2 Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public override uint2 Size { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public override string Title { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public override GPUSwapchain? Swapchain => throw new NotImplementedException();

    public override InputSystem Input => throw new NotImplementedException();


    public Sdl3Window(GPUDevice device, WindowSetting setting)
    {
        _window = SDL_CreateWindow(setting.Title, setting.Width, setting.Height, SDL_WindowFlags.Resizable);
        
    }

    public override void Close()
    {

    }

    protected override void Dispose(bool disposing)
    {

    }
}