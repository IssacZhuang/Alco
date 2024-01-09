namespace Vocore.Graphics;

public struct BlendComponent
{
    public BlendComponent(BlendFactor source, BlendFactor destination, BlendOperation operation)
    {
        SrcFactor = source;
        DstFactor = destination;
        Operation = operation;
    }
    public BlendFactor SrcFactor { get; set; }
    public BlendFactor DstFactor { get; set; }
    public BlendOperation Operation { get; set; }
}