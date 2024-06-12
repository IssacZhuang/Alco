namespace Vocore.Graphics;

public struct SwapcahinDescriptor
{
    public SwapcahinDescriptor(SurfaceSource source, uint width, uint height, bool isVSyncEnabled)
    {
        Source = source;
        Width = width;
        Height = height;
        IsVSyncEnabled = isVSyncEnabled;
    }

    public SurfaceSource Source { get; init; }
    public uint Width { get; init; }
    public uint Height { get; init; }
    public bool IsVSyncEnabled { get; init; }
}