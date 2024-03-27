namespace Vocore.Graphics;

public struct IndirectData
{
    public IndirectData(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        VertexCount = vertexCount;
        InstanceCount = instanceCount;
        FirstVertex = firstVertex;
        FirstInstance = firstInstance;
    }
    public uint VertexCount;
    public uint InstanceCount;
    public uint FirstVertex;
    public uint FirstInstance;
}