// Example-level bindless sampler handle: a sampler description and its descriptor heap index.

using System.Diagnostics;
using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>A sampler descriptor written into the rendering system's bindless sampler heap.</summary>
public sealed class Sampler : RenderingResource
{
    private readonly SamplerDesc _desc;
    private uint _index;

    internal Sampler(RenderingSystem system, in SamplerDesc desc, uint index) : base(system)
    {
        _desc = desc;
        _index = index;
    }

    /// <summary>The sampler description.</summary>
    public SamplerDesc Desc => _desc;

    /// <summary>The sampler descriptor heap index.</summary>
    public uint Index
    {
        get
        {
            Debug.Assert(!IsDisposed);
            return _index;
        }
    }

    /// <summary>The sampler descriptor handle.</summary>
    public GPUSamplerId Id => new(Index);

    /// <summary>Converts the sampler to its descriptor heap index.</summary>
    /// <param name="sampler">The sampler; it must be alive.</param>
    /// <returns>The descriptor heap index.</returns>
    public static implicit operator uint(Sampler sampler)
    {
        Debug.Assert(sampler != null);
        return sampler.Index;
    }

    /// <summary>Releases the descriptor slot.</summary>
    protected override void Release()
    {
        _system.ReleaseSampler(_index);
        _index = Texture.NoIndex;
    }
}
