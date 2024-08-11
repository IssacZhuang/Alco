
using SDL3;
using Vocore.Graphics;

using static SDL3.SDL3;

namespace Vocore.Engine;

public unsafe class Sdl3Window : Window
{
    private readonly SDL_Window _window;
    private string _title;

    public override WindowMode WindowMode
    {
        get
        {
            return ConvertWindowMode(SDL_GetWindowFlags(_window));
        }
        set
        {
            //Todo: Implement this
        }
    }

    public override int2 Position
    {
        get
        {
            int2 result = default;
            SDL_GetWindowPosition(_window, out result.x, out result.y);
            return result;
        }
        set
        {
            _ = SDL_SetWindowPosition(_window, value.x, value.y);
        }
    }

    public override uint2 Size
    {
        get
        {
            SDL_GetWindowSize(_window, out int x, out int y);
            return new uint2(x, y);
        }
        set
        {
            _ = SDL_SetWindowSize(_window, (int)value.x, (int)value.y);
        }
    }
    public override string Title
    {
        get
        {
            return _title;
        }
        set
        {
            SDL_SetWindowTitle(_window, value);
            _title = value;
        }
    }

    public override GPUSwapchain? Swapchain => throw new NotImplementedException();

    public override InputSystem Input => throw new NotImplementedException();


    public Sdl3Window(GPUDevice device, WindowSetting setting)
    {
        _window = SDL_CreateWindow(setting.Title, setting.Width, setting.Height, ConvetWindowMode(setting.WindowMode));
        _title = setting.Title;
        if (_window.IsNull)
        {
            throw new Exception("Failed to create SDL window");
        }


    }

    public override void Close()
    {
        
    }

    protected override void Dispose(bool disposing)
    {
        SDL_DestroyWindow(_window);
    }

    private SDL_WindowFlags ConvetWindowMode(WindowMode mode)
    {
        return mode switch
        {
            WindowMode.Normal => SDL_WindowFlags.Resizable,
            WindowMode.Minimized => SDL_WindowFlags.Minimized,
            WindowMode.Maximized => SDL_WindowFlags.Maximized,
            WindowMode.Fullscreen => SDL_WindowFlags.Fullscreen,
            _ => SDL_WindowFlags.Resizable
        };
    }

    private WindowMode ConvertWindowMode(SDL_WindowFlags flags)
    {
        return flags switch
        {
            SDL_WindowFlags.Resizable => WindowMode.Normal,
            SDL_WindowFlags.Minimized => WindowMode.Minimized,
            SDL_WindowFlags.Maximized => WindowMode.Maximized,
            SDL_WindowFlags.Fullscreen => WindowMode.Fullscreen,
            _ => WindowMode.Normal
        };
    }
}