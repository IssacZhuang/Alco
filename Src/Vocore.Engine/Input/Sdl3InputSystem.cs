using System.Numerics;
using SDL3;
using static SDL3.SDL3;

namespace Vocore.Engine;

public unsafe class Sdl3InputSystem : InputSystem
{
    private SDL_Window _window;

    private const int MaxKeyCount = 512;
    private const int MaxMouseCount = 16;

    private struct State
    {
        public fixed bool iskeyDown[MaxKeyCount];
        public fixed bool iskeyUp[MaxKeyCount];
        public fixed bool iskeyPressing[MaxKeyCount];
        public fixed bool isMouseDown[MaxMouseCount];
        public fixed bool isMouseUp[MaxMouseCount];
        public fixed bool isMousePressing[MaxMouseCount];
    }

    public override Vector2 MousePosition
    {
        get
        {
            Vector2 result = default;
            SDL_GetMouseState(&result.X, &result.Y);
            return result;
        }
        set
        {
            SDL_WarpMouseInWindow(_window, (int)value.X, (int)value.Y);
        }
    }

    public override Vector2 MouseDelta
    {
        get
        {
            return new Vector2(0, 0);
        }
    }

    public override bool ForceMouseInScreenCenter
    {
        get
        {
            return false;
        }
        set
        {

        }
    }

    public Sdl3InputSystem(SDL_Window window)
    {
        _window = window;
    }

    public override bool IsKeyDown(KeyCode key)
    {
        return false;
    }

    public override bool IsKeyPressing(KeyCode key)
    {
        return false;
    }

    public override bool IsKeyUp(KeyCode key)
    {
        return false;
    }

    public override bool IsMouseDown(Mouse button)
    {
        return false;
    }

    public override bool IsMousePressing(Mouse button)
    {
        return false;
    }

    public override bool IsMouseScrolling(out Vector2 delta)
    {
        delta = default;
        return false;
    }

    public override bool IsMouseUp(Mouse button)
    {
        return false;
    }

    internal override void DoEvent()
    {
        while (SDL_PollEvent(out SDL_Event e))
        {
            switch (e.type)
            {
                case SDL_EventType.KeyDown:
                    
                    break;
            }
        }
    }

    internal override void Reset()
    {

    }

    internal override void Update()
    {

    }


}