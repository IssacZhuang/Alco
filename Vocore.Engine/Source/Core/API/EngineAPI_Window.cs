using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using Veldrid.Sdl2;

namespace Vocore.Engine
{
    /// <summary>
    /// The API of the SDL2 window
    /// </summary>
    public class EngineAPI_Window
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

        public Vector2 SizeF
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (_window == null)
                {
                    return new Vector2(0, 0);
                }
                return new Vector2(_window.Width, _window.Height);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (_window != null && (_window.Width != value.X || _window.Height != value.Y))
                {
                    _window.Width = (int)value.X;
                    _window.Height = (int)value.Y;
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

    // exposed API of the GameEngine.Window
    public static class Window
    {
        public static WindowState WindowState
        {
            get
            {
                return GameEngine.Instance.Window.WindowState;
            }
            set
            {
                GameEngine.Instance.Window.WindowState = value;
            }
        }

        public static int2 Size
        {
            get
            {
                return GameEngine.Instance.Window.Size;
            }
            set
            {
                GameEngine.Instance.Window.Size = value;
            }
        }

        public static Vector2 SizeF
        {
            get
            {
                return GameEngine.Instance.Window.SizeF;
            }
            set
            {
                GameEngine.Instance.Window.SizeF = value;
            }
        }

        public static int Width
        {
            get
            {
                return GameEngine.Instance.Window.Width;
            }
            set
            {
                GameEngine.Instance.Window.Width = value;
            }
        }

        public static int Height
        {
            get
            {
                return GameEngine.Instance.Window.WindowHeight;
            }
            set
            {
                GameEngine.Instance.Window.WindowHeight = value;
            }
        }

        public static float AspectRatio
        {
            get
            {
                return GameEngine.Instance.Window.AspectRatio;
            }
        }

        public static Vector2 MousePosition
        {
            get
            {
                return GameEngine.Instance.Input.MousePosition;
            }
            set
            {
                GameEngine.Instance.Input.MousePosition = value;
            }
        }

        public static Vector2 MouseDelta
        {
            get
            {
                return GameEngine.Instance.Input.MouseDelta;
            }
        }

        public static float WheelDelta
        {
            get
            {
                return GameEngine.Instance.Input.WheelDelta;
            }
        }

        
    }
}