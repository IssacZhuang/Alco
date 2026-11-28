using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private Shader? _volLightMarchShader;
    private Shader? _volLightCompositeShader;
    private bool _volLightShadersLoaded;

    private bool _recordedVolLightEnabled;
    private bool _recordedVolLightHalfRes;
    private Extent2D _recordedVolLightTraceExtent;
    private int _recordedVolLightSteps;
    private uint _volLightFrameCounter;
    private Vector4 _recordedVolLightFogParams;
    private Vector4 _recordedVolLightParams;
    private Vector4 _recordedVolLightAmbientAndAlt;
    private Vector4 _recordedVolLightAtmosphere;

    /// <summary>Gets or sets runtime settings for shadowed volumetric light shafts and exponential height fog.</summary>
    public World3DVolumetricLightSettings VolumetricLight { get; set; } = new();

    /// <summary>Gets the recorded volumetric light telemetry for the latest frame.</summary>
    public World3DVolumetricLightStats LastRecordedVolumetricLight { get; private set; } =
        new(false, default, 0);

    private void PrepareVolumetricLight(Extent2D viewport, in World3DCamera camera)
    {
        _recordedVolLightEnabled = false;
        _recordedVolLightTraceExtent = default;
        _recordedVolLightSteps = 0;

        World3DVolumetricLightSettings settings = VolumetricLight;
        if (!_recordedWorld || !settings.Enabled || viewport.X == 0 || viewport.Y == 0
            || settings.Intensity <= 0f && settings.AmbientFogIntensity <= 0f)
        {
            LastRecordedVolumetricLight = new World3DVolumetricLightStats(false, default, 0);
            return;
        }

        EnsureVolumetricLightShaders();
        ++_volLightFrameCounter;

        bool halfRes = settings.HalfResolution;
        Extent2D trace = halfRes
            ? new Extent2D(Math.Max(1u, (viewport.X + 1u) >> 1), Math.Max(1u, (viewport.Y + 1u) >> 1))
            : viewport;
        int steps = Math.Clamp(settings.StepCount, 4, 64);

        EvaluateFilteredSkyAndSun(Lighting, out _, out Vector3 sunColor, out float sunIntensity, out Vector3 horizon, out _);

        _recordedVolLightEnabled = true;
        _recordedVolLightHalfRes = halfRes;
        _recordedVolLightTraceExtent = trace;
        _recordedVolLightSteps = steps;
        _recordedVolLightFogParams = new Vector4(
            Math.Max(settings.BaseDensity, 0f),
            Math.Max(settings.HeightFalloff, 0f),
            settings.BaseHeightMeters,
            Math.Max(settings.MaxDistanceMeters, 1f));
        _recordedVolLightParams = new Vector4(
            sunColor * (sunIntensity * Math.Max(settings.Intensity, 0f)),
            Math.Clamp(settings.Anisotropy, -0.95f, 0.95f));
        _recordedVolLightAmbientAndAlt = new Vector4(
            horizon * Math.Max(settings.AmbientFogIntensity, 0f),
            (float)camera.Eye.Z);
        _recordedVolLightAtmosphere = new Vector4(
            Lighting.RayleighScale, Lighting.MieScale, Math.Clamp(Lighting.MieAnisotropy, -0.99f, 0.99f), Lighting.SkyExposure);

        LastRecordedVolumetricLight = new World3DVolumetricLightStats(true, trace, steps);
    }

    private void EnsureVolumetricLightShaders()
    {
        if (_volLightShadersLoaded)
            return;

        _system.Shaders.Compiler.RegisterVirtualModule("World3DCloudCommon", "World3DCloudCommon.slang", ReadWorldShader("World3DCloudCommon"));
        _volLightMarchShader = LoadSceneCompute<VolumetricLightMarchRoot>("World3DVolumetricLightMarch");
        _volLightCompositeShader = LoadSceneCompute<VolumetricLightCompositeRoot>("World3DVolumetricLightComposite");
        _volLightShadersLoaded = true;
    }

    private sealed class VolumetricLightNode(World3DScene scene, RenderChain chain) : IRenderGraphNode
    {
        private const int PassMarch = 0;
        private const int PassComposite = 1;

        private readonly World3DScene _scene = scene;
        private readonly RenderChain _chain = chain;
        private RGTexture _scatterRG;
        private RGTexture _hdrInRG;
        private RGTexture _hdrOutRG;

        void IRenderGraphNode.Setup(RenderGraphBuilder builder)
        {
            _scatterRG = default;
            _hdrInRG = default;
            _hdrOutRG = default;
            if (!_scene._recordedWorld || !_scene._recordedVolLightEnabled || !_chain.Current.IsValid
                || !_scene._gbufferTargets.Depth.IsValid
                || _scene.LightingDebugView != World3DLightingDebugView.None)
                return;

            _hdrInRG = _chain.Current;
            Extent2D trace = _scene._recordedVolLightTraceExtent;
            _scatterRG = builder.CreateTexture(new RGTextureDesc
            {
                Name = "World3D.VolLight.Scatter",
                Extent = new Extent3D(trace.X, trace.Y, 1u),
                Format = Format.Rgba16Float,
            });

            RGBuffer views = builder.ImportBuffer(_scene._frameViews.Buffer!, "World3D.Views");
            RenderGraphPassBuilder march = builder.AddPass("World3D.VolLight.March", PassMarch);
            march.Read(views, RGUsage.ComputeRead);
            if (!_scene._frameShadowData.IsEmpty)
                march.Read(builder.ImportBuffer(_scene._frameShadowData.Buffer!, "World3D.ShadowFrame"), RGUsage.ComputeRead);
            if (_scene._frameShadowArrayRG.IsValid) march.Read(_scene._frameShadowArrayRG, RGUsage.ComputeRead);
            if (_scene._frameShadowMinMaxRG.IsValid) march.Read(_scene._frameShadowMinMaxRG, RGUsage.ComputeRead);
            if (_scene._frameFarHeightRG.IsValid) march.Read(_scene._frameFarHeightRG, RGUsage.ComputeRead);
            if (_scene._frameCloudShadowRG.IsValid) march.Read(_scene._frameCloudShadowRG, RGUsage.ComputeRead);
            march.Read(_scene._gbufferTargets.Depth, RGUsage.ComputeRead);
            march.Write(_scatterRG, RGUsage.ComputeWrite);

            _hdrOutRG = builder.CreateTexture(new RGTextureDesc
            {
                Name = "World3D.VolLight.HDR",
                Extent = builder.GetExtent(_hdrInRG),
                Format = Format.Rgba16Float,
            });
            RenderGraphPassBuilder comp = builder.AddPass("World3D.VolLight.Composite", PassComposite);
            comp.Read(views, RGUsage.ComputeRead);
            comp.Read(_hdrInRG, RGUsage.ComputeRead);
            comp.Read(_scatterRG, RGUsage.ComputeRead);
            comp.Read(_scene._gbufferTargets.Depth, RGUsage.ComputeRead);
            comp.Write(_hdrOutRG, RGUsage.ComputeWrite);

            _chain.Advance(_hdrOutRG);
        }

        void IRenderGraphNode.Execute(in RenderGraphContext context, int passId)
        {
            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            Extent2D trace = _scene._recordedVolLightTraceExtent;
            if (passId == PassMarch)
            {
                VolumetricLightMarchRoot root = new(
                    _scene._recordedVolLightFogParams,
                    _scene._recordedVolLightParams,
                    _scene._recordedVolLightAmbientAndAlt,
                    _scene._recordedVolLightAtmosphere,
                    _scene._frameViews.View,
                    _scene._frameShadowData.View,
                    GPUTextureId.FromSampled(context.GetTexture(_scene._gbufferTargets.Depth)),
                    GPUTextureId.FromStorage(context.GetTexture(_scatterRG)),
                    trace.X,
                    trace.Y,
                    (uint)_scene._recordedVolLightSteps,
                    _scene._volLightFrameCounter,
                    0u,
                    0u);
                compute.Dispatch(_scene._volLightMarchShader!, in root, new Extent3D((trace.X + 7u) / 8u, (trace.Y + 7u) / 8u, 1u));
                return;
            }

            Extent3D full = context.GetExtent(_hdrOutRG);
            VolumetricLightCompositeRoot compRoot = new(
                _scene._frameViews.View,
                GPUTextureId.FromSampled(context.GetTexture(_hdrInRG)),
                GPUTextureId.FromSampled(context.GetTexture(_scatterRG)),
                GPUTextureId.FromSampled(context.GetTexture(_scene._gbufferTargets.Depth)),
                GPUTextureId.FromStorage(context.GetTexture(_hdrOutRG)),
                trace.X,
                trace.Y,
                _scene._recordedVolLightHalfRes ? 1u : 0u,
                0u);
            compute.Dispatch(_scene._volLightCompositeShader!, in compRoot, new Extent3D((full.X + 7u) / 8u, (full.Y + 7u) / 8u, 1u));
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 112)]
    private readonly record struct VolumetricLightMarchRoot(
        Vector4 FogParams,
        Vector4 LightParams,
        Vector4 AmbientAndAlt,
        Vector4 AtmosphereParams,
        GPUBufferView Views,
        GPUBufferView ShadowFrame,
        GPUTextureId DepthTex,
        GPUTextureId OutScatterTex,
        uint TraceWidth,
        uint TraceHeight,
        uint StepCount,
        uint FrameIndex,
        uint Reserved0,
        uint Reserved1);

    [StructLayout(LayoutKind.Sequential, Size = 40)]
    private readonly record struct VolumetricLightCompositeRoot(
        GPUBufferView Views,
        GPUTextureId HdrInTex,
        GPUTextureId ScatterTex,
        GPUTextureId DepthTex,
        GPUTextureId HdrOutTex,
        uint TraceWidth,
        uint TraceHeight,
        uint HalfResolution,
        uint Reserved0);
}
