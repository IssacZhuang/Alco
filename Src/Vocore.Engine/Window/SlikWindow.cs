
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Vocore.Engine;

internal class SilkWindow : Window
{
    private readonly IWindow _slikWindow;

    internal IWindow InternalWindow => _slikWindow;

    public SilkWindow(IWindow slikWindow)
    {
        _slikWindow = slikWindow;
        _slikWindow.Resize += OnSilkResize;
    }



    public override WindowMode WindowMode
    {
        get
        {
            return (WindowMode)_slikWindow.WindowState;
        }
        set
        {
            _slikWindow.WindowState = value;
        }
    }

    public override int2 Size
    {
        get
        {
            return new int2(_slikWindow.Size.X, _slikWindow.Size.Y);
        }
        set
        {
            _slikWindow.Size = new Vector2D<int>(value.x, value.y);
        }
    }

    public override float AspectRatio
    {
        get
        {
            return _slikWindow.Size.X / (float)_slikWindow.Size.Y;
        }
    }

    public override string Title
    {
        get
        {
            return _slikWindow.Title;
        }
        set
        {
            _slikWindow.Title = value;
        }
    }

    private void OnSilkResize(Vector2D<int> size)
    {
        OnResize?.Invoke(new int2(size.X, size.Y));
    }

    internal override void Close()
    {
        _slikWindow.Close();
        _slikWindow.Dispose();   
    }
}