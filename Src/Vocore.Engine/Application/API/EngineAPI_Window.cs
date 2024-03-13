using System;
using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Vocore.Engine
{
    /// <summary>
    /// The API of the SDL2 window
    /// </summary>
    public class EngineAPI_Window
    {
        private readonly IWindow? _window;
        internal EngineAPI_Window(IWindow? window)
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
                    return WindowState.Normal;
                }
                return _window.WindowState;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (_window == null || _window.WindowState == value)
                {
                    return;
                }
                _window.WindowState = value;
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
                return new int2(_window.Size.X, _window.Size.Y);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (_window != null && (_window.Size.X != value.x || _window.Size.X != value.y))
                {
                    _window.Size = new Vector2D<int>(value.x, value.y);
                }
            }
        }

        public Vector2 SizeF
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return new Vector2(Size.x, Size.y);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (_window != null && (_window.Size.X != value.X || _window.Size.Y != value.Y))
                {
                    _window.Size = new Vector2D<int>((int)value.X, (int)value.Y);
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
                return _window.Size.X;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (_window != null && _window.Size.X != value)
                {
                    _window.Size = new Vector2D<int>(value, _window.Size.Y);
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
                return _window.Size.Y;

            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (_window != null && _window.Size.Y != value)
                {
                    _window.Size = new Vector2D<int>(_window.Size.X, value);
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
                return (float)_window.Size.X / (float)_window.Size.Y;
            }
        }
    }
}