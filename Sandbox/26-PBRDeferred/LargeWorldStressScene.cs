using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Alco;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;
using Alco.World3D;

/// <summary>
/// Multi-kilometer procedural open-world stress scene streamed at high world coordinates via
/// <see cref="World3DAssetPipeline"/> and <see cref="World3DSceneAssets"/>.
/// </summary>
internal sealed class LargeWorldStressScene : IDisposable
{
    private const double SectorSizeMeters = 256.0;
    private const int WorldGridExtent = 32; // 32 * 256 m = 8192 m across
    private const int HeightfieldResolution = 512;
    private const float HeightfieldCoverageMeters = 8192f;
    private const int MaxActivePointLights = 24;

    private readonly RenderingSystem _rendering;
    private readonly LocalAssetHost _assetHost = new();
    private readonly AssetSystem _assets;
    private readonly ImageUploadQueue _uploadQueue;
    private readonly World3DScene _scene;
    private readonly World3DSceneAssets _sceneAssets;
    private readonly string _assetDir;
    private readonly World3DPosition _worldOrigin;
    private readonly int _streamingRadius;
    private readonly bool _hasGpu;

    private readonly ModelAsset _groundModel;
    private readonly ModelAsset _buildingModel;
    private readonly ModelAsset _towerModel;
    private readonly ModelAsset _foliageModel;
    private readonly ModelAsset _glassModel;
    private readonly ModelAsset _pillarModel;
    private readonly ModelAsset _beaconAmberModel;
    private readonly ModelAsset _beaconCyanModel;
    private readonly ModelAsset _roverModel;

    private readonly Dictionary<(int X, int Y), ActiveSector> _sectors = [];
    private readonly List<(int X, int Y)> _pruneScratch = [];
    private readonly List<DynamicRover> _rovers = [];
    private readonly List<World3DPointLight> _pointLightsScratch = new(MaxActivePointLights);
    private readonly List<double> _windowRecordMs = new(64);
    private readonly float[] _heightfieldData;
    private readonly World3DCell _heightfieldAnchorCell;

    private int _currentCenterSectorX = int.MinValue;
    private int _currentCenterSectorY = int.MinValue;
    private int _patchCounter;
    private int _windowResetMask;
    private bool _heightfieldInitialized;
    private bool _disposed;

    /// <summary>Creates the 8 km × 8 km stress scene anchored at <paramref name="worldOrigin"/>.</summary>
    public LargeWorldStressScene(
        RenderingSystem rendering,
        World3DScene scene,
        World3DPosition worldOrigin,
        int streamingRadius,
        bool hasGpu)
    {
        _rendering = rendering;
        _scene = scene;
        _worldOrigin = worldOrigin;
        _streamingRadius = Math.Clamp(streamingRadius, 1, 8);
        _hasGpu = hasGpu;

        _sceneAssets = new World3DSceneAssets(scene);
        _assetDir = Path.Combine(Path.GetTempPath(), "alco_w3d_largeworld_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_assetDir);

        _uploadQueue = new ImageUploadQueue(rendering);
        try
        {
            GenerateArchetypeAssets(_assetDir);
            _assets = new AssetSystem(_assetHost);
            if (hasGpu)
                _assets.RegisterAssetLoader(new AssetLoaderImage(_uploadQueue));
            else
                _assets.RegisterAssetLoader(new AssetLoaderImageNoGPU(rendering, _assets));
            _assets.RegisterAssetLoader(new AssetLoaderMaterialAsset(_assets));
            World3DAssetPipeline.RegisterLoaders(_assets);
            _assets.AddFileSource(new DirectoryFileSource(_assetDir));

            _groundModel = _assets.Load<ModelAsset>("ground.amdl");
            _buildingModel = _assets.Load<ModelAsset>("building.amdl");
            _towerModel = _assets.Load<ModelAsset>("tower.amdl");
            _foliageModel = _assets.Load<ModelAsset>("foliage.amdl");
            _glassModel = _assets.Load<ModelAsset>("glass.amdl");
            _pillarModel = _assets.Load<ModelAsset>("pillar.amdl");
            _beaconAmberModel = _assets.Load<ModelAsset>("beacon_amber.amdl");
            _beaconCyanModel = _assets.Load<ModelAsset>("beacon_cyan.amdl");
            _roverModel = _assets.Load<ModelAsset>("rover.amdl");

            while (_uploadQueue.HasPendingWork)
            {
                _uploadQueue.Pump();
                Thread.Sleep(1);
            }
        }
        catch
        {
            _sceneAssets.Dispose();
            _uploadQueue.Dispose();
            _assetHost.Dispose();
            try
            {
                if (Directory.Exists(_assetDir))
                    Directory.Delete(_assetDir, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            throw;
        }

        World3DSceneCoordinates.TrySplitPosition(worldOrigin, out _heightfieldAnchorCell, out _);
        _heightfieldData = BuildFarHeightfieldGrid(_worldOrigin, _heightfieldAnchorCell,
            HeightfieldResolution, HeightfieldCoverageMeters);
    }

    /// <summary>Gets whether any initial geometry LOD uploads remain pending on the scene queue.</summary>
    public bool HasPendingGeometryUploads => _scene.GetResourceStats().PendingLodUploads > 0;

    /// <summary>Gets the number of currently mounted streaming sectors.</summary>
    public int ActiveSectorCount => _sectors.Count;

    /// <summary>Configures lighting, shadows, VoxelGI, clouds, SSR, and initial sectors around <paramref name="camera"/>.</summary>
    public void ConfigureAndPopulateInitial(in World3DCamera camera)
    {
        ConfigureSceneFeatures();
        _scene.TrySetFarHeightfield(HeightfieldResolution, HeightfieldResolution,
            HeightfieldCoverageMeters, _heightfieldData, _heightfieldAnchorCell);
        _heightfieldInitialized = true;
        UpdateStreamingRing(camera.Eye);
        UpdatePointLights(0);
    }

    /// <summary>Advances streaming sectors, dynamic movers, point lights, and periodic heightfield patches before frame recording.</summary>
    public void PrepareFrame(in World3DCamera camera, int frameIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        UpdateStreamingRing(camera.Eye);
        UpdateDynamicRovers(frameIndex);
        UpdatePointLights(frameIndex);

        if (_heightfieldInitialized && frameIndex > 0 && frameIndex % 30 == 0)
            StreamHeightfieldPatch(frameIndex);

        _sceneAssets.ReclaimReleased();
    }

    /// <summary>Records frame timing and VoxelGI telemetry after frame recording.</summary>
    public void RecordFrameTelemetry(int frameIndex, double recordMs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _windowResetMask |= _scene.LastRecordedVoxelGi.ResetCascadeMask;
        _windowRecordMs.Add(recordMs);
        if (frameIndex > 0 && frameIndex % 60 == 0)
            LogWindowTelemetry(frameIndex);
    }

    /// <summary>Computes a smooth multi-kilometer fly trajectory sweeping sectors and terrain heights inside the 8 km city.</summary>
    public World3DCamera ComputeFlyStressCamera(int frameIndex)
    {
        // Sweep across a ~4.4 km Lissajous figure-8 at ~150 m/s (crossing a 256 m sector roughly every 100 frames).
        const double radiusX = 2200.0;
        const double radiusY = 1850.0;
        const double omegaX = 0.068;
        const double omegaY = 0.092;
        double t = frameIndex * (1.0 / 60.0);

        double x = _worldOrigin.X + radiusX * Math.Cos(t * omegaX);
        double y = _worldOrigin.Y + radiusY * Math.Sin(t * omegaY);
        double z = _worldOrigin.Z + 36.0 + 28.0 * Math.Sin(t * 0.45) + 12.0 * Math.Cos(t * 0.21);

        double dx = -radiusX * omegaX * Math.Sin(t * omegaX);
        double dy = radiusY * omegaY * Math.Cos(t * omegaY);
        double dz = -0.10 * Math.Sqrt(dx * dx + dy * dy);
        Vector3 forward = Vector3.Normalize(new Vector3((float)dx, (float)dy, (float)dz));
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, forward));
        Vector3 up = Vector3.Cross(forward, right);
        Quaternion rot = Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            forward.X, forward.Y, forward.Z, 0f,
            right.X, right.Y, right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            0f, 0f, 0f, 1f));
        return new World3DCamera(new World3DPosition(x, y, z), rot, 0.85f, 0.2f);
    }

    /// <summary>
    /// Unmounts all active sectors, reclaims released scene resources across retirement frames,
    /// and verifies zero leaked handles or geometry pages in <see cref="World3DScene"/>.
    /// </summary>
    public bool TeardownAndVerifyNoSceneLeaks(RenderContext context, MainPresenterProxy submitFrame, in World3DCamera camera)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (ActiveSector sector in _sectors.Values)
            DestroySector(sector);
        _sectors.Clear();
        _rovers.Clear();
        _scene.SetPointLights([]);
        _scene.ClearFarHeightfield();

        for (int i = 0; i < 4; ++i)
        {
            _sceneAssets.ReclaimReleased();
            submitFrame(camera);
        }

        if (_hasGpu)
            _rendering.WaitIdle();
        _sceneAssets.ReclaimReleased();
        _rendering.Tick();

        World3DSceneResourceStats stats = _scene.GetResourceStats();
        RenderingMemoryStats mem = _rendering.GetMemoryStats();
        bool clean = stats.LiveSectors == 0
            && stats.LiveInstances == 0
            && stats.LiveMaterials == 0
            && stats.LiveGeometries == 0
            && stats.LiveTextures == 0
            && stats.LiveGeometryPages == 0
            && stats.LiveGeometryPageBytes == 0
            && stats.PendingLodUploads == 0
            && stats.PendingRetirementBatches == 0;

        Log.Info($"[LargeWorld Teardown] sectors={stats.LiveSectors}, instances={stats.LiveInstances}, " +
            $"materials={stats.LiveMaterials}, geometries={stats.LiveGeometries}, textures={stats.LiveTextures}, " +
            $"geomPages={stats.LiveGeometryPages} ({stats.LiveGeometryPageBytes} B), tablePages={stats.LiveTablePages}, " +
            $"pendingLods={stats.PendingLodUploads}, pendingRetire={stats.PendingRetirementBatches}");
        Log.Info($"[LargeWorld Teardown VRAM] liveBuffers={mem.LiveBuffers}, liveTextures={mem.LiveTextures}, " +
            $"liveSamplers={mem.LiveSamplers}, gpuUsedMiB={mem.GpuBufferPool.UsedBytes / (1024.0 * 1024.0):F2}, " +
            $"cpuUsedMiB={mem.CpuBufferPool.UsedBytes / (1024.0 * 1024.0):F2}, texUsedMiB={mem.TexturePool.UsedBytes / (1024.0 * 1024.0):F2}");
        Log.Info($"[LargeWorld Scene LeakCheck] SCENE_LEAK_CHECK={(clean ? "PASS" : "FAIL")}");
        return clean;
    }

    public delegate bool MainPresenterProxy(in World3DCamera camera);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _sectors.Clear();
        _rovers.Clear();
        _sceneAssets.Dispose();
        _uploadQueue.Dispose();
        _assetHost.Dispose();

        try
        {
            if (Directory.Exists(_assetDir))
                Directory.Delete(_assetDir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void ConfigureSceneFeatures()
    {
        _scene.MaximumDrawDistance = 4200f;
        _scene.Occlusion.Enabled = true;
        _scene.Occlusion.TwoPhase = true;
        _scene.Shadows.Enabled = true;
        _scene.Shadows.MaxDistance = 260f;
        _scene.Shadows.UseMinMaxAcceleration = true;
        _scene.Shadows.FarField.Enabled = true;
        _scene.Shadows.FarField.EnableStaticFarCascade = true;
        _scene.Shadows.FarField.AutoCaptureFromScene = true;
        _scene.Shadows.FarField.WorldCoverageMeters = HeightfieldCoverageMeters;
        _scene.Shadows.FarField.HorizonMaxDistance = 4096f;
        _scene.Shadows.Rsm.Enabled = true;
        _scene.Lighting = _scene.Lighting with
        {
            DirectionToSun = ProceduralSkyUtility.GetDirectionToSun(9.2f),
            SunIntensity = 12f,
            SkyExposure = 1.05f,
            AmbientFloor = 0.08f,
        };
    }

    private void UpdateStreamingRing(World3DPosition eye)
    {
        int halfGrid = WorldGridExtent / 2;
        int centerX = Math.Clamp((int)Math.Floor((eye.X - _worldOrigin.X) / SectorSizeMeters), -halfGrid, halfGrid - 1);
        int centerY = Math.Clamp((int)Math.Floor((eye.Y - _worldOrigin.Y) / SectorSizeMeters), -halfGrid, halfGrid - 1);
        if (centerX == _currentCenterSectorX && centerY == _currentCenterSectorY)
            return;

        _currentCenterSectorX = centerX;
        _currentCenterSectorY = centerY;

        _pruneScratch.Clear();
        foreach ((int sx, int sy) key in _sectors.Keys)
        {
            if (Math.Abs(key.sx - centerX) > _streamingRadius || Math.Abs(key.sy - centerY) > _streamingRadius)
                _pruneScratch.Add(key);
        }
        foreach ((int sx, int sy) key in _pruneScratch)
        {
            if (_sectors.Remove(key, out ActiveSector? dead))
                DestroySector(dead);
        }

        int minSx = Math.Max(-halfGrid, centerX - _streamingRadius);
        int maxSx = Math.Min(halfGrid - 1, centerX + _streamingRadius);
        int minSy = Math.Max(-halfGrid, centerY - _streamingRadius);
        int maxSy = Math.Min(halfGrid - 1, centerY + _streamingRadius);

        for (int sy = minSy; sy <= maxSy; ++sy)
        {
            for (int sx = minSx; sx <= maxSx; ++sx)
            {
                if (!_sectors.ContainsKey((sx, sy)))
                    MountSector(sx, sy);
            }
        }
    }

    private void MountSector(int sx, int sy)
    {
        if (!_scene.TryAttachSector(out WorldSectorHandle sectorHandle))
            throw new InvalidOperationException($"World3DScene rejected sector attach at ({sx}, {sy}).");

        double originX = _worldOrigin.X + sx * SectorSizeMeters;
        double originY = _worldOrigin.Y + sy * SectorSizeMeters;
        double originZ = _worldOrigin.Z;
        uint seed = HashSector(sx, sy);

        List<World3DModelInstance> instances = new(270);
        List<DynamicRover> sectorRovers = new(4);
        ReadOnlySpan<float> geomLods = [0f, 110f, 320f];

        // 1. Ground tile (256 x 256 m).
        TryAddModel(_groundModel, sectorHandle, new World3DPosition(originX + 128.0, originY + 128.0, originZ - 0.5),
            Quaternion.Identity, new Vector3(256f, 256f, 1f), geomLods, instances, castsShadow: false);

        // 2. 6x6 city building grid (36 opaque multi-LOD buildings).
        for (int gx = 0; gx < 6; ++gx)
        {
            for (int gy = 0; gy < 6; ++gy)
            {
                uint h = HashValue(seed, gx * 6 + gy);
                double px = originX + 22.0 + gx * 40.0 + ((h & 15u) - 7.5);
                double py = originY + 22.0 + gy * 40.0 + (((h >> 4) & 15u) - 7.5);
                float widthX = 11f + ((h >> 8) & 7u) * 1.4f;
                float widthY = 11f + ((h >> 11) & 7u) * 1.4f;
                float heightZ = 12f + ((h >> 14) & 15u) * 2.2f;
                float yaw = ((h >> 18) & 3u) * (MathF.PI * 0.5f);
                TryAddModel(_buildingModel, sectorHandle, new World3DPosition(px, py, originZ),
                    Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw), new Vector3(widthX, widthY, heightZ),
                    geomLods, instances);
            }
        }

        // 3. Tall 150 m landmark spire (~1 per sector on parity check, testing CSM -> StaticFar -> FarHeightfield transitions).
        if (((sx + sy) & 1) == 0)
        {
            double tx = originX + 128.0 + ((seed & 31u) - 16.0);
            double ty = originY + 128.0 + (((seed >> 5) & 31u) - 16.0);
            float hTower = 135f + ((seed >> 10) & 15u) * 2.5f;
            TryAddModel(_towerModel, sectorHandle, new World3DPosition(tx, ty, originZ),
                Quaternion.Identity, new Vector3(22f, 22f, hTower), geomLods, instances);
        }

        // 4. 120 alpha-mask foliage clusters.
        for (int f = 0; f < 120; ++f)
        {
            uint h = HashValue(seed ^ 0xA511E9B3u, f);
            double px = originX + 6.0 + (h & 255u) * (244.0 / 255.0);
            double py = originY + 6.0 + ((h >> 8) & 255u) * (244.0 / 255.0);
            float scale = 3.5f + ((h >> 16) & 7u) * 0.5f;
            float yaw = ((h >> 20) & 31u) * (MathF.PI / 16f);
            TryAddModel(_foliageModel, sectorHandle, new World3DPosition(px, py, originZ),
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw), new Vector3(scale, scale, scale * 1.35f),
                geomLods, instances);
        }

        // 5. 24 transmissive glass pavilions / skybridges (Blend + Forward refraction).
        for (int g = 0; g < 24; ++g)
        {
            uint h = HashValue(seed ^ 0x3C6EF372u, g);
            double px = originX + 16.0 + (g % 6) * 42.0;
            double py = originY + 32.0 + (g / 6) * 56.0;
            double pz = originZ + 12.0 + ((h >> 4) & 7u) * 2.0;
            TryAddModel(_glassModel, sectorHandle, new World3DPosition(px, py, pz),
                Quaternion.Identity, new Vector3(14f, 4.5f, 3.2f), geomLods, instances,
                castsShadow: false, affectsGlobalIllumination: false);
        }

        // 6. 40 reflective metallic chrome monoliths (SSR targets).
        for (int p = 0; p < 40; ++p)
        {
            uint h = HashValue(seed ^ 0x9E3779B9u, p);
            double px = originX + 12.0 + (p % 10) * 24.0;
            double py = originY + 12.0 + (p / 10) * 60.0 + ((h & 7u) - 3.5);
            TryAddModel(_pillarModel, sectorHandle, new World3DPosition(px, py, originZ),
                Quaternion.Identity, new Vector3(2.2f, 2.2f, 9f + (h & 7u)), geomLods, instances);
        }

        // 7. 24 emissive beacons feeding VoxelGI radiance + street accents.
        for (int b = 0; b < 24; ++b)
        {
            ModelAsset model = (b & 1) == 0 ? _beaconAmberModel : _beaconCyanModel;
            double px = originX + 10.0 + (b % 6) * 42.0;
            double py = originY + 18.0 + (b / 6) * 62.0;
            TryAddModel(model, sectorHandle, new World3DPosition(px, py, originZ),
                Quaternion.Identity, new Vector3(1.4f, 1.4f, 5.5f), geomLods, instances, castsShadow: false);
        }

        // 8. 4 dynamic movers circling within the sector (updating per-frame for dynamic VoxelGI cascade).
        for (int r = 0; r < 4; ++r)
        {
            double cx = originX + 64.0 + (r % 2) * 128.0;
            double cy = originY + 64.0 + (r / 2) * 128.0;
            double height = originZ + (r >= 2 ? 22.0 : 2.2);
            if (_sceneAssets.TryInstantiateModel(_roverModel, sectorHandle,
                new World3DPosition(cx + 18.0, cy, height), Quaternion.Identity, new Vector3(3.2f, 1.8f, 1.4f),
                out World3DModelInstance? inst, dynamic: true, castsShadow: true, affectsGlobalIllumination: true,
                lodMinimumDistances: geomLods))
            {
                instances.Add(inst);
                DynamicRover rover = new(inst.Instances[0], cx, cy, height, 18.0 + r * 3.0, r * 1.57);
                sectorRovers.Add(rover);
                _rovers.Add(rover);
            }
        }

        _sectors.Add((sx, sy), new ActiveSector(sectorHandle, instances, sectorRovers));
    }

    private void DestroySector(ActiveSector sector)
    {
        foreach (DynamicRover rover in sector.Rovers)
            _rovers.Remove(rover);
        foreach (World3DModelInstance inst in sector.Instances)
            _sceneAssets.DestroyModelInstance(inst);
        _scene.TryDetachSector(sector.Handle);
    }

    private void TryAddModel(
        ModelAsset model,
        WorldSectorHandle sector,
        World3DPosition position,
        Quaternion rotation,
        Vector3 scale,
        ReadOnlySpan<float> lodDistances,
        List<World3DModelInstance> dst,
        bool castsShadow = true,
        bool affectsGlobalIllumination = true)
    {
        if (_sceneAssets.TryInstantiateModel(model, sector, position, rotation, scale,
            out World3DModelInstance? inst, dynamic: false, castsShadow: castsShadow,
            affectsGlobalIllumination: affectsGlobalIllumination, lodMinimumDistances: lodDistances))
        {
            dst.Add(inst);
        }
    }

    private void UpdateDynamicRovers(int frameIndex)
    {
        double angleOffset = frameIndex * 0.035;
        WorldGeometryHandle geom = default;
        WorldMaterialHandle mat = default;
        WorldSectorHandle sector = default;
        bool fetchedProto = false;

        foreach (DynamicRover rover in _rovers)
        {
            if (!_scene.TryGetInstance(rover.Instance, out World3DInstanceDesc current))
                continue;
            if (!fetchedProto)
            {
                geom = current.Geometry;
                mat = current.Material;
                sector = current.Sector;
                fetchedProto = true;
            }
            _ = geom;
            _ = mat;
            _ = sector;

            double theta = rover.Phase + angleOffset;
            double x = rover.CenterX + rover.Radius * Math.Cos(theta);
            double y = rover.CenterY + rover.Radius * Math.Sin(theta);
            float yaw = (float)(theta + Math.PI * 0.5);
            World3DTransform nextXform = new(
                new World3DPosition(x, y, rover.Height),
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw),
                current.Transform.Scale);
            _scene.TryUpdateInstance(rover.Instance, current with { Transform = nextXform });
        }
    }

    private void UpdatePointLights(int frameIndex)
    {
        _pointLightsScratch.Clear();
        double spin = frameIndex * 0.025;
        for (int i = 0; i < 16; ++i)
        {
            double angle = spin + i * (Math.PI * 2.0 / 16.0);
            double r = 24.0 + (i % 3) * 28.0;
            World3DPosition pos = new(
                _worldOrigin.X + r * Math.Cos(angle),
                _worldOrigin.Y + r * Math.Sin(angle),
                _worldOrigin.Z + 8.0 + (i % 4) * 3.0);
            Vector3 col = (i & 1) == 0 ? new Vector3(1.0f, 0.72f, 0.32f) : new Vector3(0.32f, 0.82f, 1.0f);
            _pointLightsScratch.Add(new World3DPointLight(pos, col, 35f, 28f));
        }
        _scene.SetPointLights(CollectionsMarshal.AsSpan(_pointLightsScratch));
    }

    private void StreamHeightfieldPatch(int frameIndex)
    {
        const int patchW = 32;
        const int patchH = 32;
        int tileX = (_patchCounter % 12) * 32 + 32;
        int tileY = ((_patchCounter / 12) % 12) * 32 + 32;
        ++_patchCounter;

        Span<float> patch = stackalloc float[patchW * patchH];
        double baseZ = _worldOrigin.Z;
        for (int py = 0; py < patchH; ++py)
        {
            for (int px = 0; px < patchW; ++px)
            {
                int ix = tileX + px;
                int iy = tileY + py;
                float existing = _heightfieldData[iy * HeightfieldResolution + ix];
                patch[py * patchW + px] = MathF.Max(existing, (float)(baseZ + 2.0 * Math.Sin(frameIndex * 0.05 + px * 0.2)));
            }
        }
        _scene.TryUpdateFarHeightfieldRegion(tileX, tileY, patchW, patchH, patch);
    }

    private void LogWindowTelemetry(int frameIndex)
    {
        _windowRecordMs.Sort();
        double p50 = Percentile(_windowRecordMs, 0.50);
        double p95 = Percentile(_windowRecordMs, 0.95);
        double p99 = Percentile(_windowRecordMs, 0.99);
        _windowRecordMs.Clear();

        World3DSceneResourceStats res = _scene.GetResourceStats();
        RenderingMemoryStats mem = _rendering.GetMemoryStats();
        World3DVoxelGiStats gi = _scene.LastRecordedVoxelGi;
        Log.Info($"[LargeWorld frame={frameIndex}] sectors={res.LiveSectors}, instances={res.LiveInstances}, " +
            $"recordMs(p50/p95/p99)={p50:F2}/{p95:F2}/{p99:F2}, giReset=0x{gi.ResetCascadeMask:X}, giWindowReset=0x{_windowResetMask:X}, giEvict={gi.EvictedStaticBricksThisFrame}, " +
            $"gpuUsedMiB={mem.GpuBufferPool.UsedBytes / (1024.0 * 1024.0):F2}, texUsedMiB={mem.TexturePool.UsedBytes / (1024.0 * 1024.0):F2}");
        _windowResetMask = 0;
    }

    private static double Percentile(List<double> sorted, double q) =>
        sorted.Count == 0 ? 0.0 : sorted[Math.Clamp((int)Math.Floor(sorted.Count * q), 0, sorted.Count - 1)];

    private static float[] BuildFarHeightfieldGrid(World3DPosition origin, World3DCell anchorCell, int res, float coverageMeters)
    {
        float[] grid = new float[res * res];
        double anchorX = (double)anchorCell.X * World3DSceneCoordinates.CellSize;
        double anchorY = (double)anchorCell.Y * World3DSceneCoordinates.CellSize;
        int halfGrid = WorldGridExtent / 2;

        for (int iy = 0; iy < res; ++iy)
        {
            double worldY = anchorY + ((iy + 0.5) / res - 0.5) * coverageMeters;
            for (int ix = 0; ix < res; ++ix)
            {
                double worldX = anchorX + ((ix + 0.5) / res - 0.5) * coverageMeters;
                double hill = 4.0 * Math.Sin((worldX - origin.X) * 0.0022) * Math.Cos((worldY - origin.Y) * 0.0022);
                double h = origin.Z + hill;

                int sx = (int)Math.Floor((worldX - origin.X) / SectorSizeMeters);
                int sy = (int)Math.Floor((worldY - origin.Y) / SectorSizeMeters);
                if (sx >= -halfGrid && sx < halfGrid && sy >= -halfGrid && sy < halfGrid && ((sx + sy) & 1) == 0)
                {
                    uint seed = HashSector(sx, sy);
                    double tx = origin.X + sx * SectorSizeMeters + 128.0 + ((seed & 31u) - 16.0);
                    double ty = origin.Y + sy * SectorSizeMeters + 128.0 + (((seed >> 5) & 31u) - 16.0);
                    if (Math.Abs(worldX - tx) < 16.0 && Math.Abs(worldY - ty) < 16.0)
                        h = Math.Max(h, origin.Z + 135.0 + ((seed >> 10) & 15u) * 2.5);
                }
                grid[iy * res + ix] = (float)h;
            }
        }
        return grid;
    }

    private static uint HashSector(int sx, int sy)
    {
        uint h = (uint)(sx * 73856093) ^ (uint)(sy * 19349663) ^ 0x9E3779B9u;
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return h;
    }

    private static uint HashValue(uint seed, int index)
    {
        uint h = seed ^ (uint)(index * 0x27D4EB2D);
        h ^= h >> 15;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        return h;
    }

    private static void GenerateArchetypeAssets(string dir)
    {
        WriteStoneTextures(dir);
        WriteBuildingTextures(dir);
        WriteFoliageTexture(dir);

        WriteBoxMultiLodAmsh(Path.Combine(dir, "ground.amsh"), "ground", centerZ: 0.5f, topCap: false, spire: false);
        WriteBoxMultiLodAmsh(Path.Combine(dir, "building.amsh"), "building", centerZ: 0.5f, topCap: true, spire: false);
        WriteBoxMultiLodAmsh(Path.Combine(dir, "tower.amsh"), "tower", centerZ: 0.5f, topCap: true, spire: true);
        WriteCrossedCardMultiLodAmsh(Path.Combine(dir, "foliage.amsh"), "foliage");
        WriteBoxMultiLodAmsh(Path.Combine(dir, "glass.amsh"), "glass", centerZ: 0.5f, topCap: false, spire: false);
        WriteBoxMultiLodAmsh(Path.Combine(dir, "pillar.amsh"), "pillar", centerZ: 0.5f, topCap: false, spire: false);
        WriteBoxMultiLodAmsh(Path.Combine(dir, "beacon.amsh"), "beacon", centerZ: 0.5f, topCap: false, spire: false);
        WriteBoxMultiLodAmsh(Path.Combine(dir, "rover.amsh"), "rover", centerZ: 0.5f, topCap: true, spire: false);

        File.WriteAllText(Path.Combine(dir, "ground.amat"), """
            {
                "$type": "Alco.World3D.PbrMaterialAsset",
                "version": "1.0",
                "baseColorFactor": { "r": 0.9, "g": 0.9, "b": 0.92, "a": 1.0 },
                "metallicFactor": 0.05,
                "roughnessFactor": 0.72,
                "textures": {
                    "baseColor": "stone_albedo.png",
                    "normal": "stone_normal.png"
                }
            }
            """);
        File.WriteAllText(Path.Combine(dir, "building.amat"), """
            {
                "$type": "Alco.World3D.PbrMaterialAsset",
                "version": "1.0",
                "baseColorFactor": { "r": 0.85, "g": 0.80, "b": 0.76, "a": 1.0 },
                "metallicFactor": 0.12,
                "roughnessFactor": 0.62,
                "textures": {
                    "baseColor": "building_albedo.png",
                    "normal": "stone_normal.png"
                }
            }
            """);
        File.WriteAllText(Path.Combine(dir, "tower.amat"), """
            {
                "$type": "Alco.World3D.PbrMaterialAsset",
                "version": "1.0",
                "baseColorFactor": { "r": 0.76, "g": 0.82, "b": 0.90, "a": 1.0 },
                "metallicFactor": 0.55,
                "roughnessFactor": 0.28,
                "textures": {
                    "baseColor": "building_albedo.png"
                }
            }
            """);
        File.WriteAllText(Path.Combine(dir, "foliage.amat"), """
            {
                "$type": "Alco.World3D.PbrMaterialAsset",
                "version": "1.0",
                "baseColorFactor": { "r": 1.0, "g": 1.0, "b": 1.0, "a": 1.0 },
                "roughnessFactor": 0.82,
                "alphaMode": "Mask",
                "alphaCutoff": 0.45,
                "doubleSided": true,
                "textures": {
                    "baseColor": "foliage_leaf.png"
                }
            }
            """);
        File.WriteAllText(Path.Combine(dir, "glass.amat"), """
            {
                "$type": "Alco.World3D.PbrMaterialAsset",
                "version": "1.0",
                "baseColorFactor": { "r": 0.55, "g": 0.88, "b": 0.98, "a": 0.35 },
                "metallicFactor": 0.05,
                "roughnessFactor": 0.08,
                "alphaMode": "Blend",
                "doubleSided": true,
                "transmissionFactor": 0.86,
                "ior": 1.52
            }
            """);
        File.WriteAllText(Path.Combine(dir, "chrome.amat"), """
            {
                "$type": "Alco.World3D.PbrMaterialAsset",
                "version": "1.0",
                "baseColorFactor": { "r": 0.92, "g": 0.94, "b": 0.97, "a": 1.0 },
                "metallicFactor": 0.96,
                "roughnessFactor": 0.10
            }
            """);
        File.WriteAllText(Path.Combine(dir, "beacon_amber.amat"), """
            {
                "$type": "Alco.World3D.PbrMaterialAsset",
                "version": "1.0",
                "baseColorFactor": { "r": 1.0, "g": 0.65, "b": 0.22, "a": 1.0 },
                "roughnessFactor": 0.35,
                "emissiveFactor": { "r": 4.2, "g": 2.0, "b": 0.35 }
            }
            """);
        File.WriteAllText(Path.Combine(dir, "beacon_cyan.amat"), """
            {
                "$type": "Alco.World3D.PbrMaterialAsset",
                "version": "1.0",
                "baseColorFactor": { "r": 0.28, "g": 0.85, "b": 1.0, "a": 1.0 },
                "roughnessFactor": 0.35,
                "emissiveFactor": { "r": 0.30, "g": 3.0, "b": 4.5 }
            }
            """);
        File.WriteAllText(Path.Combine(dir, "rover.amat"), """
            {
                "$type": "Alco.World3D.PbrMaterialAsset",
                "version": "1.0",
                "baseColorFactor": { "r": 0.95, "g": 0.25, "b": 0.18, "a": 1.0 },
                "metallicFactor": 0.75,
                "roughnessFactor": 0.22,
                "emissiveFactor": { "r": 1.8, "g": 0.35, "b": 0.15 }
            }
            """);

        WriteModel(dir, "ground.amdl", "ground.amsh", "ground", "ground.amat");
        WriteModel(dir, "building.amdl", "building.amsh", "building", "building.amat");
        WriteModel(dir, "tower.amdl", "tower.amsh", "tower", "tower.amat");
        WriteModel(dir, "foliage.amdl", "foliage.amsh", "foliage", "foliage.amat");
        WriteModel(dir, "glass.amdl", "glass.amsh", "glass", "glass.amat");
        WriteModel(dir, "pillar.amdl", "pillar.amsh", "pillar", "chrome.amat");
        WriteModel(dir, "beacon_amber.amdl", "beacon.amsh", "beacon", "beacon_amber.amat");
        WriteModel(dir, "beacon_cyan.amdl", "beacon.amsh", "beacon", "beacon_cyan.amat");
        WriteModel(dir, "rover.amdl", "rover.amsh", "rover", "rover.amat");
    }

    private static void WriteModel(string dir, string file, string mesh, string slot, string material)
    {
        File.WriteAllText(Path.Combine(dir, file), $$"""
            {
                "version": "1.0",
                "mesh": "{{mesh}}",
                "slots": [ { "name": "{{slot}}", "material": "{{material}}" } ]
            }
            """);
    }

    private static void WriteStoneTextures(string dir)
    {
        const int size = 64;
        byte[] albedo = new byte[size * size * 4];
        byte[] normal = new byte[size * size * 4];
        for (int y = 0; y < size; ++y)
        {
            for (int x = 0; x < size; ++x)
            {
                int i = (y * size + x) * 4;
                bool border = x < 2 || y < 2 || x == size / 2 || y == size / 2;
                int shade = border ? 105 : 148 + ((x * 7 + y * 13) & 15);
                albedo[i] = (byte)shade;
                albedo[i + 1] = (byte)Math.Min(255, shade + 4);
                albedo[i + 2] = (byte)Math.Min(255, shade + 8);
                albedo[i + 3] = 255;

                normal[i] = border ? (byte)112 : (byte)128;
                normal[i + 1] = border ? (byte)144 : (byte)128;
                normal[i + 2] = 255;
                normal[i + 3] = 255;
            }
        }
        File.WriteAllBytes(Path.Combine(dir, "stone_albedo.png"), ImageEncodeUtility.EncodePng(albedo, size, size));
        File.WriteAllBytes(Path.Combine(dir, "stone_normal.png"), ImageEncodeUtility.EncodePng(normal, size, size));
    }

    private static void WriteBuildingTextures(string dir)
    {
        const int size = 64;
        byte[] albedo = new byte[size * size * 4];
        for (int y = 0; y < size; ++y)
        {
            for (int x = 0; x < size; ++x)
            {
                int i = (y * size + x) * 4;
                bool window = (x % 16) is >= 4 and <= 11 && (y % 16) is >= 4 and <= 12;
                albedo[i] = window ? (byte)64 : (byte)176;
                albedo[i + 1] = window ? (byte)96 : (byte)170;
                albedo[i + 2] = window ? (byte)132 : (byte)162;
                albedo[i + 3] = 255;
            }
        }
        File.WriteAllBytes(Path.Combine(dir, "building_albedo.png"), ImageEncodeUtility.EncodePng(albedo, size, size));
    }

    private static void WriteFoliageTexture(string dir)
    {
        const int size = 64;
        byte[] rgba = new byte[size * size * 4];
        for (int y = 0; y < size; ++y)
        {
            float ny = (y + 0.5f) / size * 2f - 1f;
            for (int x = 0; x < size; ++x)
            {
                float nx = (x + 0.5f) / size * 2f - 1f;
                float dist = MathF.Sqrt(nx * nx * 1.15f + ny * ny * 0.9f);
                float lobe = 0.82f + 0.14f * MathF.Sin(MathF.Atan2(ny, nx) * 5f);
                bool inside = dist < lobe;
                int i = (y * size + x) * 4;
                rgba[i] = inside ? (byte)(52 + (int)(28 * MathF.Max(0f, 1f - dist))) : (byte)32;
                rgba[i + 1] = inside ? (byte)(128 + (int)(55 * MathF.Max(0f, 1f - dist))) : (byte)64;
                rgba[i + 2] = inside ? (byte)42 : (byte)24;
                rgba[i + 3] = inside ? (byte)255 : (byte)0;
            }
        }
        File.WriteAllBytes(Path.Combine(dir, "foliage_leaf.png"), ImageEncodeUtility.EncodePng(rgba, size, size));
    }

    private static void WriteBoxMultiLodAmsh(string path, string slotName, float centerZ, bool topCap, bool spire)
    {
        // LOD0: multi-part compound geometry; LOD1: two-tier box; LOD2: single bounding box.
        (VertexPBR[] v0, uint[] i0) = BuildCompoundBox(centerZ, topCap, spire, detail: 2);
        (VertexPBR[] v1, uint[] i1) = BuildCompoundBox(centerZ, topCap, spire: false, detail: 1);
        (VertexPBR[] v2, uint[] i2) = BuildCompoundBox(centerZ, topCap: false, spire: false, detail: 0);

        MeshAssetBuildData data = new()
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Streams = MeshVertexLayout.CreatePBR(),
            Lods =
            [
                new MeshAssetBuildLod
                {
                    Vertices = MemoryMarshal.AsBytes(v0.AsSpan()).ToArray(),
                    Indices = i0,
                    MaxError = 0f,
                    SubMeshes = [new MeshSubMeshMeta(slotName, 0, (uint)i0.Length)],
                },
                new MeshAssetBuildLod
                {
                    Vertices = MemoryMarshal.AsBytes(v1.AsSpan()).ToArray(),
                    Indices = i1,
                    MaxError = 0.4f,
                    SubMeshes = [new MeshSubMeshMeta(slotName, 0, (uint)i1.Length)],
                },
                new MeshAssetBuildLod
                {
                    Vertices = MemoryMarshal.AsBytes(v2.AsSpan()).ToArray(),
                    Indices = i2,
                    MaxError = 1.0f,
                    SubMeshes = [new MeshSubMeshMeta(slotName, 0, (uint)i2.Length)],
                },
            ],
        };
        using FileStream fs = File.Create(path);
        MeshAssetWriter.Write(data, fs);
    }

    private static void WriteCrossedCardMultiLodAmsh(string path, string slotName)
    {
        (VertexPBR[] v0, uint[] i0) = BuildCrossedCards(cardCount: 3);
        (VertexPBR[] v1, uint[] i1) = BuildCrossedCards(cardCount: 2);
        (VertexPBR[] v2, uint[] i2) = BuildCrossedCards(cardCount: 1);

        MeshAssetBuildData data = new()
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Streams = MeshVertexLayout.CreatePBR(),
            Lods =
            [
                new MeshAssetBuildLod
                {
                    Vertices = MemoryMarshal.AsBytes(v0.AsSpan()).ToArray(),
                    Indices = i0,
                    MaxError = 0f,
                    SubMeshes = [new MeshSubMeshMeta(slotName, 0, (uint)i0.Length)],
                },
                new MeshAssetBuildLod
                {
                    Vertices = MemoryMarshal.AsBytes(v1.AsSpan()).ToArray(),
                    Indices = i1,
                    MaxError = 0.35f,
                    SubMeshes = [new MeshSubMeshMeta(slotName, 0, (uint)i1.Length)],
                },
                new MeshAssetBuildLod
                {
                    Vertices = MemoryMarshal.AsBytes(v2.AsSpan()).ToArray(),
                    Indices = i2,
                    MaxError = 0.9f,
                    SubMeshes = [new MeshSubMeshMeta(slotName, 0, (uint)i2.Length)],
                },
            ],
        };
        using FileStream fs = File.Create(path);
        MeshAssetWriter.Write(data, fs);
    }

    private static (VertexPBR[] Vertices, uint[] Indices) BuildCompoundBox(float centerZ, bool topCap, bool spire, int detail)
    {
        List<VertexPBR> verts = new(72);
        List<uint> indices = new(108);

        AppendBox(verts, indices, new Vector3(0f, 0f, centerZ), new Vector3(1f, 1f, detail == 0 ? 1f : (spire ? 0.68f : 0.85f)), uvScale: 2f);
        if (detail >= 1 && topCap)
        {
            AppendBox(verts, indices, new Vector3(0f, 0f, centerZ + 0.38f),
                new Vector3(0.76f, 0.76f, spire ? 0.24f : 0.18f), uvScale: 1.5f);
        }
        if (detail >= 2 && spire)
        {
            AppendBox(verts, indices, new Vector3(0f, 0f, centerZ + 0.56f),
                new Vector3(0.32f, 0.32f, 0.16f), uvScale: 1f);
        }
        return (verts.ToArray(), indices.ToArray());
    }

    private static (VertexPBR[] Vertices, uint[] Indices) BuildCrossedCards(int cardCount)
    {
        List<VertexPBR> verts = new(cardCount * 4);
        List<uint> indices = new(cardCount * 6);
        for (int c = 0; c < cardCount; ++c)
        {
            float angle = c * (MathF.PI / cardCount);
            Vector3 u = new(MathF.Cos(angle) * 0.5f, MathF.Sin(angle) * 0.5f, 0f);
            Vector3 v = Vector3.UnitZ * 0.5f;
            Vector3 normal = Vector3.Normalize(Vector3.Cross(u, v));
            Vector4 tangent = new(Vector3.Normalize(u), 1f);
            Vector3 center = new(0f, 0f, 0.5f);

            uint first = (uint)verts.Count;
            verts.Add(new VertexPBR(center - u - v, normal, new Vector2(0f, 1f), tangent));
            verts.Add(new VertexPBR(center + u - v, normal, new Vector2(1f, 1f), tangent));
            verts.Add(new VertexPBR(center - u + v, normal, new Vector2(0f, 0f), tangent));
            verts.Add(new VertexPBR(center + u + v, normal, new Vector2(1f, 0f), tangent));

            indices.Add(first);
            indices.Add(first + 1);
            indices.Add(first + 2);
            indices.Add(first + 1);
            indices.Add(first + 3);
            indices.Add(first + 2);
        }
        return (verts.ToArray(), indices.ToArray());
    }

    private static void AppendBox(List<VertexPBR> verts, List<uint> indices, Vector3 center, Vector3 size, float uvScale)
    {
        ReadOnlySpan<Vector3> normals =
        [
            Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ,
        ];
        Vector3 half = size * 0.5f;
        foreach (Vector3 normal in normals)
        {
            Vector3 u = MathF.Abs(normal.Z) > 0.5f ? Vector3.UnitX : Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, normal));
            Vector3 v = Vector3.Cross(normal, u);
            Vector4 tangent = new(u, 1f);
            uint first = (uint)verts.Count;

            verts.Add(new VertexPBR(center + (normal - u - v) * half, normal, new Vector2(0f, 0f), tangent));
            verts.Add(new VertexPBR(center + (normal + u - v) * half, normal, new Vector2(uvScale, 0f), tangent));
            verts.Add(new VertexPBR(center + (normal - u + v) * half, normal, new Vector2(0f, uvScale), tangent));
            verts.Add(new VertexPBR(center + (normal + u + v) * half, normal, new Vector2(uvScale, uvScale), tangent));

            indices.Add(first);
            indices.Add(first + 1);
            indices.Add(first + 2);
            indices.Add(first + 1);
            indices.Add(first + 3);
            indices.Add(first + 2);
        }
    }

    private sealed class ActiveSector(WorldSectorHandle handle, List<World3DModelInstance> instances, List<DynamicRover> rovers)
    {
        public WorldSectorHandle Handle { get; } = handle;
        public List<World3DModelInstance> Instances { get; } = instances;
        public List<DynamicRover> Rovers { get; } = rovers;
    }

    private sealed class DynamicRover(WorldInstanceHandle instance, double centerX, double centerY, double height,
        double radius, double phase)
    {
        public WorldInstanceHandle Instance { get; } = instance;
        public double CenterX { get; } = centerX;
        public double CenterY { get; } = centerY;
        public double Height { get; } = height;
        public double Radius { get; } = radius;
        public double Phase { get; } = phase;
    }

    private sealed class LocalAssetHost : IAssetSystemHost, IDisposable
    {
        public event Action? OnDispose;
        public void PostToMainThread(Action action) => action();
        public void LogInfo(ReadOnlySpan<char> message) { }
        public void LogWarning(ReadOnlySpan<char> message) { }
        public void LogError(ReadOnlySpan<char> message) { }
        public void LogSuccess(ReadOnlySpan<char> message) { }
        public void Dispose() => OnDispose?.Invoke();
    }
}
