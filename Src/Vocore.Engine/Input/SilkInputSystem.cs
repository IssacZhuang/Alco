using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace Vocore.Engine;

/// <summary>
/// The input class for Silk.NET mouse and keyboard input.
/// </summary>
public unsafe class SilkInputSystem : InputSystem
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
    private readonly IWindow _window;
    private readonly IInputContext _input;

    private State _state;
    private Vector2 _mousePosition;
    private Vector2 _mouseDelta;

    private IMouse? _defaultMouse;
    private IKeyboard? _defaultKeyboard;

    /// <inheritdoc />
    public override bool ForceMouseInScreenCenter { get; set; }

    /// <inheritdoc />
    public override Vector2 MousePosition
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

    /// <inheritdoc />
    public override Vector2 MouseDelta
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _mouseDelta;
    }

    internal SilkInputSystem(IWindow window)
    {
        _window = window;
        _input = window.CreateInput();
        _input.ConnectionChanged += OnConnectionChanged;

        RefreshDevice();
    }

    internal override void DoEvent()
    {
        _window?.DoEvents();
    }

    internal override void Reset()
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

    internal override void Update()
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

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool IsKeyDown(KeyCode key)
    {
        int offset = key;

        if (offset < 0 || offset >= MaxKeyCount)
        {
            return false;
        }

        return _state.iskeyDown[offset];
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool IsKeyUp(KeyCode key)
    {
        int offset = key;

        if (offset < 0 || offset >= MaxKeyCount)
        {
            return false;
        }

        return _state.iskeyUp[offset];
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool IsKeyPressing(KeyCode key)
    {
        int offset = key;

        if (offset < 0 || offset >= MaxKeyCount)
        {
            return false;
        }

        return _state.iskeyPressing[offset];
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool IsMouseDown(Mouse button)
    {
        int offset = button;

        if (offset < 0 || offset >= MaxMouseCount)
        {
            return false;
        }

        return _state.isMouseDown[offset];
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool IsMouseUp(Mouse button)
    {
        int offset = button;

        if (offset < 0 || offset >= MaxMouseCount)
        {
            return false;
        }

        return _state.isMouseUp[offset];
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool IsMousePressing(Mouse button)
    {
        int offset = button;

        if (offset < 0 || offset >= MaxMouseCount)
        {
            return false;
        }

        return _state.isMousePressing[offset];
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ResetMouseToCenter()
    {
        if (_defaultMouse == null)
        {
            return;
        }
        _mousePosition = new Vector2(_window!.Size.X / 2, _window.Size.Y / 2);
        _defaultMouse.Position = _mousePosition;
        _mouseDelta = Vector2.Zero;
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

    private void RefreshDevice()
    {
        _defaultMouse = _input.Mice.FirstOrDefault();
        _defaultKeyboard = _input.Keyboards.FirstOrDefault();

        if (_defaultMouse != null)
        {
            _defaultMouse.MouseDown += OnMouseDown;
            _defaultMouse.MouseUp += OnMouseUp;
        }
        else
        {
            Log.Warning("No mouse connected");
        }

        if (_defaultKeyboard != null)
        {
            _defaultKeyboard.KeyDown += OnKeyDown;
            _defaultKeyboard.KeyUp += OnKeyUp;
        }
        else
        {
            Log.Warning("No keyboard connected");
        }
    }

    private void OnConnectionChanged(IInputDevice device, bool conneted)
    {
        Log.Info($"Device {device.Name} is {(conneted ? "connected" : "disconnected")}");
        RefreshDevice();
    }
}
