using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Input;
using Silk.NET.Input.Glfw;
using Silk.NET.Windowing;

namespace Vocore.Engine
{
    public class EngineAPI_Input
    {
        private readonly IWindow? _window;
        private readonly IInputContext? _input;
        private readonly IMouse? _defaultMouse;
        private readonly IKeyboard? _defaultKeyboard;

        private Vector2 _mousePosition;
        private Vector2 _mouseDelta;
        internal EngineAPI_Input(IWindow? window)
        {
            _window = window;

            if (_window != null)
            {
                _input = _window.CreateInput();
                _defaultMouse = _input.Mice[0];
                _defaultKeyboard = _input.Keyboards[0];

                _defaultMouse.MouseMove += OnMouseMove;
                
            }
        }

        // Events

        public void OnMouseMove(IMouse mouse, Vector2 position)
        {
            _mouseDelta = position - _mousePosition;
            _mousePosition = position;
        }

        // API 


        /// <summary>
        /// The mouse position in screen space.
        /// </summary>
        public Vector2 MousePosition
        {
            get
            {
                if (_defaultMouse == null)
                {
                    return Vector2.Zero;
                }
                return _defaultMouse.Position;
            }
            set
            {
                if (_defaultMouse != null)
                {
                    _defaultMouse.Position = value;
                }
            }
        }

        /// <summary>
        /// The mouse movement since the last frame.
        /// </summary>
        public Vector2 MouseDelta
        {
            get
            {
                return _mouseDelta;
            }
        }

        public bool IsKeyDown(Key key)
        {
            if (_defaultKeyboard == null)
            {
                return false;
            }
            return _defaultKeyboard.IsKeyPressed(key);
        }

        // public bool IsKeyUp(Key key)
        // {
        //     if (_snapshot == null)
        //     {
        //         return false;
        //     }
        //     return _snapshot.IsKeyUp(key);
        // }

        // public bool IsKeyPressing(Key key)
        // {
        //     if (_snapshot == null)
        //     {
        //         return false;
        //     }
        //     return _snapshot.IsKeyPressing(key);
        // }

        // public bool IsMouseDown(MouseButton button)
        // {
        //     if (_snapshot == null)
        //     {
        //         return false;
        //     }
        //     return _snapshot.IsMouseDown(button);
        // }

        // public bool IsMouseUp(MouseButton button)
        // {
        //     if (_snapshot == null)
        //     {
        //         return false;
        //     }
        //     return _snapshot.IsMouseUp(button);
        // }

        // public bool IsMousePressing(MouseButton button)
        // {
        //     if (_snapshot == null)
        //     {
        //         return false;
        //     }
        //     return _snapshot.IsMousePressing(button);
        // }
    }
}