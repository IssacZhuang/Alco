namespace Alco.Graphics;

public struct RenderBundleDescriptor
{
    public RenderBundleDescriptor(string name)
    {
        Name = name;
    }

    public string Name { get; init; }
}