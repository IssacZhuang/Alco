namespace Vocore.Graphics;

public struct CommandBufferDescriptor
{
    public CommandBufferDescriptor(string name, bool isRenderCommandReuseable = false)
    {
        Name = name;
        IsRenderCommandReuseable = isRenderCommandReuseable;
    }

    public string Name { get; init; }
    public bool IsRenderCommandReuseable { get; init; }
}