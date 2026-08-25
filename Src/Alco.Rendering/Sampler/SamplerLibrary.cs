using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// One entry of the engine's shared sampler bank: the shader-side member name of
/// <c>alco-rendering-core.slang</c>'s <c>_samplers</c> block and the descriptor it
/// resolves to. Shaders sample through the bank
/// (<c>_texture.Sample(_samplers._linearClamp, uv)</c>) instead of declaring
/// per-texture sampler companions.
/// </summary>
public enum SharedSamplerKind
{
    LinearClamp,
    LinearRepeat,
    NearestClamp,
    NearestRepeat,
    LinearMirrorRepeat,
    NearestMirrorRepeat,
    AnisotropicClamp,
    AnisotropicRepeat,
    DepthComparison,
}

/// <summary>
/// The engine's shared sampler bank, owned and lazily created by the
/// <see cref="RenderingSystem"/> — the GPU device only creates raw samplers
/// (<see cref="GPUDevice.CreateSampler"/>); which samplers exist and how they
/// map to shader member names is a rendering-layer policy.
/// <br/>The shader-side counterpart is the <c>_samplers</c> ParameterBlock of
/// <c>alco-rendering-core.slang</c>; every shader importing the core module gets
/// the block reflected into one of its bind groups, and the parameter set
/// resolves each member entry from this library by name. Materials can override
/// individual members through <c>ShaderParameterSet.SetSampler</c> (custom
/// samplers are an independent resource, never attached to a texture).
/// </summary>
public sealed class SamplerLibrary : IDisposable
{
    private readonly GPUDevice _device;
    private readonly GPUSampler?[] _samplers = new GPUSampler?[(int)SharedSamplerKind.DepthComparison + 1];

    internal SamplerLibrary(GPUDevice device)
    {
        _device = device;
    }

    /// <summary>The sampler with linear filtering, clamp-to-edge addressing.</summary>
    public GPUSampler LinearClamp => GetOrCreate(SharedSamplerKind.LinearClamp);

    /// <summary>The sampler with linear filtering, repeat addressing.</summary>
    public GPUSampler LinearRepeat => GetOrCreate(SharedSamplerKind.LinearRepeat);

    /// <summary>The sampler with nearest filtering, clamp-to-edge addressing.</summary>
    public GPUSampler NearestClamp => GetOrCreate(SharedSamplerKind.NearestClamp);

    /// <summary>The sampler with nearest filtering, repeat addressing.</summary>
    public GPUSampler NearestRepeat => GetOrCreate(SharedSamplerKind.NearestRepeat);

    /// <summary>The sampler with linear filtering, mirror-repeat addressing.</summary>
    public GPUSampler LinearMirrorRepeat => GetOrCreate(SharedSamplerKind.LinearMirrorRepeat);

    /// <summary>The sampler with nearest filtering, mirror-repeat addressing.</summary>
    public GPUSampler NearestMirrorRepeat => GetOrCreate(SharedSamplerKind.NearestMirrorRepeat);

    /// <summary>The sampler with 8x anisotropic linear filtering, clamp-to-edge addressing.</summary>
    public GPUSampler AnisotropicClamp => GetOrCreate(SharedSamplerKind.AnisotropicClamp);

    /// <summary>The sampler with 8x anisotropic linear filtering, repeat addressing.</summary>
    public GPUSampler AnisotropicRepeat => GetOrCreate(SharedSamplerKind.AnisotropicRepeat);

    /// <summary>
    /// The comparison sampler for shadow map PCF (linear filtering, clamp to edge,
    /// less-or-equal comparison).
    /// </summary>
    public GPUSampler DepthComparison => GetOrCreate(SharedSamplerKind.DepthComparison);

    /// <summary>Gets the bank sampler of the given kind.</summary>
    public GPUSampler this[SharedSamplerKind kind] => GetOrCreate(kind);

    /// <summary>
    /// Resolves a bank sampler by its shader-side member name (e.g.
    /// <c>_linearClamp</c>). This is the single name table of the shared sampler
    /// convention; an unknown name means a shader declared a sampler that is
    /// neither a bank member nor material-bound — resolved loudly at bind group
    /// assembly, not silently ignored.
    /// </summary>
    public bool TryGetByName(string shaderMemberName, out GPUSampler? sampler)
    {
        switch (shaderMemberName)
        {
            case "_linearClamp": sampler = LinearClamp; return true;
            case "_linearRepeat": sampler = LinearRepeat; return true;
            case "_nearestClamp": sampler = NearestClamp; return true;
            case "_nearestRepeat": sampler = NearestRepeat; return true;
            case "_linearMirrorRepeat": sampler = LinearMirrorRepeat; return true;
            case "_nearestMirrorRepeat": sampler = NearestMirrorRepeat; return true;
            case "_anisotropicClamp": sampler = AnisotropicClamp; return true;
            case "_anisotropicRepeat": sampler = AnisotropicRepeat; return true;
            case "_depthComparison": sampler = DepthComparison; return true;
            default:
                sampler = null;
                return false;
        }
    }

    private GPUSampler GetOrCreate(SharedSamplerKind kind)
    {
        int index = (int)kind;
        GPUSampler? sampler = _samplers[index];
        if (sampler != null)
        {
            return sampler;
        }

        FilterMode filter = kind is SharedSamplerKind.NearestClamp
            or SharedSamplerKind.NearestRepeat
            or SharedSamplerKind.NearestMirrorRepeat
            ? FilterMode.Nearest
            : FilterMode.Linear;
        AddressMode address = kind is SharedSamplerKind.LinearClamp
            or SharedSamplerKind.NearestClamp
            or SharedSamplerKind.AnisotropicClamp
            or SharedSamplerKind.DepthComparison
            ? AddressMode.ClampToEdge
            : AddressMode.Repeat;
        if (kind is SharedSamplerKind.LinearMirrorRepeat or SharedSamplerKind.NearestMirrorRepeat)
        {
            address = AddressMode.MirrorRepeat;
        }

        ushort anisotropy = kind is SharedSamplerKind.AnisotropicClamp or SharedSamplerKind.AnisotropicRepeat
            ? (ushort)8
            : (ushort)1;

        sampler = kind == SharedSamplerKind.DepthComparison
            ? _device.CreateSampler(new SamplerDescriptor(
                FilterMode.Linear, FilterMode.Linear, FilterMode.Linear,
                AddressMode.ClampToEdge, AddressMode.ClampToEdge, AddressMode.ClampToEdge,
                compare: CompareFunction.LessEqual,
                name: "shared_sampler_depth_comparison"))
            : _device.CreateSampler(new SamplerDescriptor(
                filter, filter, filter,
                address, address, address,
                maxAnisotropy: anisotropy,
                name: $"shared_sampler_{kind.ToString().ToLowerInvariant()}"));

        _samplers[index] = sampler;
        return sampler;
    }

    public void Dispose()
    {
        foreach (GPUSampler? sampler in _samplers)
        {
            sampler?.Dispose();
        }
        Array.Clear(_samplers);
    }
}
