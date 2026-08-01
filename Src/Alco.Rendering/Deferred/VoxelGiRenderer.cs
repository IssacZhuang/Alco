using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Voxel global illumination renderer for the deferred PBR pipeline: a cascaded
/// voxel clipmap (4 levels, each a cube of <c>resolution</c>^3 voxels at twice
/// the previous level's voxel size, following the camera) with compute
/// voxelization, direct-light injection and voxel cone tracing.
/// <br/>Static meshes are registered once (<see cref="RegisterStaticMesh"/>) and
/// re-voxelized only when a clipmap level recenters on the camera; dynamic
/// meshes are registered once (<see cref="RegisterDynamicMesh"/>) and their
/// instances submitted every frame (<see cref="SubmitDynamicInstance"/>).
/// <br/>Call <see cref="Render"/> after the G-buffer pass and before the lighting
/// pass; the resulting <see cref="IndirectTexture"/> atlas (diffuse in the left
/// half, specular in the right half) is sampled by the deferred lighting pass
/// (see <see cref="PBRDeferredPipeline.SetGlobalIllumination"/>).
/// <br/>Attribute voxels live in storage buffers (packed, point-sampled by the
/// injection pass); the HDR radiance volume is one mip-mapped RGBA16Float
/// <see cref="Texture3D"/> with all clipmap levels stacked along its depth axis,
/// cone-traced with hardware trilinear filtering.
/// </summary>
public sealed class VoxelGiRenderer : AutoDisposable
{
    /// <summary>
    /// Per-frame data uploaded to every voxel GI shader. Layout must match the
    /// <c>_data</c> cbuffer in VoxelCommon.hlsli exactly. The caller fills the
    /// lighting fields; the renderer fills the clipmap fields (level origins,
    /// <see cref="ClipmapParams"/>) and the trace/G-buffer resolution components
    /// of <see cref="GiParams"/> / <see cref="GiParams2"/>.
    /// </summary>
    public struct VoxelGiData
    {
        /// <summary>Inverse of the camera view-projection matrix.</summary>
        public Matrix4x4 InvViewProjection;
        /// <summary>Sun light view-projection matrix of shadow cascade 0 (nearest).</summary>
        public Matrix4x4 SunViewProjection0;
        /// <summary>Sun light view-projection matrix of shadow cascade 1.</summary>
        public Matrix4x4 SunViewProjection1;
        /// <summary>Sun light view-projection matrix of shadow cascade 2.</summary>
        public Matrix4x4 SunViewProjection2;
        /// <summary>Sun light view-projection matrix of shadow cascade 3 (farthest).</summary>
        public Matrix4x4 SunViewProjection3;
        /// <summary>Clipmap level 0 origin: xyz = min corner in world space, w = voxel size (filled by the renderer).</summary>
        public Vector4 LevelOrigin0;
        /// <summary>Clipmap level 1 origin (filled by the renderer).</summary>
        public Vector4 LevelOrigin1;
        /// <summary>Clipmap level 2 origin (filled by the renderer).</summary>
        public Vector4 LevelOrigin2;
        /// <summary>Clipmap level 3 origin (filled by the renderer).</summary>
        public Vector4 LevelOrigin3;
        /// <summary>Camera position in world space (w unused).</summary>
        public Vector4 CameraPosition;
        /// <summary>Normalized direction the sun light travels (w unused).</summary>
        public Vector4 SunDirection;
        /// <summary>Sun linear color (rgb) and intensity (w).</summary>
        public Vector4 SunColorAndIntensity;
        /// <summary>Azimuthally filtered physical-sky radiance at the horizon.</summary>
        public Vector4 SkyHorizonColor;
        /// <summary>Physical-sky radiance at the zenith.</summary>
        public Vector4 SkyZenithColor;
        /// <summary>Point light 0 position (w unused).</summary>
        public Vector4 PointLight0Position;
        /// <summary>Point light 0 color (rgb) and intensity (w). Zero intensity disables the light.</summary>
        public Vector4 PointLight0Color;
        /// <summary>Point light 1 position (w unused).</summary>
        public Vector4 PointLight1Position;
        /// <summary>Point light 1 color (rgb) and intensity (w).</summary>
        public Vector4 PointLight1Color;
        /// <summary>Point light 2 position (w unused).</summary>
        public Vector4 PointLight2Position;
        /// <summary>Point light 2 color (rgb) and intensity (w).</summary>
        public Vector4 PointLight2Color;
        /// <summary>Point light 3 position (w unused).</summary>
        public Vector4 PointLight3Position;
        /// <summary>Point light 3 color (rgb) and intensity (w).</summary>
        public Vector4 PointLight3Color;
        /// <summary>View-distance end boundary of each shadow cascade.</summary>
        public Vector4 CascadeSplits;
        /// <summary>World units per shadow texel of each cascade.</summary>
        public Vector4 CascadeTexelSizes;
        /// <summary>x=level resolution y=level count z=mip count (filled by the renderer).</summary>
        public Vector4 ClipmapParams;
        /// <summary>x=shadowEnabled y=pointLightEnabled z=shadowMapSize w=unused.</summary>
        public Vector4 LightingParams;
        /// <summary>x=emissiveScale y=traceMaxDistance zw=trace resolution in pixels (filled by the renderer).</summary>
        public Vector4 GiParams;
        /// <summary>x=debugView yz=G-buffer resolution in pixels (filled by the renderer) w=unused.</summary>
        public Vector4 GiParams2;
    }

    /// <summary>
    /// Push constant payload for one voxelize dispatch. Layout must match the
    /// <c>VoxelizeConstants</c> struct in Voxelize.hlsl exactly (128 bytes, the
    /// device push-constant limit).
    /// </summary>
    private struct VoxelizeConstants
    {
        public Matrix4x4 Model;
        public Vector4 BaseColor;
        public Vector4 Emissive;
        public Vector4 Params;  // x=levelIndex, y=indexIs16Bit, z=vertexStrideUints, w=alphaCutoff
        public Vector4 Params2; // x=triangleCount
    }

    /// <summary>A registered mesh with its GPU-resident voxelization data.</summary>
    private sealed class MeshRegistration
    {
        public required GraphicsBuffer Vertices;
        public required GraphicsBuffer Indices;
        public uint TriangleCount;
        public uint VertexStrideUints;
        public bool Index16Bit;
        public Texture2D? Albedo;
        public Texture2D? Emissive;
    }

    /// <summary>A dynamic mesh instance submitted for one frame.</summary>
    private struct DynamicInstance
    {
        public MeshRegistration Registration;
        public Matrix4x4 World;
        public Vector4 BaseColor;
        public Vector3 Emissive;
        public float AlphaCutoff;
    }

    private readonly RenderingSystem _rendering;
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandBuffer;
    private readonly ComputeMaterial _clearMaterial;
    private readonly ComputeMaterial _voxelizeMaterial;
    private readonly ComputeMaterial _injectMaterial;
    private readonly ComputeMaterial _mipMaterial;
    private readonly ComputeMaterial _traceMaterial;
    private readonly GraphicsValueBuffer<VoxelGiData> _dataBuffer;

    private readonly int _resolution;
    private readonly int _mipCount;
    private readonly float _baseVoxelSize;

    // Per-level attribute voxel buffers (static cached + dynamic per-frame) and
    // one radiance Texture3D shared by all levels (each level's mip cube is
    // stacked along the texture depth axis).
    private readonly GraphicsBuffer[] _attrStatic = new GraphicsBuffer[LevelCount];
    private readonly GraphicsBuffer[] _attrDynamic = new GraphicsBuffer[LevelCount];
    private readonly Texture3D _radiance;

    private readonly Vector4[] _levelOrigins = new Vector4[LevelCount];
    private readonly bool[] _staticDirty = new bool[LevelCount];
    private bool _originsInitialized;

    private readonly List<(MeshRegistration Registration, Matrix4x4 World, Vector4 BaseColor, Vector3 Emissive, float AlphaCutoff)> _staticMeshes = new();
    private readonly List<MeshRegistration> _dynamicMeshes = new();
    private readonly List<DynamicInstance> _instances = new();

    private RenderTexture _indirectAtlas;
    private RenderTexture? _boundGBuffer;
    private RenderTexture? _boundShadowMap;

    private const int LevelCount = 4;

    /// <summary>
    /// The gathered indirect radiance atlas (twice the half G-buffer width:
    /// diffuse radiance in the left half, specular in the right half), sampled
    /// by the deferred lighting pass.
    /// </summary>
    public RenderTexture IndirectTexture => _indirectAtlas;

    /// <summary>
    /// Create the voxel GI renderer with its compute shaders.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    /// <param name="clearShader">The voxel clear shader (VoxelClear.hlsl).</param>
    /// <param name="voxelizeShader">The triangle voxelization shader (Voxelize.hlsl).</param>
    /// <param name="injectShader">The direct light injection shader (VoxelInject.hlsl).</param>
    /// <param name="mipShader">The radiance mip downsample shader (VoxelMip.hlsl).</param>
    /// <param name="traceShader">The cone tracing shader (VoxelTrace.hlsl).</param>
    /// <param name="width">The initial G-buffer width in pixels.</param>
    /// <param name="height">The initial G-buffer height in pixels.</param>
    /// <param name="resolution">The voxel resolution of each clipmap level (power of two).</param>
    /// <param name="baseVoxelSize">The voxel size of the finest level in world units.</param>
    /// <exception cref="ArgumentException">The resolution is not a power of two.</exception>
    public VoxelGiRenderer(
        RenderingSystem rendering,
        Shader clearShader,
        Shader voxelizeShader,
        Shader injectShader,
        Shader mipShader,
        Shader traceShader,
        uint width,
        uint height,
        int resolution = 128,
        float baseVoxelSize = 0.1f)
    {
        if (resolution < 16 || (resolution & (resolution - 1)) != 0)
        {
            throw new ArgumentException("The voxel resolution must be a power of two and at least 16.", nameof(resolution));
        }

        _rendering = rendering;
        _device = rendering.GraphicsDevice;
        _resolution = resolution;
        _baseVoxelSize = baseVoxelSize;
        _mipCount = (int)MathF.Log2(resolution) + 1;

        _commandBuffer = _device.CreateCommandBuffer("voxel_gi");
        _clearMaterial = rendering.CreateComputeMaterial(clearShader);
        _voxelizeMaterial = rendering.CreateComputeMaterial(voxelizeShader);
        _injectMaterial = rendering.CreateComputeMaterial(injectShader);
        _mipMaterial = rendering.CreateComputeMaterial(mipShader);
        _traceMaterial = rendering.CreateComputeMaterial(traceShader);
        _dataBuffer = rendering.CreateGraphicsValueBuffer<VoxelGiData>("voxel_gi_data");

        _clearMaterial.SetBuffer("_data", _dataBuffer);
        _voxelizeMaterial.SetBuffer("_data", _dataBuffer);
        _injectMaterial.SetBuffer("_data", _dataBuffer);
        _mipMaterial.SetBuffer("_data", _dataBuffer);
        _traceMaterial.SetBuffer("_data", _dataBuffer);

        // Attribute voxel: uint2 (albedo+occupancy, normal+emissive) per voxel,
        // one storage buffer per level.
        uint attrBytes = (uint)(resolution * resolution * resolution * 8);

        for (int level = 0; level < LevelCount; level++)
        {
            float voxelSize = baseVoxelSize * (1 << level);
            _levelOrigins[level] = new Vector4(Vector3.Zero, voxelSize);
            _staticDirty[level] = true;
            _attrStatic[level] = new GraphicsBuffer(rendering, attrBytes, $"voxel_attr_static_{level}");
            _attrDynamic[level] = new GraphicsBuffer(rendering, attrBytes, $"voxel_attr_dynamic_{level}");
        }

        // Radiance: one RGBA16Float Texture3D with a full mip chain; all levels
        // are stacked along the depth axis (resolution^3 per level per mip),
        // sampled with hardware trilinear filtering by the cone tracing pass.
        _radiance = rendering.CreateTexture3D((uint)resolution, (uint)resolution, (uint)(resolution * LevelCount),
            PixelFormat.RGBA16Float, (uint)_mipCount, name: "voxel_radiance");

        _injectMaterial.SetTexture3DStorage("_radianceOut", _radiance, 0);
        _traceMaterial.SetTexture("_radiance", _radiance);

        _indirectAtlas = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, TraceWidth(width) * 2, TraceHeight(height), "voxel_indirect_gi");
        _traceMaterial.SetRenderTexture("_indirectGI", _indirectAtlas);
    }

    private static uint TraceWidth(uint gbufferWidth) => Math.Max(gbufferWidth / 2, 1);

    private static uint TraceHeight(uint gbufferHeight) => Math.Max(gbufferHeight / 2, 1);

    /// <summary>
    /// Register a static mesh for voxelization. The mesh's vertex/index data is
    /// copied once into voxelization storage buffers; the mesh itself stays
    /// untouched. Static meshes are re-voxelized whenever a clipmap level
    /// recenters or <see cref="InvalidateStatic"/> was called.
    /// </summary>
    /// <param name="mesh">The mesh to voxelize (single submesh).</param>
    /// <param name="vertexStrideBytes">The vertex stride in bytes (32 for VertexPositionNormalTexture, 48 for VertexPositionNormalTextureTangent); the layout must start with position(3) / normal(3) / uv(2) floats.</param>
    /// <param name="albedo">The albedo texture sampled at the triangle centroid; null binds the shared white texture.</param>
    /// <param name="emissive">The emissive texture sampled at the triangle centroid; null binds the shared black texture.</param>
    /// <param name="baseColor">The linear base color, multiplied with the albedo texture.</param>
    /// <param name="emissiveFactor">The linear emissive factor, multiplied with the emissive texture.</param>
    /// <param name="alphaCutoff">Alpha test threshold; 0 disables alpha testing.</param>
    /// <param name="world">The world transform of the mesh.</param>
    /// <returns>The static mesh handle, used with <see cref="UpdateStaticMesh"/>.</returns>
    public int RegisterStaticMesh(Mesh mesh, uint vertexStrideBytes, Texture2D? albedo, Texture2D? emissive,
        in Vector4 baseColor, in Vector3 emissiveFactor, float alphaCutoff, in Matrix4x4 world)
    {
        MeshRegistration registration = CreateRegistration(mesh, vertexStrideBytes, albedo, emissive);
        _staticMeshes.Add((registration, world, baseColor, emissiveFactor, alphaCutoff));
        InvalidateStatic();
        return _staticMeshes.Count - 1;
    }

    /// <summary>
    /// Update the transform and surface parameters of a registered static mesh
    /// and schedule a static re-voxelization.
    /// </summary>
    /// <param name="handle">The handle returned by <see cref="RegisterStaticMesh"/>.</param>
    /// <param name="baseColor">The linear base color.</param>
    /// <param name="emissiveFactor">The linear emissive factor.</param>
    /// <param name="alphaCutoff">Alpha test threshold; 0 disables alpha testing.</param>
    /// <param name="world">The world transform of the mesh.</param>
    public void UpdateStaticMesh(int handle, in Vector4 baseColor, in Vector3 emissiveFactor, float alphaCutoff, in Matrix4x4 world)
    {
        (MeshRegistration registration, _, _, _, _) = _staticMeshes[handle];
        _staticMeshes[handle] = (registration, world, baseColor, emissiveFactor, alphaCutoff);
        InvalidateStatic();
    }

    /// <summary>
    /// Register a dynamic mesh for per-frame voxelization. Instances are
    /// submitted every frame via <see cref="SubmitDynamicInstance"/>.
    /// </summary>
    /// <param name="mesh">The mesh to voxelize (single submesh).</param>
    /// <param name="vertexStrideBytes">The vertex stride in bytes (32 or 48); see <see cref="RegisterStaticMesh"/>.</param>
    /// <param name="albedo">The albedo texture; null binds the shared white texture.</param>
    /// <param name="emissive">The emissive texture; null binds the shared black texture.</param>
    /// <returns>The dynamic mesh handle for <see cref="SubmitDynamicInstance"/>.</returns>
    public int RegisterDynamicMesh(Mesh mesh, uint vertexStrideBytes, Texture2D? albedo, Texture2D? emissive)
    {
        MeshRegistration registration = CreateRegistration(mesh, vertexStrideBytes, albedo, emissive);
        _dynamicMeshes.Add(registration);
        return _dynamicMeshes.Count - 1;
    }

    /// <summary>
    /// Submit one instance of a registered dynamic mesh for voxelization this
    /// frame. The instance list is consumed by <see cref="Render"/>.
    /// </summary>
    /// <param name="handle">The handle returned by <see cref="RegisterDynamicMesh"/>.</param>
    /// <param name="world">The world transform of the instance.</param>
    /// <param name="baseColor">The linear base color, multiplied with the albedo texture.</param>
    /// <param name="emissiveFactor">The linear emissive factor, multiplied with the emissive texture.</param>
    /// <param name="alphaCutoff">Alpha test threshold; 0 disables alpha testing.</param>
    public void SubmitDynamicInstance(int handle, in Matrix4x4 world, in Vector4 baseColor, in Vector3 emissiveFactor, float alphaCutoff)
    {
        _instances.Add(new DynamicInstance
        {
            Registration = _dynamicMeshes[handle],
            World = world,
            BaseColor = baseColor,
            Emissive = emissiveFactor,
            AlphaCutoff = alphaCutoff,
        });
    }

    /// <summary>
    /// Remove all static mesh registrations (dynamic registrations are kept).
    /// </summary>
    public void ClearStaticMeshes()
    {
        for (int i = 0; i < _staticMeshes.Count; i++)
        {
            DisposeRegistration(_staticMeshes[i].Registration);
        }
        _staticMeshes.Clear();
        InvalidateStatic();
    }

    /// <summary>Schedule a static re-voxelization of every clipmap level.</summary>
    public void InvalidateStatic()
    {
        for (int level = 0; level < LevelCount; level++)
        {
            _staticDirty[level] = true;
        }
    }

    /// <summary>
    /// Recreate the indirect atlas at a new G-buffer resolution.
    /// </summary>
    /// <param name="width">The new G-buffer width in pixels.</param>
    /// <param name="height">The new G-buffer height in pixels.</param>
    public void Resize(uint width, uint height)
    {
        _indirectAtlas.Dispose();
        _indirectAtlas = _rendering.CreateRenderTexture(_rendering.PreferredLightMapPass, TraceWidth(width) * 2, TraceHeight(height), "voxel_indirect_gi");
        _traceMaterial.SetRenderTexture("_indirectGI", _indirectAtlas);
        _boundGBuffer = null;
    }

    /// <summary>
    /// Run the voxel GI passes: voxelize (static on dirty, dynamic every frame),
    /// inject direct lighting, rebuild the radiance mips and trace cones from
    /// the G-buffer. Must be called after the G-buffer pass and before the
    /// lighting pass; dynamic instances are consumed (cleared) by the call.
    /// </summary>
    /// <param name="gbuffer">The pipeline G-buffer (depth + world-normal + metallic-roughness-ao attachments).</param>
    /// <param name="shadowMap">The pipeline shadow map (2x2 cascade atlas).</param>
    /// <param name="data">Per-frame data; the clipmap fields are filled by the renderer.</param>
    /// <param name="cameraPosition">The world-space camera position driving the clipmap.</param>
    public void Render(RenderTexture gbuffer, RenderTexture shadowMap, ref VoxelGiData data, in Vector3 cameraPosition)
    {
        UpdateClipmapOrigins(cameraPosition);

        data.LevelOrigin0 = _levelOrigins[0];
        data.LevelOrigin1 = _levelOrigins[1];
        data.LevelOrigin2 = _levelOrigins[2];
        data.LevelOrigin3 = _levelOrigins[3];
        data.ClipmapParams = new Vector4(_resolution, LevelCount, _mipCount, 0.0f);
        uint traceWidth = Math.Max(_indirectAtlas.Width / 2, 1);
        data.GiParams = new Vector4(data.GiParams.X, data.GiParams.Y, traceWidth, _indirectAtlas.Height);
        data.GiParams2 = new Vector4(data.GiParams2.X, gbuffer.Width, gbuffer.Height, 0.0f);
        _dataBuffer.UpdateBuffer(data);

        // The G-buffer and shadow map render textures are stable across frames
        // (recreated on resize); avoid rebinding every frame.
        if (!ReferenceEquals(_boundGBuffer, gbuffer))
        {
            _traceMaterial.SetRenderTextureDepth("_gbufferDepth", gbuffer);
            _traceMaterial.SetRenderTexture("_normal", gbuffer, 1);
            _traceMaterial.SetRenderTexture("_mrAO", gbuffer, 2);
            _boundGBuffer = gbuffer;
        }
        if (!ReferenceEquals(_boundShadowMap, shadowMap))
        {
            _injectMaterial.SetRenderTextureDepth("_shadowMap", shadowMap);
            _boundShadowMap = shadowMap;
        }

        uint resolution = (uint)_resolution;
        _commandBuffer.Begin();
        using (GPUCommandBuffer.ComputePass computePass = _commandBuffer.BeginCompute())
        {
            // Static re-voxelization, only for the levels that recently moved.
            for (int level = 0; level < LevelCount; level++)
            {
                if (!_staticDirty[level])
                {
                    continue;
                }
                _clearMaterial.SetBuffer("_attrOut", _attrStatic[level]);
                _clearMaterial.DispatchBySize(computePass, resolution, resolution, resolution);
                for (int i = 0; i < _staticMeshes.Count; i++)
                {
                    (MeshRegistration registration, Matrix4x4 world, Vector4 baseColor, Vector3 emissive, float alphaCutoff) = _staticMeshes[i];
                    DispatchVoxelize(computePass, registration, _attrStatic[level], level, world, baseColor, emissive, alphaCutoff);
                }
                _staticDirty[level] = false;
            }

            // Dynamic voxelization, every frame.
            for (int level = 0; level < LevelCount; level++)
            {
                _clearMaterial.SetBuffer("_attrOut", _attrDynamic[level]);
                _clearMaterial.DispatchBySize(computePass, resolution, resolution, resolution);
                for (int i = 0; i < _instances.Count; i++)
                {
                    DynamicInstance instance = _instances[i];
                    DispatchVoxelize(computePass, instance.Registration, _attrDynamic[level], level,
                        instance.World, instance.BaseColor, instance.Emissive, instance.AlphaCutoff);
                }
            }

            // Direct lighting injection into radiance mip 0.
            for (int level = 0; level < LevelCount; level++)
            {
                _injectMaterial.SetBuffer("_attrStatic", _attrStatic[level]);
                _injectMaterial.SetBuffer("_attrDynamic", _attrDynamic[level]);
                _injectMaterial.DispatchBySizeWithConstant(computePass, resolution, resolution, resolution, new Vector4(level, 0, 0, 0));
            }

            // Radiance mip chains. Each transition reads mip N and writes mip N+1
            // through single-mip views: the non-overlapping subresource ranges of
            // the two views avoid the read/write usage conflict within one dispatch.
            for (int mip = 0; mip < _mipCount - 1; mip++)
            {
                _mipMaterial.SetTexture3DRead("_radianceLoad", _radiance, (uint)mip);
                _mipMaterial.SetTexture3DStorage("_radianceOut", _radiance, (uint)(mip + 1));
                uint dstResolution = (uint)Math.Max(_resolution >> (mip + 1), 1);
                for (int level = 0; level < LevelCount; level++)
                {
                    _mipMaterial.DispatchBySizeWithConstant(computePass, dstResolution, dstResolution, dstResolution, new Vector4(mip, level, 0, 0));
                }
            }

            // Cone tracing from the G-buffer.
            _traceMaterial.DispatchBySize(computePass, traceWidth, _indirectAtlas.Height, 1);
        }
        _commandBuffer.End();
        _device.Submit(_commandBuffer);

        _instances.Clear();
    }

    /// <summary>
    /// Recenter each clipmap level on the camera: origins snap to the voxel grid
    /// and only move when the camera leaves the central half of the level
    /// region, which marks the level for static re-voxelization.
    /// </summary>
    private void UpdateClipmapOrigins(in Vector3 cameraPosition)
    {
        for (int level = 0; level < LevelCount; level++)
        {
            float voxelSize = _baseVoxelSize * (1 << level);
            float region = voxelSize * _resolution;
            Vector3 desired = cameraPosition - new Vector3(region * 0.5f);
            Vector3 snapped = new(
                MathF.Floor(desired.X / voxelSize) * voxelSize,
                MathF.Floor(desired.Y / voxelSize) * voxelSize,
                MathF.Floor(desired.Z / voxelSize) * voxelSize);
            Vector3 origin = new(_levelOrigins[level].X, _levelOrigins[level].Y, _levelOrigins[level].Z);
            Vector3 delta = snapped - origin;
            bool recenter = !_originsInitialized
                || MathF.Abs(delta.X) >= region * 0.25f
                || MathF.Abs(delta.Y) >= region * 0.25f
                || MathF.Abs(delta.Z) >= region * 0.25f;
            if (recenter)
            {
                _levelOrigins[level] = new Vector4(snapped, voxelSize);
                _staticDirty[level] = true;
            }
        }
        _originsInitialized = true;
    }

    private void DispatchVoxelize(GPUCommandBuffer.ComputePass computePass, MeshRegistration registration,
        GraphicsBuffer attrOut, int level, in Matrix4x4 world, in Vector4 baseColor, in Vector3 emissive, float alphaCutoff)
    {
        if (registration.TriangleCount == 0)
        {
            return;
        }

        _voxelizeMaterial.SetBuffer("_vertices", registration.Vertices);
        _voxelizeMaterial.SetBuffer("_indices", registration.Indices);
        _voxelizeMaterial.SetBuffer("_attrOut", attrOut);
        _voxelizeMaterial.SetTexture("_albedoTexture", registration.Albedo ?? _rendering.TextureWhite);
        _voxelizeMaterial.SetTexture("_emissiveTexture", registration.Emissive ?? _rendering.TextureBlack);
        _voxelizeMaterial.DispatchBySizeWithConstant(computePass, registration.TriangleCount, 8, 1, new VoxelizeConstants
        {
            Model = world,
            BaseColor = baseColor,
            Emissive = new Vector4(emissive, 0.0f),
            Params = new Vector4(level, registration.Index16Bit ? 1.0f : 0.0f, registration.VertexStrideUints, alphaCutoff),
            Params2 = new Vector4(registration.TriangleCount, 0.0f, 0.0f, 0.0f),
        });
    }

    private MeshRegistration CreateRegistration(Mesh mesh, uint vertexStrideBytes, Texture2D? albedo, Texture2D? emissive)
    {
        SubMeshData subMesh = mesh.GetSubMesh(0);
        uint vertexBytes = mesh.VertexBuffer.Size;
        uint indexBytes = mesh.IndexBuffer.Size;

        var registration = new MeshRegistration
        {
            Vertices = new GraphicsBuffer(_rendering, vertexBytes, $"voxel_vertices_{mesh.Name}"),
            Indices = new GraphicsBuffer(_rendering, indexBytes, $"voxel_indices_{mesh.Name}"),
            TriangleCount = subMesh.IndexCount / 3,
            VertexStrideUints = vertexStrideBytes / 4,
            Index16Bit = subMesh.IndexFormat == IndexFormat.UInt16,
            Albedo = albedo,
            Emissive = emissive,
        };

        // Copy the mesh data into the voxelization buffers (mesh buffers are
        // created with CopySrc usage for this).
        _commandBuffer.Begin();
        _commandBuffer.CopyBuffer(mesh.VertexBuffer, registration.Vertices.NativeBuffer, vertexBytes);
        _commandBuffer.CopyBuffer(mesh.IndexBuffer, registration.Indices.NativeBuffer, indexBytes);
        _commandBuffer.End();
        _device.Submit(_commandBuffer);

        return registration;
    }

    private static void DisposeRegistration(MeshRegistration registration)
    {
        registration.Vertices.Dispose();
        registration.Indices.Dispose();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            for (int i = 0; i < _staticMeshes.Count; i++)
            {
                DisposeRegistration(_staticMeshes[i].Registration);
            }
            for (int i = 0; i < _dynamicMeshes.Count; i++)
            {
                DisposeRegistration(_dynamicMeshes[i]);
            }
            for (int level = 0; level < LevelCount; level++)
            {
                _attrStatic[level].Dispose();
                _attrDynamic[level].Dispose();
            }
            _radiance.Dispose();
            _indirectAtlas.Dispose();
            _dataBuffer.Dispose();
            _commandBuffer.Dispose();
        }
    }
}
