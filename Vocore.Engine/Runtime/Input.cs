using System;
using System.Collections.Generic;
using Veldrid;
using System.Runtime.CompilerServices;
using System.Numerics;



namespace Vocore.Engine
{
    public static class Input
    {
        

        private static InputSnapshot Snapshot
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return RuntimeGlobal.InputSnapshot;
            }
        }
        public static Vector2 MousePosition
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Snapshot.MousePosition;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                RuntimeGlobal.Window.SetMousePosition(value);
            }
        }

        private static readonly HashSet<Key> KeyStates = new HashSet<Key>();

        public static void UpdateKeyStates()
        {
            foreach (var keyEvent in Snapshot.KeyEvents)
            {
                if (keyEvent.Down)
                {
                    KeyStates.Add(keyEvent.Key);
                }
                else
                {
                    KeyStates.Remove(keyEvent.Key);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsMousePressing(MouseButton button)
        {
            return Snapshot.IsMouseDown(button);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsMouseKeyDown(MouseButton button)
        {
            return Snapshot.MouseEvents.Any(m => m.MouseButton == button && m.Down);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsMouseKeyUp(MouseButton button)
        {
            return Snapshot.MouseEvents.Any(m => m.MouseButton == button && !m.Down);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsKeyDown(Key key)
        {
            return Snapshot.KeyEvents.Any(k => k.Key == key && k.Down);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsKeyUp(Key key)
        {
            return Snapshot.KeyEvents.Any(k => k.Key == key && !k.Down);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsKeyPressing(Key key)
        {
            return KeyStates.Contains(key);
        }
    }
}