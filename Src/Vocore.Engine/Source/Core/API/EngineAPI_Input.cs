using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Veldrid;
using Veldrid.Sdl2;

namespace Vocore.Engine
{
    public class EngineAPI_Input
    {
        private readonly Sdl2Window? _window;
        private readonly EngineInputSnapshot? _snapshot;
        private readonly List<KeyEvent> _emptyKeyEvents;
        private readonly List<MouseEvent> _emptyMouseEvents;
        internal EngineAPI_Input(Sdl2Window? window)
        {
            _window = window;
            _snapshot = null;
            _emptyKeyEvents = new List<KeyEvent>();
            _emptyMouseEvents = new List<MouseEvent>();
            if (_window != null)
            {
                _snapshot = _window.PumpEvents();
            }
        }


        /// <summary>
        /// The mouse position in screen space.
        /// </summary>
        public Vector2 MousePosition
        {
            get
            {
                if (_snapshot == null)
                {
                    return Vector2.Zero;
                }
                return _snapshot.MousePosition;
            }
            set
            {
                if (_window != null)
                {
                    _window.SetMousePosition(value);
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
                if (_snapshot == null)
                {
                    return Vector2.Zero;
                }
                return _snapshot.MouseDelta;
            }
        }

        /// <summary>
        /// The mouse wheel delta since the last frame.
        /// </summary>
        public float WheelDelta
        {
            get
            {
                if (_snapshot == null)
                {
                    return 0f;
                }
                return _snapshot.WheelDelta;
            }
        }

        /// <summary>
        /// The key event in the last frame.
        /// </summary>
        public IReadOnlyList<KeyEvent> KeyEvents
        {
            get
            {
                if (_snapshot == null)
                {
                    return _emptyKeyEvents;
                }
                return _snapshot.KeyEvents;
            }
        }

        /// <summary>
        /// The mouse event in the last frame.
        /// </summary>
        public IReadOnlyList<MouseEvent> MouseEvents
        {
            get
            {
                if (_snapshot == null)
                {
                    return _emptyMouseEvents;
                }
                return _snapshot.MouseEvents;
            }
        }

        public bool IsKeyDown(Key key)
        {
            if (_snapshot == null)
            {
                return false;
            }
            return _snapshot.IsKeyDown(key);
        }

        public bool IsKeyUp(Key key)
        {
            if (_snapshot == null)
            {
                return false;
            }
            return _snapshot.IsKeyUp(key);
        }

        public bool IsKeyPressing(Key key)
        {
            if (_snapshot == null)
            {
                return false;
            }
            return _snapshot.IsKeyPressing(key);
        }

        public bool IsMouseDown(MouseButton button)
        {
            if (_snapshot == null)
            {
                return false;
            }
            return _snapshot.IsMouseDown(button);
        }

        public bool IsMouseUp(MouseButton button)
        {
            if (_snapshot == null)
            {
                return false;
            }
            return _snapshot.IsMouseUp(button);
        }

        public bool IsMousePressing(MouseButton button)
        {
            if (_snapshot == null)
            {
                return false;
            }
            return _snapshot.IsMousePressing(button);
        }
    }

    
}