using System.Diagnostics.CodeAnalysis;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The engine's shared sampler bank, owned by the <see cref="RenderingSystem"/> —
/// the GPU device only creates raw samplers
/// (<see cref="GPUDevice.CreateSampler"/>); which samplers exist and how they
/// map to shader member names is a rendering-layer policy.
/// <br/>The shader-side counterpart is the <c>_samplers</c> ParameterBlock of
/// <c>alco-rendering-core.slang</c>; every shader importing the core module gets
/// the block reflected into one of its bind groups. The bank is immutable
/// engine-wide state and is never overridable: the bank serves a whole
/// sampler-only bind group as one shared <see cref="GPUResourceGroup"/>
/// (see <see cref="GetSamplerGroup"/>) that every material binds as-is.
/// Custom samplers are a separate concept — a module declares its own sampler
/// member and the material binds it through
/// <c>GraphicsMaterial.SetSampler</c>; they never interact with the bank.
/// <br/>Thread safety: the samplers are created eagerly at construction and the
/// shared group cache is synchronized, so materials on any number of threads
/// resolve the bank concurrently (the GPU device's create APIs are thread safe;
/// only command recording is per context).
/// </summary>
public sealed class SharedSamplers : IDisposable
{
    private readonly GPUDevice _device;

    // Shared, immutable groups binding the bank samplers, matched structurally
    // (ordered binding/type/name triples — no key strings are built or stored).
    // The bank block of every shader has the same structure, so in practice one
    // native group serves all materials and frames, and lookups allocate nothing.
    // Guarded by _groupLock: materials may be constructed on any thread.
    private readonly List<BankGroupCache> _samplerGroups = new();
    private readonly Lock _groupLock = new();

    private sealed class BankGroupCache
    {
        public required (uint Binding, BindingType Type, string Name)[] Entries;
        public required GPUBindGroup Layout;
        public required GPUResourceGroup Group;
    }

    internal SharedSamplers(GPUDevice device)
    {
        _device = device;

        // Eager one-time creation: the bank is engine-wide immutable state, the
        // samplers are tiny, and creating them up front leaves nothing mutable
        // for concurrent readers to race on.
        _linearClamp = device.CreateSampler(new SamplerDescriptor(
            FilterMode.Linear, FilterMode.Linear, FilterMode.Linear,
            AddressMode.ClampToEdge, AddressMode.ClampToEdge, AddressMode.ClampToEdge,
            name: "shared_sampler_linear_clamp"));
        _linearRepeat = device.CreateSampler(new SamplerDescriptor(
            FilterMode.Linear, FilterMode.Linear, FilterMode.Linear,
            AddressMode.Repeat, AddressMode.Repeat, AddressMode.Repeat,
            name: "shared_sampler_linear_repeat"));
        _nearestClamp = device.CreateSampler(new SamplerDescriptor(
            FilterMode.Nearest, FilterMode.Nearest, FilterMode.Nearest,
            AddressMode.ClampToEdge, AddressMode.ClampToEdge, AddressMode.ClampToEdge,
            name: "shared_sampler_nearest_clamp"));
        _nearestRepeat = device.CreateSampler(new SamplerDescriptor(
            FilterMode.Nearest, FilterMode.Nearest, FilterMode.Nearest,
            AddressMode.Repeat, AddressMode.Repeat, AddressMode.Repeat,
            name: "shared_sampler_nearest_repeat"));
        _linearMirrorRepeat = device.CreateSampler(new SamplerDescriptor(
            FilterMode.Linear, FilterMode.Linear, FilterMode.Linear,
            AddressMode.MirrorRepeat, AddressMode.MirrorRepeat, AddressMode.MirrorRepeat,
            name: "shared_sampler_linear_mirror_repeat"));
        _nearestMirrorRepeat = device.CreateSampler(new SamplerDescriptor(
            FilterMode.Nearest, FilterMode.Nearest, FilterMode.Nearest,
            AddressMode.MirrorRepeat, AddressMode.MirrorRepeat, AddressMode.MirrorRepeat,
            name: "shared_sampler_nearest_mirror_repeat"));
        _anisotropicClamp = device.CreateSampler(new SamplerDescriptor(
            FilterMode.Linear, FilterMode.Linear, FilterMode.Linear,
            AddressMode.ClampToEdge, AddressMode.ClampToEdge, AddressMode.ClampToEdge,
            maxAnisotropy: 8,
            name: "shared_sampler_anisotropic_clamp"));
        _anisotropicRepeat = device.CreateSampler(new SamplerDescriptor(
            FilterMode.Linear, FilterMode.Linear, FilterMode.Linear,
            AddressMode.Repeat, AddressMode.Repeat, AddressMode.Repeat,
            maxAnisotropy: 8,
            name: "shared_sampler_anisotropic_repeat"));
        _depthComparison = device.CreateSampler(new SamplerDescriptor(
            FilterMode.Linear, FilterMode.Linear, FilterMode.Linear,
            AddressMode.ClampToEdge, AddressMode.ClampToEdge, AddressMode.ClampToEdge,
            compare: CompareFunction.LessEqual,
            name: "shared_sampler_depth_comparison"));
    }

    private readonly GPUSampler _linearClamp;
    private readonly GPUSampler _linearRepeat;
    private readonly GPUSampler _nearestClamp;
    private readonly GPUSampler _nearestRepeat;
    private readonly GPUSampler _linearMirrorRepeat;
    private readonly GPUSampler _nearestMirrorRepeat;
    private readonly GPUSampler _anisotropicClamp;
    private readonly GPUSampler _anisotropicRepeat;
    private readonly GPUSampler _depthComparison;

    /// <summary>The sampler with linear filtering, clamp-to-edge addressing.</summary>
    public GPUSampler LinearClamp => _linearClamp;

    /// <summary>The sampler with linear filtering, repeat addressing.</summary>
    public GPUSampler LinearRepeat => _linearRepeat;

    /// <summary>The sampler with nearest filtering, clamp-to-edge addressing.</summary>
    public GPUSampler NearestClamp => _nearestClamp;

    /// <summary>The sampler with nearest filtering, repeat addressing.</summary>
    public GPUSampler NearestRepeat => _nearestRepeat;

    /// <summary>The sampler with linear filtering, mirror-repeat addressing.</summary>
    public GPUSampler LinearMirrorRepeat => _linearMirrorRepeat;

    /// <summary>The sampler with nearest filtering, mirror-repeat addressing.</summary>
    public GPUSampler NearestMirrorRepeat => _nearestMirrorRepeat;

    /// <summary>The sampler with 8x anisotropic linear filtering, clamp-to-edge addressing.</summary>
    public GPUSampler AnisotropicClamp => _anisotropicClamp;

    /// <summary>The sampler with 8x anisotropic linear filtering, repeat addressing.</summary>
    public GPUSampler AnisotropicRepeat => _anisotropicRepeat;

    /// <summary>
    /// The comparison sampler for shadow map PCF (linear filtering, clamp to edge,
    /// less-or-equal comparison).
    /// </summary>
    public GPUSampler DepthComparison => _depthComparison;

    /// <summary>
    /// Whether the given shader-side member name is a shared sampler bank member.
    /// A pure name-table probe — used by the parameter set to classify sampler
    /// entries.
    /// </summary>
    public bool IsBankMember(string shaderMemberName) => shaderMemberName is
        "_linearClamp" or "_linearRepeat" or "_nearestClamp" or "_nearestRepeat"
        or "_linearMirrorRepeat" or "_nearestMirrorRepeat" or "_anisotropicClamp"
        or "_anisotropicRepeat" or "_depthComparison";

    /// <summary>
    /// Resolves a bank sampler by its shader-side member name (e.g.
    /// <c>_linearClamp</c>). This is the single name table of the shared sampler
    /// convention; an unknown name means a shader declared a sampler that is
    /// neither a bank member nor material-bound — resolved loudly at bind group
    /// assembly, not silently ignored.
    /// </summary>
    public bool TryGetByName(string shaderMemberName, [NotNullWhen(true)] out GPUSampler? sampler)
    {
        switch (shaderMemberName)
        {
            case "_linearClamp": sampler = _linearClamp; return true;
            case "_linearRepeat": sampler = _linearRepeat; return true;
            case "_nearestClamp": sampler = _nearestClamp; return true;
            case "_nearestRepeat": sampler = _nearestRepeat; return true;
            case "_linearMirrorRepeat": sampler = _linearMirrorRepeat; return true;
            case "_nearestMirrorRepeat": sampler = _nearestMirrorRepeat; return true;
            case "_anisotropicClamp": sampler = _anisotropicClamp; return true;
            case "_anisotropicRepeat": sampler = _anisotropicRepeat; return true;
            case "_depthComparison": sampler = _depthComparison; return true;
            default: sampler = null; return false;
        }
    }

    /// <summary>
    /// Gets the shared, immutable resource group binding the bank samplers for a
    /// reflected sampler-only bind group layout (the <c>_samplers</c> block of the
    /// core module reflected into a shader). The group is created once per
    /// structural layout and shared by every material and frame — bank samplers
    /// are engine-wide constants and are never overridden. Lookups after the
    /// first build allocate nothing (structural compare, no key strings).
    /// <br/>Thread safe: the whole lookup-and-create runs under the group lock —
    /// this is a material-construction path, not a per-frame hot path.
    /// </summary>
    /// <param name="layout">The reflected bind group layout; every entry must be
    /// a bank member.</param>
    /// <exception cref="GraphicsException">The layout declares an entry that is
    /// not a bank member.</exception>
    public GPUResourceGroup GetSamplerGroup(BindGroupLayout layout)
    {
        lock (_groupLock)
        {
            IReadOnlyList<BindGroupEntryInfo> bindings = layout.Bindings;
            for (int i = 0; i < _samplerGroups.Count; i++)
            {
                BankGroupCache cached = _samplerGroups[i];
                if (cached.Entries.Length != bindings.Count)
                {
                    continue;
                }

                bool matches = true;
                for (int e = 0; e < bindings.Count; e++)
                {
                    BindGroupEntry entry = bindings[e].Entry;
                    if (cached.Entries[e].Binding != entry.Binding
                        || cached.Entries[e].Type != entry.Type
                        || !string.Equals(cached.Entries[e].Name, entry.Name, StringComparison.Ordinal))
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches)
                {
                    return cached.Group;
                }
            }

            ResourceBindingEntry[] entries = new ResourceBindingEntry[bindings.Count];
            (uint, BindingType, string)[] structure = new (uint, BindingType, string)[bindings.Count];
            for (int i = 0; i < bindings.Count; i++)
            {
                BindGroupEntry entry = bindings[i].Entry;
                if (!TryGetByName(entry.Name, out GPUSampler? sampler))
                {
                    throw new GraphicsException(
                        $"Bind group entry '{entry.Name}' is not a shared sampler bank member; the bank only builds sampler-only groups of bank members.");
                }
                entries[i] = new ResourceBindingEntry(entry.Binding, sampler);
                structure[i] = (entry.Binding, entry.Type, entry.Name);
            }

            GPUBindGroup bindGroupLayout = _device.CreateBindGroup(layout.ToDescriptor("sampler_bank_layout"));
            GPUResourceGroup group = _device.CreateResourceGroup(
                new ResourceGroupDescriptor(bindGroupLayout, entries, "sampler_bank_group"));
            _samplerGroups.Add(new BankGroupCache
            {
                Entries = structure,
                Layout = bindGroupLayout,
                Group = group,
            });
            return group;
        }
    }

    public void Dispose()
    {
        lock (_groupLock)
        {
            for (int i = 0; i < _samplerGroups.Count; i++)
            {
                _samplerGroups[i].Group.Dispose();
                _samplerGroups[i].Layout.Dispose();
            }
            _samplerGroups.Clear();
        }

        _linearClamp.Dispose();
        _linearRepeat.Dispose();
        _nearestClamp.Dispose();
        _nearestRepeat.Dispose();
        _linearMirrorRepeat.Dispose();
        _nearestMirrorRepeat.Dispose();
        _anisotropicClamp.Dispose();
        _anisotropicRepeat.Dispose();
        _depthComparison.Dispose();
    }
}
