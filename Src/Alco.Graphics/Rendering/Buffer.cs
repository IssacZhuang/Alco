// Example-level buffer: independently owned, nonmoving storage in the rendering system's growable CPU-visible heap pool.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>A fixed-capacity GPU data allocation; writes and destruction require its preceding GPU uses to have completed.</summary>
public sealed unsafe class Buffer : RenderingResource
{
    private readonly HeapAllocation<byte> _allocation;

    internal Buffer(RenderingSystem system, HeapAllocation<byte> allocation) : base(system)
    {
        _allocation = allocation;
    }

    /// <summary>The allocation's capacity in bytes.</summary>
    public ulong SizeBytes => _allocation.Range.SizeBytes;

    /// <summary>The borrowed backing-buffer range; using it directly does not retain this buffer.</summary>
    public GPURange Range
    {
        get
        {
            Debug.Assert(!IsDestroyed);
            return _allocation.Range.GPURange;
        }
    }

    /// <summary>Copies CPU data into the mapped allocation without submitting commands or waiting for GPU work.</summary>
    /// <typeparam name="T">The unmanaged source element type.</typeparam>
    /// <param name="source">The data to copy; the source may be released when this method returns.</param>
    /// <param name="byteOffset">The destination byte offset; the complete source must fit in the allocation.</param>
    public void SetData<T>(ReadOnlySpan<T> source, ulong byteOffset = 0) where T : unmanaged
    {
        Debug.Assert(!IsDestroyed);
        ulong byteCount = (ulong)source.Length * (ulong)sizeof(T);
        Debug.Assert(byteOffset <= SizeBytes && byteCount <= SizeBytes - byteOffset);
        if (source.IsEmpty)
            return;
        fixed (T* data = source)
            NativeMemory.Copy(data, _allocation.Range.Cpu + byteOffset, (nuint)byteCount);
    }

    internal RenderingSystem System => _system;

    /// <summary>The borrowed shader-visible buffer view; using it directly does not retain this buffer.</summary>
    public GPUBufferView View
    {
        get
        {
            Debug.Assert(!IsDestroyed);
            return _allocation.Range.View;
        }
    }

    /// <summary>Returns the owned storage to the rendering system.</summary>
    protected override void Release()
    {
        _system.ReleaseBuffer(_allocation);
    }
}
