// Example-level shader handle: automatic stage inference, template/interface composition, lazy specialization variants, and shared pipelines.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Alco.Graphics.LowLevel;
using Alco.Graphics.Slang;

namespace Alco.Graphics;

/// <summary>The pipeline kind of a shader's selected entry points.</summary>
public enum ShaderKind
{
    /// <summary>Vertex and optional pixel stages.</summary>
    Graphics,

    /// <summary>One compute stage.</summary>
    Compute,

    /// <summary>Mesh with optional task and pixel stages.</summary>
    Mesh,
}

/// <summary>The bindless kind of one root field, derived from its reflected type.</summary>
public enum MaterialFieldKind
{
    /// <summary>An ordinary data field written through <see cref="Material.SetData{T}"/>.</summary>
    Data,

    /// <summary>A shader <c>GPUBufferView</c> field written through <see cref="Material.SetBuffer"/>.</summary>
    Buffer,

    /// <summary>A texture descriptor index field (<c>GPUTextureId</c>).</summary>
    Texture,

    /// <summary>A sampler descriptor index field (<c>GPUSamplerId</c>).</summary>
    Sampler,
}

/// <summary>One reflected root field with its uniform offset and size.</summary>
public sealed class RootField
{
    /// <summary>The field's declared name.</summary>
    public required string Name { get; init; }

    /// <summary>The byte offset within the root struct.</summary>
    public required uint Offset { get; init; }

    /// <summary>The field size in bytes.</summary>
    public required uint SizeBytes { get; init; }

    /// <summary>The bindless field kind.</summary>
    public required MaterialFieldKind Kind { get; init; }
}

/// <summary>The reflected layout of one shader's GPU_ROOT struct.</summary>
public sealed class RootLayout
{
    private readonly RootField[] _fields;
    private readonly RootField[] _leafFields;
    private readonly ShaderRootLayout? _sourceLayout;

    /// <summary>Creates a layout over the reflected fields.</summary>
    /// <param name="sizeBytes">The root struct size in bytes.</param>
    /// <param name="fields">The fields in declaration order.</param>
    public RootLayout(uint sizeBytes, RootField[] fields)
    {
        SizeBytes = sizeBytes;
        _fields = fields;
        _leafFields = fields;
    }

    internal RootLayout(ShaderRootLayout source)
    {
        _sourceLayout = source;
        SizeBytes = source.SizeBytes;
        _fields = new RootField[source.Fields.Count];
        for (int index = 0; index < _fields.Length; index++)
            _fields[index] = ToRootField(source.Fields[index]);
        List<RootField> leaves = new(source.Fields.Count);
        CollectLeafFields(source.Fields, prefix: null, baseOffset: 0, leaves);
        _leafFields = leaves.ToArray();
    }

    /// <summary>The root struct size in bytes.</summary>
    public uint SizeBytes { get; }

    /// <summary>The top-level fields in declaration order.</summary>
    public ReadOnlySpan<RootField> Fields => _fields;

    /// <summary>All leaf fields (flattening nested structs with dotted names and cumulative offsets).</summary>
    internal ReadOnlySpan<RootField> LeafFields => _leafFields;

    /// <summary>Finds a field by name or dotted nested path.</summary>
    /// <param name="name">The field name or dot-separated nested path.</param>
    /// <returns>The field, or null when the root declares no such name.</returns>
    public RootField? Find(string name)
    {
        foreach (RootField field in _fields)
        {
            if (field.Name == name)
                return field;
        }
        if (_sourceLayout != null && name.Contains('.'))
        {
            foreach (RootField leaf in _leafFields)
            {
                if (leaf.Name == name)
                    return leaf;
            }
            ShaderFieldLayout? nested = _sourceLayout.Find(name);
            if (nested != null)
                return ToRootField(nested);
        }
        return null;
    }

    /// <summary>Gets a strongly-typed handle for root field <paramref name="name"/>, validating size and kind against <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The unmanaged type matching the shader field layout.</typeparam>
    /// <param name="name">The field name or dot-separated nested path.</param>
    /// <returns>The validated typed field handle.</returns>
    /// <exception cref="InvalidOperationException">The field is missing, out of bounds, or mismatches <typeparamref name="T"/>.</exception>
    public RootField<T> GetField<T>(string name) where T : unmanaged
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        RootField found = Find(name)
            ?? throw new InvalidOperationException($"Root layout does not declare field '{name}'.");
        uint expectedSize = (uint)Unsafe.SizeOf<T>();
        if (found.SizeBytes != expectedSize || found.Offset + found.SizeBytes > SizeBytes)
        {
            throw new InvalidOperationException(
                $"Root field '{name}' has size {found.SizeBytes} at offset {found.Offset} (root {SizeBytes}B), expected {expectedSize}B for '{typeof(T).Name}'.");
        }
        MaterialFieldKind expectedKind = ExpectedKind<T>();
        if (found.Kind != expectedKind)
        {
            throw new InvalidOperationException(
                $"Root field '{name}' has kind {found.Kind}, expected {expectedKind} for '{typeof(T).Name}'.");
        }
        return new RootField<T>(found.Offset, found.SizeBytes);
    }

    /// <summary>Attempts to resolve a strongly-typed handle for root field <paramref name="name"/>.</summary>
    /// <typeparam name="T">The unmanaged type matching the shader field layout.</typeparam>
    /// <param name="name">The field name or dot-separated nested path.</param>
    /// <param name="field">The resolved field handle when compatible; otherwise default.</param>
    /// <returns><see langword="true"/> when found with matching size and kind.</returns>
    public bool TryGetField<T>(string name, out RootField<T> field) where T : unmanaged
    {
        if (string.IsNullOrEmpty(name))
        {
            field = default;
            return false;
        }
        RootField? found = Find(name);
        uint expectedSize = (uint)Unsafe.SizeOf<T>();
        if (found == null || found.SizeBytes != expectedSize || found.Offset + found.SizeBytes > SizeBytes ||
            found.Kind != ExpectedKind<T>())
        {
            field = default;
            return false;
        }
        field = new RootField<T>(found.Offset, found.SizeBytes);
        return true;
    }

    private static MaterialFieldKind ExpectedKind<T>() where T : unmanaged
    {
        if (typeof(T) == typeof(GPUBufferView))
            return MaterialFieldKind.Buffer;
        if (typeof(T) == typeof(GPUTextureId))
            return MaterialFieldKind.Texture;
        if (typeof(T) == typeof(GPUSamplerId))
            return MaterialFieldKind.Sampler;
        return MaterialFieldKind.Data;
    }

    internal RootField? FindCompatibleLeaf(RootField field)
    {
        foreach (RootField candidate in _leafFields)
        {
            if (candidate.Name == field.Name && candidate.Kind == field.Kind && candidate.SizeBytes == field.SizeBytes)
                return candidate;
        }
        return null;
    }

    internal bool MatchesLayout(RootLayout other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (SizeBytes != other.SizeBytes || _leafFields.Length != other._leafFields.Length)
            return false;
        for (int index = 0; index < _leafFields.Length; index++)
        {
            RootField left = _leafFields[index];
            RootField right = other._leafFields[index];
            if (left.Offset != right.Offset || left.SizeBytes != right.SizeBytes || left.Kind != right.Kind || left.Name != right.Name)
                return false;
        }
        return true;
    }

    private static void CollectLeafFields(IReadOnlyList<ShaderFieldLayout> fields, string? prefix, uint baseOffset, List<RootField> destination)
    {
        for (int index = 0; index < fields.Count; index++)
        {
            ShaderFieldLayout field = fields[index];
            string fullName = prefix == null ? field.Name : $"{prefix}.{field.Name}";
            uint offset = baseOffset + field.OffsetBytes;
            if (field.Semantic == ShaderFieldSemantic.Data && field.Type.Kind == ShaderTypeKind.Struct && field.Type.Fields.Count > 0)
            {
                CollectLeafFields(field.Type.Fields, fullName, offset, destination);
            }
            else
            {
                destination.Add(new RootField
                {
                    Name = fullName,
                    Offset = offset,
                    SizeBytes = field.SizeBytes,
                    Kind = ToFieldKind(field.Semantic),
                });
            }
        }
    }

    private static RootField ToRootField(ShaderFieldLayout field) => new()
    {
        Name = field.Name,
        Offset = field.OffsetBytes,
        SizeBytes = field.SizeBytes,
        Kind = ToFieldKind(field.Semantic),
    };

    private static MaterialFieldKind ToFieldKind(ShaderFieldSemantic semantic) => semantic switch
    {
        ShaderFieldSemantic.BufferView => MaterialFieldKind.Buffer,
        ShaderFieldSemantic.TextureId => MaterialFieldKind.Texture,
        ShaderFieldSemantic.SamplerId => MaterialFieldKind.Sampler,
        _ => MaterialFieldKind.Data,
    };
}

/// <summary>Blend and channel write-mask settings applied to a single color attachment.</summary>
public record struct ColorTargetBlendState
{
    /// <summary>The blend state applied to the color attachment.</summary>
    public BlendState Blend;

    /// <summary>The RGBA color write mask applied to the color attachment.</summary>
    public byte WriteMask;

    /// <summary>Initializes an opaque, all-channels-writable attachment state.</summary>
    public ColorTargetBlendState()
    {
        Blend = BlendState.Opaque;
        WriteMask = 0xf;
    }
}

/// <summary>Inline fixed-capacity storage for per-attachment blend and write-mask overrides.</summary>
[InlineArray((int)Backend.MaxColorAttachments)]
public struct ColorTargetBlendArray : IEquatable<ColorTargetBlendArray>
{
    private ColorTargetBlendState _element0;

    /// <inheritdoc />
    public readonly bool Equals(ColorTargetBlendArray other)
    {
        for (int index = 0; index < (int)Backend.MaxColorAttachments; ++index)
        {
            if (this[index] != other[index])
                return false;
        }
        return true;
    }

    /// <inheritdoc />
    public override readonly bool Equals(object? obj) => obj is ColorTargetBlendArray other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode()
    {
        HashCode hash = default;
        for (int index = 0; index < (int)Backend.MaxColorAttachments; ++index)
            hash.Add(this[index]);
        return hash.ToHashCode();
    }

    /// <summary>Compares two inline attachment arrays element-by-element.</summary>
    public static bool operator ==(ColorTargetBlendArray left, ColorTargetBlendArray right) => left.Equals(right);

    /// <summary>Compares two inline attachment arrays for inequality.</summary>
    public static bool operator !=(ColorTargetBlendArray left, ColorTargetBlendArray right) => !left.Equals(right);
}

/// <summary>The pipeline-defining state of a material: per-target blend, write mask, rasterization, and topology.</summary>
public record struct PipelineState
{
    /// <summary>The blend state applied to every color target unless overridden per target.</summary>
    public BlendState Blend;

    /// <summary>The color write mask applied to every color target unless overridden per target.</summary>
    public byte WriteMask;

    /// <summary>The rasterizer configuration.</summary>
    public RasterizationState Rasterization;

    /// <summary>The vertex assembly topology.</summary>
    public PrimitiveTopology Topology;

    /// <summary>Bitmask of color attachment indices whose blend or write mask overrides the shared default.</summary>
    public byte TargetOverrideMask;

    /// <summary>Per-attachment blend and write-mask overrides indexed by color target slot.</summary>
    public ColorTargetBlendArray TargetOverrides;

    /// <summary>Initializes a triangle-list state with disabled blending and all color channels writable.</summary>
    public PipelineState()
    {
        Blend = new BlendState();
        WriteMask = 0xf;
        Rasterization = new RasterizationState();
        Topology = PrimitiveTopology.TriangleList;
        TargetOverrideMask = 0;
        TargetOverrides = default;
    }

    /// <summary>Sets an independent blend state and write mask for color target <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based color attachment index.</param>
    /// <param name="blend">The blend state for the target.</param>
    /// <param name="writeMask">The RGBA channel write mask for the target.</param>
    public void SetColorTarget(int index, BlendState blend, byte writeMask = 0xf)
    {
        if ((uint)index >= Backend.MaxColorAttachments)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Color target index must be in [0, {Backend.MaxColorAttachments - 1}].");
        TargetOverrideMask |= (byte)(1 << index);
        TargetOverrides[index] = new ColorTargetBlendState { Blend = blend, WriteMask = writeMask };
    }

    /// <summary>Clears all per-target overrides so every target uses <see cref="Blend"/> and <see cref="WriteMask"/>.</summary>
    public void ClearColorTargetOverrides()
    {
        TargetOverrideMask = 0;
        TargetOverrides = default;
    }

    /// <summary>Resolves the effective blend state and write mask for color target <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based color attachment index.</param>
    /// <returns>The overridden state if present; otherwise the shared <see cref="Blend"/> and <see cref="WriteMask"/>.</returns>
    public readonly ColorTargetBlendState ResolveColorTarget(int index)
    {
        if ((uint)index >= Backend.MaxColorAttachments)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Color target index must be in [0, {Backend.MaxColorAttachments - 1}].");
        if ((TargetOverrideMask & (1 << index)) != 0)
            return TargetOverrides[index];
        return new ColorTargetBlendState { Blend = Blend, WriteMask = WriteMask };
    }

    /// <inheritdoc />
    public readonly bool Equals(PipelineState other)
    {
        if (Blend != other.Blend || WriteMask != other.WriteMask ||
            Rasterization != other.Rasterization || Topology != other.Topology ||
            TargetOverrideMask != other.TargetOverrideMask)
            return false;

        byte mask = TargetOverrideMask;
        while (mask != 0)
        {
            int index = BitOperations.TrailingZeroCount(mask);
            if (TargetOverrides[index] != other.TargetOverrides[index])
                return false;
            mask &= (byte)(mask - 1);
        }
        return true;
    }

    /// <inheritdoc />
    public override readonly int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(Blend);
        hash.Add(WriteMask);
        hash.Add(Rasterization);
        hash.Add(Topology);
        hash.Add(TargetOverrideMask);
        byte mask = TargetOverrideMask;
        while (mask != 0)
        {
            int index = BitOperations.TrailingZeroCount(mask);
            hash.Add(TargetOverrides[index]);
            mask &= (byte)(mask - 1);
        }
        return hash.ToHashCode();
    }

    internal readonly bool Matches(PipelineState state, ShaderKind kind)
    {
        if (Blend != state.Blend || WriteMask != state.WriteMask ||
            Rasterization != state.Rasterization ||
            (kind == ShaderKind.Graphics && Topology != state.Topology) ||
            TargetOverrideMask != state.TargetOverrideMask)
            return false;

        byte mask = TargetOverrideMask;
        while (mask != 0)
        {
            int index = BitOperations.TrailingZeroCount(mask);
            if (TargetOverrides[index] != state.TargetOverrides[index])
                return false;
            mask &= (byte)(mask - 1);
        }
        return true;
    }
}

[InlineArray((int)Backend.MaxColorAttachments)]
internal struct TargetWordArray
{
    private ulong _element0;
}

internal readonly struct PipelineStateKey : IEquatable<PipelineStateKey>
{
    internal readonly ulong Word0;
    internal readonly ulong Word1;
    internal readonly ulong Word2;
    internal readonly byte TargetOverrideMask;
    internal readonly TargetWordArray TargetWords;

    internal PipelineStateKey(in PipelineState state, ShaderKind kind)
    {
        Word0 = PackTargetWord(state.Blend, state.WriteMask);

        byte topology = kind == ShaderKind.Graphics ? (byte)state.Topology : (byte)0;
        ulong w1 = topology;
        w1 |= (ulong)(byte)state.Rasterization.Cull << 8;
        w1 |= (ulong)(byte)state.Rasterization.Fill << 16;
        w1 |= (ulong)CanonicalFloatBits(state.Rasterization.DepthBiasConstant) << 32;
        Word1 = w1;

        ulong w2 = CanonicalFloatBits(state.Rasterization.DepthBiasClamp);
        w2 |= (ulong)CanonicalFloatBits(state.Rasterization.DepthBiasSlope) << 32;
        Word2 = w2;

        TargetOverrideMask = state.TargetOverrideMask;
        TargetWordArray words = default;
        byte mask = state.TargetOverrideMask;
        while (mask != 0)
        {
            int index = BitOperations.TrailingZeroCount(mask);
            ColorTargetBlendState target = state.TargetOverrides[index];
            words[index] = PackTargetWord(target.Blend, target.WriteMask);
            mask &= (byte)(mask - 1);
        }
        TargetWords = words;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong PackTargetWord(BlendState blend, byte writeMask)
    {
        ulong word = blend.Enabled ? 1ul : 0ul;
        word |= (ulong)(byte)blend.Color.Source << 8;
        word |= (ulong)(byte)blend.Color.Destination << 16;
        word |= (ulong)(byte)blend.Color.Operation << 24;
        word |= (ulong)(byte)blend.Alpha.Source << 32;
        word |= (ulong)(byte)blend.Alpha.Destination << 40;
        word |= (ulong)(byte)blend.Alpha.Operation << 48;
        word |= (ulong)writeMask << 56;
        return word;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(PipelineStateKey other)
    {
        if (Word0 != other.Word0 || Word1 != other.Word1 || Word2 != other.Word2 || TargetOverrideMask != other.TargetOverrideMask)
            return false;
        byte mask = TargetOverrideMask;
        while (mask != 0)
        {
            int index = BitOperations.TrailingZeroCount(mask);
            if (TargetWords[index] != other.TargetWords[index])
                return false;
            mask &= (byte)(mask - 1);
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is PipelineStateKey other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(Word0);
        hash.Add(Word1);
        hash.Add(Word2);
        hash.Add(TargetOverrideMask);
        byte mask = TargetOverrideMask;
        while (mask != 0)
        {
            int index = BitOperations.TrailingZeroCount(mask);
            hash.Add(TargetWords[index]);
            mask &= (byte)(mask - 1);
        }
        return hash.ToHashCode();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint CanonicalFloatBits(float value)
    {
        if (value == 0.0f)
            return 0u;
        if (float.IsNaN(value))
            return 0x7FC00000u;
        return BitConverter.SingleToUInt32Bits(value);
    }
}

/// <summary>One compiled specialization variant of a <see cref="Shader"/>, owning its reflected root layout and GPU pipelines.</summary>
internal sealed class ShaderVariant
{
    private readonly Shader _owner;
    private readonly CompiledShaderStage? _primary;
    private readonly CompiledShaderStage? _task;
    private readonly CompiledShaderStage? _pixel;
    private readonly GPUPipeline? _computePipeline;
    private readonly object _pipelineSync = new();
    private PipelineTable _pipelineTable = PipelineTable.Empty;
    private List<InFlightPipeline>? _inFlight;
    private int _activePipelineOperations;
    private bool _destroyed;

    internal ShaderVariant(Shader owner, CompiledShader compiled, ShaderKind kind, string[] specializations)
    {
        _owner = owner;
        Compiled = compiled;
        Specializations = Array.AsReadOnly(specializations);
        ShaderRootLayout root = compiled.Reflection.Root
            ?? throw new InvalidOperationException($"Shader '{owner.Name}' declares no GPU_ROOT root struct.");
        Root = new RootLayout(root);

        if (kind == ShaderKind.Compute)
        {
            CompiledShaderStage compute = compiled.GetStage(ShaderStageKind.Compute);
            _computePipeline = owner.System.Device.CreateComputePipeline(compute.ToGpuStage());
        }
        else if (kind == ShaderKind.Mesh)
        {
            _primary = compiled.GetStage(ShaderStageKind.Mesh);
            _task = FindUniqueOptionalStage(compiled, ShaderStageKind.Task);
            _pixel = FindUniqueOptionalStage(compiled, ShaderStageKind.Pixel);
        }
        else if (kind == ShaderKind.Graphics)
        {
            _primary = compiled.GetStage(ShaderStageKind.Vertex);
            _pixel = FindUniqueOptionalStage(compiled, ShaderStageKind.Pixel);
        }
        else
            throw new ArgumentOutOfRangeException(nameof(kind));
    }

    internal CompiledShader Compiled { get; }
    internal RootLayout Root { get; }
    internal IReadOnlyList<string> Specializations { get; }
    internal string OwnerName => _owner.Name;

    internal GPUPipeline GetComputePipeline()
    {
        return _computePipeline ?? throw new InvalidOperationException($"Shader '{_owner.Name}' is not a compute shader.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal GPUPipeline? TryGetCachedPipeline(in RenderPassSignature pass, in PipelineStateKey stateKey)
    {
        PipelineTable table = Volatile.Read(ref _pipelineTable);
        PipelineSlot[] slots = table.Slots;
        int mask = table.Mask;
        ulong hash = ComputeKeyHash(in pass, in stateKey);
        int index = (int)hash & mask;
        while (true)
        {
            ref readonly PipelineSlot slot = ref slots[index];
            GPUPipeline? pipeline = slot.Pipeline;
            if (pipeline == null)
                return null;
            if (slot.Hash == hash && slot.Pass.Equals(pass) && slot.StateKey.Equals(stateKey))
                return pipeline;
            index = (index + 1) & mask;
        }
    }

    internal GPUPipeline GetPipeline(in PipelineState state, in PipelineStateKey stateKey, in RenderPassSignature pass, out bool created)
    {
        if (_owner.Kind == ShaderKind.Compute)
            throw new InvalidOperationException($"Compute shader '{_owner.Name}' has no raster pipeline.");
        GPUPipeline? cached = TryGetCachedPipeline(in pass, in stateKey);
        if (cached != null)
        {
            created = false;
            return cached;
        }
        return GetOrCreatePipelineSlow(in state, in stateKey, in pass, allowDestroyedReturnNull: false, out created)!;
    }

    internal void WarmUpPipelineCore(in PipelineState state, in PipelineStateKey stateKey, in RenderPassSignature pass,
        DepthStencilState depthStencil, bool needsDepthStencilVariants)
    {
        if (!needsDepthStencilVariants)
        {
            GetOrCreatePipelineSlow(in state, in stateKey, in pass, allowDestroyedReturnNull: true, out _);
            return;
        }

        // A shader may be destroyed before its rendering system drains the queue.
        lock (_pipelineSync)
        {
            if (_destroyed)
                return;
            ++_activePipelineOperations;
        }
        try
        {
            if (GetOrCreatePipelineSlow(in state, in stateKey, in pass, allowDestroyedReturnNull: true, out _) is D3D12Pso pipeline)
                pipeline.PrepareDepthStencilVariants(depthStencil);
        }
        finally
        {
            lock (_pipelineSync)
                EndPipelineOperation();
        }
    }

    internal void Destroy()
    {
        PipelineTable table;
        lock (_pipelineSync)
        {
            _destroyed = true;
            while (_activePipelineOperations > 0)
                Monitor.Wait(_pipelineSync);
            table = _pipelineTable;
            Volatile.Write(ref _pipelineTable, PipelineTable.Empty);
        }

        _computePipeline?.Destroy();
        PipelineSlot[] slots = table.Slots;
        for (int index = 0; index < slots.Length; ++index)
            slots[index].Pipeline?.Destroy();
    }

    private GPUPipeline? GetOrCreatePipelineSlow(in PipelineState state, in PipelineStateKey stateKey,
        in RenderPassSignature pass, bool allowDestroyedReturnNull, out bool created)
    {
        created = false;
        InFlightPipeline? existingInFlight = null;
        InFlightPipeline? ownedInFlight = null;

        lock (_pipelineSync)
        {
            GPUPipeline? cached = TryGetCachedPipeline(in pass, in stateKey);
            if (cached != null)
                return cached;
            if (_destroyed)
            {
                if (allowDestroyedReturnNull)
                    return null;
                throw new ObjectDisposedException(nameof(Shader), $"Shader '{_owner.Name}' has been destroyed.");
            }

            if (_inFlight != null)
            {
                for (int index = 0; index < _inFlight.Count; ++index)
                {
                    InFlightPipeline candidate = _inFlight[index];
                    if (candidate.Pass.Equals(pass) && candidate.StateKey.Equals(stateKey))
                    {
                        existingInFlight = candidate;
                        break;
                    }
                }
            }

            if (existingInFlight == null)
            {
                ownedInFlight = new InFlightPipeline(in pass, in stateKey);
                _inFlight ??= [];
                _inFlight.Add(ownedInFlight);
                ++_activePipelineOperations;
            }
        }

        if (existingInFlight != null)
        {
            GPUPipeline? awaited = existingInFlight.Completion.Task.GetAwaiter().GetResult();
            if (awaited == null && !allowDestroyedReturnNull)
                throw new ObjectDisposedException(nameof(Shader), $"Shader '{_owner.Name}' has been destroyed.");
            return awaited;
        }

        GPUPipeline? createdPipeline = null;
        GPUPipeline? publishedPipeline = null;
        Exception? failure = null;
        try
        {
            createdPipeline = CreateNativePipeline(in state, in pass);
            lock (_pipelineSync)
            {
                if (!_destroyed)
                {
                    ulong hash = ComputeKeyHash(in pass, in stateKey);
                    PipelineTable updated = InsertEntry(_pipelineTable, hash, in pass, in stateKey, createdPipeline);
                    Volatile.Write(ref _pipelineTable, updated);
                    publishedPipeline = createdPipeline;
                    created = true;
                }
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            try
            {
                if (publishedPipeline == null)
                    createdPipeline?.Destroy();
            }
            finally
            {
                lock (_pipelineSync)
                {
                    _inFlight!.Remove(ownedInFlight!);
                    if (failure != null)
                    {
                        ownedInFlight!.Completion.SetException(failure);
                        _ = ownedInFlight.Completion.Task.Exception;
                    }
                    else
                    {
                        ownedInFlight!.Completion.SetResult(publishedPipeline);
                    }
                    EndPipelineOperation();
                }
            }
        }

        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();

        if (publishedPipeline == null && !allowDestroyedReturnNull)
            throw new ObjectDisposedException(nameof(Shader), $"Shader '{_owner.Name}' has been destroyed.");
        return publishedPipeline;
    }

    private void EndPipelineOperation()
    {
        if (--_activePipelineOperations == 0)
            Monitor.PulseAll(_pipelineSync);
    }

    private GPUPipeline CreateNativePipeline(in PipelineState state, in RenderPassSignature pass)
    {
        int colorCount = pass.ColorCount;
        Span<ColorTargetDesc> targetStorage = stackalloc ColorTargetDesc[(int)Backend.MaxColorAttachments];
        Span<ColorTargetDesc> targets = targetStorage[..colorCount];
        for (int index = 0; index < colorCount; ++index)
        {
            ColorTargetBlendState resolved = state.ResolveColorTarget(index);
            targets[index] = new ColorTargetDesc
            {
                Format = pass.GetColorFormat(index),
                Blend = resolved.Blend,
                WriteMask = resolved.WriteMask,
            };
        }

        return _owner.Kind == ShaderKind.Mesh
            ? _owner.System.Device.CreateMeshPipeline(new MeshPipelineDesc
            {
                Task = _task != null ? _task.ToGpuStage() : default,
                Mesh = _primary!.ToGpuStage(),
                Pixel = _pixel != null ? _pixel.ToGpuStage() : default,
                ColorTargets = targets,
                DepthFormat = pass.DepthFormat,
                StencilFormat = pass.StencilFormat,
                Rasterization = state.Rasterization,
                SampleCount = pass.SampleCount,
            })
            : _owner.System.Device.CreateGraphicsPipeline(new GraphicsPipelineDesc
            {
                Vertex = _primary!.ToGpuStage(),
                Pixel = _pixel != null ? _pixel.ToGpuStage() : default,
                ColorTargets = targets,
                DepthFormat = pass.DepthFormat,
                StencilFormat = pass.StencilFormat,
                Rasterization = state.Rasterization,
                SampleCount = pass.SampleCount,
                Topology = state.Topology,
            });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ComputeKeyHash(in RenderPassSignature pass, in PipelineStateKey stateKey)
    {
        ulong hash = pass.ColorFormatsPacked ^ ((ulong)pass.MetaPacked * 0x9E3779B97F4A7C15ul);
        hash = BitOperations.RotateLeft(hash ^ stateKey.Word0, 17) * 0xBF58476D1CE4E5B9ul;
        hash = BitOperations.RotateLeft(hash ^ stateKey.Word1, 23) * 0x94D049BB133111EBul;
        hash ^= stateKey.Word2;
        if (stateKey.TargetOverrideMask != 0)
        {
            hash = BitOperations.RotateLeft(hash ^ stateKey.TargetOverrideMask, 13) * 0xBF58476D1CE4E5B9ul;
            byte mask = stateKey.TargetOverrideMask;
            while (mask != 0)
            {
                int index = BitOperations.TrailingZeroCount(mask);
                hash = BitOperations.RotateLeft(hash ^ stateKey.TargetWords[index], 19) * 0x94D049BB133111EBul;
                mask &= (byte)(mask - 1);
            }
        }
        hash ^= hash >> 30;
        hash *= 0xBF58476D1CE4E5B9ul;
        hash ^= hash >> 27;
        return hash == 0 ? 1ul : hash;
    }

    private static PipelineTable InsertEntry(PipelineTable current, ulong hash, in RenderPassSignature pass,
        in PipelineStateKey stateKey, GPUPipeline pipeline)
    {
        int nextCount = current.Count + 1;
        int capacity = current.Slots.Length < 8 ? 8 : current.Slots.Length;
        if (nextCount * 2 > capacity)
            capacity *= 2;
        PipelineSlot[] nextSlots = new PipelineSlot[capacity];
        int mask = capacity - 1;

        PipelineSlot[] oldSlots = current.Slots;
        for (int index = 0; index < oldSlots.Length; ++index)
        {
            ref readonly PipelineSlot existing = ref oldSlots[index];
            if (existing.Pipeline == null)
                continue;
            int destination = (int)existing.Hash & mask;
            while (nextSlots[destination].Pipeline != null)
                destination = (destination + 1) & mask;
            nextSlots[destination] = existing;
        }

        int target = (int)hash & mask;
        while (nextSlots[target].Pipeline != null)
            target = (target + 1) & mask;
        nextSlots[target] = new PipelineSlot
        {
            Hash = hash,
            Pass = pass,
            StateKey = stateKey,
            Pipeline = pipeline,
        };
        return new PipelineTable(nextSlots, nextCount);
    }

    private static CompiledShaderStage? FindUniqueOptionalStage(CompiledShader compiled, ShaderStageKind stage)
    {
        CompiledShaderStage? match = null;
        for (int index = 0; index < compiled.Stages.Count; ++index)
        {
            if (compiled.Stages[index].Stage != stage)
                continue;
            if (match != null)
                throw new InvalidOperationException($"Shader '{compiled.ModuleName}' has more than one {stage} stage.");
            match = compiled.Stages[index];
        }
        return match;
    }

    private struct PipelineSlot
    {
        public ulong Hash;
        public RenderPassSignature Pass;
        public PipelineStateKey StateKey;
        public GPUPipeline? Pipeline;
    }

    private sealed class PipelineTable
    {
        internal static readonly PipelineTable Empty = new(new PipelineSlot[1], 0);

        internal readonly PipelineSlot[] Slots;
        internal readonly int Mask;
        internal readonly int Count;

        internal PipelineTable(PipelineSlot[] slots, int count)
        {
            Slots = slots;
            Mask = slots.Length - 1;
            Count = count;
        }
    }

    private sealed class InFlightPipeline
    {
        internal readonly RenderPassSignature Pass;
        internal readonly PipelineStateKey StateKey;
        internal readonly TaskCompletionSource<GPUPipeline?> Completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal InFlightPipeline(in RenderPassSignature pass, in PipelineStateKey stateKey)
        {
            Pass = pass;
            StateKey = stateKey;
        }
    }
}

/// <summary>A multi-variant shader handle supporting automatic stage inference, template/interface composition, and lazy specialization.</summary>
public sealed class Shader : RenderingResource
{
    private readonly Func<string[], CompiledShader> _compileVariant;
    private readonly Lock _variantLock = new();
    private VariantEntry[] _variants = [];
    private ShaderVariant? _defaultVariant;
    private bool _released;

    /// <summary>Creates a shader handle for a module, automatically inferring Graphics, Compute, or Mesh when <paramref name="kind"/> is null.</summary>
    /// <param name="system">The rendering system that owns the device.</param>
    /// <param name="compiler">The shader compiler resolving the module; must stay alive while new specializations are compiled.</param>
    /// <param name="module">The module name in the compiler's search paths.</param>
    /// <param name="kind">Optional explicit pipeline kind; null infers automatically from the module's entry points.</param>
    /// <param name="name">The shader name; defaults to the module name.</param>
    public Shader(RenderingSystem system, ShaderCompiler compiler, string module, ShaderKind? kind = null, string name = "") : base(system)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(compiler);
            ArgumentException.ThrowIfNullOrEmpty(module);

            CachedModuleMetadata meta = compiler.GetOrInspectModuleMetadata(module);
            ShaderKind resolvedKind = kind ?? FromPipelineKind(ShaderModule.InferPipelineKind(module, compiler.Target, meta.EntryPoints));
            ShaderPipelineKind pipelineKind = ToPipelineKind(resolvedKind);

            Name = name.Length != 0 ? name : module;
            Kind = resolvedKind;
            ValueSpecializationParameters = CollectValueSpecializationParameters(meta.EntryPoints, resolvedKind);
            _compileVariant = specs => compiler.Compile(module, pipelineKind,
                specs.Length > 0 ? new ShaderCompileOptions { SpecializationArgs = specs } : null);

            if (!HasGenericStages(meta.EntryPoints, resolvedKind))
                _defaultVariant = GetOrCompileVariant([]);
        }
        catch
        {
            GC.SuppressFinalize(this);
            throw;
        }
    }

    /// <summary>Creates a shader handle over a pre-loaded module, automatically inferring the pipeline kind when <paramref name="kind"/> is null.</summary>
    /// <param name="system">The rendering system that owns the device.</param>
    /// <param name="module">The parsed shader module.</param>
    /// <param name="kind">Optional explicit pipeline kind; null infers automatically from the module's entry points.</param>
    /// <param name="name">The shader name; defaults to the module name.</param>
    public Shader(RenderingSystem system, ShaderModule module, ShaderKind? kind = null, string name = "") : base(system)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(module);
            ShaderKind resolvedKind = kind ?? FromPipelineKind(module.InferPipelineKind());
            ShaderPipelineKind pipelineKind = ToPipelineKind(resolvedKind);

            Name = name.Length != 0 ? name : module.Name;
            Kind = resolvedKind;
            ValueSpecializationParameters = CollectValueSpecializationParameters(module.EntryPoints, resolvedKind);
            _compileVariant = specs => module.Compile(pipelineKind,
                specs.Length > 0 ? new ShaderCompileOptions { SpecializationArgs = specs } : null);

            if (!HasGenericStages(module.EntryPoints, resolvedKind))
                _defaultVariant = GetOrCompileVariant([]);
        }
        catch
        {
            GC.SuppressFinalize(this);
            throw;
        }
    }

    private Shader(
        RenderingSystem system,
        string name,
        ShaderKind kind,
        IReadOnlyList<string> valueSpecializationParameters,
        Func<string[], CompiledShader> compileVariant) : base(system)
    {
        Name = name;
        Kind = kind;
        ValueSpecializationParameters = valueSpecializationParameters;
        _compileVariant = compileVariant;
    }

    internal static Shader FromSource(
        RenderingSystem system,
        ShaderCompiler compiler,
        string moduleName,
        string virtualPath,
        string source,
        ShaderKind? kind = null,
        string name = "")
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        ArgumentException.ThrowIfNullOrEmpty(virtualPath);
        ArgumentNullException.ThrowIfNull(source);

        CachedModuleMetadata meta = compiler.GetOrInspectSourceMetadata(moduleName, virtualPath, source);
        ShaderKind resolvedKind = kind ?? FromPipelineKind(ShaderModule.InferPipelineKind(moduleName, compiler.Target, meta.EntryPoints));
        ShaderPipelineKind pipelineKind = ToPipelineKind(resolvedKind);
        string resolvedName = name.Length != 0 ? name : moduleName;
        Shader shader = new(
            system,
            resolvedName,
            resolvedKind,
            CollectValueSpecializationParameters(meta.EntryPoints, resolvedKind),
            specs => compiler.CompileSource(moduleName, virtualPath, source, pipelineKind,
                specs.Length > 0 ? new ShaderCompileOptions { SpecializationArgs = specs } : null));

        try
        {
            if (!HasGenericStages(meta.EntryPoints, resolvedKind))
                shader._defaultVariant = shader.GetOrCompileVariant([]);
            return shader;
        }
        catch
        {
            shader.DestroyImmediate();
            throw;
        }
    }

    /// <summary>Creates a composed multi-variant shader pairing a template module with a surface module implementing its contract.</summary>
    /// <param name="system">The rendering system that owns the device.</param>
    /// <param name="compiler">The shader compiler; must stay alive while new value specializations are compiled.</param>
    /// <param name="templateModule">The pass template module declaring generic entry points.</param>
    /// <param name="surfaceModule">The companion module exporting one struct implementing the template's contract.</param>
    /// <param name="kind">Optional explicit pipeline kind; null infers automatically from the template module.</param>
    /// <param name="name">The shader name; defaults to <c>template+surface</c>.</param>
    /// <returns>The composed multi-variant shader handle.</returns>
    public static Shader Compose(
        RenderingSystem system,
        ShaderCompiler compiler,
        string templateModule,
        string surfaceModule,
        ShaderKind? kind = null,
        string name = "")
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentException.ThrowIfNullOrEmpty(templateModule);
        ArgumentException.ThrowIfNullOrEmpty(surfaceModule);

        CachedModuleMetadata templateMeta = compiler.GetOrInspectModuleMetadata(templateModule);
        ShaderKind resolvedKind = kind ?? FromPipelineKind(ShaderModule.InferPipelineKind(templateModule, compiler.Target, templateMeta.EntryPoints));
        ShaderPipelineKind pipelineKind = ToPipelineKind(resolvedKind);
        string resolvedName = name.Length != 0 ? name : $"{templateModule}+{surfaceModule}";
        return new Shader(
            system,
            resolvedName,
            resolvedKind,
            CollectValueSpecializationParameters(templateMeta.EntryPoints, resolvedKind),
            specs => compiler.CompileComposed(templateModule, surfaceModule, pipelineKind,
                specs.Length > 0 ? new ShaderCompileOptions { SpecializationArgs = specs } : null));
    }

    /// <summary>Normalizes C# specialization values into canonical Slang expression strings.</summary>
    /// <param name="specializations">Boolean, integer, enum, or Slang expression string arguments.</param>
    /// <returns>The normalized specialization strings.</returns>
    public static string[] NormalizeSpecializations(ReadOnlySpan<object> specializations) =>
        ShaderCompiler.NormalizeSpecializations(specializations);

    /// <summary>The shader name used in diagnostics.</summary>
    public string Name { get; }

    /// <summary>The pipeline kind of the selected program.</summary>
    public ShaderKind Kind { get; }

    /// <summary>Value specialization parameter names expected across this shader's selected entry points in compilation order.</summary>
    public IReadOnlyList<string> ValueSpecializationParameters { get; }

    /// <summary>The rendering system this shader creates pipelines on.</summary>
    public RenderingSystem System => _system;

    /// <summary>The reflected root struct layout of the default specialization variant.</summary>
    public RootLayout Root => DefaultVariant.Root;

    /// <summary>The full shader reflection snapshot of the default specialization variant.</summary>
    public ShaderReflection Reflection => DefaultVariant.Compiled.Reflection;

    /// <summary>Gets a strongly-typed handle for root field <paramref name="name"/> in the default root layout.</summary>
    /// <typeparam name="T">The unmanaged type matching the shader field layout.</typeparam>
    /// <param name="name">The field name or dot-separated nested path.</param>
    /// <returns>The validated typed field handle.</returns>
    public RootField<T> GetRootField<T>(string name) where T : unmanaged => Root.GetField<T>(name);

    /// <summary>Attempts to resolve a strongly-typed handle for root field <paramref name="name"/> in the default root layout.</summary>
    /// <typeparam name="T">The unmanaged type matching the shader field layout.</typeparam>
    /// <param name="name">The field name or dot-separated nested path.</param>
    /// <param name="field">The resolved field handle when compatible; otherwise default.</param>
    /// <returns><see langword="true"/> when found with matching size and kind.</returns>
    public bool TryGetRootField<T>(string name, out RootField<T> field) where T : unmanaged =>
        Root.TryGetField(name, out field);

    internal ShaderVariant DefaultVariant => _defaultVariant ??= GetOrCompileVariant([]);

    /// <summary>Creates a material over this shader pinned to the given specialization arguments.</summary>
    /// <param name="name">The material name; defaults to the shader name.</param>
    /// <param name="specializations">Optional specialization arguments (bool, integer, enum, or Slang expression string).</param>
    /// <returns>The material; it keeps this shader and its bound resources alive and needs no destruction.</returns>
    public Material CreateMaterial(string name = "", params ReadOnlySpan<object> specializations)
    {
        Debug.Assert(!IsDisposed);
        return new Material(this, name, GetOrCompileVariant(NormalizeSpecializations(specializations)));
    }

    internal GPUPipeline GetComputePipeline(params ReadOnlySpan<object> specializations)
    {
        Debug.Assert(!IsDisposed);
        ShaderVariant variant = specializations.IsEmpty ? DefaultVariant : GetOrCompileVariant(NormalizeSpecializations(specializations));
        return variant.GetComputePipeline();
    }

    internal GPUPipeline GetPipeline(in PipelineState state, RenderPassSignature pass)
    {
        Debug.Assert(!IsDisposed);
        return DefaultVariant.GetPipeline(in state, new PipelineStateKey(in state, Kind), in pass, out _);
    }

    internal GPUPipeline GetPipeline(in PipelineState state, RenderPassSignature pass, params ReadOnlySpan<object> specializations)
    {
        Debug.Assert(!IsDisposed);
        ShaderVariant variant = specializations.IsEmpty ? DefaultVariant : GetOrCompileVariant(NormalizeSpecializations(specializations));
        return variant.GetPipeline(in state, new PipelineStateKey(in state, Kind), in pass, out _);
    }

    internal GPUPipeline GetPipeline(in PipelineState state, ReadOnlySpan<Format> colorFormats, Format depthFormat, Format stencilFormat)
    {
        Debug.Assert(!IsDisposed);
        return DefaultVariant.GetPipeline(in state, new PipelineStateKey(in state, Kind),
            new RenderPassSignature(colorFormats, depthFormat, stencilFormat), out _);
    }

    internal GPUPipeline GetPipeline(in PipelineState state, ReadOnlySpan<Format> colorFormats, Format depthFormat, Format stencilFormat,
        params ReadOnlySpan<object> specializations)
    {
        Debug.Assert(!IsDisposed);
        ShaderVariant variant = specializations.IsEmpty ? DefaultVariant : GetOrCompileVariant(NormalizeSpecializations(specializations));
        return variant.GetPipeline(in state, new PipelineStateKey(in state, Kind),
            new RenderPassSignature(colorFormats, depthFormat, stencilFormat), out _);
    }

    /// <summary>Warms up the default specialization in the background with depth and stencil tests disabled.</summary>
    /// <param name="state">The pipeline-defining material state.</param>
    /// <param name="pass">The render-pass attachment signature.</param>
    /// <returns>A task completing when the pipeline is ready or warmup is skipped due to shutdown.</returns>
    public Task WarmUpPipeline(in PipelineState state, RenderPassSignature pass)
    {
        return WarmUpPipeline(in state, pass, default(DepthStencilState));
    }

    /// <summary>Warms up a specialization in the background with depth and stencil tests disabled.</summary>
    /// <param name="state">The pipeline-defining material state.</param>
    /// <param name="pass">The render-pass attachment signature.</param>
    /// <param name="specializations">The specialization arguments for the variant.</param>
    /// <returns>A task completing when the pipeline is ready or warmup is skipped due to shutdown.</returns>
    public Task WarmUpPipeline(in PipelineState state, RenderPassSignature pass, params ReadOnlySpan<object> specializations)
    {
        return WarmUpPipeline(in state, pass, default(DepthStencilState), specializations);
    }

    /// <summary>Warms up a graphics or mesh pipeline in the background for the specified draw state.</summary>
    /// <param name="state">The pipeline-defining material state.</param>
    /// <param name="pass">The render-pass attachment signature.</param>
    /// <param name="depthStencil">The draw's depth and stencil state.</param>
    /// <param name="specializations">The specialization arguments, if any.</param>
    /// <returns>A task completing when the pipeline is ready or warmup is skipped due to shutdown.</returns>
    public Task WarmUpPipeline(in PipelineState state, RenderPassSignature pass, DepthStencilState depthStencil,
        params ReadOnlySpan<object> specializations)
    {
        if (Kind == ShaderKind.Compute)
            throw new InvalidOperationException($"Compute shader '{Name}' has no raster pipeline.");
        if (IsDisposed)
            return Task.CompletedTask;

        string[] normalized = specializations.IsEmpty ? [] : NormalizeSpecializations(specializations);
        PipelineStateKey stateKey = new(in state, Kind);
        ShaderVariant? variant = (specializations.IsEmpty ? Volatile.Read(ref _defaultVariant) : null) ??
            FindVariant(Volatile.Read(ref _variants), normalized);
        return _system.EnqueueShaderWarmUp(this, variant, variant == null ? normalized : null, in state, in stateKey, in pass, in depthStencil);
    }

    /// <summary>Warms up the default specialization in the background with depth and stencil tests disabled.</summary>
    /// <param name="state">The pipeline-defining material state.</param>
    /// <param name="colorFormats">The render pass color target formats.</param>
    /// <param name="depthFormat">The render pass depth format.</param>
    /// <param name="stencilFormat">The render pass stencil format.</param>
    /// <param name="sampleCount">The render pass sample count.</param>
    /// <returns>A task completing when the pipeline is cached or skipped due to shutdown.</returns>
    public Task WarmUpPipeline(in PipelineState state, ReadOnlySpan<Format> colorFormats,
        Format depthFormat = Format.Undefined, Format stencilFormat = Format.Undefined, uint sampleCount = 1)
    {
        return WarmUpPipeline(in state, new RenderPassSignature(colorFormats, depthFormat, stencilFormat, sampleCount));
    }

    internal ShaderVariant GetOrCompileVariant(string[] normalizedSpecializations)
    {
        Debug.Assert(!IsDisposed);
        ShaderVariant? cached = FindVariant(Volatile.Read(ref _variants), normalizedSpecializations);
        if (cached != null)
            return cached;

        lock (_variantLock)
        {
            if (_released)
                throw new ObjectDisposedException(nameof(Shader), $"Shader '{Name}' has been destroyed.");
            VariantEntry[] variants = _variants;
            cached = FindVariant(variants, normalizedSpecializations);
            if (cached != null)
                return cached;

            CompiledShader compiled = _compileVariant(normalizedSpecializations);
            ShaderVariant created = new(this, compiled, Kind, normalizedSpecializations);
            VariantEntry[] updated = new VariantEntry[variants.Length + 1];
            variants.CopyTo(updated, 0);
            updated[^1] = new VariantEntry { Key = normalizedSpecializations, Variant = created };
            Volatile.Write(ref _variants, updated);
            return created;
        }
    }

    internal ShaderVariant? TryGetOrCompileVariant(string[] normalizedSpecializations)
    {
        if (Volatile.Read(ref _released) || IsDisposed)
            return null;
        ShaderVariant? cached = FindVariant(Volatile.Read(ref _variants), normalizedSpecializations);
        if (cached != null)
            return cached;

        lock (_variantLock)
        {
            if (_released || IsDisposed)
                return null;
            VariantEntry[] variants = _variants;
            cached = FindVariant(variants, normalizedSpecializations);
            if (cached != null)
                return cached;

            CompiledShader compiled = _compileVariant(normalizedSpecializations);
            ShaderVariant created = new(this, compiled, Kind, normalizedSpecializations);
            VariantEntry[] updated = new VariantEntry[variants.Length + 1];
            variants.CopyTo(updated, 0);
            updated[^1] = new VariantEntry { Key = normalizedSpecializations, Variant = created };
            Volatile.Write(ref _variants, updated);
            return created;
        }
    }

    /// <summary>Destroys all variants' pipelines.</summary>
    protected override void Release()
    {
        VariantEntry[] snapshot;
        lock (_variantLock)
        {
            Volatile.Write(ref _released, true);
            snapshot = _variants;
            _variants = [];
            _defaultVariant = null;
        }

        foreach (VariantEntry entry in snapshot)
            entry.Variant.Destroy();
    }

    private static ShaderVariant? FindVariant(VariantEntry[] variants, ReadOnlySpan<string> specializations)
    {
        for (int index = 0; index < variants.Length; index++)
        {
            if (specializations.SequenceEqual(variants[index].Key))
                return variants[index].Variant;
        }
        return null;
    }

    private static bool HasGenericStages(IReadOnlyList<ShaderModuleEntryPoint> entryPoints, ShaderKind kind)
    {
        ShaderStageKind stages = StagesForKind(kind);
        for (int i = 0; i < entryPoints.Count; i++)
        {
            ShaderModuleEntryPoint entryPoint = entryPoints[i];
            if ((entryPoint.Stage & stages) != 0 && entryPoint.HasGenericParameters)
                return true;
        }
        return false;
    }

    private static string[] CollectValueSpecializationParameters(IReadOnlyList<ShaderModuleEntryPoint> entryPoints, ShaderKind kind)
    {
        ShaderStageKind stages = StagesForKind(kind);
        List<string>? names = null;
        for (int i = 0; i < entryPoints.Count; i++)
        {
            ShaderModuleEntryPoint entryPoint = entryPoints[i];
            if ((entryPoint.Stage & stages) == 0)
                continue;
            for (int g = 0; g < entryPoint.GenericParameters.Count; g++)
            {
                ShaderGenericParam gp = entryPoint.GenericParameters[g];
                if (gp.IsTypeParameter)
                    continue;
                names ??= [];
                names.Add(gp.Name);
            }
        }
        return names == null ? [] : names.ToArray();
    }

    private static ShaderStageKind StagesForKind(ShaderKind kind) => kind switch
    {
        ShaderKind.Graphics => ShaderStageKind.Vertex | ShaderStageKind.Pixel,
        ShaderKind.Compute => ShaderStageKind.Compute,
        ShaderKind.Mesh => ShaderStageKind.Task | ShaderStageKind.Mesh | ShaderStageKind.Pixel,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static ShaderPipelineKind ToPipelineKind(ShaderKind kind) => kind switch
    {
        ShaderKind.Graphics => ShaderPipelineKind.Graphics,
        ShaderKind.Compute => ShaderPipelineKind.Compute,
        ShaderKind.Mesh => ShaderPipelineKind.Mesh,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static ShaderKind FromPipelineKind(ShaderPipelineKind kind) => kind switch
    {
        ShaderPipelineKind.Graphics => ShaderKind.Graphics,
        ShaderPipelineKind.Compute => ShaderKind.Compute,
        ShaderPipelineKind.Mesh => ShaderKind.Mesh,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private struct VariantEntry
    {
        public string[] Key;
        public ShaderVariant Variant;
    }
}
