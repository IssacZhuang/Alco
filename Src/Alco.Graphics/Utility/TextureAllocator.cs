// Texture placement allocation and explicit allocation-token lifetimes.

using System.Diagnostics;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

/// <summary>A placed texture and its allocator-owned allocation token; default represents no allocation.</summary>
public struct PlacedTexture
{
    /// <summary>The texture, or null when no allocation exists.</summary>
    public GPUTexture? Texture;

    /// <summary>The token returned to the originating allocator when freed.</summary>
    public uint Token;
}

/// <summary>Places textures in caller-owned heap storage with explicit allocation lifetimes.</summary>
public sealed unsafe class TextureAllocator
{
    private readonly GPUDevice _device;
    private GPUTextureHeap _heap;
    private HeapAllocator.RangeAllocator _ranges;

    /// <summary>Creates an allocator over a live texture heap; keep the heap alive until every allocation is freed.</summary>
    /// <param name="device">The heap's device.</param>
    /// <param name="heap">The backing heap.</param>
    /// <param name="maxTextures">The allocation limit, from one through <see cref="HeapAllocator.MaximumAllocationCount"/>.</param>
    public TextureAllocator(GPUDevice device, in GPUTextureHeap heap, uint maxTextures)
    {
        Debug.Assert(heap.Owner != null);
        _device = device;
        _heap = heap;
        _ranges = new HeapAllocator.RangeAllocator(heap.Size, maxTextures, device.Caps.TextureHeapAlignment);
    }

    /// <summary>Allocates an uninitialized texture. Externally synchronize this allocator.</summary>
    /// <param name="desc">The texture description.</param>
    /// <returns>The allocation, or an empty result if size, alignment, or available space is insufficient.</returns>
    /// <exception cref="GPUException">Texture creation failed; its reserved heap range is released.</exception>
    public PlacedTexture Allocate(in TextureDesc desc)
    {
        SizeAlign requirements = _device.GetTextureSizeAlign(desc);
        if (requirements.Size == 0 || requirements.Align > _ranges.ElementSize) return default;
        HeapAllocator.Range range = _ranges.Allocate(requirements.Size);
        if (range.Offset == HeapAllocator.UnusedNode)
            return default;
        try
        {
            return new PlacedTexture
            {
                Texture = _device.CreateTexture(desc, _heap, range.Offset * _ranges.ElementSize),
                Token = range.Token,
            };
        }
        catch (GPUException)
        {
            _ranges.Free(range.Token);
            throw;
        }
    }

    /// <summary>Frees an allocation after its views and GPU uses have finished; an empty allocation is ignored.</summary>
    /// <param name="texture">An allocation from this allocator, reset to default on return.</param>
    public void Free(ref PlacedTexture texture)
    {
        if (texture.Texture == null)
            return;

        _ranges.Free(texture.Token);
        texture.Texture.Destroy();
        texture = default;
    }
}
