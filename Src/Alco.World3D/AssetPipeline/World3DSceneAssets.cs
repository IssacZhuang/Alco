using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>Opaque handle representing one instantiated <see cref="ModelAsset"/> managed by <see cref="World3DSceneAssets"/>.</summary>
public sealed class World3DModelInstance
{
    internal World3DModelInstance(ModelAsset model, WorldSectorHandle sector, IReadOnlyList<WorldInstanceHandle> instances)
    {
        Model = model;
        Sector = sector;
        Instances = instances;
    }

    /// <summary>The source model asset that produced this instance.</summary>
    public ModelAsset Model { get; }

    /// <summary>The owning streaming sector handle.</summary>
    public WorldSectorHandle Sector { get; }

    /// <summary>The underlying per-submesh scene instance handles.</summary>
    public IReadOnlyList<WorldInstanceHandle> Instances { get; }

    /// <summary>Whether this model instance has been destroyed or its parent bridge disposed.</summary>
    public bool IsDestroyed { get; internal set; }
}

/// <summary>
/// Ref-counted bridge connecting <see cref="Alco.IO.AssetSystem"/> assets (<see cref="Image"/>,
/// <see cref="MaterialAsset"/>, <see cref="MeshAsset"/>, <see cref="ModelAsset"/>) to a live
/// <see cref="World3DScene"/> with zero-copy external texture registration and deferred GPU reclamation.
/// </summary>
public sealed class World3DSceneAssets : IDisposable
{
    private readonly World3DScene _scene;
    private readonly Dictionary<Image, TextureRef> _textures = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MaterialAsset, MaterialRef> _materials = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<GeometryKey, GeometryRef> _geometries = [];
    private readonly Dictionary<World3DModelInstance, List<InstancePart>> _liveModels = new(ReferenceEqualityComparer.Instance);
    private readonly List<WorldGeometryHandle> _pendingRemoveGeometries = [];
    private readonly List<WorldMaterialHandle> _pendingRemoveMaterials = [];
    private readonly List<WorldTextureHandle> _pendingRemoveTextures = [];
    private bool _disposed;

    /// <summary>Creates a ref-counted asset bridge bound to <paramref name="scene"/>.</summary>
    /// <param name="scene">The target World3D scene; must outlive this bridge.</param>
    public World3DSceneAssets(World3DScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _scene = scene;
    }

    /// <summary>Gets the target scene driven by this asset bridge.</summary>
    public World3DScene Scene => _scene;

    /// <summary>Gets the count of cached textures with non-zero CPU references.</summary>
    public int ActiveTextureCount => _textures.Count;

    /// <summary>Gets the count of cached materials with non-zero CPU references.</summary>
    public int ActiveMaterialCount => _materials.Count;

    /// <summary>Gets the count of cached geometries with non-zero CPU references.</summary>
    public int ActiveGeometryCount => _geometries.Count;

    /// <summary>Gets the number of live instantiated models currently tracked by this bridge.</summary>
    public int LiveModelCount => _liveModels.Count;

    /// <summary>Acquires a ref-counted scene texture handle backed by <paramref name="image"/>'s GPU texture.</summary>
    /// <param name="image">The image asset to register or retain.</param>
    /// <param name="handle">Receives the scene texture handle.</param>
    /// <returns><see langword="true"/> when the texture was retained or registered successfully.</returns>
    public bool TryAcquireTexture(Image image, out WorldTextureHandle handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(image);

        if (_textures.TryGetValue(image, out TextureRef? existing))
        {
            ++existing.RefCount;
            handle = existing.Handle;
            return true;
        }

        if (!_scene.TryRegisterExternalTexture(image.Texture, out handle))
            return false;

        _textures.Add(image, new TextureRef(handle));
        return true;
    }

    /// <summary>Releases one CPU reference to <paramref name="image"/>, queueing its scene handle for deferred removal on zero.</summary>
    /// <param name="image">The previously acquired image asset.</param>
    /// <returns><see langword="true"/> when a reference existed and was decremented.</returns>
    public bool ReleaseTexture(Image image)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(image);

        if (!_textures.TryGetValue(image, out TextureRef? entry))
            return false;

        if (--entry.RefCount == 0)
        {
            _textures.Remove(image);
            _pendingRemoveTextures.Add(entry.Handle);
        }
        return true;
    }

    /// <summary>Acquires a ref-counted scene material handle for <paramref name="material"/> and its bound textures.</summary>
    /// <param name="material">The PBR or custom-surface material asset.</param>
    /// <param name="handle">Receives the scene material handle.</param>
    /// <returns><see langword="true"/> when the material and all bound textures were valid.</returns>
    public bool TryAcquireMaterial(MaterialAsset material, out WorldMaterialHandle handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(material);

        if (_materials.TryGetValue(material, out MaterialRef? existing))
        {
            ++existing.RefCount;
            handle = existing.Handle;
            return true;
        }

        handle = default;
        List<Image> boundImages = [];
        if (!TryBuildDescAndRetainTextures(material, boundImages, out World3DMaterialDesc desc))
        {
            ReleaseImageList(boundImages);
            return false;
        }

        if (!_scene.TryRegisterMaterial(in desc, out handle))
        {
            ReleaseImageList(boundImages);
            return false;
        }

        _materials.Add(material, new MaterialRef(handle, boundImages));
        return true;
    }

    /// <summary>Hot-reloads a currently live <paramref name="material"/> inside the scene without changing its handle.</summary>
    /// <param name="material">The updated material asset whose cached handle should refresh.</param>
    /// <returns><see langword="true"/> when the material was cached and updated in the scene.</returns>
    public bool TryUpdateMaterial(MaterialAsset material)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(material);

        if (!_materials.TryGetValue(material, out MaterialRef? existing))
            return false;

        List<Image> newImages = [];
        if (!TryBuildDescAndRetainTextures(material, newImages, out World3DMaterialDesc desc))
        {
            ReleaseImageList(newImages);
            return false;
        }

        if (!_scene.TryUpdateMaterial(existing.Handle, in desc))
        {
            ReleaseImageList(newImages);
            return false;
        }

        ReleaseImageList(existing.BoundImages);
        existing.BoundImages = newImages;
        return true;
    }

    /// <summary>Releases one CPU reference to <paramref name="material"/>, queueing handle and texture cleanup on zero.</summary>
    /// <param name="material">The previously acquired material asset.</param>
    /// <returns><see langword="true"/> when a reference existed and was decremented.</returns>
    public bool ReleaseMaterial(MaterialAsset material)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(material);

        if (!_materials.TryGetValue(material, out MaterialRef? existing))
            return false;

        if (--existing.RefCount == 0)
        {
            _materials.Remove(material);
            ReleaseImageList(existing.BoundImages);
            _pendingRemoveMaterials.Add(existing.Handle);
        }
        return true;
    }

    /// <summary>Acquires a ref-counted multi-LOD geometry handle for one submesh of <paramref name="mesh"/>.</summary>
    /// <param name="mesh">The source mesh asset.</param>
    /// <param name="submeshIndex">Zero-based submesh index within LOD 0.</param>
    /// <param name="handle">Receives the scene geometry handle.</param>
    /// <param name="lodMinimumDistances">Optional per-LOD camera distance thresholds; when omitted, thresholds derive from LOD error and bounds.</param>
    /// <returns><see langword="true"/> when the geometry was retained or registered with at least LOD 0.</returns>
    public bool TryAcquireGeometry(
        MeshAsset mesh,
        int submeshIndex,
        out WorldGeometryHandle handle,
        ReadOnlySpan<float> lodMinimumDistances = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(mesh);

        handle = default;
        if (mesh.IsDisposed || mesh.LodCount == 0 || (uint)submeshIndex >= (uint)mesh.GetSubMeshes(0).Length)
            return false;

        GeometryKey key = new(mesh, submeshIndex);
        if (_geometries.TryGetValue(key, out GeometryRef? existing))
        {
            ++existing.RefCount;
            handle = existing.Handle;
            return true;
        }

        int lodCount = Math.Min(mesh.LodCount, World3DScene.MaxGeometryLods);
        Span<ExtractedLod?> extracted = new ExtractedLod?[lodCount];
        Vector3 unionMin = new(float.MaxValue);
        Vector3 unionMax = new(float.MinValue);
        int validLods = 0;

        for (int lod = 0; lod < lodCount; ++lod)
        {
            MeshLodData lodData = mesh.ReadLod(lod);
            if (submeshIndex >= lodData.SubMeshes.Count ||
                !lodData.TryExtractWorld3DGeometry(submeshIndex, out VertexPBR[] vertices, out uint[] indices,
                    out Vector3 lodMin, out Vector3 lodMax))
            {
                if (lod == 0)
                    return false;
                break;
            }

            float minDist = ResolveLodDistance(mesh, lod, lodMinimumDistances, lodMin, lodMax,
                validLods > 0 ? extracted[validLods - 1]!.Value.MinDistance : 0f);
            extracted[validLods++] = new ExtractedLod(lod, vertices, indices, minDist);
            unionMin = Vector3.Min(unionMin, lodMin);
            unionMax = Vector3.Max(unionMax, lodMax);
        }

        if (validLods == 0 || !_scene.TryRegisterGeometry(unionMin, unionMax, out handle))
            return false;

        for (int i = 0; i < validLods; ++i)
        {
            ExtractedLod lodItem = extracted[i]!.Value;
            if (!_scene.TryQueueGeometryLod(handle, lodItem.LodIndex, lodItem.Vertices, lodItem.Indices, lodItem.MinDistance))
            {
                _scene.TryRemoveGeometry(handle);
                handle = default;
                return false;
            }
        }

        _geometries.Add(key, new GeometryRef(handle));
        return true;
    }

    /// <summary>Releases one CPU reference to the specified mesh submesh geometry.</summary>
    /// <param name="mesh">The mesh asset.</param>
    /// <param name="submeshIndex">Zero-based submesh index within LOD 0.</param>
    /// <returns><see langword="true"/> when a reference existed and was decremented.</returns>
    public bool ReleaseGeometry(MeshAsset mesh, int submeshIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(mesh);

        GeometryKey key = new(mesh, submeshIndex);
        if (!_geometries.TryGetValue(key, out GeometryRef? existing))
            return false;

        if (--existing.RefCount == 0)
        {
            _geometries.Remove(key);
            _pendingRemoveGeometries.Add(existing.Handle);
        }
        return true;
    }

    /// <summary>Instantiates all LOD0 submeshes of <paramref name="model"/> inside <paramref name="sector"/>.</summary>
    /// <param name="model">The composite model asset.</param>
    /// <param name="sector">The sector owning the created scene instances.</param>
    /// <param name="transform">World placement, rotation, and scale.</param>
    /// <param name="instance">Receives the tracked model instance handle.</param>
    /// <param name="dynamic">Whether instances are dynamic for VoxelGI clipmap updates.</param>
    /// <param name="castsShadow">Whether instances cast directional shadows.</param>
    /// <param name="affectsGlobalIllumination">Whether instances feed VoxelGI.</param>
    /// <param name="fade">Opacity multiplier in [0, 1].</param>
    /// <param name="lodMinimumDistances">Optional per-LOD distance thresholds.</param>
    /// <returns><see langword="true"/> when all submesh parts were acquired and added to the scene.</returns>
    public bool TryInstantiateModel(
        ModelAsset model,
        WorldSectorHandle sector,
        in World3DTransform transform,
        [NotNullWhen(true)] out World3DModelInstance? instance,
        bool dynamic = false,
        bool castsShadow = true,
        bool affectsGlobalIllumination = true,
        float fade = 1f,
        ReadOnlySpan<float> lodMinimumDistances = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(model);

        instance = null;
        if (model.Mesh.IsDisposed || model.Mesh.LodCount == 0)
            return false;

        ReadOnlySpan<MeshAssetSubMesh> subMeshes = model.Mesh.GetSubMeshes(0);
        if (subMeshes.Length == 0)
            return false;

        List<InstancePart> parts = new(subMeshes.Length);
        List<WorldInstanceHandle> handles = new(subMeshes.Length);

        for (int i = 0; i < subMeshes.Length; ++i)
        {
            MeshAssetSubMesh subMesh = subMeshes[i];
            MaterialAsset materialAsset = model.TryGetMaterial(subMesh.Name, out MaterialAsset? bound) && bound != null
                ? bound
                : PbrMaterialAsset.Default;

            if (!TryAcquireGeometry(model.Mesh, i, out WorldGeometryHandle geomHandle, lodMinimumDistances))
            {
                RollbackModelParts(parts);
                return false;
            }

            if (!TryAcquireMaterial(materialAsset, out WorldMaterialHandle matHandle))
            {
                ReleaseGeometry(model.Mesh, i);
                RollbackModelParts(parts);
                return false;
            }

            World3DInstanceDesc desc = new(sector, geomHandle, matHandle, transform, fade,
                castsShadow, dynamic, affectsGlobalIllumination);
            if (!_scene.TryAddInstance(in desc, out WorldInstanceHandle instHandle))
            {
                ReleaseMaterial(materialAsset);
                ReleaseGeometry(model.Mesh, i);
                RollbackModelParts(parts);
                return false;
            }

            parts.Add(new InstancePart(instHandle, model.Mesh, i, materialAsset));
            handles.Add(instHandle);
        }

        instance = new World3DModelInstance(model, sector, handles);
        _liveModels.Add(instance, parts);
        return true;
    }

    /// <summary>Convenience overload taking decomposed world coordinates, rotation, and scale.</summary>
    public bool TryInstantiateModel(
        ModelAsset model,
        WorldSectorHandle sector,
        World3DPosition worldPosition,
        Quaternion worldRotation,
        Vector3 worldScale,
        [NotNullWhen(true)] out World3DModelInstance? instance,
        bool dynamic = false,
        bool castsShadow = true,
        bool affectsGlobalIllumination = true,
        float fade = 1f,
        ReadOnlySpan<float> lodMinimumDistances = default) =>
        TryInstantiateModel(model, sector, new World3DTransform(worldPosition, worldRotation, worldScale),
            out instance, dynamic, castsShadow, affectsGlobalIllumination, fade, lodMinimumDistances);

    /// <summary>Removes all submesh instances of <paramref name="instance"/> from the scene and releases their CPU refcounts.</summary>
    /// <param name="instance">The model instance to destroy.</param>
    /// <returns><see langword="true"/> when the instance was live and removed.</returns>
    public bool DestroyModelInstance(World3DModelInstance instance)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(instance);

        if (!_liveModels.Remove(instance, out List<InstancePart>? parts))
            return false;

        foreach (InstancePart part in parts)
        {
            _scene.TryRemoveInstance(part.Instance);
            ReleaseMaterial(part.Material);
            ReleaseGeometry(part.Mesh, part.SubMeshIndex);
        }
        instance.IsDestroyed = true;
        return true;
    }

    /// <summary>
    /// Attempts to remove released geometries, materials, and retired textures from the scene in dependency order.
    /// Any handle still leased by an in-flight GPU frame remains queued until a subsequent call after retirement.
    /// </summary>
    /// <returns>The number of scene handles removed during this pass.</returns>
    public int ReclaimReleased()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int reclaimed = 0;
        reclaimed += ReclaimList(_pendingRemoveGeometries, _scene.TryRemoveGeometry);
        reclaimed += ReclaimList(_pendingRemoveMaterials, _scene.TryRemoveMaterial);
        reclaimed += ReclaimList(_pendingRemoveTextures, _scene.TryRemoveTexture);
        return reclaimed;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        if (_liveModels.Count > 0)
        {
            foreach ((World3DModelInstance instance, List<InstancePart> parts) in _liveModels)
            {
                foreach (InstancePart part in parts)
                {
                    _scene.TryRemoveInstance(part.Instance);
                    ReleaseMaterial(part.Material);
                    ReleaseGeometry(part.Mesh, part.SubMeshIndex);
                }
                instance.IsDestroyed = true;
            }
            _liveModels.Clear();
        }

        foreach (GeometryRef g in _geometries.Values)
            _pendingRemoveGeometries.Add(g.Handle);
        _geometries.Clear();

        foreach (MaterialRef m in _materials.Values)
        {
            ReleaseImageListInternal(m.BoundImages);
            _pendingRemoveMaterials.Add(m.Handle);
        }
        _materials.Clear();

        foreach (TextureRef t in _textures.Values)
            _pendingRemoveTextures.Add(t.Handle);
        _textures.Clear();

        if (_pendingRemoveGeometries.Count > 0)
            ReclaimList(_pendingRemoveGeometries, _scene.TryRemoveGeometry);
        if (_pendingRemoveMaterials.Count > 0)
            ReclaimList(_pendingRemoveMaterials, _scene.TryRemoveMaterial);
        if (_pendingRemoveTextures.Count > 0)
            ReclaimList(_pendingRemoveTextures, _scene.TryRemoveTexture);
        _disposed = true;
    }

    private void RollbackModelParts(List<InstancePart> parts)
    {
        for (int i = parts.Count - 1; i >= 0; --i)
        {
            InstancePart part = parts[i];
            _scene.TryRemoveInstance(part.Instance);
            ReleaseMaterial(part.Material);
            ReleaseGeometry(part.Mesh, part.SubMeshIndex);
        }
    }

    private bool TryBuildDescAndRetainTextures(
        MaterialAsset material,
        List<Image> retainedImages,
        out World3DMaterialDesc description)
    {
        if (material is PbrMaterialAsset pbr)
        {
            if (!pbr.TryBuildMaterialDesc(image => ResolveAndTrackTexture(image, retainedImages), out description, out _))
                return false;
            return true;
        }

        WorldTextureHandle baseColorTex = default;
        WorldTextureHandle normalTex = default;
        WorldTextureHandle mrTex = default;
        WorldTextureHandle emissiveTex = default;

        foreach ((string slot, Image image) in material.Textures)
        {
            WorldTextureHandle texHandle = ResolveAndTrackTexture(image, retainedImages);
            if (texHandle == default)
            {
                description = default;
                return false;
            }

            if (slot.Equals("baseColor", StringComparison.OrdinalIgnoreCase))
                baseColorTex = texHandle;
            else if (slot.Equals("normal", StringComparison.OrdinalIgnoreCase))
                normalTex = texHandle;
            else if (slot.Equals("metallicRoughness", StringComparison.OrdinalIgnoreCase))
                mrTex = texHandle;
            else if (slot.Equals("emissive", StringComparison.OrdinalIgnoreCase))
                emissiveTex = texHandle;
            else
            {
                description = default;
                return false;
            }
        }

        Vector4 baseColor = Vector4.One;
        Vector3 emissive = Vector3.Zero;
        float metallic = 0f;
        float roughness = 1f;
        float alphaCutoff = 0.5f;
        float transmission = 0f;
        float ior = 1.5f;
        bool doubleSided = false;
        World3DAlphaMode alphaMode = World3DAlphaMode.Opaque;
        World3DMaterialSampler sampler = World3DMaterialSampler.LinearRepeat;

        foreach ((string key, ShaderValue value) in material.Parameters)
        {
            if (key.Equals("baseColor", StringComparison.OrdinalIgnoreCase) && TryReadVec4(value, out Vector4 c))
                baseColor = c;
            else if (key.Equals("emissive", StringComparison.OrdinalIgnoreCase) && TryReadVec3(value, out Vector3 e))
                emissive = e;
            else if (key.Equals("metallic", StringComparison.OrdinalIgnoreCase) && TryReadFloat(value, out float m))
                metallic = m;
            else if (key.Equals("roughness", StringComparison.OrdinalIgnoreCase) && TryReadFloat(value, out float r))
                roughness = r;
            else if (key.Equals("alphaCutoff", StringComparison.OrdinalIgnoreCase) && TryReadFloat(value, out float cut))
                alphaCutoff = cut;
            else if (key.Equals("transmission", StringComparison.OrdinalIgnoreCase) && TryReadFloat(value, out float t))
                transmission = t;
            else if (key.Equals("ior", StringComparison.OrdinalIgnoreCase) && TryReadFloat(value, out float i))
                ior = i;
            else if (key.Equals("doubleSided", StringComparison.OrdinalIgnoreCase) && TryReadBool(value, out bool ds))
                doubleSided = ds;
            else if (key.Equals("alphaMode", StringComparison.OrdinalIgnoreCase) && TryReadAlphaMode(value, out World3DAlphaMode am))
                alphaMode = am;
            else if (key.Equals("sampler", StringComparison.OrdinalIgnoreCase) && TryReadSampler(value, out World3DMaterialSampler smp))
                sampler = smp;
            else
            {
                description = default;
                return false;
            }
        }

        description = new World3DMaterialDesc
        {
            BaseColor = baseColor,
            Emissive = emissive,
            Metallic = metallic,
            Roughness = roughness,
            AlphaCutoff = alphaCutoff,
            AlphaMode = alphaMode,
            DoubleSided = doubleSided,
            Sampler = sampler,
            BaseColorTexture = baseColorTex,
            NormalTexture = normalTex,
            MetallicRoughnessTexture = mrTex,
            EmissiveTexture = emissiveTex,
            Transmission = transmission,
            Ior = ior,
            SurfaceModule = string.IsNullOrWhiteSpace(material.Surface)
                ? PbrMaterialAsset.DefaultSurfaceModule
                : material.Surface.Trim(),
        };
        return true;
    }

    private WorldTextureHandle ResolveAndTrackTexture(Image image, List<Image> retainedImages)
    {
        if (!TryAcquireTexture(image, out WorldTextureHandle texHandle))
            return default;
        retainedImages.Add(image);
        return texHandle;
    }

    private void ReleaseImageList(List<Image> images) => ReleaseImageListInternal(images);

    private void ReleaseImageListInternal(List<Image> images)
    {
        foreach (Image image in images)
        {
            if (_textures.TryGetValue(image, out TextureRef? entry) && --entry.RefCount == 0)
            {
                _textures.Remove(image);
                _pendingRemoveTextures.Add(entry.Handle);
            }
        }
    }

    private static float ResolveLodDistance(
        MeshAsset mesh,
        int lodIndex,
        ReadOnlySpan<float> overrides,
        Vector3 min,
        Vector3 max,
        float previousDistance)
    {
        if (lodIndex < overrides.Length && overrides[lodIndex] >= 0f && float.IsFinite(overrides[lodIndex]))
            return MathF.Max(previousDistance, overrides[lodIndex]);
        if (lodIndex == 0)
            return 0f;

        float maxError = MathF.Max(0f, mesh.GetLod(lodIndex).MaxError);
        float radius = (max - min).Length() * 0.5f;
        float heuristic = MathF.Max(maxError * 40f, radius * 4f * lodIndex);
        return MathF.Max(previousDistance + 16f, heuristic);
    }

    private static int ReclaimList<THandle>(List<THandle> pending, Func<THandle, bool> remove)
    {
        int removed = 0;
        int keep = 0;
        for (int i = 0; i < pending.Count; ++i)
        {
            THandle handle = pending[i];
            if (remove(handle))
                ++removed;
            else
                pending[keep++] = handle;
        }
        pending.RemoveRange(keep, pending.Count - keep);
        return removed;
    }

    private static bool TryReadFloat(ShaderValue v, out float result)
    {
        if (v.Kind == ShaderValueKind.Float32 && v.ElementCount >= 1)
        {
            result = v.GetFloat();
            return float.IsFinite(result);
        }
        if (v.Kind == ShaderValueKind.Int32 && v.ElementCount >= 1)
        {
            result = v.GetInt();
            return true;
        }
        result = 0f;
        return false;
    }

    private static bool TryReadVec3(ShaderValue v, out Vector3 result)
    {
        if (v.Kind == ShaderValueKind.Float32 && v.ElementCount is >= 1 and <= 4)
        {
            ReadOnlySpan<float> f = v.GetFloats();
            result = f.Length switch
            {
                1 => new Vector3(f[0]),
                2 => new Vector3(f[0], f[1], 0f),
                _ => new Vector3(f[0], f[1], f[2]),
            };
            return true;
        }
        result = default;
        return false;
    }

    private static bool TryReadVec4(ShaderValue v, out Vector4 result)
    {
        if (v.Kind == ShaderValueKind.Float32 && v.ElementCount is >= 1 and <= 4)
        {
            ReadOnlySpan<float> f = v.GetFloats();
            result = f.Length switch
            {
                1 => new Vector4(f[0], f[0], f[0], 1f),
                2 => new Vector4(f[0], f[1], 0f, 1f),
                3 => new Vector4(f[0], f[1], f[2], 1f),
                _ => new Vector4(f[0], f[1], f[2], f[3]),
            };
            return true;
        }
        result = default;
        return false;
    }

    private static bool TryReadBool(ShaderValue v, out bool result)
    {
        if (v.Kind is ShaderValueKind.Bool32 or ShaderValueKind.Int32 or ShaderValueKind.UInt32)
        {
            result = v.GetInt() != 0;
            return true;
        }
        result = false;
        return false;
    }

    private static bool TryReadAlphaMode(ShaderValue v, out World3DAlphaMode mode)
    {
        if (v.Kind is ShaderValueKind.Int32 or ShaderValueKind.UInt32)
        {
            int raw = v.GetInt();
            if ((uint)raw <= (uint)World3DAlphaMode.Blend)
            {
                mode = (World3DAlphaMode)raw;
                return true;
            }
        }
        mode = default;
        return false;
    }

    private static bool TryReadSampler(ShaderValue v, out World3DMaterialSampler sampler)
    {
        if (v.Kind is ShaderValueKind.Int32 or ShaderValueKind.UInt32)
        {
            int raw = v.GetInt();
            if (Enum.IsDefined((World3DMaterialSampler)raw))
            {
                sampler = (World3DMaterialSampler)raw;
                return true;
            }
        }
        sampler = default;
        return false;
    }

    private readonly record struct GeometryKey(MeshAsset Mesh, int SubMeshIndex) : IEquatable<GeometryKey>
    {
        public bool Equals(GeometryKey other) => ReferenceEquals(Mesh, other.Mesh) && SubMeshIndex == other.SubMeshIndex;
        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Mesh), SubMeshIndex);
    }

    private sealed class TextureRef(WorldTextureHandle handle)
    {
        public WorldTextureHandle Handle { get; } = handle;
        public int RefCount = 1;
    }

    private sealed class MaterialRef(WorldMaterialHandle handle, List<Image> boundImages)
    {
        public WorldMaterialHandle Handle { get; } = handle;
        public List<Image> BoundImages = boundImages;
        public int RefCount = 1;
    }

    private sealed class GeometryRef(WorldGeometryHandle handle)
    {
        public WorldGeometryHandle Handle { get; } = handle;
        public int RefCount = 1;
    }

    private readonly record struct InstancePart(WorldInstanceHandle Instance, MeshAsset Mesh, int SubMeshIndex, MaterialAsset Material);

    private readonly record struct ExtractedLod(int LodIndex, VertexPBR[] Vertices, uint[] Indices, float MinDistance);
}
