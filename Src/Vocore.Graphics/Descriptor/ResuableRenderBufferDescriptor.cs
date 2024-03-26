namespace Vocore.Graphics;

public struct ResuableRenderBufferDescriptor
{
    public ResuableRenderBufferDescriptor(string name)
    {
        Name = name;
    }

    public string Name { get; init; }
}