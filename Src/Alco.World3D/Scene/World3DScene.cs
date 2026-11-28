using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Alco.Graphics;

namespace Alco.World3D;

/// <summary>Owns a persistent 3D GPU scene and records its changes on one ordered render context.</summary>
/// <remarks>Use and dispose the scene on the render thread, before disposing its rendering system.</remarks>
public sealed partial class World3DScene : IDisposable
{
    /// <summary>The maximum number of independently resident LODs per geometry.</summary>
    public const int MaxGeometryLods = 4;

    private readonly Guid _id = Guid.NewGuid();
    private readonly RenderingSystem _system;
    private readonly GpuStore _gpu;
    private readonly RenderGraph _graph;
    private readonly SlotMap<GeometryEntry> _geometries = new();
    private readonly SlotMap<MaterialEntry> _materials = new();
    private readonly SlotMap<InstanceEntry> _instances = new();
    private readonly SlotMap<SectorEntry> _sectors = new();
    private readonly SlotMap<TextureEntry> _textures = new();
    private readonly List<Action> _retireOnSubmit = [];
    private readonly ConcurrentQueue<Action[]> _retired = new();
    private ulong _shadowSceneRevision = 1;
    private bool _disposed;

    /// <summary>Creates an empty scene using the given rendering system.</summary>
    /// <param name="system">The owner of scene buffers, textures, and shaders.</param>
    public World3DScene(RenderingSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        ObjectDisposedException.ThrowIf(system.IsDisposed, system);
        _system = system;
        _gpu = new GpuStore(system);
        ShaderCompiler compiler = system.Shaders.Compiler;
        compiler.RegisterVirtualModule("World3DOcclusionCommon", "World3DOcclusionCommon.slang", ReadWorldShader("World3DOcclusionCommon"));
        compiler.RegisterVirtualModule("World3DShadowSample", "World3DShadowSample.slang", ReadWorldShader("World3DShadowSample"));
        _gbufferPrograms = new GBufferPrograms(system);
        _graph = new RenderGraph(system, new Extent2D(1, 1), "World3D")
            .Use(new SyncNode(_gpu))
            .Use(new ClearNode(this))
            .Use(new GeometryPasses(this))
            .Use(new ShadowPasses(this))
            .Use(new FarShadowPasses(this))
            .Use(new VolumetricCloudsPreNode(this))
            .Use(new VoxelGiPasses(this));
        AddLightingPipeline();
    }

    /// <summary>Gets the upload budget for vertex and index data per recorded frame, rounded down to four bytes.</summary>
    public ulong GeometryUploadBudgetBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>Gets or sets the maximum camera draw distance; nonpositive values leave it unlimited.</summary>
    public float MaximumDrawDistance { get; set; }

    /// <summary>Gets the GPU occlusion culling configuration for this scene.</summary>
    public World3DOcclusionSettings Occlusion { get; } = new();

    /// <summary>Gets the directional CSM, static far cascade, far heightfield, and RSM shadow configuration.</summary>
    public World3DShadowSettings Shadows { get; } = new();

    /// <summary>Gets or sets the diagnostic visualization overlay for deferred lighting.</summary>
    public World3DLightingDebugView LightingDebugView { get; set; } = World3DLightingDebugView.None;

    /// <summary>Gets the GBuffer submissions recorded in the most recent frame.</summary>
    public World3DDrawStats LastRecordedDraws { get; private set; }

    /// <summary>Gets the occlusion configuration and latest completed async GPU cull readback counters.</summary>
    public World3DOcclusionStats LastRecordedOcclusion { get; private set; }

    /// <summary>Gets the shadow update, cache, and far-field status for the most recently recorded frame.</summary>
    public World3DShadowStats LastRecordedShadows { get; private set; }

    /// <summary>Gets the number of logically live instances.</summary>
    public int InstanceCount => _instances.Count;

    /// <summary>Gets the persistent uploads of the most recently recorded scene frame.</summary>
    public World3DSceneSyncStats LastRecordedSync { get; private set; }

    /// <summary>Creates an empty sector, or returns false when the sector table is full.</summary>
    /// <param name="handle">Receives the new sector handle.</param>
    /// <returns>Whether the sector was attached.</returns>
    public bool TryAttachSector(out WorldSectorHandle handle)
    {
        BeginEdit();
        handle = default;
        if (!_sectors.TryAdd(new SectorEntry(), out int slot, out uint generation))
            return false;
        handle = new WorldSectorHandle(_id, slot, generation);
        return true;
    }

    /// <summary>Unloads this sector's instances without removing shared geometry or materials.</summary>
    /// <param name="handle">The sector to detach.</param>
    /// <returns>Whether the sector was live in this scene.</returns>
    public bool TryDetachSector(WorldSectorHandle handle)
    {
        BeginEdit();
        if (!TryGetSector(handle, out SectorEntry? sector))
            return false;
        bool detachedAny = sector.Instances.Count != 0;
        foreach (int slot in sector.Instances)
            RemoveInstance(slot, _instances.GetLive(slot), unlinkSector: false);
        _sectors.Remove(handle.Slot);
        _sectors.Reclaim(handle.Slot);
        if (detachedAny)
            MarkShadowSceneDirty();
        return true;
    }

    /// <summary>Registers immutable geometry bounds enclosing every LOD that may be uploaded.</summary>
    /// <param name="localMin">The lower corner of the geometry's local bound.</param>
    /// <param name="localMax">The upper corner of the geometry's local bound.</param>
    /// <param name="handle">Receives the new geometry handle.</param>
    /// <returns>Whether the geometry was registered.</returns>
    public bool TryRegisterGeometry(Vector3 localMin, Vector3 localMax, out WorldGeometryHandle handle)
    {
        BeginEdit();
        handle = default;
        if (!IsBound(localMin, localMax) || !_geometries.TryAdd(new GeometryEntry(localMin, localMax), out int slot, out uint generation))
            return false;
        handle = new WorldGeometryHandle(_id, slot, generation);
        return true;
    }

    /// <summary>Removes unreferenced geometry and cancels its queued LOD uploads.</summary>
    /// <param name="handle">The geometry to remove.</param>
    /// <returns>Whether the geometry was live and unreferenced.</returns>
    public bool TryRemoveGeometry(WorldGeometryHandle handle)
    {
        BeginEdit();
        if (!TryGetGeometry(handle, out GeometryEntry? geometry) || geometry.Instances.Count != 0)
            return false;
        RemoveGeometryLods(geometry);
        _gpu.ClearGeometry(handle.Slot);
        _geometries.Remove(handle.Slot);
        _retireOnSubmit.Add(() => _geometries.Reclaim(handle.Slot));
        return true;
    }

    /// <summary>Registers a material and retains its texture bindings.</summary>
    /// <param name="description">The PBR factors, routing, and texture handles.</param>
    /// <param name="handle">Receives the new material handle.</param>
    /// <returns>Whether all bindings were valid and the material was registered.</returns>
    public bool TryRegisterMaterial(in World3DMaterialDesc description, out WorldMaterialHandle handle)
    {
        BeginEdit();
        handle = default;
        if (!TryResolveMaterial(in description, out MaterialGpu gpu, out MaterialTextures bindings) ||
            !_materials.TryAdd(new MaterialEntry(bindings, new MaterialRoute(in description)), out int slot, out uint generation))
            return false;
        bindings.Retain();
        _gpu.SetMaterial(slot, in gpu);
        handle = new WorldMaterialHandle(_id, slot, generation);
        return true;
    }

    /// <summary>Updates a material without changing its handle or the instances that refer to it.</summary>
    /// <param name="handle">The material to update.</param>
    /// <param name="description">The replacement factors, routing, and bindings.</param>
    /// <returns>Whether the material and replacement bindings were valid.</returns>
    public bool TryUpdateMaterial(WorldMaterialHandle handle, in World3DMaterialDesc description)
    {
        BeginEdit();
        if (!TryGetMaterial(handle, out MaterialEntry? material) ||
            !TryResolveMaterial(in description, out MaterialGpu gpu, out MaterialTextures bindings))
            return false;
        if (material.Textures != bindings)
        {
            _retireOnSubmit.Add(material.Textures.Release);
            bindings.Retain();
            material.Textures = bindings;
        }
        MaterialRoute route = new(in description);
        bool hadNonBlendGi = material.Route.AlphaMode != World3DAlphaMode.Blend;
        if (material.Route != route)
        {
            material.Route = route;
            _dirtyDrawInstances.UnionWith(material.Instances);
        }
        _gpu.SetMaterial(handle.Slot, in gpu);
        if (material.Instances.Count != 0)
        {
            MarkShadowSceneDirty();
            if (hadNonBlendGi || route.AlphaMode != World3DAlphaMode.Blend)
            {
                if (material.Instances.Count > 48)
                {
                    _voxelGiForceReset = true;
                }
                else
                {
                    foreach (int slot in material.Instances)
                    {
                        InstanceEntry inst = _instances.GetLive(slot);
                        if (!inst.Description.Dynamic && inst.Description.AffectsGlobalIllumination)
                            MarkVoxelGiSpatialDirty(inst.Spatial);
                    }
                }
            }
        }
        return true;
    }

    /// <summary>Removes a material not referenced by any live instance.</summary>
    /// <param name="handle">The material to remove.</param>
    /// <returns>Whether the material was live and unreferenced.</returns>
    public bool TryRemoveMaterial(WorldMaterialHandle handle)
    {
        BeginEdit();
        if (!TryGetMaterial(handle, out MaterialEntry? material) || material.Instances.Count != 0)
            return false;
        _gpu.SetMaterial(handle.Slot, default);
        _materials.Remove(handle.Slot);
        _retireOnSubmit.Add(() =>
        {
            material.Textures.Release();
            _materials.Reclaim(handle.Slot);
        });
        return true;
    }

    /// <summary>Creates a scene-owned 2D sampled texture from tightly packed mip payloads.</summary>
    /// <param name="description">A single-layer, single-sample color texture description.</param>
    /// <param name="mipData">All mip levels in order, with no row padding.</param>
    /// <param name="handle">Receives the texture handle; the scene owns the GPU object.</param>
    /// <returns>Whether the description was valid and device storage and descriptor capacity were available.</returns>
    public bool TryCreateTexture(in TextureDesc description, ReadOnlySpan<byte> mipData, out WorldTextureHandle handle)
    {
        BeginEdit();
        handle = default;
        if (description.Type != TextureType.TwoD || description.LayerCount != 1 || description.SampleCount != 1 ||
            description.Extent.X == 0 || description.Extent.Y == 0 || description.Extent.Z != 1 ||
            description.MipLevels == 0 || description.MipLevels > 1 + BitOperations.Log2(Math.Max(description.Extent.X, description.Extent.Y)) ||
            (uint)description.Format >= (uint)Format.Undefined || mipData.IsEmpty)
            return false;
        TextureFormatInfo format = Texture.GetFormatInfo(description.Format);
        if (format.Depth || format.Stencil || format.BytesPerBlock == 0)
            return false;

        Span<int> mipSizes = stackalloc int[(int)description.MipLevels];
        int total = 0;
        for (int mip = 0; mip < mipSizes.Length; ++mip)
        {
            ulong x = Math.Max(1u, description.Extent.X >> mip);
            ulong y = Math.Max(1u, description.Extent.Y >> mip);
            ulong blocksX = (x + format.BlockExtent.X - 1) / format.BlockExtent.X;
            ulong blocksY = (y + format.BlockExtent.Y - 1) / format.BlockExtent.Y;
            if (blocksX * blocksY > (ulong)(mipData.Length - total) / format.BytesPerBlock)
                return false;
            total += mipSizes[mip] = (int)(blocksX * blocksY * format.BytesPerBlock);
        }
        if (total != mipData.Length)
            return false;

        Texture texture;
        try
        {
            TextureDesc sampled = description with { Usage = TextureUsage.Sampled | TextureUsage.TransferDestination };
            texture = _system.CreateTexture(sampled);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (GPUException e) when (e.Error is Error.OutOfMemory or Error.Unsupported)
        {
            return false;
        }
        try
        {
            int offset = 0;
            for (int mip = 0; mip < mipSizes.Length; ++mip)
            {
                _system.UploadTexture(texture, mipData.Slice(offset, mipSizes[mip]), new TextureCopyDesc { MipLevel = (uint)mip });
                offset += mipSizes[mip];
            }
            if (_textures.TryAdd(new TextureEntry(texture), out int slot, out uint generation))
            {
                handle = new WorldTextureHandle(_id, slot, generation);
                return true;
            }
        }
        catch
        {
            texture.Dispose();
            throw;
        }
        texture.Dispose();
        return false;
    }

    /// <summary>Releases a texture no longer leased by live or in-flight material entries.</summary>
    /// <param name="handle">The texture to remove.</param>
    /// <returns>Whether the texture was live and its material leases had retired.</returns>
    public bool TryRemoveTexture(WorldTextureHandle handle)
    {
        BeginEdit();
        if (!TryGetTexture(handle, out TextureEntry? texture) || texture.References != 0)
            return false;
        _textures.Remove(handle.Slot);
        _textures.Reclaim(handle.Slot);
        texture.Texture.Dispose();
        return true;
    }

    /// <summary>Adds an instance referencing live resources and a live sector in this scene.</summary>
    /// <param name="description">The instance placement, resources, and opacity.</param>
    /// <param name="handle">Receives the new instance handle.</param>
    /// <returns>Whether the instance was added.</returns>
    public bool TryAddInstance(in World3DInstanceDesc description, out WorldInstanceHandle handle)
    {
        BeginEdit();
        handle = default;
        if (!TryResolveInstance(in description, out InstanceGpu gpu, out InstanceEntry? instance) ||
            !_instances.TryAdd(instance, out int slot, out uint generation))
            return false;
        LinkInstance(slot, instance);
        _dirtyDrawInstances.Add(slot);
        _gpu.SetInstance(slot, in gpu);
        MarkShadowSceneDirty();
        if (!description.Dynamic && description.AffectsGlobalIllumination
            && instance.Material.Route.AlphaMode != World3DAlphaMode.Blend)
            MarkVoxelGiSpatialDirty(gpu.Spatial);
        handle = new WorldInstanceHandle(_id, slot, generation);
        return true;
    }

    /// <summary>Replaces an instance's placement, resources, and sector without changing its handle.</summary>
    /// <param name="handle">The instance to update.</param>
    /// <param name="description">The replacement placement, resources, and opacity.</param>
    /// <returns>Whether the instance and the new description were valid.</returns>
    public bool TryUpdateInstance(WorldInstanceHandle handle, in World3DInstanceDesc description)
    {
        BeginEdit();
        if (!TryGetInstanceEntry(handle, out InstanceEntry? previous) ||
            !TryResolveInstance(in description, out InstanceGpu gpu, out InstanceEntry? instance))
            return false;
        if (!ReferenceEquals(previous.Geometry, instance.Geometry) || GetDrawRoute(previous) != GetDrawRoute(instance))
            _dirtyDrawInstances.Add(handle.Slot);
        if (!previous.Description.Dynamic && previous.Description.AffectsGlobalIllumination
            && previous.Material.Route.AlphaMode != World3DAlphaMode.Blend)
            MarkVoxelGiSpatialDirty(previous.Spatial);
        UnlinkInstance(handle.Slot, previous, unlinkSector: true);
        _instances.SetLive(handle.Slot, instance);
        LinkInstance(handle.Slot, instance);
        _gpu.SetInstance(handle.Slot, in gpu);
        MarkShadowSceneDirty();
        if (!description.Dynamic && description.AffectsGlobalIllumination
            && instance.Material.Route.AlphaMode != World3DAlphaMode.Blend)
            MarkVoxelGiSpatialDirty(gpu.Spatial);
        return true;
    }

    /// <summary>Gets the authoritative description of a live instance.</summary>
    /// <param name="handle">The instance to query.</param>
    /// <param name="description">Receives the current CPU description.</param>
    /// <returns>Whether the handle identifies a live instance in this scene.</returns>
    public bool TryGetInstance(WorldInstanceHandle handle, out World3DInstanceDesc description)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        description = default;
        if (!TryGetInstanceEntry(handle, out InstanceEntry? instance))
            return false;
        description = instance.Description;
        return true;
    }

    /// <summary>Removes an instance, invalidating its CPU handle immediately.</summary>
    /// <param name="handle">The instance to remove.</param>
    /// <returns>Whether the instance was live in this scene.</returns>
    public bool TryRemoveInstance(WorldInstanceHandle handle)
    {
        BeginEdit();
        if (!TryGetInstanceEntry(handle, out InstanceEntry? instance))
            return false;
        RemoveInstance(handle.Slot, instance, unlinkSector: true);
        return true;
    }

    /// <summary>Retires the scene's GPU resources behind outstanding work.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        ReconcileFrame();
        _disposed = true;
        _graph.Dispose();
        _gbufferPrograms.Dispose();
        _drawLayout.Dispose();
        DisposeOcclusionResources();
        DisposeShadowResources();
        DisposeFarShadowResources();
        DisposeVolumetricClouds();
        DisposeVoxelGi();
        DisposeSsr();
        DisposeForwardPasses();
        _selection?.Dispose();
        _phase2Selection?.Dispose();
        _drawArgs?.Dispose();
        _phase2DrawArgs?.Dispose();
        _drawRefs?.Dispose();
        _phase2DrawRefs?.Dispose();
        _gpu.Dispose();
        foreach (TextureEntry texture in _textures.LiveValues())
            texture.Texture.Dispose();
        foreach (PendingLod pending in _pendingLods)
            pending.Source?.Dispose();
        _textures.Clear();
        _geometries.Clear();
        _materials.Clear();
        _instances.Clear();
        _sectors.Clear();
        _pendingLods.Clear();
        _recordedLods.Clear();
        _retireOnSubmit.Clear();
        _retired.Clear();
    }

    private void BeginEdit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_recordedFrame != 0 && _context!.CurrentFrameId == _recordedFrame)
            throw new InvalidOperationException("Submit or discard the recorded World3D frame before editing the scene.");
        ReconcileFrame();
        while (_retired.TryDequeue(out Action[]? batch))
        {
            foreach (Action reclaim in batch)
                reclaim();
        }
    }

    private bool TryGetGeometry(WorldGeometryHandle handle, [NotNullWhen(true)] out GeometryEntry? geometry) =>
        _geometries.TryGet(handle.SceneId == _id ? handle.Slot : -1, handle.Generation, out geometry);

    private bool TryGetMaterial(WorldMaterialHandle handle, [NotNullWhen(true)] out MaterialEntry? material) =>
        _materials.TryGet(handle.SceneId == _id ? handle.Slot : -1, handle.Generation, out material);

    private bool TryGetSector(WorldSectorHandle handle, [NotNullWhen(true)] out SectorEntry? sector) =>
        _sectors.TryGet(handle.SceneId == _id ? handle.Slot : -1, handle.Generation, out sector);

    private bool TryGetTexture(WorldTextureHandle handle, [NotNullWhen(true)] out TextureEntry? texture) =>
        _textures.TryGet(handle.SceneId == _id ? handle.Slot : -1, handle.Generation, out texture);

    private bool TryGetInstanceEntry(WorldInstanceHandle handle, [NotNullWhen(true)] out InstanceEntry? instance) =>
        _instances.TryGet(handle.SceneId == _id ? handle.Slot : -1, handle.Generation, out instance);

    private bool TryResolveInstance(in World3DInstanceDesc desc, out InstanceGpu gpu, [NotNullWhen(true)] out InstanceEntry? instance)
    {
        gpu = default;
        instance = null;
        if (!IsUnit(desc.Fade) || !TryGetGeometry(desc.Geometry, out GeometryEntry? geometry) ||
            !TryGetMaterial(desc.Material, out MaterialEntry? material) || !TryGetSector(desc.Sector, out SectorEntry? sector) ||
            !World3DSceneCoordinates.TryPackInstance(desc.Transform, geometry.LocalMin, geometry.LocalMax, out World3DInstanceSpatialGpu spatial))
            return false;
        uint instanceFlags = 1u
            | (desc.CastsShadow ? 2u : 0u)
            | (desc.Dynamic ? 4u : 0u)
            | (desc.AffectsGlobalIllumination ? 8u : 0u);
        gpu = new InstanceGpu(spatial, (uint)desc.Geometry.Slot, (uint)desc.Material.Slot, instanceFlags, desc.Fade);
        instance = new InstanceEntry(desc, geometry, material, sector, spatial);
        return true;
    }

    private void MarkShadowSceneDirty() => ++_shadowSceneRevision;

    private static void LinkInstance(int slot, InstanceEntry instance)
    {
        instance.Geometry.Instances.Add(slot);
        instance.Material.Instances.Add(slot);
        instance.Sector.Instances.Add(slot);
    }

    private static void UnlinkInstance(int slot, InstanceEntry instance, bool unlinkSector)
    {
        instance.Geometry.Instances.Remove(slot);
        instance.Material.Instances.Remove(slot);
        if (unlinkSector)
            instance.Sector.Instances.Remove(slot);
    }

    private void RemoveInstance(int slot, InstanceEntry instance, bool unlinkSector)
    {
        if (!instance.Description.Dynamic && instance.Description.AffectsGlobalIllumination
            && instance.Material.Route.AlphaMode != World3DAlphaMode.Blend)
            MarkVoxelGiSpatialDirty(instance.Spatial);
        UnlinkInstance(slot, instance, unlinkSector);
        _instances.Remove(slot);
        _dirtyDrawInstances.Add(slot);
        _gpu.SetInstance(slot, default);
        MarkShadowSceneDirty();
        _retireOnSubmit.Add(() => _instances.Reclaim(slot));
    }

    private bool TryResolveMaterial(in World3DMaterialDesc desc, out MaterialGpu gpu, out MaterialTextures bindings)
    {
        gpu = default;
        bindings = default;
        if (!IsUnit(desc.Metallic) || !IsUnit(desc.Roughness) || !IsUnit(desc.AlphaCutoff) ||
            !IsUnit(desc.Transmission) || (desc.Ior != 0f && !(desc.Ior >= 1f && desc.Ior <= 3f)) ||
            !IsFinite(desc.Emissive) ||
            !IsFinite(new Vector3(desc.BaseColor.X, desc.BaseColor.Y, desc.BaseColor.Z)) || !IsUnit(desc.BaseColor.W) ||
            desc.AlphaMode > World3DAlphaMode.Blend || (desc.SurfaceModule != null && string.IsNullOrWhiteSpace(desc.SurfaceModule)) ||
            !TryResolveTexture(desc.BaseColorTexture, out TextureEntry? color) ||
            !TryResolveTexture(desc.NormalTexture, out TextureEntry? normal) ||
            !TryResolveTexture(desc.MetallicRoughnessTexture, out TextureEntry? metallicRoughness) ||
            !TryResolveTexture(desc.EmissiveTexture, out TextureEntry? emissive))
            return false;
        SharedSamplers samplers = _system.Samplers;
        Sampler? sampler = desc.Sampler switch
        {
            World3DMaterialSampler.LinearRepeat => samplers.LinearRepeat,
            World3DMaterialSampler.LinearClamp => samplers.LinearClamp,
            World3DMaterialSampler.NearestRepeat => samplers.NearestRepeat,
            World3DMaterialSampler.NearestClamp => samplers.NearestClamp,
            World3DMaterialSampler.AnisotropicRepeat => samplers.AnisotropicRepeat,
            World3DMaterialSampler.AnisotropicClamp => samplers.AnisotropicClamp,
            _ => null,
        };
        if (sampler == null)
            return false;
        string moduleKey = string.IsNullOrEmpty(desc.SurfaceModule) ? StandardVoxelSurfaceModule : desc.SurfaceModule;
        if (!_surfaceBucketIds.ContainsKey(moduleKey) && _surfaceBucketModules.Count >= 256)
            return false;
        uint surfaceBucket = ResolveSurfaceBucket(moduleKey);
        GPUTextureId white = _system.TextureWhite.SampledId;
        float effectiveIor = desc.Ior == 0f ? 1.5f : desc.Ior;
        uint transmissionByte = (uint)Math.Clamp((int)MathF.Round(desc.Transmission * 255f), 0, 255);
        uint iorByte = (uint)Math.Clamp((int)MathF.Round(((effectiveIor - 1f) * 0.5f) * 255f), 0, 255);
        uint flags = (uint)desc.AlphaMode
            | (desc.DoubleSided ? 4u : 0u)
            | (surfaceBucket << 8)
            | (transmissionByte << 16)
            | (iorByte << 24);
        gpu = new MaterialGpu(desc.BaseColor, new Vector4(desc.Emissive, desc.AlphaCutoff),
            new Vector2(desc.Metallic, desc.Roughness), flags, sampler.Id, color?.Texture.SampledId ?? white,
            normal?.Texture.SampledId ?? GPUTextureId.Invalid, metallicRoughness?.Texture.SampledId ?? white,
            emissive?.Texture.SampledId ?? GPUTextureId.Invalid);
        bindings = new MaterialTextures(color, normal, metallicRoughness, emissive);
        return true;
    }

    private bool TryResolveTexture(WorldTextureHandle handle, out TextureEntry? texture)
    {
        texture = null;
        return handle == default || TryGetTexture(handle, out texture);
    }

    private static bool IsUnit(float value) => value >= 0 && value <= 1;

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static bool Contains(Vector3 min, Vector3 max, Vector3 point) =>
        point.X >= min.X && point.Y >= min.Y && point.Z >= min.Z && point.X <= max.X && point.Y <= max.Y && point.Z <= max.Z;

    private static bool IsBound(Vector3 min, Vector3 max) => IsFinite(min) && IsFinite(max) && Contains(min, max, min);

    private sealed record InstanceEntry(World3DInstanceDesc Description, GeometryEntry Geometry, MaterialEntry Material,
        SectorEntry Sector, World3DInstanceSpatialGpu Spatial);

    private sealed class SectorEntry
    {
        public readonly HashSet<int> Instances = [];
    }

    private sealed class MaterialEntry(MaterialTextures textures, MaterialRoute route)
    {
        public MaterialTextures Textures = textures;
        public MaterialRoute Route = route;
        public readonly HashSet<int> Instances = [];
    }

    private sealed class TextureEntry(Texture texture)
    {
        public readonly Texture Texture = texture;
        public int References;
    }

    private readonly record struct MaterialTextures(TextureEntry? Color, TextureEntry? Normal, TextureEntry? MetallicRoughness, TextureEntry? Emissive)
    {
        public void Retain() => ChangeReferences(1);
        public void Release() => ChangeReferences(-1);

        private void ChangeReferences(int delta)
        {
            if (Color != null) Color.References += delta;
            if (Normal != null) Normal.References += delta;
            if (MetallicRoughness != null) MetallicRoughness.References += delta;
            if (Emissive != null) Emissive.References += delta;
        }
    }

    private sealed class SlotMap<T> where T : class
    {
        private readonly List<(T? Value, uint Generation)> _slots = [];
        private readonly Stack<int> _free = [];

        public int Count { get; private set; }

        /// <summary>Gets the exclusive upper bound of the slot range, including holes.</summary>
        public int End => _slots.Count;

        public bool TryAdd(T value, out int slot, out uint generation)
        {
            generation = 0;
            if (_free.TryPop(out slot))
            {
                generation = _slots[slot].Generation;
                _slots[slot] = (value, generation);
            }
            else
            {
                if (_slots.Count == MaxTableSlots)
                    return false;
                slot = _slots.Count;
                generation = 1;
                _slots.Add((value, generation));
            }
            ++Count;
            return true;
        }

        public bool TryGet(int slot, uint generation, [NotNullWhen(true)] out T? value)
        {
            value = null;
            if ((uint)slot >= (uint)_slots.Count || generation == 0 || _slots[slot].Generation != generation)
                return false;
            value = _slots[slot].Value;
            return value != null;
        }

        public T GetLive(int slot) => _slots[slot].Value!;

        /// <summary>Gets the current value at an existing slot, or null for a tombstone.</summary>
        public T? GetSlot(int slot) => _slots[slot].Value;

        public void SetLive(int slot, T value) => _slots[slot] = (value, _slots[slot].Generation);

        public void Remove(int slot)
        {
            uint generation = _slots[slot].Generation;
            _slots[slot] = (null, generation == uint.MaxValue ? 0 : generation + 1);
            --Count;
        }

        public void Reclaim(int slot)
        {
            if (_slots[slot].Generation != 0)
                _free.Push(slot);
        }

        public IEnumerable<T> LiveValues()
        {
            foreach ((T? value, _) in _slots)
            {
                if (value != null)
                    yield return value;
            }
        }

        public void Clear()
        {
            _slots.Clear();
            _free.Clear();
            Count = 0;
        }
    }
}
