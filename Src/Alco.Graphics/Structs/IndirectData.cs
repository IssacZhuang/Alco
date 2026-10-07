namespace Alco.Graphics;

/// <summary>
/// Argument record of an indirect non-indexed draw command, matching the GPU
/// buffer layout (four 32-bit words) consumed by <c>DrawIndirect</c>.
/// </summary>
public struct IndirectData
{
    /// <summary>
    /// Initializes the record with the draw arguments.
    /// </summary>
    public IndirectData(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        VertexCount = vertexCount;
        InstanceCount = instanceCount;
        FirstVertex = firstVertex;
        FirstInstance = firstInstance;
    }
    /// <summary>
    /// How many vertices to draw per instance.
    /// </summary>
    public uint VertexCount;
    /// <summary>
    /// How many instances to draw.
    /// </summary>
    public uint InstanceCount;
    /// <summary>
    /// Index of the first vertex in the vertex buffers.
    /// </summary>
    public uint FirstVertex;
    /// <summary>
    /// Index of the first instance; addressing per-draw data through it requires
    /// <see cref="GPUFeatures.IndirectFirstInstance"/>.
    /// </summary>
    public uint FirstInstance;
}