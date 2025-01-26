namespace Alco.Graphics;

public struct CommandBufferDescriptor
{
    public CommandBufferDescriptor(string name)
    {
        Name = name;
    }

    public string Name { get; init; }
}