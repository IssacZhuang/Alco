using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private const int CloudBaseNoiseSize = 128;
    private const int CloudDetailNoiseSize = 32;
    private const double CloudBaseTileMeters = 4096.0;
    private const double CloudDetailTileMeters = 512.0;
    private const double CloudWeatherTileMeters = 16384.0;

    private Texture? _cloudBaseNoiseTex;
    private Texture? _cloudDetailNoiseTex;
    private Texture? _cloudShadowTex;
    private readonly Texture?[] _cloudHistoryTex = new Texture?[2];

    private Shader? _cloudNoiseShader;
    private Shader? _cloudShadowShader;
    private Shader? _cloudMarchShader;
    private Shader? _cloudTemporalShader;
    private Shader? _cloudCompositeShader;
    private bool _cloudShadersLoaded;

    private bool _cloudNoiseDirty = true;
    private int _cloudShadowAllocRes;
    private Extent2D _cloudHistoryExtent;
    private int _submittedCloudHistoryIndex = -1;
    private ulong _submittedCloudDiscardVersion;
    private uint _cloudFrameCounter;
    private double _cloudElapsedSeconds;

    private bool _recordedCloudsEnabled;
    private bool _recordedCloudShadowActive;
    private bool _recordedCloudHistoryValid;
    private int _recordedCurrCloudHistoryIndex = -1;
    private int _recordedPrevCloudHistoryIndex = -1;
    private Extent2D _recordedCloudTraceExtent;
    private Extent2D _recordedCloudShadowExtent;
    private uint _recordedCloudFrameIndex;
    private BufferSlice _recordedCloudFrame;
    private BufferSlice _recordedCloudViews;
    private World3DView _cloudPrevView;
    private Vector4 _recordedCloudShadowOriginAndExtent;
    private Vector4 _recordedCloudShadowAltitudeParams;

    private RGTexture _frameCloudBaseNoiseRG;
    private RGTexture _frameCloudDetailNoiseRG;
    private RGTexture _frameCloudShadowRG;

    /// <summary>Gets or sets runtime settings for volumetric clouds and same-frame cloud shadows.</summary>
    public World3DVolumetricCloudsSettings VolumetricClouds { get; set; } = new();

    /// <summary>Gets the recorded volumetric cloud telemetry for the latest frame.</summary>
    public World3DVolumetricCloudsStats LastRecordedVolumetricClouds { get; private set; } =
        new(false, false, default, default, false, 0);

    private void PrepareVolumetricClouds(RenderContext context, in World3DCamera camera, Extent2D viewport,
        in World3DView primaryView, float deltaTime)
    {
        _ = primaryView;
        _recordedCloudsEnabled = false;
        _recordedCloudShadowActive = false;
        _recordedCloudHistoryValid = false;
        _recordedCurrCloudHistoryIndex = -1;
        _recordedPrevCloudHistoryIndex = -1;
        _recordedCloudTraceExtent = default;
        _recordedCloudShadowExtent = default;
        _recordedCloudFrame = default;
        _recordedCloudViews = default;
        _recordedCloudShadowOriginAndExtent = default;
        _recordedCloudShadowAltitudeParams = default;
        _frameCloudBaseNoiseRG = default;
        _frameCloudDetailNoiseRG = default;
        _frameCloudShadowRG = default;

        World3DVolumetricCloudsSettings settings = VolumetricClouds;
        bool active = _recordedWorld && settings.Enabled && viewport.X > 0 && viewport.Y > 0
            && settings.CloudTopMeters > settings.CloudBottomMeters;
        if (!active)
        {
            _submittedCloudHistoryIndex = -1;
            LastRecordedVolumetricClouds = new World3DVolumetricCloudsStats(false, false, default, default, false, _cloudFrameCounter);
            return;
        }

        EnsureCloudShaders();
        EnsureCloudTextures(settings, viewport, context.DiscardVersion,
            out Extent2D traceExtent, out Extent2D shadowExtent, out bool historyValid, out int prevIdx, out int currIdx);

        double dt = float.IsFinite(deltaTime) ? Math.Clamp((double)deltaTime, 0.0, 0.25) : 0.0;
        _cloudElapsedSeconds += dt;
        ++_cloudFrameCounter;

        double windX = float.IsFinite(settings.WindVelocity.X) ? settings.WindVelocity.X : 0.0;
        double windY = float.IsFinite(settings.WindVelocity.Y) ? settings.WindVelocity.Y : 0.0;
        double windZ = float.IsFinite(settings.WindVelocity.Z) ? settings.WindVelocity.Z : 0.0;

        double basePhaseX = PositiveMod((camera.Eye.X - _cloudElapsedSeconds * windX) / CloudBaseTileMeters, 1.0);
        double basePhaseY = PositiveMod((camera.Eye.Y - _cloudElapsedSeconds * windY) / CloudBaseTileMeters, 1.0);
        double detailPhaseX = PositiveMod((camera.Eye.X - _cloudElapsedSeconds * windX * 1.15) / CloudDetailTileMeters, 1.0);
        double detailPhaseY = PositiveMod((camera.Eye.Y - _cloudElapsedSeconds * windY * 1.15) / CloudDetailTileMeters, 1.0);
        double detailPhaseZ = PositiveMod((_cloudElapsedSeconds * windZ) / 64.0, 1.0);
        double weatherPhaseX = PositiveMod((camera.Eye.X - _cloudElapsedSeconds * windX * 0.65) / CloudWeatherTileMeters, 1.0);
        double weatherPhaseY = PositiveMod((camera.Eye.Y - _cloudElapsedSeconds * windY * 0.65) / CloudWeatherTileMeters, 1.0);

        bool castShadows = settings.CastShadows && shadowExtent.X > 0;
        float shadowCoverage = Math.Clamp(settings.ShadowCoverageMeters, 512f, 65536f);
        double shadowTexelMeters = shadowCoverage / Math.Max(shadowExtent.X, 1u);
        double snapX = Math.Floor(camera.Eye.X / shadowTexelMeters) * shadowTexelMeters;
        double snapY = Math.Floor(camera.Eye.Y / shadowTexelMeters) * shadowTexelMeters;
        Vector2 shadowCenterRelEye = new((float)(snapX - camera.Eye.X), (float)(snapY - camera.Eye.Y));

        EvaluateFilteredSkyAndSun(Lighting, out Vector3 sunDir, out Vector3 sunColor, out float sunIntensity, out _, out _);

        uint flags = 1u;
        if (castShadows) flags |= 2u;
        if (historyValid) flags |= 4u;
        if (Math.Clamp(settings.ResolutionScale, 0.25f, 1.0f) < 0.99f) flags |= 8u;

        CloudFrameGpu frame = new(
            new Vector4(settings.CloudBottomMeters, settings.CloudTopMeters, Math.Clamp(settings.Coverage, 0f, 1f), Math.Max(settings.Density, 0f)),
            new Vector4(Math.Clamp(settings.DetailStrength, 0f, 1f), Math.Clamp(settings.ErosionStrength, 0f, 1f),
                Math.Max(settings.ExtinctionScale, 0.01f), Math.Clamp(settings.MultiScatterStrength, 0f, 1f)),
            new Vector4(Math.Clamp(settings.ForwardPhaseG, 0f, 0.95f), Math.Clamp(settings.BackPhaseG, -0.95f, 0f),
                Math.Max(settings.SilverLiningIntensity, 0f), Math.Max(settings.MaxDistanceMeters, 1000f)),
            new Vector4(sunDir, sunIntensity),
            new Vector4(sunColor, Math.Clamp(settings.ShadowStrength, 0f, 1f)),
            new Vector4(Lighting.RayleighScale, Lighting.MieScale, Math.Clamp(Lighting.MieAnisotropy, -0.99f, 0.99f), Lighting.SkyExposure),
            new Vector4(Lighting.StarIntensity, Lighting.NightFloor, Lighting.SunRadianceScale, Lighting.AmbientFloor),
            new Vector4((float)basePhaseX, (float)basePhaseY, (float)detailPhaseZ, (float)camera.Eye.Z),
            new Vector4((float)detailPhaseX, (float)detailPhaseY, (float)weatherPhaseX, (float)weatherPhaseY),
            new Vector4(shadowCenterRelEye.X, shadowCenterRelEye.Y, shadowCoverage, 1f / shadowCoverage),
            (uint)Math.Clamp(settings.MaxSteps, 8, 128),
            (uint)Math.Clamp(settings.LightSteps, 1, 12),
            _cloudFrameCounter,
            flags);

        Span<World3DView> cloudViews = stackalloc World3DView[2];
        cloudViews[0] = primaryView;
        cloudViews[1] = historyValid ? _cloudPrevView : primaryView;

        _recordedCloudFrame = context.UploadTransient(in frame);
        _recordedCloudViews = context.UploadTransient<World3DView>(cloudViews);
        _recordedCloudsEnabled = true;
        _recordedCloudShadowActive = castShadows;
        _recordedCloudHistoryValid = historyValid;
        _recordedCurrCloudHistoryIndex = currIdx;
        _recordedPrevCloudHistoryIndex = prevIdx;
        _recordedCloudTraceExtent = traceExtent;
        _recordedCloudShadowExtent = shadowExtent;
        _recordedCloudFrameIndex = _cloudFrameCounter;
        _recordedCloudShadowOriginAndExtent = new Vector4(shadowCenterRelEye.X, shadowCenterRelEye.Y, 1f / shadowCoverage, Math.Clamp(settings.ShadowStrength, 0f, 1f));
        _recordedCloudShadowAltitudeParams = new Vector4((float)camera.Eye.Z, settings.CloudBottomMeters, settings.CloudTopMeters, 0f);

        LastRecordedVolumetricClouds = new World3DVolumetricCloudsStats(
            true,
            castShadows,
            traceExtent,
            shadowExtent,
            historyValid,
            _cloudFrameCounter);
    }

    private void ReconcileVolumetricClouds(bool submitted)
    {
        if (submitted && _recordedWorld && _recordedCloudsEnabled && _recordedCurrCloudHistoryIndex >= 0)
        {
            _cloudNoiseDirty = false;
            _submittedCloudHistoryIndex = _recordedCurrCloudHistoryIndex;
            _submittedCloudDiscardVersion = _recordedDiscardVersion;
            _cloudPrevView = _recordedView;
        }
        else if (!submitted || !_recordedCloudsEnabled || !_recordedWorld)
        {
            _submittedCloudHistoryIndex = -1;
        }
    }

    private void DisposeVolumetricClouds()
    {
        _cloudBaseNoiseTex?.Dispose(); _cloudBaseNoiseTex = null;
        _cloudDetailNoiseTex?.Dispose(); _cloudDetailNoiseTex = null;
        _cloudShadowTex?.Dispose(); _cloudShadowTex = null;
        for (int i = 0; i < _cloudHistoryTex.Length; ++i)
        {
            _cloudHistoryTex[i]?.Dispose();
            _cloudHistoryTex[i] = null;
        }
        _cloudNoiseDirty = true;
        _cloudShadowAllocRes = 0;
        _cloudHistoryExtent = default;
        _submittedCloudHistoryIndex = -1;
    }

    private void EnsureCloudShaders()
    {
        if (_cloudShadersLoaded)
            return;

        ShaderCompiler compiler = _system.Shaders.Compiler;
        compiler.RegisterVirtualModule("World3DCloudCommon", "World3DCloudCommon.slang", ReadWorldShader("World3DCloudCommon"));
        IReadOnlyList<ShaderTypeLayout> commonTypes = compiler.LoadModuleFromSource(
            "World3DCloudCommon", "World3DCloudCommon.slang", ReadWorldShader("World3DCloudCommon")).GetTypes();
        GpuStore.ValidateLayout<CloudFrameGpu>(commonTypes.First(t => t.TypeName == "World3DCloudFrameGpu"));

        _cloudNoiseShader = LoadSceneCompute<CloudNoiseRoot>("World3DCloudNoise");
        _cloudShadowShader = LoadSceneCompute<CloudShadowRoot>("World3DCloudShadow");
        _cloudMarchShader = LoadSceneCompute<CloudMarchRoot>("World3DCloudMarch");
        _cloudTemporalShader = LoadSceneCompute<CloudTemporalRoot>("World3DCloudTemporal");
        _cloudCompositeShader = LoadSceneCompute<CloudCompositeRoot>("World3DCloudComposite");
        _cloudShadersLoaded = true;
    }

    private void EnsureCloudTextures(World3DVolumetricCloudsSettings settings, Extent2D viewport, ulong discardVersion,
        out Extent2D traceExtent, out Extent2D shadowExtent, out bool historyValid, out int prevIdx, out int currIdx)
    {
        if (_cloudBaseNoiseTex == null)
        {
            _cloudBaseNoiseTex = _system.CreateTexture(new TextureDesc
            {
                Type = TextureType.ThreeD,
                Extent = new Extent3D(CloudBaseNoiseSize, CloudBaseNoiseSize, CloudBaseNoiseSize),
                MipLevels = 1,
                LayerCount = 1,
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.Storage,
            });
            _cloudNoiseDirty = true;
        }

        if (_cloudDetailNoiseTex == null)
        {
            _cloudDetailNoiseTex = _system.CreateTexture(new TextureDesc
            {
                Type = TextureType.ThreeD,
                Extent = new Extent3D(CloudDetailNoiseSize, CloudDetailNoiseSize, CloudDetailNoiseSize),
                MipLevels = 1,
                LayerCount = 1,
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.Storage,
            });
            _cloudNoiseDirty = true;
        }

        int shadowRes = settings.CastShadows ? Math.Clamp(settings.ShadowResolution, 64, 1024) : 0;
        if (shadowRes > 0 && (_cloudShadowTex == null || _cloudShadowAllocRes != shadowRes))
        {
            _cloudShadowTex?.Dispose();
            _cloudShadowTex = _system.CreateTexture(new TextureDesc
            {
                Type = TextureType.TwoD,
                Extent = new Extent3D((uint)shadowRes, (uint)shadowRes, 1),
                MipLevels = 1,
                LayerCount = 1,
                Format = Format.R32Float,
                Usage = TextureUsage.Sampled | TextureUsage.Storage,
            });
            _cloudShadowAllocRes = shadowRes;
        }
        shadowExtent = shadowRes > 0 ? new Extent2D((uint)shadowRes, (uint)shadowRes) : default;

        float scale = Math.Clamp(settings.ResolutionScale, 0.25f, 1.0f);
        traceExtent = new Extent2D(
            Math.Max(1u, (uint)MathF.Ceiling(viewport.X * scale)),
            Math.Max(1u, (uint)MathF.Ceiling(viewport.Y * scale)));

        if (!_cloudHistoryExtent.Equals(traceExtent) || _cloudHistoryTex[0] == null || _cloudHistoryTex[1] == null)
        {
            for (int i = 0; i < 2; ++i)
            {
                _cloudHistoryTex[i]?.Dispose();
                _cloudHistoryTex[i] = _system.CreateTexture(new TextureDesc
                {
                    Type = TextureType.TwoD,
                    Extent = new Extent3D(traceExtent.X, traceExtent.Y, 1),
                    MipLevels = 1,
                    LayerCount = 1,
                    Format = Format.Rgba16Float,
                    Usage = TextureUsage.Sampled | TextureUsage.Storage,
                });
            }
            _cloudHistoryExtent = traceExtent;
            _submittedCloudHistoryIndex = -1;
        }

        historyValid = _submittedCloudHistoryIndex >= 0
            && _submittedCloudDiscardVersion == discardVersion
            && _cloudHistoryTex[_submittedCloudHistoryIndex] is { IsDisposed: false };
        prevIdx = historyValid ? _submittedCloudHistoryIndex : 0;
        currIdx = historyValid ? 1 - _submittedCloudHistoryIndex : 0;
    }

    private static double PositiveMod(double value, double modulus)
    {
        double rem = value % modulus;
        return rem < 0.0 ? rem + modulus : rem;
    }

    private sealed class VolumetricCloudsPreNode(World3DScene scene) : IRenderGraphNode
    {
        private const int PassNoise = 0;
        private const int PassShadow = 1;

        private readonly World3DScene _scene = scene;

        void IRenderGraphNode.Setup(RenderGraphBuilder builder)
        {
            _scene._frameCloudBaseNoiseRG = default;
            _scene._frameCloudDetailNoiseRG = default;
            _scene._frameCloudShadowRG = default;
            if (!_scene._recordedWorld || !_scene._recordedCloudsEnabled || _scene._cloudBaseNoiseTex == null || _scene._cloudDetailNoiseTex == null)
                return;

            RGTexture baseNoise = builder.ImportTexture(_scene._cloudBaseNoiseTex, "World3D.CloudBaseNoise");
            RGTexture detailNoise = builder.ImportTexture(_scene._cloudDetailNoiseTex, "World3D.CloudDetailNoise");
            _scene._frameCloudBaseNoiseRG = baseNoise;
            _scene._frameCloudDetailNoiseRG = detailNoise;

            if (_scene._cloudNoiseDirty)
            {
                RenderGraphPassBuilder bakePass = builder.AddPass("World3D.Clouds.NoiseBake", PassNoise);
                bakePass.Write(baseNoise, RGUsage.ComputeWrite);
                bakePass.Write(detailNoise, RGUsage.ComputeWrite);
            }

            if (_scene._recordedCloudShadowActive && _scene._cloudShadowTex != null)
            {
                RGTexture cloudShadow = builder.ImportTexture(_scene._cloudShadowTex, "World3D.CloudShadow");
                _scene._frameCloudShadowRG = cloudShadow;
                RenderGraphPassBuilder shadowPass = builder.AddPass("World3D.Clouds.Shadow", PassShadow);
                if (!_scene._recordedCloudFrame.IsEmpty)
                    shadowPass.Read(builder.ImportBuffer(_scene._recordedCloudFrame.Buffer!, "World3D.CloudFrame"), RGUsage.ComputeRead);
                shadowPass.Read(baseNoise, RGUsage.ComputeRead);
                shadowPass.Read(detailNoise, RGUsage.ComputeRead);
                shadowPass.Write(cloudShadow, RGUsage.ComputeWrite);
            }
        }

        void IRenderGraphNode.Execute(in RenderGraphContext context, int passId)
        {
            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            if (passId == PassNoise)
            {
                CloudNoiseRoot baseRoot = new(
                    _scene._cloudBaseNoiseTex!.GetStorageId(0), CloudBaseNoiseSize, 0u, 4u, 0u);
                uint baseGroups = (CloudBaseNoiseSize + 7u) / 8u;
                compute.Dispatch(_scene._cloudNoiseShader!, in baseRoot, new Extent3D(baseGroups, baseGroups, baseGroups));

                CloudNoiseRoot detailRoot = new(
                    _scene._cloudDetailNoiseTex!.GetStorageId(0), CloudDetailNoiseSize, 1u, 4u, 0u);
                uint detailGroups = (CloudDetailNoiseSize + 7u) / 8u;
                compute.Dispatch(_scene._cloudNoiseShader!, in detailRoot, new Extent3D(detailGroups, detailGroups, detailGroups));
                return;
            }

            uint res = _scene._recordedCloudShadowExtent.X;
            CloudShadowRoot shadowRoot = new(
                _scene._recordedCloudFrame.View,
                _scene._cloudBaseNoiseTex!.SampledId,
                _scene._cloudDetailNoiseTex!.SampledId,
                _scene._cloudShadowTex!.GetStorageId(0),
                res, 0u, 0u, 0u);
            uint g = (res + 7u) / 8u;
            compute.Dispatch(_scene._cloudShadowShader!, in shadowRoot, new Extent3D(g, g, 1u));
        }
    }

    private sealed class VolumetricCloudsPostNode(World3DScene scene, RenderChain chain) : IRenderGraphNode
    {
        private const int PassMarch = 0;
        private const int PassTemporal = 1;
        private const int PassComposite = 2;

        private readonly World3DScene _scene = scene;
        private readonly RenderChain _chain = chain;
        private RGTexture _rawCloudsRG;
        private RGTexture _historyInRG;
        private RGTexture _historyOutRG;
        private RGTexture _hdrInRG;
        private RGTexture _hdrOutRG;

        void IRenderGraphNode.Setup(RenderGraphBuilder builder)
        {
            _rawCloudsRG = default;
            _historyInRG = default;
            _historyOutRG = default;
            _hdrInRG = default;
            _hdrOutRG = default;
            World3DLightingDebugView dbg = _scene.LightingDebugView;
            bool allowPostClouds = dbg == World3DLightingDebugView.None || dbg == World3DLightingDebugView.CloudOpacity;
            if (!_scene._recordedWorld || !_scene._recordedCloudsEnabled || !_chain.Current.IsValid
                || !_scene._gbufferTargets.Depth.IsValid || _scene._recordedCurrCloudHistoryIndex < 0
                || !allowPostClouds)
                return;

            _hdrInRG = _chain.Current;
            Extent2D trace = _scene._recordedCloudTraceExtent;
            Extent3D traceExtent = new(trace.X, trace.Y, 1u);

            _rawCloudsRG = builder.CreateTexture(new RGTextureDesc
            {
                Name = "World3D.Clouds.Raw",
                Extent = traceExtent,
                Format = Format.Rgba16Float,
            });
            Texture histOutTex = _scene._cloudHistoryTex[_scene._recordedCurrCloudHistoryIndex]!;
            _historyOutRG = builder.ImportTexture(histOutTex, "World3D.Clouds.HistoryOut");

            RGBuffer views = builder.ImportBuffer(_scene._frameViews.Buffer!, "World3D.Views");
            RGBuffer cloudViews = builder.ImportBuffer(_scene._recordedCloudViews.Buffer!, "World3D.CloudViews");
            RGBuffer cloudFrame = builder.ImportBuffer(_scene._recordedCloudFrame.Buffer!, "World3D.CloudFrame");
            RenderGraphPassBuilder march = builder.AddPass("World3D.Clouds.March", PassMarch);
            march.Read(views, RGUsage.ComputeRead);
            march.Read(cloudFrame, RGUsage.ComputeRead);
            if (_scene._frameCloudBaseNoiseRG.IsValid) march.Read(_scene._frameCloudBaseNoiseRG, RGUsage.ComputeRead);
            if (_scene._frameCloudDetailNoiseRG.IsValid) march.Read(_scene._frameCloudDetailNoiseRG, RGUsage.ComputeRead);
            march.Read(_scene._gbufferTargets.Depth, RGUsage.ComputeRead);
            march.Write(_rawCloudsRG, RGUsage.ComputeWrite);

            RenderGraphPassBuilder temporal = builder.AddPass("World3D.Clouds.Temporal", PassTemporal);
            temporal.Read(cloudViews, RGUsage.ComputeRead);
            temporal.Read(cloudFrame, RGUsage.ComputeRead);
            temporal.Read(_rawCloudsRG, RGUsage.ComputeRead);
            temporal.Read(_scene._gbufferTargets.Depth, RGUsage.ComputeRead);
            if (_scene._recordedCloudHistoryValid && _scene._recordedPrevCloudHistoryIndex >= 0)
            {
                Texture histInTex = _scene._cloudHistoryTex[_scene._recordedPrevCloudHistoryIndex]!;
                _historyInRG = builder.ImportTexture(histInTex, "World3D.Clouds.HistoryIn");
                temporal.Read(_historyInRG, RGUsage.ComputeRead);
            }
            temporal.Write(_historyOutRG, RGUsage.ComputeWrite);

            _hdrOutRG = builder.CreateTexture(new RGTextureDesc
            {
                Name = "World3D.Clouds.HDR",
                Extent = builder.GetExtent(_hdrInRG),
                Format = Format.Rgba16Float,
            });
            RenderGraphPassBuilder composite = builder.AddPass("World3D.Clouds.Composite", PassComposite);
            composite.Read(views, RGUsage.ComputeRead);
            composite.Read(cloudFrame, RGUsage.ComputeRead);
            if (!_scene._frameShadowData.IsEmpty)
                composite.Read(builder.ImportBuffer(_scene._frameShadowData.Buffer!, "World3D.ShadowFrame"), RGUsage.ComputeRead);
            if (_scene._frameCloudShadowRG.IsValid)
                composite.Read(_scene._frameCloudShadowRG, RGUsage.ComputeRead);
            composite.Read(_hdrInRG, RGUsage.ComputeRead);
            composite.Read(_historyOutRG, RGUsage.ComputeRead);
            composite.Read(_scene._gbufferTargets.Depth, RGUsage.ComputeRead);
            composite.Write(_hdrOutRG, RGUsage.ComputeWrite);

            _chain.Advance(_hdrOutRG);
        }

        void IRenderGraphNode.Execute(in RenderGraphContext context, int passId)
        {
            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            Extent2D trace = _scene._recordedCloudTraceExtent;
            if (passId == PassMarch)
            {
                CloudMarchRoot root = new(
                    _scene._frameViews.View,
                    _scene._recordedCloudFrame.View,
                    _scene._cloudBaseNoiseTex!.SampledId,
                    _scene._cloudDetailNoiseTex!.SampledId,
                    GPUTextureId.FromSampled(context.GetTexture(_scene._gbufferTargets.Depth)),
                    GPUTextureId.FromStorage(context.GetTexture(_rawCloudsRG)),
                    trace.X,
                    trace.Y);
                compute.Dispatch(_scene._cloudMarchShader!, in root, new Extent3D((trace.X + 7u) / 8u, (trace.Y + 7u) / 8u, 1u));
                return;
            }

            if (passId == PassTemporal)
            {
                Texture histOutTex = _scene._cloudHistoryTex[_scene._recordedCurrCloudHistoryIndex]!;
                CloudTemporalRoot root = new(
                    _scene._recordedCloudViews.View,
                    _scene._recordedCloudFrame.View,
                    GPUTextureId.FromSampled(context.GetTexture(_rawCloudsRG)),
                    _scene._recordedCloudHistoryValid ? _scene._cloudHistoryTex[_scene._recordedPrevCloudHistoryIndex]!.SampledId : GPUTextureId.Invalid,
                    GPUTextureId.FromSampled(context.GetTexture(_scene._gbufferTargets.Depth)),
                    histOutTex.GetStorageId(0),
                    trace.X,
                    trace.Y,
                    Math.Clamp(_scene.VolumetricClouds.TemporalBlend, 0f, 0.98f),
                    0u);
                compute.Dispatch(_scene._cloudTemporalShader!, in root, new Extent3D((trace.X + 7u) / 8u, (trace.Y + 7u) / 8u, 1u));
                return;
            }

            Extent3D full = context.GetExtent(_hdrOutRG);
            Texture resolvedClouds = _scene._cloudHistoryTex[_scene._recordedCurrCloudHistoryIndex]!;
            CloudCompositeRoot compRoot = new(
                _scene._frameViews.View,
                _scene._frameShadowData.View,
                _scene._recordedCloudFrame.View,
                GPUTextureId.FromSampled(context.GetTexture(_hdrInRG)),
                resolvedClouds.SampledId,
                GPUTextureId.FromSampled(context.GetTexture(_scene._gbufferTargets.Depth)),
                GPUTextureId.FromStorage(context.GetTexture(_hdrOutRG)),
                trace.X,
                trace.Y);
            compute.Dispatch(_scene._cloudCompositeShader!, in compRoot, new Extent3D((full.X + 7u) / 8u, (full.Y + 7u) / 8u, 1u));
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 176)]
    private readonly record struct CloudFrameGpu(
        Vector4 LayerParams,
        Vector4 NoiseParams,
        Vector4 ScatteringParams,
        Vector4 SunDirParams,
        Vector4 SunColorParams,
        Vector4 AtmosphereParams,
        Vector4 SkyParams,
        Vector4 BasePhaseAndAlt,
        Vector4 DetailPhaseAndWeather,
        Vector4 ShadowOriginExtent,
        uint MaxSteps,
        uint LightSteps,
        uint FrameIndex,
        uint Flags);

    [StructLayout(LayoutKind.Sequential, Size = 20)]
    private readonly record struct CloudNoiseRoot(
        GPUTextureId OutputTex,
        uint VolumeSize,
        uint Mode,
        uint BaseFreq,
        uint Reserved0);

    [StructLayout(LayoutKind.Sequential, Size = 36)]
    private readonly record struct CloudShadowRoot(
        GPUBufferView CloudFrame,
        GPUTextureId BaseNoiseTex,
        GPUTextureId DetailNoiseTex,
        GPUTextureId OutShadowTex,
        uint Resolution,
        uint Reserved0,
        uint Reserved1,
        uint Reserved2);

    [StructLayout(LayoutKind.Sequential, Size = 40)]
    private readonly record struct CloudMarchRoot(
        GPUBufferView Views,
        GPUBufferView CloudFrame,
        GPUTextureId BaseNoiseTex,
        GPUTextureId DetailNoiseTex,
        GPUTextureId DepthTex,
        GPUTextureId OutRawTex,
        uint TraceWidth,
        uint TraceHeight);

    [StructLayout(LayoutKind.Sequential, Size = 48)]
    private readonly record struct CloudTemporalRoot(
        GPUBufferView Views,
        GPUBufferView CloudFrame,
        GPUTextureId RawTex,
        GPUTextureId HistoryInTex,
        GPUTextureId DepthTex,
        GPUTextureId HistoryOutTex,
        uint TraceWidth,
        uint TraceHeight,
        float TemporalBlend,
        uint Reserved0);

    [StructLayout(LayoutKind.Sequential, Size = 48)]
    private readonly record struct CloudCompositeRoot(
        GPUBufferView Views,
        GPUBufferView ShadowFrame,
        GPUBufferView CloudFrame,
        GPUTextureId HdrInTex,
        GPUTextureId ResolvedCloudsTex,
        GPUTextureId DepthTex,
        GPUTextureId HdrOutTex,
        uint TraceWidth,
        uint TraceHeight);
}
