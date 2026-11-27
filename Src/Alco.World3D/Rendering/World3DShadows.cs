using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private const int MainCascadeCount = 4;
    private const int TotalShadowCascadeSlices = 5;
    private const uint MinMaxBlockSize = 8;

    private struct CascadeCacheState
    {
        public bool Valid;
        public ulong LastUpdateFrame;
        public ulong SceneRevision;
        public Vector3 SunDir;
        public int Resolution;
        public float WorldSize;
        public float MinRadius;
        public float LodBias;
        public World3DPosition AnchorWorld;
        public World3DCascadeGpu GpuData;
        public World3DView ViewData;
    }

    private struct RsmCacheState
    {
        public bool Valid;
        public ulong SceneRevision;
        public Vector3 SunDir;
        public int Resolution;
        public float WorldExtent;
        public float DepthRange;
        public World3DPosition AnchorWorld;
        public World3DCascadeGpu GpuCascade;
    }

    private readonly CascadeCacheState[] _cascadeCache = new CascadeCacheState[TotalShadowCascadeSlices];
    private RsmCacheState _rsmCache;
    private ulong _shadowFrameCounter;
    private ulong _lastShadowDiscardVersion;

    private Texture? _shadowArray;
    private Texture? _shadowMinMaxTemp;
    private Texture? _shadowMinMaxArray;
    private Texture? _rsmFlux;
    private Texture? _rsmNormal;
    private Texture? _rsmDepth;

    private Shader? _minMaxReduceShader;
    private Shader? _minMaxDilateShader;

    private int _recordedShadowResolution;
    private int _recordedMinMaxResolution;
    private bool _recordedShadowsEnabled;
    private bool _recordedUseMinMax;
    private bool _recordedStaticFarActive;
    private bool _recordedRsmEnabled;
    private bool _recordedRsmUpdate;
    private uint _recordedRsmViewSlot;
    private readonly uint[] _recordedSliceViewSlots = new uint[TotalShadowCascadeSlices];
    private readonly bool[] _recordedSliceUpdated = new bool[TotalShadowCascadeSlices];
    private readonly CascadeCacheState[] _pendingCascadeState = new CascadeCacheState[TotalShadowCascadeSlices];
    private RsmCacheState _pendingRsmState;
    private readonly float[] _recordedCascadeSplits = new float[MainCascadeCount];
    private float _recordedStaticFarSplit;
    private Vector3 _recordedShadowSunDir;
    private Vector4 _recordedRsmSunDirIntensity;
    private Vector4 _recordedRsmSunColor;
    private uint _recordedShadowDrawCalls;
    private BufferSlice _frameShadowData;

    private RGTexture _frameShadowArrayRG;
    private RGTexture _frameShadowMinMaxRG;
    private RGTexture _frameRsmFluxRG;
    private RGTexture _frameRsmNormalRG;
    private RGTexture _frameRsmDepthRG;

    private void PrepareShadows(RenderContext context, in World3DCamera camera, in World3DView primaryView,
        List<World3DView> views, List<World3DCullViewGpu> cullViews, List<uint> targetSlots)
    {
        Array.Clear(_recordedSliceUpdated, 0, TotalShadowCascadeSlices);
        Array.Clear(_recordedSliceViewSlots, 0, TotalShadowCascadeSlices);
        _recordedShadowsEnabled = false;
        _recordedUseMinMax = false;
        _recordedStaticFarActive = false;
        _recordedRsmEnabled = false;
        _recordedRsmUpdate = false;
        _recordedRsmViewSlot = 0;
        _recordedShadowDrawCalls = 0;
        _frameShadowArrayRG = default;
        _frameShadowMinMaxRG = default;
        _frameRsmFluxRG = default;

        Vector3 rawSun = Lighting.DirectionToSun;
        float sunLenSq = rawSun.LengthSquared();
        Vector3 sunDir = sunLenSq > 1e-8f && float.IsFinite(sunLenSq)
            ? rawSun / MathF.Sqrt(sunLenSq) : Vector3.UnitZ;
        _recordedShadowSunDir = sunDir;

        if (!_recordedWorld || !Shadows.Enabled)
        {
            LastRecordedShadows = new World3DShadowStats(false, 0, 0, 0, 0, false, false, false, false);
            return;
        }

        if (_lastShadowDiscardVersion != context.DiscardVersion)
        {
            InvalidateShadowCaches();
            _lastShadowDiscardVersion = context.DiscardVersion;
        }

        int res = Math.Clamp(Shadows.CascadeResolution, 64, 4096);
        float maxDist = Math.Clamp(Shadows.MaxDistance, 10f, 5000f);
        float lambda = Math.Clamp(Shadows.CascadeSplitLambda, 0f, 1f);
        float minBiasMeters = Math.Max(0f, Shadows.MinTexelSizeLodBiasMeters);
        bool useMinMax = Shadows.UseMinMaxAcceleration;
        int mmRes = Math.Max(1, res / (int)MinMaxBlockSize);

        EnsureShadowTextures(res, useMinMax ? mmRes : 0);

        _recordedShadowsEnabled = true;
        _recordedShadowResolution = res;
        _recordedMinMaxResolution = mmRes;
        _recordedUseMinMax = useMinMax;
        ++_shadowFrameCounter;

        // Build left-handed light basis (LightForward points from sun into scene).
        Vector3 lightForward = sunDir.Z > 0.005f ? -sunDir : -Vector3.Normalize(new Vector3(sunDir.X, sunDir.Y, 0.005f));
        Vector3 refUp = MathF.Abs(lightForward.Z) < 0.99f ? Vector3.UnitZ : Vector3.UnitX;
        Vector3 lightRight = Vector3.Normalize(Vector3.Cross(refUp, lightForward));
        Vector3 lightUp = Vector3.Normalize(Vector3.Cross(lightForward, lightRight));

        Vector3 camForward = Vector3.Transform(Vector3.UnitX, Quaternion.Normalize(camera.Rotation));
        float aspect = (float)primaryView.ViewportSizeInvSize.X / Math.Max(1f, primaryView.ViewportSizeInvSize.Y);
        float nearSplit = Math.Clamp(camera.Near, 0.05f, maxDist * 0.25f);
        ComputePracticalSplits(nearSplit, maxDist, lambda, _recordedCascadeSplits);

        float prevSplit = nearSplit;
        uint updatedMask = 0;
        uint cachedMask = 0;

        for (int c = 0; c < MainCascadeCount; ++c)
        {
            float splitFar = _recordedCascadeSplits[c];
            ComputeFrustumSliceSphere(camera.Fov, aspect, prevSplit, splitFar, out float centerOffset, out float radius);
            prevSplit = splitFar;

            float worldSize = MathF.Ceiling(radius * 2f * 4f) * 0.25f;
            float minObjRadius = c < Shadows.CascadeMinObjectRadius.Length ? MathF.Max(0f, Shadows.CascadeMinObjectRadius[c]) : 0f;
            float lodBias = (minBiasMeters > 0f && c >= 2) ? (worldSize / res) / MathF.Max(minBiasMeters, 1e-4f) * 18f : 0f;
            int interval = c < Shadows.CascadeUpdateIntervals.Length ? Math.Clamp(Shadows.CascadeUpdateIntervals[c], 1, 120) : 1;

            World3DPosition sliceCenter = new(
                camera.Eye.X + camForward.X * centerOffset,
                camera.Eye.Y + camForward.Y * centerOffset,
                camera.Eye.Z + camForward.Z * centerOffset);

            ref CascadeCacheState cached = ref _cascadeCache[c];
            bool sunChanged = !cached.Valid || Vector3.Dot(cached.SunDir, sunDir) < 0.999995f;
            bool paramsChanged = cached.Resolution != res || MathF.Abs(cached.WorldSize - worldSize) > 0.05f
                || MathF.Abs(cached.MinRadius - minObjRadius) > 0.001f || MathF.Abs(cached.LodBias - lodBias) > 0.01f;
            bool sceneChanged = cached.SceneRevision != _shadowSceneRevision;
            bool drifted = DistanceMeters(sliceCenter, cached.AnchorWorld) > worldSize * 0.25;
            bool intervalDue = (_shadowFrameCounter - cached.LastUpdateFrame) >= (ulong)interval;

            bool needsRender = !cached.Valid || sunChanged || paramsChanged || sceneChanged || drifted || intervalDue;
            if (needsRender)
            {
                BuildSnappedOrthoCascade(sliceCenter, worldSize, res, lightRight, lightUp, lightForward,
                    out World3DCascadeGpu gpuCascade, out World3DView viewData, out World3DPosition snappedAnchor, out _);

                _pendingCascadeState[c] = new CascadeCacheState
                {
                    Valid = true,
                    LastUpdateFrame = _shadowFrameCounter,
                    SceneRevision = _shadowSceneRevision,
                    SunDir = sunDir,
                    Resolution = res,
                    WorldSize = worldSize,
                    MinRadius = minObjRadius,
                    LodBias = lodBias,
                    AnchorWorld = snappedAnchor,
                    GpuData = gpuCascade,
                    ViewData = viewData,
                };
                _recordedSliceUpdated[c] = true;
                updatedMask |= 1u << c;

                if (_frameDrawSlots != 0)
                {
                    uint slot = (uint)views.Count;
                    _recordedSliceViewSlots[c] = slot;
                    views.Add(viewData);
                    targetSlots.Add(slot);
                    cullViews.Add(new World3DCullViewGpu(
                        viewData.CameraVP,
                        viewData.EyeCell,
                        viewData.EyeLocalNear,
                        slot,
                        CullViewEnabled | CullViewOrtho | CullViewShadowCasterOnly,
                        minObjRadius,
                        lodBias,
                        0f));
                }
            }
            else
            {
                _pendingCascadeState[c] = cached;
                cachedMask |= 1u << c;
            }
        }

        // Optional Slice 4: Static Far Cascade.
        World3DFarShadowSettings farSettings = Shadows.FarField;
        bool staticFarActive = farSettings.EnableStaticFarCascade;
        _recordedStaticFarActive = staticFarActive;
        _recordedStaticFarSplit = Math.Max(farSettings.StaticFarDistance, maxDist + 64f);
        if (staticFarActive)
        {
            float staticWorldSize = MathF.Ceiling(_recordedStaticFarSplit * 2f);
            float staticMinRadius = MathF.Max(0f, farSettings.StaticFarMinObjectRadius);
            float staticLodBias = (minBiasMeters > 0f) ? (staticWorldSize / res) / MathF.Max(minBiasMeters, 1e-4f) * 20f : 50f;
            int staticInterval = Math.Clamp(farSettings.StaticFarRefreshIntervalFrames, 1, 600);

            ref CascadeCacheState cached = ref _cascadeCache[MainCascadeCount];
            bool sunChanged = !cached.Valid || Vector3.Dot(cached.SunDir, sunDir) < 0.99999f;
            bool paramsChanged = cached.Resolution != res || MathF.Abs(cached.WorldSize - staticWorldSize) > 0.5f
                || MathF.Abs(cached.MinRadius - staticMinRadius) > 0.01f;
            bool sceneChanged = cached.SceneRevision != _shadowSceneRevision;
            bool drifted = DistanceMeters(camera.Eye, cached.AnchorWorld) > _recordedStaticFarSplit * 0.45;
            bool intervalDue = (_shadowFrameCounter - cached.LastUpdateFrame) >= (ulong)staticInterval;

            bool needsRender = !cached.Valid || sunChanged || paramsChanged || sceneChanged || drifted || intervalDue;
            if (needsRender)
            {
                BuildSnappedOrthoCascade(camera.Eye, staticWorldSize, res, lightRight, lightUp, lightForward,
                    out World3DCascadeGpu gpuCascade, out World3DView viewData, out World3DPosition snappedAnchor, out _);

                _pendingCascadeState[MainCascadeCount] = new CascadeCacheState
                {
                    Valid = true,
                    LastUpdateFrame = _shadowFrameCounter,
                    SceneRevision = _shadowSceneRevision,
                    SunDir = sunDir,
                    Resolution = res,
                    WorldSize = staticWorldSize,
                    MinRadius = staticMinRadius,
                    LodBias = staticLodBias,
                    AnchorWorld = snappedAnchor,
                    GpuData = gpuCascade,
                    ViewData = viewData,
                };
                _recordedSliceUpdated[MainCascadeCount] = true;
                updatedMask |= 1u << MainCascadeCount;

                if (_frameDrawSlots != 0)
                {
                    uint slot = (uint)views.Count;
                    _recordedSliceViewSlots[MainCascadeCount] = slot;
                    views.Add(viewData);
                    targetSlots.Add(slot);
                    cullViews.Add(new World3DCullViewGpu(
                        viewData.CameraVP,
                        viewData.EyeCell,
                        viewData.EyeLocalNear,
                        slot,
                        CullViewEnabled | CullViewOrtho | CullViewShadowCasterOnly,
                        staticMinRadius,
                        staticLodBias,
                        0f));
                }
            }
            else
            {
                _pendingCascadeState[MainCascadeCount] = cached;
                cachedMask |= 1u << MainCascadeCount;
            }
        }

        // Optional RSM pass reserved for Phase 3 VoxelGI.
        World3DRsmSettings rsm = Shadows.Rsm;
        if (rsm.Enabled)
        {
            int rsmRes = Math.Clamp(rsm.Resolution, 64, 1024);
            float rsmExtent = Math.Clamp(rsm.WorldExtentMeters, 16f, 512f);
            EnsureRsmTextures(rsmRes);
            _recordedRsmEnabled = true;

            Vector3 sunColor = ProceduralSkyUtility.GetSunColor(sunDir) * Lighting.SunColor;
            float sunIntensity = Lighting.SunIntensity * ProceduralSkyUtility.GetSunLightScale(sunDir);
            _recordedRsmSunDirIntensity = new Vector4(sunDir, sunIntensity);
            _recordedRsmSunColor = new Vector4(sunColor, 0f);

            bool sunChanged = !_rsmCache.Valid || Vector3.Dot(_rsmCache.SunDir, sunDir) < 0.99999f;
            bool paramsChanged = _rsmCache.Resolution != rsmRes || MathF.Abs(_rsmCache.WorldExtent - rsmExtent) > 0.25f;
            bool sceneChanged = _rsmCache.SceneRevision != _shadowSceneRevision;
            bool drifted = DistanceMeters(camera.Eye, _rsmCache.AnchorWorld) > rsmExtent * 0.15;

            if (!_rsmCache.Valid || sunChanged || paramsChanged || sceneChanged || drifted)
            {
                BuildSnappedOrthoCascade(camera.Eye, rsmExtent, rsmRes, lightRight, lightUp, lightForward,
                    out World3DCascadeGpu rsmCascade, out World3DView rsmView, out World3DPosition snappedAnchor,
                    out float rsmDepthRange);
                _pendingRsmState = new RsmCacheState
                {
                    Valid = true,
                    SceneRevision = _shadowSceneRevision,
                    SunDir = sunDir,
                    Resolution = rsmRes,
                    WorldExtent = rsmExtent,
                    DepthRange = rsmDepthRange,
                    AnchorWorld = snappedAnchor,
                    GpuCascade = rsmCascade,
                };
                _recordedRsmUpdate = true;

                if (_frameDrawSlots != 0)
                {
                    uint slot = (uint)views.Count;
                    _recordedRsmViewSlot = slot;
                    views.Add(rsmView);
                    targetSlots.Add(slot);
                    cullViews.Add(new World3DCullViewGpu(
                        rsmView.CameraVP,
                        rsmView.EyeCell,
                        rsmView.EyeLocalNear,
                        slot,
                        CullViewEnabled | CullViewOrtho | CullViewShadowCasterOnly,
                        0f,
                        0f,
                        0f));
                }
            }
            else
            {
                _pendingRsmState = _rsmCache;
            }
        }

        LastRecordedShadows = new World3DShadowStats(
            true,
            res,
            updatedMask,
            cachedMask,
            0,
            useMinMax,
            false,
            staticFarActive,
            _recordedRsmUpdate);
    }

    private BufferSlice UploadShadowFrame(RenderContext context)
    {
        if (!_recordedWorld)
            return default;

        uint flags = 0;
        if (_recordedShadowsEnabled) flags |= 1u;
        if (_recordedShadowsEnabled && _recordedUseMinMax) flags |= 2u;
        if (_recordedShadowsEnabled && _recordedStaticFarActive) flags |= 4u;
        if (_recordedShadowsEnabled && _recordedFarHeightActive) flags |= 8u;
        if (_recordedShadowsEnabled && _recordedRsmEnabled && _rsmFlux != null) flags |= 16u;
        if (_recordedOcclusionEnabled && _frameCurrHiZTexture != null) flags |= 32u;

        float farCov = Math.Clamp(Shadows.FarField.WorldCoverageMeters, 256f, 65536f);
        float invFarCov = 1f / farCov;
        int farRes = Math.Clamp(Shadows.FarField.Resolution, 64, 2048);
        float texelStep = farCov / farRes;
        int rsmRes = Math.Clamp(Shadows.Rsm.Resolution, 64, 1024);

        World3DShadowFrameGpu gpu = new(
            _pendingCascadeState[0].GpuData,
            _pendingCascadeState[1].GpuData,
            _pendingCascadeState[2].GpuData,
            _pendingCascadeState[3].GpuData,
            _pendingCascadeState[4].GpuData,
            new Vector4(_recordedCascadeSplits[0], _recordedCascadeSplits[1], _recordedCascadeSplits[2], _recordedCascadeSplits[3]),
            new Vector4(_recordedStaticFarSplit, 32f, Math.Clamp(Shadows.MaxDistance, 10f, 5000f), Math.Clamp(Shadows.ShadowTightness, 0f, 1f)),
            new Vector4(_recordedShadowResolution, _recordedShadowResolution > 0 ? 1f / _recordedShadowResolution : 0f, _recordedMinMaxResolution, 1.0f),
            new Vector4(invFarCov, farCov * 0.5f, Math.Max(Shadows.FarField.HorizonMaxDistance, 128f), texelStep),
            new Vector4(_recordedShadowSunDir, 0f),
            new Vector4(Math.Clamp(Shadows.Rsm.WorldExtentMeters, 16f, 512f), 1f / rsmRes, 0f, 0f),
            flags,
            _recordedHiZMip0.X,
            _recordedHiZMip0.Y,
            _recordedHiZMipCount,
            (uint)LightingDebugView,
            (uint)(_recordedRsmEnabled ? rsmRes : 0),
            (uint)_recordedFarHeightResolution,
            _recordedFarHeightMipCount,
            _recordedShadowsEnabled && _shadowArray != null ? GPUTextureId.FromSampled(_shadowArray) : default,
            _recordedShadowsEnabled && _recordedUseMinMax && _shadowMinMaxArray != null ? GPUTextureId.FromSampled(_shadowMinMaxArray) : default,
            _recordedFarHeightActive && _farHeightPyramid != null ? GPUTextureId.FromSampled(_farHeightPyramid) : default,
            _recordedOcclusionEnabled && _frameCurrHiZTexture != null ? GPUTextureId.FromSampled(_frameCurrHiZTexture) : default,
            _recordedRsmEnabled && _rsmFlux != null ? GPUTextureId.FromSampled(_rsmFlux) : default,
            _recordedRsmEnabled && _rsmNormal != null ? GPUTextureId.FromSampled(_rsmNormal) : default);

        return context.UploadTransient(in gpu);
    }

    private void ReconcileShadows(bool submitted)
    {
        if (submitted && _recordedWorld && _recordedShadowsEnabled)
        {
            for (int c = 0; c < MainCascadeCount; ++c)
                _cascadeCache[c] = _pendingCascadeState[c];
            if (_recordedStaticFarActive)
                _cascadeCache[MainCascadeCount] = _pendingCascadeState[MainCascadeCount];
            else
                _cascadeCache[MainCascadeCount].Valid = false;

            if (_recordedRsmEnabled)
                _rsmCache = _pendingRsmState;
            else
                _rsmCache.Valid = false;
        }
        else if (!submitted)
        {
            InvalidateShadowCaches();
        }
    }

    private void InvalidateShadowCaches()
    {
        for (int i = 0; i < TotalShadowCascadeSlices; ++i)
            _cascadeCache[i].Valid = false;
        _rsmCache.Valid = false;
    }

    private void DisposeShadowResources()
    {
        _shadowArray?.Dispose();
        _shadowArray = null;
        _shadowMinMaxTemp?.Dispose();
        _shadowMinMaxTemp = null;
        _shadowMinMaxArray?.Dispose();
        _shadowMinMaxArray = null;
        _rsmFlux?.Dispose();
        _rsmFlux = null;
        _rsmNormal?.Dispose();
        _rsmNormal = null;
        _rsmDepth?.Dispose();
        _rsmDepth = null;
        InvalidateShadowCaches();
    }

    private void EnsureShadowTextures(int res, int minMaxRes)
    {
        if (_shadowArray == null || _shadowArray.Extent.X != (uint)res)
        {
            _shadowArray?.Dispose();
            _shadowArray = _system.CreateTexture(new TextureDesc
            {
                Type = TextureType.TwoDArray,
                Extent = new Extent3D((uint)res, (uint)res, 1),
                MipLevels = 1,
                LayerCount = TotalShadowCascadeSlices,
                Format = Format.D32Float,
                Usage = TextureUsage.Sampled | TextureUsage.DepthStencilAttachment,
            });
            InvalidateShadowCaches();
        }

        if (minMaxRes > 0 && (_shadowMinMaxArray == null || _shadowMinMaxArray.Extent.X != (uint)minMaxRes))
        {
            _shadowMinMaxTemp?.Dispose();
            _shadowMinMaxArray?.Dispose();
            TextureDesc mmDesc = new()
            {
                Type = TextureType.TwoDArray,
                Extent = new Extent3D((uint)minMaxRes, (uint)minMaxRes, 1),
                MipLevels = 1,
                LayerCount = TotalShadowCascadeSlices,
                Format = Format.Rg32Float,
                Usage = TextureUsage.Sampled | TextureUsage.Storage,
            };
            _shadowMinMaxTemp = _system.CreateTexture(mmDesc);
            _shadowMinMaxArray = _system.CreateTexture(mmDesc);
        }
    }

    private void EnsureRsmTextures(int res)
    {
        if (_rsmFlux != null && _rsmFlux.Extent.X == (uint)res)
            return;
        _rsmFlux?.Dispose();
        _rsmNormal?.Dispose();
        _rsmDepth?.Dispose();
        _rsmFlux = _system.CreateTexture(new TextureDesc
        {
            Type = TextureType.TwoD,
            Extent = new Extent3D((uint)res, (uint)res, 1),
            Format = Format.Rgba16Float,
            Usage = TextureUsage.Sampled | TextureUsage.ColorAttachment,
        });
        _rsmNormal = _system.CreateTexture(new TextureDesc
        {
            Type = TextureType.TwoD,
            Extent = new Extent3D((uint)res, (uint)res, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.ColorAttachment,
        });
        _rsmDepth = _system.CreateTexture(new TextureDesc
        {
            Type = TextureType.TwoD,
            Extent = new Extent3D((uint)res, (uint)res, 1),
            Format = Format.D32Float,
            Usage = TextureUsage.Sampled | TextureUsage.DepthStencilAttachment,
        });
        _rsmCache.Valid = false;
    }

    private static void ComputePracticalSplits(float nearPlane, float maxDistance, float lambda, float[] splits)
    {
        float ratio = maxDistance / MathF.Max(nearPlane, 0.05f);
        for (int i = 0; i < MainCascadeCount; ++i)
        {
            float p = (i + 1) / (float)MainCascadeCount;
            float log = nearPlane * MathF.Pow(ratio, p);
            float uniform = nearPlane + (maxDistance - nearPlane) * p;
            splits[i] = float.Lerp(uniform, log, lambda);
        }
    }

    private static void ComputeFrustumSliceSphere(float verticalFov, float aspectRatio, float nearDist, float farDist,
        out float centerOffset, out float radius)
    {
        float tanHalfY = MathF.Tan(verticalFov * 0.5f);
        float tanHalfDiagSq = tanHalfY * tanHalfY * (aspectRatio * aspectRatio + 1f);
        float candidateOffset = 0.5f * (farDist + nearDist) * (1f + tanHalfDiagSq);
        if (candidateOffset <= farDist)
        {
            centerOffset = candidateOffset;
            float dx = centerOffset - nearDist;
            radius = MathF.Sqrt(dx * dx + nearDist * nearDist * tanHalfDiagSq);
        }
        else
        {
            centerOffset = farDist;
            radius = farDist * MathF.Sqrt(tanHalfDiagSq);
        }
    }

    private static void BuildSnappedOrthoCascade(
        World3DPosition centerWorld,
        float worldSize,
        int resolution,
        Vector3 lightRight,
        Vector3 lightUp,
        Vector3 lightForward,
        out World3DCascadeGpu gpuCascade,
        out World3DView viewData,
        out World3DPosition snappedAnchorWorld,
        out float depthRange)
    {
        double texelSize = (double)worldSize / resolution;
        double u = centerWorld.X * lightRight.X + centerWorld.Y * lightRight.Y + centerWorld.Z * lightRight.Z;
        double v = centerWorld.X * lightUp.X + centerWorld.Y * lightUp.Y + centerWorld.Z * lightUp.Z;
        double w = centerWorld.X * lightForward.X + centerWorld.Y * lightForward.Y + centerWorld.Z * lightForward.Z;

        double snappedU = Math.Round(u / texelSize) * texelSize;
        double snappedV = Math.Round(v / texelSize) * texelSize;
        double snappedW = Math.Round(w);

        snappedAnchorWorld = new World3DPosition(
            snappedU * lightRight.X + snappedV * lightUp.X + snappedW * lightForward.X,
            snappedU * lightRight.Y + snappedV * lightUp.Y + snappedW * lightForward.Y,
            snappedU * lightRight.Z + snappedV * lightUp.Z + snappedW * lightForward.Z);

        World3DSceneCoordinates.TrySplitPosition(snappedAnchorWorld, out World3DCell cell, out Vector3 local);
        depthRange = MathF.Max(worldSize * 2f + 1000f, 1500f);
        float halfDepth = depthRange * 0.5f;
        Vector3 eyeRel = -lightForward * halfDepth;
        Matrix4x4 lightView = Matrix4x4.CreateLookAtLeftHanded(eyeRel, Vector3.Zero, lightUp);
        Matrix4x4 lightProj = Matrix4x4.CreateOrthographicLeftHanded(worldSize, worldSize, 0f, depthRange);
        Matrix4x4 lightVP = lightView * lightProj;
        World3DView.TryCreateOrthographic(lightVP, cell, local, resolution, out viewData);

        gpuCascade = new World3DCascadeGpu(lightVP, cell, new Vector4(local, worldSize));
    }

    private static double DistanceMeters(in World3DPosition a, in World3DPosition b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private sealed class ShadowPasses(World3DScene scene) : IRenderGraphNode
    {
        private const int SliceBase = 0;
        private const int ReduceBase = 10;
        private const int DilateBase = 20;
        private const int RsmPass = 30;

        private readonly World3DScene _scene = scene;
        private RGTexture _minMaxTempRG;

        void IRenderGraphNode.Setup(RenderGraphBuilder builder)
        {
            _scene._frameShadowArrayRG = default;
            _scene._frameShadowMinMaxRG = default;
            _scene._frameRsmFluxRG = default;
            _scene._frameRsmNormalRG = default;
            _scene._frameRsmDepthRG = default;

            if (!_scene._recordedWorld || !_scene._recordedShadowsEnabled || _scene._shadowArray == null)
                return;

            RGTexture shadowArray = builder.ImportTexture(_scene._shadowArray, "World3D.ShadowArray");
            _scene._frameShadowArrayRG = shadowArray;

            RGTexture minMaxArray = default;
            RGTexture minMaxTemp = default;
            if (_scene._recordedUseMinMax && _scene._shadowMinMaxArray != null && _scene._shadowMinMaxTemp != null)
            {
                minMaxTemp = builder.ImportTexture(_scene._shadowMinMaxTemp, "World3D.ShadowMinMaxTemp");
                minMaxArray = builder.ImportTexture(_scene._shadowMinMaxArray, "World3D.ShadowMinMaxArray");
                _minMaxTempRG = minMaxTemp;
                _scene._frameShadowMinMaxRG = minMaxArray;
            }

            RGBuffer views = builder.ImportBuffer(_scene._frameViews.Buffer!, "World3D.Views");
            RGBuffer args = _scene._frameDrawSlots != 0 ? builder.ImportBuffer(_scene._drawArgs!, "World3D.IndexedArgs") : default;
            RGBuffer drawRefs = _scene._frameDrawSlots != 0 ? builder.ImportBuffer(_scene._drawRefs!, "World3D.DrawRefs") : default;

            int lastSlice = -1;
            for (int c = TotalShadowCascadeSlices - 1; c >= 0; --c)
            {
                if (_scene._recordedSliceUpdated[c])
                {
                    lastSlice = c;
                    break;
                }
            }

            for (int c = 0; c < TotalShadowCascadeSlices; ++c)
            {
                if (!_scene._recordedSliceUpdated[c])
                    continue;

                RenderGraphPassBuilder depthPass = builder.AddPass($"World3D.Shadow.Cascade{c}", SliceBase + c);
                depthPass.ReadWrite(shadowArray, RGUsage.DepthStencil);
                if (c == lastSlice)
                    depthPass.ProducesOutput();
                if (_scene._frameDrawSlots != 0)
                    DeclareShadowGeometryReads(_scene, builder, depthPass, views, args, drawRefs);

                if (_scene._recordedUseMinMax && minMaxArray.IsValid && minMaxTemp.IsValid)
                {
                    RenderGraphPassBuilder reduce = builder.AddPass($"World3D.Shadow.MinMaxReduce{c}", ReduceBase + c);
                    reduce.Read(shadowArray, RGUsage.ComputeRead);
                    reduce.ReadWrite(minMaxTemp, RGUsage.ComputeReadWrite);

                    RenderGraphPassBuilder dilate = builder.AddPass($"World3D.Shadow.MinMaxDilate{c}", DilateBase + c);
                    dilate.Read(minMaxTemp, RGUsage.ComputeRead);
                    dilate.ReadWrite(minMaxArray, RGUsage.ComputeReadWrite);
                    if (c == lastSlice)
                        dilate.ProducesOutput();
                }
            }

            if (_scene._recordedRsmEnabled && _scene._rsmFlux != null && _scene._rsmNormal != null && _scene._rsmDepth != null)
            {
                RGTexture rsmFlux = builder.ImportTexture(_scene._rsmFlux, "World3D.RsmFlux");
                RGTexture rsmNormal = builder.ImportTexture(_scene._rsmNormal, "World3D.RsmNormal");
                RGTexture rsmDepth = builder.ImportTexture(_scene._rsmDepth, "World3D.RsmDepth");
                _scene._frameRsmFluxRG = rsmFlux;
                _scene._frameRsmNormalRG = rsmNormal;
                _scene._frameRsmDepthRG = rsmDepth;
                if (_scene._recordedRsmUpdate)
                {
                    RenderGraphPassBuilder rsmPass = builder.AddPass("World3D.Rsm", RsmPass);
                    rsmPass.Write(rsmFlux, RGUsage.ColorAttachmentWrite);
                    rsmPass.Write(rsmNormal, RGUsage.ColorAttachmentWrite);
                    rsmPass.Write(rsmDepth, RGUsage.DepthStencil);
                    rsmPass.ProducesOutput();
                    if (_scene._frameDrawSlots != 0)
                        DeclareShadowGeometryReads(_scene, builder, rsmPass, views, args, drawRefs);
                }
            }
        }

        void IRenderGraphNode.Execute(in RenderGraphContext context, int passId)
        {
            if (passId == RsmPass)
            {
                ExecuteRsm(in context);
                return;
            }
            if (passId >= DilateBase)
            {
                ExecuteMinMaxDilate(in context, passId - DilateBase);
                return;
            }
            if (passId >= ReduceBase)
            {
                ExecuteMinMaxReduce(in context, passId - ReduceBase);
                return;
            }
            ExecuteCascadeDepth(in context, passId - SliceBase);
        }

        private void ExecuteCascadeDepth(in RenderGraphContext context, int slice)
        {
            RenderTarget target = _scene._shadowArray!.GetRenderTarget(0, (uint)slice);
            RenderingDesc desc = new()
            {
                Depth = new DepthAttachment { Target = target, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = 1.0f },
            };
            using RenderContext.RenderScope pass = context.RenderContext.BeginRenderPass(desc);
            if (_scene._frameDrawSlots == 0)
                return;

            uint viewSlot = _scene._recordedSliceViewSlots[slice];
            _scene._recordedShadowDrawCalls += _scene.SubmitShadowBucketDraws(pass, viewSlot, rsm: false);
            _scene.LastRecordedShadows = _scene.LastRecordedShadows with { ShadowDrawCalls = _scene._recordedShadowDrawCalls };
        }

        private void ExecuteMinMaxReduce(in RenderGraphContext context, int slice)
        {
            _scene._minMaxReduceShader ??= _scene.LoadSceneCompute<ShadowMinMaxReduceRoot>("World3DShadowMinMaxReduce");
            uint res = (uint)_scene._recordedShadowResolution;
            uint mmRes = (uint)_scene._recordedMinMaxResolution;
            ShadowMinMaxReduceRoot root = new(
                GPUTextureId.FromSampled(_scene._shadowArray!),
                _scene._shadowMinMaxTemp!.GetStorageId(0),
                res,
                mmRes,
                (uint)slice,
                MinMaxBlockSize);
            BufferSlice data = context.RenderContext.UploadTransient(in root);
            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            uint groups = (mmRes + 7) / 8;
            compute.Dispatch(_scene._minMaxReduceShader, data, new Extent3D(groups, groups, 1));
        }

        private void ExecuteMinMaxDilate(in RenderGraphContext context, int slice)
        {
            _scene._minMaxDilateShader ??= _scene.LoadSceneCompute<ShadowMinMaxDilateRoot>("World3DShadowMinMaxDilate");
            uint mmRes = (uint)_scene._recordedMinMaxResolution;
            ShadowMinMaxDilateRoot root = new(
                GPUTextureId.FromSampled(_scene._shadowMinMaxTemp!),
                _scene._shadowMinMaxArray!.GetStorageId(0),
                mmRes,
                (uint)slice,
                0,
                0);
            BufferSlice data = context.RenderContext.UploadTransient(in root);
            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            uint groups = (mmRes + 7) / 8;
            compute.Dispatch(_scene._minMaxDilateShader, data, new Extent3D(groups, groups, 1));
        }

        private void ExecuteRsm(in RenderGraphContext context)
        {
            RenderingDesc desc = new()
            {
                Colors =
                [
                    new ColorAttachment { Target = _scene._rsmFlux!.GetRenderTarget(0), Load = LoadOp.Clear, Store = StoreOp.Store },
                    new ColorAttachment { Target = _scene._rsmNormal!.GetRenderTarget(0), Load = LoadOp.Clear, Store = StoreOp.Store },
                ],
                Depth = new DepthAttachment { Target = _scene._rsmDepth!.GetRenderTarget(0), Load = LoadOp.Clear, Store = StoreOp.Store, Clear = 1.0f },
            };
            using RenderContext.RenderScope pass = context.RenderContext.BeginRenderPass(desc);
            if (_scene._frameDrawSlots == 0)
                return;
            _scene.SubmitShadowBucketDraws(pass, _scene._recordedRsmViewSlot, rsm: true);
        }
    }

    private static void DeclareShadowGeometryReads(World3DScene scene, RenderGraphBuilder builder, RenderGraphPassBuilder pass,
        RGBuffer views, RGBuffer args, RGBuffer drawRefs)
    {
        pass.Read(views, RGUsage.VertexRead);
        pass.Read(args, RGUsage.IndirectRead);
        pass.Read(drawRefs, RGUsage.VertexRead);
        scene._gpu.ReadTables(builder, pass, RGUsage.VertexRead | RGUsage.PixelRead, materials: true);
        foreach (DrawBucket bucket in scene._drawLayout.Buckets)
            pass.Read(builder.ImportBuffer(bucket.Key.Page, "World3D.GeometryPayload"), RGUsage.IndexRead | RGUsage.VertexRead);
    }

    [StructLayout(LayoutKind.Explicit, Size = 96)]
    private readonly record struct World3DCascadeGpu(
        [field: FieldOffset(0)] Matrix4x4 LightViewProj,
        [field: FieldOffset(64)] World3DCell CascadeCell,
        [field: FieldOffset(80)] Vector4 CascadeLocal);

    [StructLayout(LayoutKind.Explicit, Size = 640)]
    private readonly record struct World3DShadowFrameGpu(
        [field: FieldOffset(0)] World3DCascadeGpu Cascade0,
        [field: FieldOffset(96)] World3DCascadeGpu Cascade1,
        [field: FieldOffset(192)] World3DCascadeGpu Cascade2,
        [field: FieldOffset(288)] World3DCascadeGpu Cascade3,
        [field: FieldOffset(384)] World3DCascadeGpu Cascade4,
        [field: FieldOffset(480)] Vector4 CascadeSplits,
        [field: FieldOffset(496)] Vector4 FarCascadeParams,
        [field: FieldOffset(512)] Vector4 ResolutionParams,
        [field: FieldOffset(528)] Vector4 FarHeightParams,
        [field: FieldOffset(544)] Vector4 SunDirectionParams,
        [field: FieldOffset(560)] Vector4 RsmParams,
        [field: FieldOffset(576)] uint Flags,
        [field: FieldOffset(580)] uint HiZMip0Width,
        [field: FieldOffset(584)] uint HiZMip0Height,
        [field: FieldOffset(588)] uint HiZMipCount,
        [field: FieldOffset(592)] uint DebugView,
        [field: FieldOffset(596)] uint RsmResolution,
        [field: FieldOffset(600)] uint FarHeightResolution,
        [field: FieldOffset(604)] uint FarHeightMipCount,
        [field: FieldOffset(608)] GPUTextureId ShadowArray,
        [field: FieldOffset(612)] GPUTextureId ShadowMinMaxArray,
        [field: FieldOffset(616)] GPUTextureId FarHeightTex,
        [field: FieldOffset(620)] GPUTextureId HiZTex,
        [field: FieldOffset(624)] GPUTextureId RsmFluxTex,
        [field: FieldOffset(628)] GPUTextureId RsmNormalTex,
        [field: FieldOffset(632)] uint Reserved0 = 0,
        [field: FieldOffset(636)] uint Reserved1 = 0);

    [StructLayout(LayoutKind.Sequential, Size = 24)]
    private readonly record struct ShadowMinMaxReduceRoot(
        GPUTextureId ShadowArray,
        GPUTextureId DstMinMaxArray,
        uint CascadeResolution,
        uint MinMaxResolution,
        uint SliceIndex,
        uint BlockSize);

    [StructLayout(LayoutKind.Sequential, Size = 24)]
    private readonly record struct ShadowMinMaxDilateRoot(
        GPUTextureId SrcMinMaxArray,
        GPUTextureId DstMinMaxArray,
        uint MinMaxResolution,
        uint SliceIndex,
        uint Reserved0,
        uint Reserved1);
}
