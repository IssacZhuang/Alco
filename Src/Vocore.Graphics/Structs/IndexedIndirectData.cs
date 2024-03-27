namespace Vocore.Graphics;

public struct IndexedIndirectData
{
    public IndexedIndirectData(uint indexCount, uint instanceCount, uint firstIndex, uint vertexOffset, uint firstInstance)
    {
        IndexCount = indexCount;
        InstanceCount = instanceCount;
        FirstIndex = firstIndex;
        VertexOffset = vertexOffset;
        FirstInstance = firstInstance;
    }
    public uint IndexCount;
    public uint InstanceCount;
    public uint FirstIndex;
    public uint VertexOffset;
    public uint FirstInstance;
}
