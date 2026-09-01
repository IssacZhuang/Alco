using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Particles;

/// <summary>
/// The 3D GPU particle system: simulates and renders <see cref="ParticleEffect3DAsset"/>
/// instances entirely on the GPU — the 3D counterpart of
/// <see cref="GpuParticleSystem2D"/> (see its remarks for the material-batched
/// multi-draw architecture; the draw plan is built in RecordSimulation and replayed
/// in Render the same way). Renders camera-facing billboards with depth testing
/// (tested, not written) — draw it into the scene's forward/transparent pass.
/// </summary>
public sealed class GpuParticleSystem3D : AutoDisposable
{
    /// <summary>One material-homogeneous run of the per-frame draw plan.</summary>
    private readonly record struct DrawBatch(GraphicsMaterial Material, uint First, uint Count);

    private readonly RenderingSystem _rendering;
    private readonly ParticleBufferPool<GpuParticle3D, EmitterParams3D> _pool;
    private readonly MaterialCompiler _materialCompiler;
    private readonly ShaderLibrary _renderTemplate;
    private readonly ShaderLibrary _emitTemplate;
    private readonly ShaderLibrary _simulateTemplate;
    private readonly ShaderLibrary _defaultBehavior;
    private readonly ComputeMaterial _initMaterial;
    private readonly Dictionary<ShaderLibrary, (ComputeMaterial Emit, ComputeMaterial Simulate)> _behaviorMaterials = [];
    private readonly Dictionary<ParticleGroup3DAsset, GraphicsMaterial> _materials = [];
    private readonly List<Texture2D> _overLifeTextures = [];
    private readonly List<ParticleEffectInstance3D> _instances = [];
    // The per-frame draw plan (rebuilt in RecordSimulation, replayed in Render):
    // groups bucketed by material in first-seen order, flattened with their
    // drawIndex/drawStart assignment, plus one batch record per material.
    private readonly Dictionary<GraphicsMaterial, List<ParticleEffectInstance3D.GroupState>> _drawBuckets = [];
    private readonly List<GraphicsMaterial> _drawMaterials = [];
    private readonly List<ParticleEffectInstance3D.GroupState> _drawGroups = [];
    private readonly List<uint> _drawIndices = [];
    private readonly List<uint> _drawStarts = [];
    private readonly List<DrawBatch> _drawBatches = [];
    private readonly MaterialAsset _defaultAsset = new() { Name = "particles3d-default" };
    private CameraPerspectiveBuffer? _camera;
    private DepthStencilState _depthStencilState = DepthStencilState.ReadReverseZ;

    /// <summary>
    /// Creates the system with its shared pool's initial capacities.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="particleCapacity">The initial particle pool size (grows geometrically when exhausted).</param>
    /// <param name="emitterSlots">The initial emitter-slot count (one per emitter group instance).</param>
    public GpuParticleSystem3D(RenderingSystem rendering, int particleCapacity = 65536, int emitterSlots = 256)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        _rendering = rendering;
        _pool = new ParticleBufferPool<GpuParticle3D, EmitterParams3D>(rendering, particleCapacity, emitterSlots, "particles3d");
        _pool.Reallocated += OnPoolReallocated;
        ShaderSystem shaderSystem = rendering.ShaderSystem;
        _materialCompiler = new MaterialCompiler(rendering, shaderSystem.GetLibrary(ParticleAssetPipeline.DefaultSurface));
        _renderTemplate = shaderSystem.GetLibrary(ParticleAssetPipeline.RenderModule3D);
        _emitTemplate = shaderSystem.GetLibrary("GpuParticleEmit3D");
        _simulateTemplate = shaderSystem.GetLibrary("GpuParticleSimulate3D");
        _defaultBehavior = shaderSystem.GetLibrary(ParticleAssetPipeline.DefaultBehavior3D);
        _initMaterial = rendering.CreateComputeMaterial(shaderSystem.GetShader("GpuParticleInit3D"));
        // Bind the pool up front: OnPoolReallocated refreshes this, but the first
        // slice-recycle kill dispatch can precede any reallocation.
        _initMaterial.TrySetBuffer(ShaderResourceId.Particles, _pool.Particles);
        QuadMesh = rendering.MeshCenteredSprite;
    }

    /// <summary>
    /// The perspective camera the groups render with: its view-projection is bound
    /// to every group's material and its orientation provides the billboard basis.
    /// Must be set before <see cref="Render"/> is called.
    /// </summary>
    public CameraPerspectiveBuffer? Camera
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

    /// <summary>
    /// The depth state of the groups' materials; must match the pipeline's depth
    /// convention (the default <see cref="DepthStencilState.ReadReverseZ"/> fits the
    /// World3D deferred preset). Applies to materials created afterwards and all
    /// cached ones.
    /// </summary>
    public DepthStencilState DepthStencilState
    {
        get => _depthStencilState;
        set
        {
            _depthStencilState = value;
            foreach (GraphicsMaterial material in _materials.Values)
            {
                material.DepthStencilState = value;
            }
        }
    }

    /// <summary>The quad mesh the particles draw with (centered, position.xy in [-0.5, 0.5]).</summary>
    public Mesh QuadMesh { get; set; }

    /// <summary>The live effect instances.</summary>
    public IReadOnlyList<ParticleEffectInstance3D> Instances => _instances;

    /// <summary>The shared buffer pool (diagnostics).</summary>
    internal ParticleBufferPool<GpuParticle3D, EmitterParams3D> Pool => _pool;

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
    public ParticleEffectInstance3D CreateInstance(ParticleEffect3DAsset effect, in Transform3D transform, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (effect.Groups.Count == 0)
        {
            throw new InvalidDataException($"Particle effect '{effect.Name}' has no groups.");
        }

        uint indexCount = QuadMesh.GetSubMesh(0).IndexCount;
        var groups = new ParticleEffectInstance3D.GroupState[effect.Groups.Count];
        uint pendingSlot = 0;
        ParticleSlice pendingSlice = default;
        uint uploadMin = uint.MaxValue;
        uint uploadMax = 0;
        try
        {
            for (int i = 0; i < groups.Length; i++)
            {
                ParticleGroup3DAsset groupAsset = effect.Groups[i];
                pendingSlot = _pool.AllocateSlot();
                pendingSlice = _pool.AllocateSlice(Math.Max(groupAsset.MaxParticles, 1));
                EmitterParams3D parameters = EmitterParams3D.FromAsset(groupAsset, indexCount);
                parameters.Capacity = pendingSlice.Capacity;
                parameters.SliceOffset = pendingSlice.Offset;
                _pool.Params[(int)pendingSlot] = parameters;
                uploadMin = Math.Min(uploadMin, pendingSlot);
                uploadMax = Math.Max(uploadMax, pendingSlot);
                groups[i] = new ParticleEffectInstance3D.GroupState
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
        var instance = new ParticleEffectInstance3D(this, effect, transform, seed, groups);
        _instances.Add(instance);
        return instance;
    }

    /// <summary>
    /// Records the simulation of all active instances into
    /// <paramref name="commandBuffer"/> (see
    /// <see cref="GpuParticleSystem2D.RecordSimulation"/> for the recorded work and
    /// the delta-time freeze semantics).
    /// </summary>
    /// <param name="commandBuffer">The frame command buffer to record into.</param>
    /// <param name="deltaTime">The simulation time step in seconds.</param>
    public void RecordSimulation(GPUCommandBuffer commandBuffer, float deltaTime)
    {
        _pool.RecordMigration(commandBuffer);

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
        BuildDrawPlan();

        IReadOnlyList<(uint Offset, uint Count)> kills = _pool.PendingKills;
        bool anyActive = AnyActiveGroups();
        if (kills.Count == 0 && !anyActive)
        {
            return;
        }

        using (GPUCommandBuffer.ComputePass computePass = commandBuffer.BeginCompute())
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
            for (int i = 0; i < _drawGroups.Count; i++)
            {
                ParticleEffectInstance3D.GroupState group = _drawGroups[i];
                (ComputeMaterial emit, ComputeMaterial simulate) = GetBehaviorMaterials(group.Asset.Behavior);
                // The emit pass also resets the draw-args record, so it runs every
                // frame the group is active — even with zero spawns.
                var constant = new GpuParticleSlotConstant
                {
                    EmitterSlot = group.Slot,
                    DrawIndex = _drawIndices[i],
                    DrawStart = _drawStarts[i],
                };
                emit.DispatchByGroupWithConstant(
                    computePass, Math.Max((group.SpawnCount + 63) / 64, 1), 1, 1, constant);
                simulate.DispatchByGroupWithConstant(
                    computePass, (group.Slice.Capacity + 63) / 64, 1, 1, constant);
            }
        }
    }

    /// <summary>
    /// Records the batched indirect billboard draws of the frame's draw plan into
    /// the current pass: one shared-billboard-basis multi-draw per material (see
    /// <see cref="GpuParticleSystem2D.Render"/>).
    /// </summary>
    /// <param name="pass">The render pass scope of the scene pass.</param>
    public void Render(RenderPassScope pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (_camera == null)
        {
            throw new InvalidOperationException($"The {nameof(Camera)} of the particle system was not set.");
        }
        // The billboard basis is shared by every group of the frame (one push
        // constant per material batch); the per-draw identity travels through the
        // instance-data records.
        Quaternion cameraRotation = _camera.Data.Transform.Rotation;
        var constant = new GpuParticleDraw3DConstant
        {
            CameraRight = new Vector4(Vector3.Transform(Vector3.UnitY, cameraRotation), 0f),
            CameraUp = new Vector4(Vector3.Transform(Vector3.UnitZ, cameraRotation), 0f),
        };
        for (int i = 0; i < _drawBatches.Count; i++)
        {
            DrawBatch batch = _drawBatches[i];
            pass.MultiDrawIndexedIndirectWithConstant(
                QuadMesh,
                batch.Material,
                _pool.DrawArgs,
                batch.First * 20,
                batch.Count,
                _pool.InstanceData,
                constant);
        }
    }

    /// <summary>
    /// Rebuilds the per-frame draw plan (see <see cref="GpuParticleSystem2D.BuildDrawPlan"/>):
    /// the visible active groups bucketed by material (first-seen order), each
    /// assigned a drawIndex into the compacted draw-args array and a drawStart (the
    /// draw's firstInstance and base into the instance-data buffer, spaced by the
    /// group's slice capacity).
    /// </summary>
    private void BuildDrawPlan()
    {
        foreach (KeyValuePair<GraphicsMaterial, List<ParticleEffectInstance3D.GroupState>> bucket in _drawBuckets)
        {
            bucket.Value.Clear();
        }
        _drawMaterials.Clear();
        for (int i = 0; i < _instances.Count; i++)
        {
            ParticleEffectInstance3D instance = _instances[i];
            if (!instance.IsActive || !instance.IsVisible)
            {
                continue;
            }
            foreach (ParticleEffectInstance3D.GroupState group in instance.Groups)
            {
                if (!group.Active || !group.Visible)
                {
                    continue;
                }
                if (!_drawBuckets.TryGetValue(group.Material, out List<ParticleEffectInstance3D.GroupState>? bucket))
                {
                    bucket = [];
                    _drawBuckets[group.Material] = bucket;
                }
                else if (bucket.Count == 0)
                {
                    // The bucket survives across frames; an empty one means this is
                    // the material's first group of the frame, so it (re)enters the
                    // first-seen order.
                    _drawMaterials.Add(group.Material);
                }
                bucket.Add(group);
            }
        }

        _drawGroups.Clear();
        _drawIndices.Clear();
        _drawStarts.Clear();
        _drawBatches.Clear();
        uint drawIndex = 0;
        uint drawStart = 0;
        for (int m = 0; m < _drawMaterials.Count; m++)
        {
            List<ParticleEffectInstance3D.GroupState> bucket = _drawBuckets[_drawMaterials[m]];
            uint first = drawIndex;
            for (int g = 0; g < bucket.Count; g++)
            {
                ParticleEffectInstance3D.GroupState group = bucket[g];
                _drawGroups.Add(group);
                _drawIndices.Add(drawIndex);
                _drawStarts.Add(drawStart);
                drawIndex++;
                drawStart += group.Slice.Capacity;
            }
            _drawBatches.Add(new DrawBatch(_drawMaterials[m], first, (uint)bucket.Count));
        }
    }

    internal ref EmitterParams3D ParamsRef(uint slot) => ref _pool.Params.AsSpan()[(int)slot];

    internal void ReleaseInstance(ParticleEffectInstance3D instance)
    {
        foreach (ParticleEffectInstance3D.GroupState group in instance.Groups)
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

    private GraphicsMaterial GetOrCreateMaterial(ParticleGroup3DAsset group)
    {
        if (!_materials.TryGetValue(group, out GraphicsMaterial? material))
        {
            // See GpuParticleSystem2D.GetOrCreateMaterial: the .amat compiles
            // against the 3D pass template, the group's texture derives over the
            // surface's "texture" slot.
            MaterialAsset asset = group.Material ?? _defaultAsset;
            material = _materialCompiler.Compile(
                asset,
                _renderTemplate,
                (_, shader) => _rendering.CreateGraphicsMaterial(shader, $"particles3d:{group.Name}"));
            material.BlendState = group.Blend ?? BlendState.AlphaBlend;
            material.DepthStencilState = group.Depth ?? _depthStencilState;
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
        material.TrySetBuffer(ParticleShaderKeys.InstanceData, _pool.InstanceData);
    }

    // See GpuParticleSystem2D.BindOverLifeTextures: the authored gradient/curve
    // bake into 256x1 lookup textures; groups without them bind the shared 1x1
    // white (identity) texture. The system owns the baked textures.
    private void BindOverLifeTextures(ParticleGroup3DAsset group, GraphicsMaterial material)
    {
        if (group.ColorGradient is { Count: > 0 } gradientKeys)
        {
            byte[] pixels = new byte[ParticleOverLifeBake.TextureWidth * 4];
            ParticleOverLifeBake.BakeGradient(gradientKeys, pixels);
            Texture2D texture = _rendering.CreateTexture2D(
                pixels, ParticleOverLifeBake.TextureWidth, 1,
                new ImageLoadOption(name: $"particles3d:{group.Name}:colorGradient"));
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
                new ImageLoadOption(format: PixelFormat.R16Float, name: $"particles3d:{group.Name}:sizeCurve"));
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

/// <summary>
/// The push constant of the 3D particle billboard draws (32 bytes): the billboard
/// basis shared by every draw of the frame's material batches.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuParticleDraw3DConstant
{
    /// <summary>The world-space camera right vector (xyz).</summary>
    public Vector4 CameraRight;

    /// <summary>The world-space camera up vector (xyz).</summary>
    public Vector4 CameraUp;
}
