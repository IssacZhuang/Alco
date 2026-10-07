namespace Alco.Graphics;

/// <summary>
/// Argument record of an indirect indexed draw command, matching the GPU buffer
/// layout (five 32-bit words) consumed by <c>DrawIndexedIndirect</c>.
/// </summary>
public struct IndexedIndirectData
{
    /// <summary>
    /// Initializes the record with the draw arguments.
    /// </summary>
    public IndexedIndirectData(uint indexCount, uint instanceCount, uint firstIndex, uint vertexOffset, uint firstInstance)
    {
        IndexCount = indexCount;
        InstanceCount = instanceCount;
        FirstIndex = firstIndex;
        VertexOffset = vertexOffset;
        FirstInstance = firstInstance;
    }
    /// <summary>
    /// How many indices to draw per instance.
    /// </summary>
    public uint IndexCount;
    /// <summary>
    /// How many instances to draw.
    /// </summary>
    public uint InstanceCount;
    /// <summary>
    /// Index of the first entry in the index buffer.
    /// </summary>
    public uint FirstIndex;
    /// <summary>
    /// Value added to every index fetched from the index buffer.
    /// </summary>
    public uint VertexOffset;
    /// <summary>
    /// Index of the first instance; addressing per-draw data through it requires
    /// <see cref="GPUFeatures.IndirectFirstInstance"/>.
    /// </summary>
    public uint FirstInstance;
}
