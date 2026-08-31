using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Particles;

/// <summary>
/// The 2D GPU particle system: simulates and renders any number of
/// <see cref="ParticleEffect2DAsset"/> instances entirely on the GPU. Per emitter
/// group and frame it records two compute dispatches (emit + simulate/compact)
/// into the render graph and one indexed-indirect instanced draw into the scene
/// pass — CPU work per frame is a small parameter update per active emitter.
/// <br/>All instances share one buffer pool (see <see cref="ParticleBufferPool{,}"/>),
/// which makes creating/destroying effects cheap and keeps the draw state stable.
/// <br/>Usage: insert the simulation into the pipeline with an
/// <see cref="RGNode_Callback"/> before the scene content node and call
/// <see cref="Render"/> from the scene content (or an <see cref="IRenderPassContent"/>).
/// </summary>
public sealed class GpuParticleSystem2D : AutoDisposable
{
    private readonly RenderingSystem _rendering;
    private readonly ParticleBufferPool<GpuParticle2D, EmitterParams2D> _pool;
    private readonly MaterialCompiler _materialCompiler;
    private readonly ShaderLibrary _renderTemplate;
    private readonly ShaderLibrary _emitTemplate;
    private readonly ShaderLibrary _simulateTemplate;
    private readonly ShaderLibrary _defaultBehavior;
    private readonly ComputeMaterial _initMaterial;
    private readonly Dictionary<ShaderLibrary, (ComputeMaterial Emit, ComputeMaterial Simulate)> _behaviorMaterials = [];
    private readonly Dictionary<ParticleGroup2DAsset, GraphicsMaterial> _materials = [];
    private readonly List<Texture2D> _overLifeTextures = [];
    private readonly List<ParticleEffectInstance2D> _instances = [];
    private readonly MaterialAsset _defaultAsset = new() { Name = "particles2d-default" };
    private GraphicsValueBuffer<Matrix4x4>? _camera;

    /// <summary>
    /// Creates the system with its shared pool's initial capacities.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="particleCapacity">The initial particle pool size (grows geometrically when exhausted).</param>
    /// <param name="emitterSlots">The initial emitter-slot count (one per emitter group instance).</param>
    public GpuParticleSystem2D(RenderingSystem rendering, int particleCapacity = 65536, int emitterSlots = 256)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        _rendering = rendering;
        _pool = new ParticleBufferPool<GpuParticle2D, EmitterParams2D>(rendering, particleCapacity, emitterSlots, "particles2d");
        _pool.Reallocated += OnPoolReallocated;
        ShaderSystem shaderSystem = rendering.ShaderSystem;
        _materialCompiler = new MaterialCompiler(rendering, shaderSystem.GetLibrary(ParticleAssetPipeline.DefaultSurface));
        _renderTemplate = shaderSystem.GetLibrary(ParticleAssetPipeline.RenderModule2D);
        _emitTemplate = shaderSystem.GetLibrary("GpuParticleEmit2D");
        _simulateTemplate = shaderSystem.GetLibrary("GpuParticleSimulate2D");
        _defaultBehavior = shaderSystem.GetLibrary(ParticleAssetPipeline.DefaultBehavior2D);
        _initMaterial = rendering.CreateComputeMaterial(shaderSystem.GetShader("GpuParticleInit2D"));
        // Bind the pool up front: OnPoolReallocated refreshes this, but the first
        // slice-recycle kill dispatch can precede any reallocation.
        _initMaterial.TrySetBuffer(ShaderResourceId.Particles, _pool.Particles);
        QuadMesh = rendering.MeshCenteredSprite;
    }

    /// <summary>
    /// The 2D camera the groups render with; bound to every group's material.
    /// Accepts any view-projection matrix buffer (a <see cref="Camera2DBuffer"/>,
    /// or the shared <see cref="RenderingSystem.MainCameraViewProjectionBuffer"/> to
    /// track whatever camera is currently rendering). Must be set before
    /// <see cref="Render"/> is called.
    /// </summary>
    public GraphicsValueBuffer<Matrix4x4>? Camera
    {
        get => _camera;
        set
        {
            _camera = value;
            foreach (GraphicsMaterial material in _materials.Values)
            {
                if (value != null)
                {
                    material.SetBuffer(ShaderResourceId.Camera, value);
                }
            }
        }
    }

    /// <summary>The quad mesh the particles draw with (centered, position.xy in [-0.5, 0.5]).</summary>
    public Mesh QuadMesh { get; set; }

    /// <summary>The live effect instances.</summary>
    public IReadOnlyList<ParticleEffectInstance2D> Instances => _instances;

    /// <summary>The shared buffer pool (diagnostics).</summary>
    internal ParticleBufferPool<GpuParticle2D, EmitterParams2D> Pool => _pool;

    /// <summary>The shared pool's current particle capacity (grows geometrically when exhausted).</summary>
    public int PoolParticleCapacity => _pool.ParticleCapacity;

    /// <summary>The shared pool's current emitter-slot count.</summary>
    public int PoolEmitterSlotCapacity => _pool.SlotCapacity;

    /// <summary>
    /// Creates an effect instance that starts playing immediately.
    /// </summary>
    /// <param name="effect">The effect asset.</param>
    /// <param name="transform">The emitter transform.</param>
    /// <param name="seed">The deterministic RNG seed of the instance; 0 seeds from the environment tick.</param>
    /// <returns>The new instance; dispose it to destroy the effect.</returns>
    public ParticleEffectInstance2D CreateInstance(ParticleEffect2DAsset effect, in Transform2D transform, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (effect.Groups.Count == 0)
        {
            throw new InvalidDataException($"Particle effect '{effect.Name}' has no groups.");
        }

        uint indexCount = QuadMesh.GetSubMesh(0).IndexCount;
        var groups = new ParticleEffectInstance2D.GroupState[effect.Groups.Count];
        uint pendingSlot = 0;
        ParticleSlice pendingSlice = default;
        uint uploadMin = uint.MaxValue;
        uint uploadMax = 0;
        try
        {
            for (int i = 0; i < groups.Length; i++)
            {
                ParticleGroup2DAsset groupAsset = effect.Groups[i];
                pendingSlot = _pool.AllocateSlot();
                pendingSlice = _pool.AllocateSlice(Math.Max(groupAsset.MaxParticles, 1));
                EmitterParams2D parameters = EmitterParams2D.FromAsset(groupAsset, indexCount);
                parameters.Capacity = pendingSlice.Capacity;
                parameters.SliceOffset = pendingSlice.Offset;
                _pool.Params[(int)pendingSlot] = parameters;
                uploadMin = Math.Min(uploadMin, pendingSlot);
                uploadMax = Math.Max(uploadMax, pendingSlot);
                groups[i] = new ParticleEffectInstance2D.GroupState
                {
                    Asset = groupAsset,
                    Slot = pendingSlot,
                    Slice = pendingSlice,
                    Material = GetOrCreateMaterial(groupAsset),
                    EmissionRate = groupAsset.EmissionRate,
                    Lifetime = groupAsset.Lifetime,
                };
                pendingSlice = default; // ownership moved to the group state
            }
        }
        catch
        {
            // A mid-construction failure (e.g. an invalid group material) must not
            // leak pool resources: free the in-flight group's slot/slice plus every
            // completed group's.
            _pool.FreeSlice(pendingSlice);
            if (pendingSlice.Capacity > 0)
            {
                _pool.FreeSlot(pendingSlot);
            }
            for (int i = 0; i < groups.Length && groups[i] != null; i++)
            {
                _pool.FreeSlice(groups[i].Slice);
                _pool.FreeSlot(groups[i].Slot);
            }
            throw;
        }
        _pool.Params.UpdateBufferRanged(uploadMin, uploadMax - uploadMin + 1);
        var instance = new ParticleEffectInstance2D(this, effect, transform, seed, groups);
        _instances.Add(instance);
        return instance;
    }

    /// <summary>
    /// Records the simulation of all active instances into the frame's command
    /// buffer: pending pool migrations, the parameter upload, pending slice kills,
    /// then per group the emit and simulate/compact dispatches. Call from a graph
    /// node (e.g. <see cref="RGNode_Callback"/>) ordered before the scene pass the
    /// particles draw into.
    /// </summary>
    /// <param name="context">The render graph context.</param>
    public void RecordSimulation(in RenderGraphContext context)
    {
        _pool.RecordMigration(context.RenderContext.CommandBuffer);

        float deltaTime = context.DeltaTime;
        uint dirtyMin = uint.MaxValue;
        uint dirtyMax = 0;
        for (int i = 0; i < _instances.Count; i++)
        {
            _instances[i].AdvanceFrame(deltaTime, ref dirtyMin, ref dirtyMax);
        }
        if (dirtyMin <= dirtyMax)
        {
            _pool.Params.UpdateBufferRanged(dirtyMin, dirtyMax - dirtyMin + 1);
        }

        IReadOnlyList<(uint Offset, uint Count)> kills = _pool.PendingKills;
        bool anyActive = AnyActiveGroups();
        if (kills.Count == 0 && !anyActive)
        {
            return;
        }

        using (GPUCommandBuffer.ComputePass computePass = context.RenderContext.CommandBuffer.BeginCompute())
        {
            for (int i = 0; i < kills.Count; i++)
            {
                (uint offset, uint count) = kills[i];
                _initMaterial.DispatchByGroupWithConstant(
                    computePass, (count + 63) / 64, 1, 1,
                    new GpuParticleInitConstant { SliceOffset = offset, Count = count });
            }
            _pool.ClearPendingKills();

            if (!anyActive)
            {
                return;
            }
            for (int i = 0; i < _instances.Count; i++)
            {
                ParticleEffectInstance2D instance = _instances[i];
                if (!instance.IsActive || !instance.IsVisible)
                {
                    continue;
                }
                foreach (ParticleEffectInstance2D.GroupState group in instance.Groups)
                {
                    if (!group.Active)
                    {
                        continue;
                    }
                    (ComputeMaterial emit, ComputeMaterial simulate) = GetBehaviorMaterials(group.Asset.Behavior);
                    // The emit pass also resets the draw-args record, so it runs
                    // every frame the group is active — even with zero spawns.
                    emit.DispatchByGroupWithConstant(
                        computePass, Math.Max((group.SpawnCount + 63) / 64, 1), 1, 1,
                        new GpuParticleSlotConstant { EmitterSlot = group.Slot });
                    simulate.DispatchByGroupWithConstant(
                        computePass, (group.Slice.Capacity + 63) / 64, 1, 1,
                        new GpuParticleSlotConstant { EmitterSlot = group.Slot });
                }
            }
        }
    }

    /// <summary>
    /// Records the indexed-indirect draws of all active instances into the current
    /// pass. Call from the scene content node (<see cref="RGNode_SceneContent"/> or
    /// an <see cref="IRenderPassContent"/>).
    /// </summary>
    /// <param name="pass">The render pass scope of the scene pass.</param>
    public void Render(RenderPassScope pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (_camera == null)
        {
            throw new InvalidOperationException($"The {nameof(Camera)} of the particle system was not set.");
        }
        for (int i = 0; i < _instances.Count; i++)
        {
            ParticleEffectInstance2D instance = _instances[i];
            if (!instance.IsActive || !instance.IsVisible)
            {
                continue;
            }
            foreach (ParticleEffectInstance2D.GroupState group in instance.Groups)
            {
                if (!group.Active)
                {
                    continue;
                }
                Matrix4x4 model = group.Asset.SimulationSpace == ParticleSimulationSpace.World
                    ? Matrix4x4.Identity
                    : instance.Transform.Matrix;
                pass.DrawIndexedIndirect(
                    QuadMesh,
                    group.Material,
                    _pool.DrawArgs,
                    group.Slot * 20,
                    new GpuParticleDraw2DConstant { Model = model, EmitterSlot = group.Slot });
            }
        }
    }

    internal ref EmitterParams2D ParamsRef(uint slot) => ref _pool.Params.AsSpan()[(int)slot];

    internal void ReleaseInstance(ParticleEffectInstance2D instance)
    {
        foreach (ParticleEffectInstance2D.GroupState group in instance.Groups)
        {
            _pool.FreeSlice(group.Slice);
            _pool.FreeSlot(group.Slot);
        }
        _instances.Remove(instance);
    }

    private bool AnyActiveGroups()
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            if (_instances[i].IsActive)
            {
                return true;
            }
        }
        return false;
    }

    private (ComputeMaterial Emit, ComputeMaterial Simulate) GetBehaviorMaterials(ShaderLibrary? behavior)
    {
        behavior ??= _defaultBehavior;
        if (!_behaviorMaterials.TryGetValue(behavior, out (ComputeMaterial, ComputeMaterial) materials))
        {
            ComputeMaterial emit = _rendering.CreateComputeMaterial(_materialCompiler.ComposeCompute(_emitTemplate, behavior));
            ComputeMaterial simulate = _rendering.CreateComputeMaterial(_materialCompiler.ComposeCompute(_simulateTemplate, behavior));
            BindPool(emit);
            BindPool(simulate);
            materials = (emit, simulate);
            _behaviorMaterials[behavior] = materials;
        }
        return materials;
    }

    private GraphicsMaterial GetOrCreateMaterial(ParticleGroup2DAsset group)
    {
        if (!_materials.TryGetValue(group, out GraphicsMaterial? material))
        {
            // The group's material instance: the .amat compiles against the 2D
            // pass template (its surface shades the fragments and may adjust the
            // vertices, its textures and [MaterialParams] values bind), then the
            // group's own texture derives over the surface's "texture" slot.
            MaterialAsset asset = group.Material ?? _defaultAsset;
            material = _materialCompiler.Compile(
                asset,
                _renderTemplate,
                (_, shader) => _rendering.CreateGraphicsMaterial(shader, $"particles2d:{group.Name}"));
            material.BlendState = group.Blend ?? BlendState.AlphaBlend;
            if (group.Depth is { } depth)
            {
                // Groups authoring world z in a custom render module (e.g. facade
                // sprites) depth-test (never write) the scene depth with "Read".
                material.DepthStencilState = depth;
            }
            if (group.Texture != null && !material.TrySetTexture(ShaderResourceId.Texture, group.Texture))
            {
                throw new InvalidDataException(
                    $"Particle group '{group.Name}' sets a texture, but the surface of material '{asset.Name}' " +
                    $"declares no '{ShaderResourceId.Texture}' slot to override.");
            }
            BindOverLifeTextures(group, material);
            if (_camera != null)
            {
                material.SetBuffer(ShaderResourceId.Camera, _camera);
            }
            BindPool(material);
            _materials[group] = material;
        }
        return material;
    }

    private void BindPool(ComputeMaterial material)
    {
        material.TrySetBuffer(ShaderResourceId.Particles, _pool.Particles);
        material.TrySetBuffer(ParticleShaderKeys.Emitters, _pool.Params);
        material.TrySetBuffer(ParticleShaderKeys.RenderList, _pool.RenderList);
        material.TrySetBuffer(ParticleShaderKeys.DrawArgs, _pool.DrawArgs);
    }

    // The group's over-life lookup textures: the authored gradient/curve bake
    // into a 256x1 row each (see ParticleOverLifeBake); groups without them bind
    // the shared 1x1 white texture (the identity sample) — the EmitterParams2D
    // flag bits gate the fetch, so no shader permutation is needed. Baked
    // textures are owned by the system and disposed with it.
    private void BindOverLifeTextures(ParticleGroup2DAsset group, GraphicsMaterial material)
    {
        if (group.ColorGradient is { Count: > 0 } gradientKeys)
        {
            byte[] pixels = new byte[ParticleOverLifeBake.TextureWidth * 4];
            ParticleOverLifeBake.BakeGradient(gradientKeys, pixels);
            Texture2D texture = _rendering.CreateTexture2D(
                pixels, ParticleOverLifeBake.TextureWidth, 1,
                new ImageLoadOption(name: $"particles2d:{group.Name}:colorGradient"));
            _overLifeTextures.Add(texture);
            material.SetTexture(ParticleShaderKeys.ColorGradient, texture);
        }
        else
        {
            material.SetTexture(ParticleShaderKeys.ColorGradient, _rendering.TextureWhite);
        }
        if (group.SizeCurve is { Count: > 0 } curveKeys)
        {
            Half[] texels = new Half[ParticleOverLifeBake.TextureWidth];
            ParticleOverLifeBake.BakeCurve(curveKeys, texels);
            Texture2D texture = _rendering.CreateTexture2D(
                MemoryMarshal.AsBytes<Half>(texels), ParticleOverLifeBake.TextureWidth, 1,
                new ImageLoadOption(format: PixelFormat.R16Float, name: $"particles2d:{group.Name}:sizeCurve"));
            _overLifeTextures.Add(texture);
            material.SetTexture(ParticleShaderKeys.SizeCurve, texture);
        }
        else
        {
            material.SetTexture(ParticleShaderKeys.SizeCurve, _rendering.TextureWhite);
        }
    }

    private void BindPool(GraphicsMaterial material)
    {
        material.TrySetBuffer(ShaderResourceId.Particles, _pool.Particles);
        material.TrySetBuffer(ParticleShaderKeys.Emitters, _pool.Params);
        material.TrySetBuffer(ParticleShaderKeys.RenderList, _pool.RenderList);
    }

    private void OnPoolReallocated()
    {
        foreach ((ComputeMaterial emit, ComputeMaterial simulate) in _behaviorMaterials.Values)
        {
            BindPool(emit);
            BindPool(simulate);
        }
        _initMaterial.TrySetBuffer(ShaderResourceId.Particles, _pool.Particles);
        foreach (GraphicsMaterial material in _materials.Values)
        {
            BindPool(material);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }
        for (int i = _instances.Count - 1; i >= 0; i--)
        {
            _instances[i].Dispose();
        }
        foreach ((ComputeMaterial emit, ComputeMaterial simulate) in _behaviorMaterials.Values)
        {
            emit.Dispose();
            simulate.Dispose();
        }
        foreach (GraphicsMaterial material in _materials.Values)
        {
            material.Dispose();
        }
        foreach (Texture2D texture in _overLifeTextures)
        {
            texture.Dispose();
        }
        _materialCompiler.Dispose();
        _initMaterial.Dispose();
        _pool.Reallocated -= OnPoolReallocated;
        _pool.Dispose();
    }
}

/// <summary>The push constant of the emit/simulate compute passes (16 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuParticleSlotConstant
{
    /// <summary>The emitter slot of the dispatch.</summary>
    public uint EmitterSlot;

    /// <summary>Reserved.</summary>
    public uint Pad0;

    /// <summary>Reserved.</summary>
    public uint Pad1;

    /// <summary>Reserved.</summary>
    public uint Pad2;
}

/// <summary>The push constant of the slice-init compute pass (16 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuParticleInitConstant
{
    /// <summary>The first slot of the slice to kill.</summary>
    public uint SliceOffset;

    /// <summary>The number of slots to kill.</summary>
    public uint Count;

    /// <summary>Reserved.</summary>
    public uint Pad0;

    /// <summary>Reserved.</summary>
    public uint Pad1;
}

/// <summary>The push constant of the 2D particle draw (80 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuParticleDraw2DConstant
{
    /// <summary>The emitter transform (local space) or identity (world space).</summary>
    public Matrix4x4 Model;

    /// <summary>The emitter slot of the draw.</summary>
    public uint EmitterSlot;

    /// <summary>Reserved.</summary>
    public uint Pad0;

    /// <summary>Reserved.</summary>
    public uint Pad1;

    /// <summary>Reserved.</summary>
    public uint Pad2;
}
