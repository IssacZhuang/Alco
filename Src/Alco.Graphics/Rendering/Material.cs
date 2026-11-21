// Example-level material: reflection-named resource bindings, specialization state, and per-frame root staging.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>A shader's specialization, root parameters, and optional raster state. Materials need no destruction.</summary>
/// <remarks>Only graphics and mesh materials set raster state. Do not mutate a material or its ancestors during parallel recording.</remarks>
public class Material
{
    private readonly Shader _shader;
    private protected byte[] _staging;
    private ShaderVariant _variant;
    private PipelineState _state = new();
    private PipelineStateKey _stateKey;
    private DepthStencilState _depthStencil;
    private uint _version;
    private uint _resourceVersion;
    private Binding[] _bindings = [];
    private protected int _bindingCount;

    internal Material(Shader shader, string name, ShaderVariant variant)
    {
        _shader = shader;
        Name = name.Length != 0 ? name : shader.Name;
        _variant = variant;
        _stateKey = new PipelineStateKey(in _state, shader.Kind);
        _staging = CreateInitializedStaging(variant.Root, shader.System);
    }

    /// <summary>The material name used in diagnostics.</summary>
    public string Name { get; }

    /// <summary>The shader this material binds.</summary>
    public Shader Shader => _shader;

    /// <summary>The active variant's specialization arguments, inherited by instances unless overridden.</summary>
    public virtual IReadOnlyList<string> Specializations => Variant.Specializations;

    /// <summary>The reflected root layout of this material's active variant.</summary>
    public RootLayout Root => Variant.Root;

    /// <summary>The full shader reflection snapshot of this material's active variant.</summary>
    public ShaderReflection Reflection => Variant.Compiled.Reflection;

    /// <summary>The version incremented whenever this material's own bindings, specialization, or state change.</summary>
    public uint Version => _version;

    /// <summary>The record-time depth stencil state; not part of the pipeline signature.</summary>
    public DepthStencilState DepthStencilState
    {
        get => _depthStencil;
        set
        {
            Debug.Assert(_shader.Kind != ShaderKind.Compute);
            _depthStencil = value;
            ++_version;
        }
    }

    /// <summary>The blend state applied to every color target of the material's pipelines.</summary>
    public BlendState Blend
    {
        get => _state.Blend;
        set
        {
            Debug.Assert(_shader.Kind != ShaderKind.Compute);
            _state.Blend = value;
            _stateKey = new PipelineStateKey(in _state, _shader.Kind);
            ++_version;
        }
    }

    /// <summary>The color write mask applied to every color target of the material's pipelines.</summary>
    public byte WriteMask
    {
        get => _state.WriteMask;
        set
        {
            Debug.Assert(_shader.Kind != ShaderKind.Compute);
            _state.WriteMask = value;
            _stateKey = new PipelineStateKey(in _state, _shader.Kind);
            ++_version;
        }
    }

    /// <summary>The rasterizer state of the material's pipelines.</summary>
    public RasterizationState Rasterization
    {
        get => _state.Rasterization;
        set
        {
            Debug.Assert(_shader.Kind != ShaderKind.Compute);
            _state.Rasterization = value;
            _stateKey = new PipelineStateKey(in _state, _shader.Kind);
            ++_version;
        }
    }

    /// <summary>The vertex assembly topology; mesh pipelines use the shader's output topology.</summary>
    public PrimitiveTopology Topology
    {
        get => _state.Topology;
        set
        {
            Debug.Assert(_shader.Kind != ShaderKind.Compute);
            _state.Topology = value;
            _stateKey = new PipelineStateKey(in _state, _shader.Kind);
            ++_version;
        }
    }

    /// <summary>Selects a shader specialization, preserving compatible root fields by name and overriding inheritance for instances.</summary>
    /// <param name="specializations">Boolean, integer, enum, or Slang expression string specialization arguments.</param>
    public void SetSpecializations(params ReadOnlySpan<object> specializations)
    {
        ShaderVariant variant = _shader.GetOrCompileVariant(Shader.NormalizeSpecializations(specializations));
        bool changed = SynchronizeStaging(variant);
        if (PinSpecializations() || changed)
            ++_version;
    }

    private protected virtual bool PinSpecializations() => false;

    private bool SynchronizeStaging(ShaderVariant variant)
    {
        if (_variant == variant)
            return false;
        RootLayout oldRoot = _variant.Root;
        RootLayout newRoot = variant.Root;
        if (!oldRoot.MatchesLayout(newRoot))
        {
            MigrateStagingAndBindings(oldRoot, newRoot);
            RemapOverrides(oldRoot, newRoot);
        }
        _variant = variant;
        return true;
    }

    /// <summary>Binds a sampled texture, or restores the inherited binding or system white texture when null.</summary>
    /// <param name="name">The root field name or dotted nested path.</param>
    /// <param name="texture">The texture with a sampled descriptor slot, or null to remove the binding.</param>
    /// <exception cref="InvalidOperationException">The field does not exist, is not a texture field, or the texture has no sampled slot.</exception>
    public void SetTexture(string name, Texture? texture)
    {
        RootField field = Field(name, MaterialFieldKind.Texture);
        uint index = texture?.SampledIndex ?? _shader.System.TextureWhite.SampledIndex;
        if (index == Texture.NoIndex)
            throw new InvalidOperationException($"Texture bound to '{name}' of material '{Name}' has no sampled descriptor slot.");
        WriteUInt(field, index);
        SetBinding(field.Offset, texture);
        OnFieldSet(field, texture != null);
        ++_version;
    }

    /// <summary>Binds storage mip zero, or removes the binding when null.</summary>
    /// <param name="name">The root field name or dotted nested path.</param>
    /// <param name="texture">The texture with storage descriptor slots, or null to restore inheritance or the default field value.</param>
    /// <exception cref="InvalidOperationException">The field does not exist, is not a texture field, or the texture has no storage slot.</exception>
    public void SetStorageTexture(string name, Texture? texture)
    {
        SetStorageTexture(name, texture, 0);
    }

    /// <summary>Binds one storage mip, or removes the binding when null.</summary>
    /// <param name="name">The root field name or dotted nested path.</param>
    /// <param name="texture">The texture with storage descriptor slots, or null to restore inheritance or the default field value.</param>
    /// <param name="mipLevel">The zero-based mip level, less than <see cref="Texture.MipLevels"/>; ignored when texture is null.</param>
    /// <exception cref="InvalidOperationException">The field does not exist, is not a texture field, or the texture has no storage slot.</exception>
    public void SetStorageTexture(string name, Texture? texture, uint mipLevel)
    {
        RootField field = Field(name, MaterialFieldKind.Texture);
        uint index = texture?.GetStorageIndex(mipLevel) ?? _shader.System.TextureWhite.SampledIndex;
        if (index == Texture.NoIndex)
            throw new InvalidOperationException($"Texture bound to '{name}' of material '{Name}' has no storage descriptor slot.");
        WriteUInt(field, index);
        SetBinding(field.Offset, texture);
        OnFieldSet(field, texture != null);
        ++_version;
    }

    /// <summary>Binds a sampler, or restores the inherited binding or system default sampler when null.</summary>
    /// <param name="name">The root field name or dotted nested path.</param>
    /// <param name="sampler">The sampler, or null to remove the binding.</param>
    /// <exception cref="InvalidOperationException">The field does not exist or is not a sampler field.</exception>
    public void SetSampler(string name, Sampler? sampler)
    {
        RootField field = Field(name, MaterialFieldKind.Sampler);
        WriteUInt(field, sampler?.Index ?? _shader.System.SamplerDefault.Index);
        SetBinding(field.Offset, sampler);
        OnFieldSet(field, sampler != null);
        ++_version;
    }

    /// <summary>Binds a buffer view, or restores the inherited binding or zero view when null.</summary>
    /// <param name="name">The root field name or dotted nested path.</param>
    /// <param name="buffer">The buffer owned by this rendering system, or null to remove the binding.</param>
    /// <param name="byteOffset">The byte offset within a nonnull buffer.</param>
    /// <exception cref="InvalidOperationException">The field does not exist or is not a buffer field.</exception>
    public unsafe void SetBuffer(string name, Buffer? buffer, ulong byteOffset = 0)
    {
        RootField field = Field(name, MaterialFieldKind.Buffer);
        if (buffer == null)
            WriteBufferView(field, default);
        else
        {
            Debug.Assert(!buffer.IsDestroyed && buffer.System == _shader.System && byteOffset < buffer.SizeBytes);
            WriteBufferView(field, buffer.View.OffsetBy((uint)byteOffset));
        }
        SetBinding(field.Offset, buffer);
        OnFieldSet(field, buffer != null);
        ++_version;
    }

    /// <summary>Writes an ordinary value to the named data field.</summary>
    /// <typeparam name="T">The unmanaged value type; its size must equal the field size.</typeparam>
    /// <param name="name">The root field name or dotted nested path.</param>
    /// <param name="value">The value to write.</param>
    /// <exception cref="InvalidOperationException">The field does not exist or the value size differs from the field size.</exception>
    public void SetData<T>(string name, ref T value) where T : unmanaged
    {
        RootField field = Field(name, MaterialFieldKind.Data);
        int size = Unsafe.SizeOf<T>();
        if (field.SizeBytes != (uint)size)
            throw new InvalidOperationException($"Value of {size} bytes does not fit field '{name}' of {field.SizeBytes} bytes in material '{Name}'.");
        SynchronizeStaging(Variant);
        MemoryMarshal.Write(_staging.AsSpan((int)field.Offset, size), in value);
        OnFieldSet(field, true);
        ++_version;
    }

    /// <summary>Gets or creates a shared graphics or mesh pipeline for this material's active specialization.</summary>
    /// <param name="pass">The render-pass attachment signature.</param>
    /// <returns>The pipeline; owned by the material's shader.</returns>
    public GPUPipeline GetPipeline(RenderPassSignature pass)
    {
        return Variant.GetPipeline(in _state, in _stateKey, in pass, out _);
    }

    /// <summary>Gets or creates a shared graphics or mesh pipeline for this material's active specialization.</summary>
    /// <param name="colorFormats">The render pass color target formats.</param>
    /// <param name="depthFormat">The render pass depth target format.</param>
    /// <param name="stencilFormat">The render pass stencil format.</param>
    /// <param name="sampleCount">The render pass sample count.</param>
    /// <returns>The pipeline; owned by the material's shader.</returns>
    public GPUPipeline GetPipeline(ReadOnlySpan<Format> colorFormats, Format depthFormat,
        Format stencilFormat = Format.Undefined, uint sampleCount = 1)
    {
        return Variant.GetPipeline(in _state, in _stateKey,
            new RenderPassSignature(colorFormats, depthFormat, stencilFormat, sampleCount), out _);
    }

    /// <summary>Warms up the pipeline in the background for this material's current specialization and draw state.</summary>
    /// <param name="pass">The render-pass attachment signature.</param>
    /// <returns>A task completing when the pipeline is ready or warmup is skipped due to shutdown.</returns>
    public Task WarmUpPipeline(RenderPassSignature pass)
    {
        return WarmUpPipeline(pass, _depthStencil);
    }

    /// <summary>Warms up the pipeline in the background for a draw that overrides this material's depth and stencil state.</summary>
    /// <param name="pass">The render-pass attachment signature.</param>
    /// <param name="depthStencil">The overridden depth and stencil state.</param>
    /// <returns>A task completing when the pipeline is ready or warmup is skipped due to shutdown.</returns>
    public Task WarmUpPipeline(RenderPassSignature pass, DepthStencilState depthStencil)
    {
        return _shader.System.EnqueueShaderWarmUp(_shader, Variant, null, in _state, in _stateKey, in pass, in depthStencil);
    }

    /// <summary>Warms up the pipeline in the background for this material's current specialization and draw state.</summary>
    /// <param name="colorFormats">The render pass color target formats.</param>
    /// <param name="depthFormat">The render pass depth target format.</param>
    /// <param name="stencilFormat">The render pass stencil format.</param>
    /// <param name="sampleCount">The render pass sample count.</param>
    /// <returns>A task completing when the pipeline is ready or warmup is skipped due to shutdown.</returns>
    public Task WarmUpPipeline(ReadOnlySpan<Format> colorFormats, Format depthFormat = Format.Undefined,
        Format stencilFormat = Format.Undefined, uint sampleCount = 1)
    {
        return WarmUpPipeline(new RenderPassSignature(colorFormats, depthFormat, stencilFormat, sampleCount));
    }

    /// <summary>Copies resolved root data into per-frame storage; direct command use leaves shader and bound-resource retention to the caller.</summary>
    /// <param name="frameData">The exclusive allocator; retain its storage until the GPU finishes the recorded uses.</param>
    /// <returns>The 16-byte-aligned root allocation; pass its <c>GPURange</c> to draw and dispatch commands.</returns>
    public unsafe GPUAllocation<byte> AllocateRoot(BumpAllocator frameData)
    {
        int size = (int)Variant.Root.SizeBytes;
        Debug.Assert(frameData != null && size != 0);
        GPUAllocation<byte> root = frameData.Allocate((ulong)size);
        Debug.Assert(root.Cpu != null);
        CopyRoot(root.CpuSpan);
        return root;
    }

    internal PipelineState State => _state;

    internal PipelineStateKey StateKey => _stateKey;

    internal virtual ShaderVariant Variant => _variant;

    private protected RootLayout StagingRoot => _variant.Root;

    internal virtual Material ResourceOwner => this;

    internal virtual uint ResourceVersion => _resourceVersion;

    /// <summary>Copies the resolved root bytes into <paramref name="destination"/>.</summary>
    /// <param name="destination">Destination span of length matching at least <see cref="RootLayout.SizeBytes"/>.</param>
    public virtual void CopyRoot(Span<byte> destination)
    {
        Debug.Assert(destination.Length >= _staging.Length);
        _staging.CopyTo(destination);
    }

    internal virtual void CopyRoot(Span<byte> destination, int offset)
    {
        _staging.AsSpan(offset, destination.Length).CopyTo(destination);
    }

    internal virtual RenderingResource? GetBinding(RootField field) => GetStagedBinding(field.Offset);

    private protected RenderingResource? GetStagedBinding(uint offset)
    {
        for (int index = 0; index < _bindingCount; ++index)
        {
            if (_bindings[index].Offset == offset)
                return _bindings[index].Resource;
        }
        return null;
    }

    internal void RetainResources(List<RenderingResource> resources)
    {
        Debug.Assert(!_shader.IsDestroyed);
        resources.Add(_shader);
        RetainBindings(resources);
    }

    private protected virtual void RetainBindings(List<RenderingResource> resources)
    {
        for (int index = 0; index < _bindingCount; ++index)
        {
            RenderingResource resource = _bindings[index].Resource;
            Debug.Assert(!resource.IsDestroyed);
            resources.Add(resource);
        }
        foreach (RootField field in StagingRoot.LeafFields)
        {
            if (field.Kind == MaterialFieldKind.Texture && GetStagedBinding(field.Offset) == null)
            {
                resources.Add(_shader.System.TextureWhite);
                break;
            }
        }
    }

    /// <summary>Creates an instance with independent render state and live inheritance of root fields it does not override.</summary>
    /// <returns>The instance; it keeps its parent alive and needs no destruction.</returns>
    public MaterialInstance CreateInstance()
    {
        return new MaterialInstance(this);
    }

    /// <summary>Updates whether a root field overrides its parent.</summary>
    /// <param name="field">The field that was written.</param>
    /// <param name="overridden">Whether the field has a local value.</param>
    private protected virtual void OnFieldSet(RootField field, bool overridden)
    {
    }

    private protected virtual void RemapOverrides(RootLayout oldRoot, RootLayout newRoot)
    {
    }

    private void MigrateStagingAndBindings(RootLayout oldRoot, RootLayout newRoot)
    {
        byte[] nextStaging = CreateInitializedStaging(newRoot, _shader.System);
        Binding[] oldBindings = _bindings;
        int oldBindingCount = _bindingCount;
        _bindings = [];
        _bindingCount = 0;

        foreach (RootField newField in newRoot.LeafFields)
        {
            RootField? oldField = oldRoot.FindCompatibleLeaf(newField);
            if (oldField == null)
                continue;
            _staging.AsSpan((int)oldField.Offset, (int)oldField.SizeBytes).CopyTo(nextStaging.AsSpan((int)newField.Offset, (int)newField.SizeBytes));
            for (int index = 0; index < oldBindingCount; ++index)
            {
                if (oldBindings[index].Offset == oldField.Offset)
                {
                    SetBinding(newField.Offset, oldBindings[index].Resource);
                    break;
                }
            }
        }

        _staging = nextStaging;
        ++_resourceVersion;
    }

    private static byte[] CreateInitializedStaging(RootLayout root, RenderingSystem system)
    {
        byte[] staging = new byte[root.SizeBytes];
        uint whiteIndex = system.TextureWhite.SampledIndex;
        uint defaultSampler = system.SamplerDefault.Index;
        foreach (RootField field in root.LeafFields)
        {
            if (field.Kind == MaterialFieldKind.Texture)
                MemoryMarshal.Write(staging.AsSpan((int)field.Offset, 4), in whiteIndex);
            else if (field.Kind == MaterialFieldKind.Sampler)
                MemoryMarshal.Write(staging.AsSpan((int)field.Offset, 4), in defaultSampler);
        }
        return staging;
    }

    private void SetBinding(uint offset, RenderingResource? resource)
    {
        for (int index = 0; index < _bindingCount; ++index)
        {
            if (_bindings[index].Offset != offset)
                continue;
            if (resource == null)
            {
                --_bindingCount;
                Array.Copy(_bindings, index + 1, _bindings, index, _bindingCount - index);
                _bindings[_bindingCount] = default;
                ++_resourceVersion;
            }
            else if (_bindings[index].Resource != resource)
            {
                _bindings[index].Resource = resource;
                ++_resourceVersion;
            }
            return;
        }
        if (resource == null)
            return;
        if (_bindingCount == _bindings.Length)
            Array.Resize(ref _bindings, Math.Max(4, _bindings.Length * 2));
        _bindings[_bindingCount++] = new Binding { Offset = offset, Resource = resource };
        ++_resourceVersion;
    }

    private struct Binding
    {
        public uint Offset;
        public RenderingResource Resource;
    }

    private RootField Field(string name, MaterialFieldKind kind)
    {
        RootField? field = Variant.Root.Find(name);
        if (field == null)
            throw new InvalidOperationException($"Material '{Name}' has no root field '{name}'.");
        if (field.Kind != kind)
            throw new InvalidOperationException($"Root field '{name}' of material '{Name}' holds {field.Kind}, not {kind}.");
        return field;
    }

    private void WriteUInt(RootField field, uint value)
    {
        Debug.Assert(field.SizeBytes == 4);
        SynchronizeStaging(Variant);
        MemoryMarshal.Write(_staging.AsSpan((int)field.Offset, 4), in value);
    }

    private void WriteBufferView(RootField field, GPUBufferView value)
    {
        Debug.Assert(field.SizeBytes == 8);
        SynchronizeStaging(Variant);
        MemoryMarshal.Write(_staging.AsSpan((int)field.Offset, 8), in value);
    }
}
