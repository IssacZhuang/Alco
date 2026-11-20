// System-owned shared samplers in fixed bindless heap slots.

using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>A rendering system's immutable sampler bank. Individual samplers are borrowed and must not be destroyed.</summary>
public sealed class SharedSamplers
{
    // The slot layout is shared with the built-in SharedSampler Slang module.
    internal const uint LinearRepeatIndex = 0;
    internal const uint LinearClampIndex = 1;
    internal const uint NearestRepeatIndex = 2;
    internal const uint NearestClampIndex = 3;
    internal const uint AnisotropicRepeatIndex = 4;
    internal const uint AnisotropicClampIndex = 5;
    internal const uint DepthComparisonIndex = 6;
    internal const uint Count = 7;

    private readonly Sampler[] _samplers;

    internal SharedSamplers(RenderingSystem system)
    {
        ReadOnlySpan<SamplerDesc> descriptions =
        [
            new(),
            new() { AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge, AddressW = AddressMode.ClampToEdge },
            new() { MinFilter = Filter.Nearest, MagFilter = Filter.Nearest, MipFilter = Filter.Nearest },
            new()
            {
                MinFilter = Filter.Nearest, MagFilter = Filter.Nearest, MipFilter = Filter.Nearest,
                AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge, AddressW = AddressMode.ClampToEdge,
            },
            new() { Anisotropic = true },
            new() { Anisotropic = true, AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge, AddressW = AddressMode.ClampToEdge },
            new() { CompareEnabled = true, AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge, AddressW = AddressMode.ClampToEdge },
        ];
        Sampler[] samplers = new Sampler[Count];
        int created = 0;
        try
        {
            for (; created < samplers.Length; ++created)
                samplers[created] = system.CreateSampler((uint)created, descriptions[created]);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            for (int index = created - 1; index >= 0; --index)
                samplers[index].Destroy();
            throw;
        }
        _samplers = samplers;
    }

    /// <summary>The linear-filtered repeat sampler, also used as the system default.</summary>
    public Sampler LinearRepeat => _samplers[LinearRepeatIndex];

    /// <summary>The linear-filtered clamp-to-edge sampler.</summary>
    public Sampler LinearClamp => _samplers[LinearClampIndex];

    /// <summary>The nearest-filtered repeat sampler.</summary>
    public Sampler NearestRepeat => _samplers[NearestRepeatIndex];

    /// <summary>The nearest-filtered clamp-to-edge sampler.</summary>
    public Sampler NearestClamp => _samplers[NearestClampIndex];

    /// <summary>The 4x anisotropic repeat sampler.</summary>
    public Sampler AnisotropicRepeat => _samplers[AnisotropicRepeatIndex];

    /// <summary>The 4x anisotropic clamp-to-edge sampler.</summary>
    public Sampler AnisotropicClamp => _samplers[AnisotropicClampIndex];

    /// <summary>The linear-filtered clamp-to-edge sampler comparing depth with less-or-equal.</summary>
    public Sampler DepthComparison => _samplers[DepthComparisonIndex];

    internal void Destroy()
    {
        for (int index = _samplers.Length - 1; index >= 0; --index)
            _samplers[index].Destroy();
    }
}
