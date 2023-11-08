using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Veldrid;
using Veldrid.Sdl2;

namespace Vocore.Engine
{
    public struct EngineAPI_Input
    {
        private readonly Sdl2Window? _window;
        private readonly bool[] _keyPressing;
        private InputSnapshot? _snapshot;
        internal EngineAPI_Input(Sdl2Window? window)
        {
            _window = window;
            _keyPressing = new bool[255];
            _snapshot = null;
            if (_window != null)
            {
                _window.KeyDown += HandleKeyDown;
                _window.KeyUp += HandleKeyUp;
            }
        }

        internal void Update()
        {
            if (_window != null)
            {
                _snapshot = _window.PumpEvents();
            }
        }

        private void HandleKeyDown(KeyEvent key)
        {
            _keyPressing[(byte)key.Key] = true;
        }

        private void HandleKeyUp(KeyEvent key)
        {
            _keyPressing[(byte)key.Key] = false;
        }

        public bool IsKeyDown(Key key)
        {
            if (_snapshot == null)
            {
                return false;
            }

            for (int i = 0; i < _snapshot.KeyEvents.Count; i++)
            {
                if (_snapshot.KeyEvents[i].Key == key && _snapshot.KeyEvents[i].Down)
                {
                    return true;
                }
            }

            return false;
        }

        


    }
}