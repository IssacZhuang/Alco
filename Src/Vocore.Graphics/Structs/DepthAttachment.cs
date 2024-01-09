namespace Vocore.Graphics;

public struct DepthAttachment
{
    public DepthAttachment(GPUTexture texture, float clearDepth = 1.0f, uint clearStencil = 0)
    {
        Texture = texture;
        ClearDepth = clearDepth;
        ClearStencil = clearStencil;
    }

    public GPUTexture Texture { get; set; }
    public float ClearDepth { get; init; } = 1.0f;
    public uint ClearStencil { get; init; } = 0;
}