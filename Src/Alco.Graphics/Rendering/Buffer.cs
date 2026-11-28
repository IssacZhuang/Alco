// Example-level buffer: independently owned, nonmoving storage in the rendering system's growable CPU-visible heap pool.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>A fixed-capacity GPU data allocation; writes and destruction require its preceding GPU uses to have completed.</summary>
public sealed unsafe class Buffer : RenderingResource
{
    private readonly HeapAllocation<byte> _allocation;
    private readonly MemoryType _memory;
    private readonly bool _externalOwned;

    internal Buffer(RenderingSystem system, HeapAllocation<byte> allocation, MemoryType memory) : base(system)
    {
        _allocation = allocation;
        _memory = memory;
    }

    internal Buffer(RenderingSystem system, GPUAllocation<byte> externalRange, MemoryType memory) : base(system)
    {
        _allocation = new HeapAllocation<byte> { Range = externalRange };
        _memory = memory;
        _externalOwned = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>The backing memory visibility type.</summary>
    public MemoryType Memory => _memory;

    /// <summary>The allocation's capacity in bytes.</summary>
    public ulong SizeBytes => _allocation.Range.SizeBytes;

    internal GPURange LowLevelRange
    {
        get
        {
            Debug.Assert(!IsDisposed);
            return _allocation.Range.GPURange;
        }
    }

    /// <summary>Creates a slice covering this buffer from <paramref name="byteOffset"/> to the end.</summary>
    /// <param name="byteOffset">The byte offset relative to the start of this buffer.</param>
    /// <returns>The bounded buffer slice.</returns>
    public BufferSlice Slice(ulong byteOffset = 0)
    {
        Debug.Assert(!IsDisposed);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(byteOffset, SizeBytes);
        return new BufferSlice(this, byteOffset, SizeBytes - byteOffset);
    }

    /// <summary>Creates a bounded slice of <paramref name="sizeBytes"/> starting at <paramref name="byteOffset"/>.</summary>
    /// <param name="byteOffset">The byte offset relative to the start of this buffer.</param>
    /// <param name="sizeBytes">The slice length in bytes.</param>
    /// <returns>The bounded buffer slice.</returns>
    public BufferSlice Slice(ulong byteOffset, ulong sizeBytes) => new(this, byteOffset, sizeBytes);

    /// <summary>Copies CPU data into the mapped allocation without submitting commands or waiting for GPU work.</summary>
    /// <typeparam name="T">The unmanaged source element type.</typeparam>
    /// <param name="source">The data to copy; the source may be released when this method returns.</param>
    /// <param name="byteOffset">The destination byte offset; the complete source must fit in the allocation.</param>
    public void SetData<T>(ReadOnlySpan<T> source, ulong byteOffset = 0) where T : unmanaged
    {
        Debug.Assert(!IsDisposed);
        if (_allocation.Range.Cpu == null)
            throw new InvalidOperationException("Cannot write CPU data directly to a GPU-only buffer.");
        ulong byteCount = (ulong)source.Length * (ulong)sizeof(T);
        Debug.Assert(byteOffset <= SizeBytes && byteCount <= SizeBytes - byteOffset);
        if (source.IsEmpty)
            return;
        fixed (T* data = source)
            NativeMemory.Copy(data, _allocation.Range.Cpu + byteOffset, (nuint)byteCount);
    }

    /// <summary>Copies mapped buffer data into CPU memory without submitting commands or waiting for GPU work.</summary>
    /// <typeparam name="T">The unmanaged destination element type.</typeparam>
    /// <param name="destination">The span receiving the copied bytes.</param>
    /// <param name="byteOffset">The source byte offset; the complete destination must fit in the allocation.</param>
    public void GetData<T>(Span<T> destination, ulong byteOffset = 0) where T : unmanaged
    {
        Debug.Assert(!IsDisposed);
        if (_allocation.Range.Cpu == null)
            throw new InvalidOperationException("Cannot read CPU data directly from a GPU-only buffer.");
        ArgumentOutOfRangeException.ThrowIfGreaterThan(byteOffset, SizeBytes);
        ulong byteCount = (ulong)destination.Length * (ulong)sizeof(T);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(byteCount, SizeBytes - byteOffset);
        if (destination.IsEmpty)
            return;
        fixed (T* data = destination)
            NativeMemory.Copy(_allocation.Range.Cpu + byteOffset, data, (nuint)byteCount);
    }

    internal RenderingSystem System => _system;

    /// <summary>The borrowed shader-visible buffer view; using it directly does not retain this buffer.</summary>
    public GPUBufferView View
    {
        get
        {
            Debug.Assert(!IsDisposed);
            return _allocation.Range.View;
        }
    }

    /// <summary>Gets a shader-visible buffer view offset by <paramref name="byteOffset"/> bytes.</summary>
    /// <param name="byteOffset">The byte offset within this buffer.</param>
    /// <returns>The offset buffer view.</returns>
    public GPUBufferView GetView(ulong byteOffset = 0)
    {
        Debug.Assert(!IsDisposed);
        if (byteOffset > SizeBytes)
            throw new ArgumentOutOfRangeException(nameof(byteOffset), byteOffset, "Byte offset exceeds buffer capacity.");
        return _allocation.Range.GPURange.Slice(byteOffset).View;
    }

    /// <summary>Retires this buffer for GPU-safe deferred reclamation once all prior GPU uses complete.</summary>
    /// <exception cref="InvalidOperationException">Called on a transient frame arena buffer managed by <see cref="RenderContext"/>.</exception>
    public override void Dispose()
    {
        if (_externalOwned)
            throw new InvalidOperationException("Frame arena buffers are managed by RenderContext and cannot be disposed directly.");
        base.Dispose();
    }

    /// <summary>Returns the owned storage to the rendering system.</summary>
    protected override void Release()
    {
        if (!_externalOwned)
            _system.ReleaseBuffer(_allocation, _memory);
    }
}
