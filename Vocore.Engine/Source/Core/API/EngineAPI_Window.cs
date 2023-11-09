using System;
using System.Numerics;
using System.Runtime.CompilerServices;

using Veldrid.Sdl2;

namespace Vocore.Engine
{
    /// <summary>
    /// The API of the SDL2 window
    /// </summary>
    public readonly struct EngineAPI_Window
    {
        private readonly Sdl2Window? _window;
        internal EngineAPI_Window(Sdl2Window? window)
        {
            _window = window;
        }

        public WindowState WindowState
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (_window == null)
                {
                    return WindowState.Hidden;
                }
                return _window.WindowState;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (_window != null && _window.WindowState != value)
                {
                    _window.WindowState = value;
                }
            }
        }

        public int2 Size
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (_window == null)
                {
                    return new int2(0, 0);
                }
                return new int2(_window.Width, _window.Height);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (_window != null && (_window.Width != value.x || _window.Height != value.y))
                {
                    _window.Width = value.x;
                    _window.Height = value.y;
                }
            }
        }

        public int Width
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (_window == null)
                {
                    return 0;
                }
                return _window.Width;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (_window != null && _window.Width != value)
                {
                    _window.Width = value;
                }
            }
        }

        public int WindowHeight
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (_window == null)
                {
                    return 0;
                }
                return _window.Height;

            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (_window != null && _window.Height != value)
                {
                    _window.Height = value;
                }
            }
        }

        public float AspectRatio
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (_window == null)
                {
                    return 1;
                }
                return (float)_window.Width / _window.Height;
            }
        }
    }
}