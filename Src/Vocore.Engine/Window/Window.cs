namespace Vocore.Engine;

public abstract class Window
{
    public abstract WindowMode WindowMode { get; set; }
    public abstract int2 Size { get; set; }
    public abstract float AspectRatio { get; }
    public abstract string Title { get; set; }
    internal Action<int2>? OnResize;

    internal abstract void Close();
}