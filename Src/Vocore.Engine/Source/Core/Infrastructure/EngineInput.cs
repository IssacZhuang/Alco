using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace Vocore.Engine
{
    public unsafe class EngineInput
    {
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
        private State _state;
        private Vector2 _mousePosition;
        private Vector2 _mouseDelta;
        private IWindow? _window;
        private IInputContext? _input;
        private IMouse? _defaultMouse;
        private IKeyboard? _defaultKeyboard;

        public bool ForceMouseInScreenCenter { get; set; }

        public Vector2 MousePosition
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (_defaultMouse == null)
                {
                    return Vector2.Zero;
                }
                return _defaultMouse.Position;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (_defaultMouse != null)
                {
                    _defaultMouse.Position = value;
                }
            }
        }

        public Vector2 MouseDelta
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _mouseDelta;
            }
        }

        internal EngineInput(IWindow? window)
        {

            if (window == null)
            {
                return;
            }
            _window = window;
            _input = window.CreateInput();
            if (_input != null)
            {
                _defaultMouse = _input.Mice.FirstOrDefault();
                _defaultKeyboard = _input.Keyboards.FirstOrDefault();
                _input.ConnectionChanged += OnConnectionChanged;
            }

            if (_defaultMouse != null)
            {
                _defaultMouse.MouseDown += OnMouseDown;
                _defaultMouse.MouseUp += OnMouseUp;
            }

            if (_defaultKeyboard != null)
            {
                _defaultKeyboard.KeyDown += OnKeyDown;
                _defaultKeyboard.KeyUp += OnKeyUp;
            }
        }

        /// <summary>
        /// Reset the input state on the end of the frame
        /// </summary>
        internal void Reset()
        {
            for (int i = 0; i < MaxKeyCount; i++)
            {
                _state.iskeyDown[i] = false;
                _state.iskeyUp[i] = false;
            }

            for (int i = 0; i < MaxMouseCount; i++)
            {
                _state.isMouseDown[i] = false;
                _state.isMouseUp[i] = false;
            }
        }

        internal void Update()
        {
            if (_defaultMouse == null)
            {
                return;
            }
            _mouseDelta = _mousePosition - _defaultMouse.Position;
            _mousePosition = _defaultMouse.Position;

            if (ForceMouseInScreenCenter)
            {
                _mousePosition = new Vector2(_window!.Size.X / 2, _window.Size.Y / 2);
                _defaultMouse.Position = _mousePosition;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsKeyDown(Key key)
        {
            int offset = (int)key;

            if (offset < 0 || offset >= MaxKeyCount)
            {
                return false;
            }

            return _state.iskeyDown[offset];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsKeyUp(Key key)
        {
            int offset = (int)key;

            if (offset < 0 || offset >= MaxKeyCount)
            {
                return false;
            }

            return _state.iskeyUp[offset];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsKeyPressing(Key key)
        {
            int offset = (int)key;

            if (offset < 0 || offset >= MaxKeyCount)
            {
                return false;
            }

            return _state.iskeyPressing[offset];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsMouseDown(MouseButton button)
        {
            int offset = (int)button;

            if (offset < 0 || offset >= MaxMouseCount)
            {
                return false;
            }

            return _state.isMouseDown[offset];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsMouseUp(MouseButton button)
        {
            int offset = (int)button;

            if (offset < 0 || offset >= MaxMouseCount)
            {
                return false;
            }

            return _state.isMouseUp[offset];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsMousePressing(MouseButton button)
        {
            int offset = (int)button;

            if (offset < 0 || offset >= MaxMouseCount)
            {
                return false;
            }

            return _state.isMousePressing[offset];
        }



        private void OnMouseDown(IMouse mouse, MouseButton button)
        {
            int offset = (int)button;

            if (offset < 0 || offset >= MaxMouseCount)
            {
                return;
            }

            _state.isMouseDown[offset] = true;
            _state.isMousePressing[offset] = true;
        }

        private void OnMouseUp(IMouse mouse, MouseButton button)
        {
            int offset = (int)button;

            if (offset < 0 || offset >= MaxMouseCount)
            {
                return;
            }

            _state.isMouseUp[offset] = true;
            _state.isMousePressing[offset] = false;
        }

        private void OnKeyDown(IKeyboard keyboard, Key key, int _)
        {
            int offset = (int)key;

            if (offset < 0 || offset >= MaxKeyCount)
            {
                return;
            }

            _state.iskeyDown[offset] = true;
            _state.iskeyPressing[offset] = true;
        }

        private void OnKeyUp(IKeyboard keyboard, Key key, int _)
        {
            int offset = (int)key;

            if (offset < 0 || offset >= MaxKeyCount)
            {
                return;
            }

            _state.iskeyUp[offset] = true;
            _state.iskeyPressing[offset] = false;
        }

        private void OnConnectionChanged(IInputDevice device, bool conneted)
        {
            Log.Info($"Device {device.Name} is {(conneted ? "connected" : "disconnected")}");
        }
    }
}