using System.Runtime.InteropServices;

namespace Alco.Rendering;

/// <summary>Non-indexed indirect draw argument record matching <c>DrawIndirect</c> (16 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GpuDrawIndirectArgs
{
    /// <summary>Number of vertices to draw per instance.</summary>
    public uint VertexCount;

    /// <summary>Number of instances to draw.</summary>
    public uint InstanceCount;

    /// <summary>Index of the first vertex.</summary>
    public uint FirstVertex;

    /// <summary>Index of the first instance.</summary>
    public uint FirstInstance;
}

/// <summary>Indexed indirect draw argument record matching <c>DrawIndexedIndirect</c> (20 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GpuDrawIndexedIndirectArgs
{
    /// <summary>Number of indices to draw per instance.</summary>
    public uint IndexCount;

    /// <summary>Number of instances to draw.</summary>
    public uint InstanceCount;

    /// <summary>Offset into the index buffer.</summary>
    public uint FirstIndex;

    /// <summary>Constant offset added to each vertex index.</summary>
    public int VertexOffset;

    /// <summary>Index of the first instance.</summary>
    public uint FirstInstance;
}
