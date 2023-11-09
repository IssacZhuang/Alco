using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Numerics;
using Veldrid.Sdl2;

namespace Veldrid
{
    public interface InputSnapshot
    {
        IReadOnlyList<KeyEvent> KeyEvents { get; }
        IReadOnlyList<MouseEvent> MouseEvents { get; }
        IReadOnlyList<char> KeyCharPresses { get; }
        bool IsMouseDown(MouseButton button);
        Vector2 MousePosition { get; }
        float WheelDelta { get; }
    }

    internal class SimpleInputSnapshot : InputSnapshot
    {
        public List<KeyEvent> KeyEventsList { get; private set; } = new List<KeyEvent>();
        public List<MouseEvent> MouseEventsList { get; private set; } = new List<MouseEvent>();
        public List<char> KeyCharPressesList { get; private set; } = new List<char>();

        public IReadOnlyList<KeyEvent> KeyEvents => KeyEventsList;

        public IReadOnlyList<MouseEvent> MouseEvents => MouseEventsList;

        public IReadOnlyList<char> KeyCharPresses => KeyCharPressesList;

        public Vector2 MousePosition { get; set; }

        private bool[] _mouseDown = new bool[13];
        public bool[] MouseDown => _mouseDown;
        public float WheelDelta { get; set; }

        public bool IsMouseDown(MouseButton button)
        {
            return _mouseDown[(int)button];
        }

        internal void Clear()
        {
            KeyEventsList.Clear();
            MouseEventsList.Clear();
            KeyCharPressesList.Clear();
            WheelDelta = 0f;
        }

        public void CopyTo(SimpleInputSnapshot other)
        {
            Debug.Assert(this != other);

            other.MouseEventsList.Clear();
            foreach (var me in MouseEventsList) { other.MouseEventsList.Add(me); }

            other.KeyEventsList.Clear();
            foreach (var ke in KeyEventsList) { other.KeyEventsList.Add(ke); }

            other.KeyCharPressesList.Clear();
            foreach (var kcp in KeyCharPressesList) { other.KeyCharPressesList.Add(kcp); }

            other.MousePosition = MousePosition;
            other.WheelDelta = WheelDelta;
            _mouseDown.CopyTo(other._mouseDown, 0);
        }
    }

    public class EngineInputSnapshot
    {
        private enum State : byte
        {
            None,
            Pressing,
            Up,
            Down
        }

        private struct KeyState
        {
            public State State;
            public ModifierKeys ModifierKeys;
        }

        private List<KeyEvent> _keyEvents = new List<KeyEvent>();
        private List<MouseEvent> _mouseEvents = new List<MouseEvent>();
        private State[] _mouseState = new State[13];
        private bool[] _mousePressing = new bool[13];
        private KeyState[] _keyState = new KeyState[256];
        private bool[] _keyPressing = new bool[256];
        private Vector2 _mousePosition;
        private Vector2 _mouseDelta;
        private float _wheelDelta;


        internal void PumpEvents(InputSnapshot snapshot)
        {
            _keyEvents.Clear();
            _mouseEvents.Clear();

            for (int i = 0; i < _mouseState.Length; i++)
            {
                _mouseState[i] = State.None;
            }

            for (int i = 0; i < snapshot.MouseEvents.Count; i++)
            {
                MouseEvent evt = snapshot.MouseEvents[i];
                int index = (int)evt.MouseButton;
                _mouseState[index] = evt.Down ? State.Down : State.Up;
                _mousePressing[index] = evt.Down;
                _mouseEvents.Add(evt);
            }

            for (int i = 0; i < _keyState.Length; i++)
            {
                _keyState[i].State = State.None;
            }

            for (int i = 0; i < snapshot.KeyEvents.Count; i++)
            {
                var ke = snapshot.KeyEvents[i];
                int index = (int)ke.Key;
                _keyState[index].State = ke.Down ? State.Down : State.Up;
                _keyState[index].ModifierKeys = ke.Modifiers;
                _keyPressing[index] = ke.Down;
                _keyEvents.Add(ke);
            }

            _mousePosition = snapshot.MousePosition;
            _mouseDelta = snapshot.MousePosition - _mousePosition;
            _wheelDelta = snapshot.WheelDelta;
        }

        public IReadOnlyList<KeyEvent> KeyEvents
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _keyEvents;
        }

        public IReadOnlyList<MouseEvent> MouseEvents
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _mouseEvents;
        }

        public Vector2 MousePosition
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _mousePosition;
        }

        public Vector2 MouseDelta
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _mouseDelta;
        }

        public float WheelDelta
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _wheelDelta;
        }



        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsKeyDown(Key key)
        {
            return _keyState[(int)key].State == State.Down;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsKeyDown(Key key, ModifierKeys modifierKeys)
        {
            return _keyState[(int)key].State == State.Down && _keyState[(int)key].ModifierKeys == modifierKeys;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsKeyUp(Key key)
        {
            return _keyState[(int)key].State == State.Up;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsKeyUp(Key key, ModifierKeys modifierKeys)
        {
            return _keyState[(int)key].State == State.Up && _keyState[(int)key].ModifierKeys == modifierKeys;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsKeyPressing(Key key)
        {
            return _keyPressing[(int)key];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsMouseDown(MouseButton button)
        {
            return _mouseState[(int)button] == State.Down;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsMouseUp(MouseButton button)
        {
            return _mouseState[(int)button] == State.Up;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsMousePressing(MouseButton button)
        {
            return _mousePressing[(int)button];
        }


    }
}