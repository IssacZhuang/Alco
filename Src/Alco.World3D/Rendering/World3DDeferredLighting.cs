using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>Controls the directional sun and procedural sky used to light a World3D scene.</summary>
public readonly record struct World3DLighting
{
    /// <summary>Creates the default daylight illumination.</summary>
    public World3DLighting() { }

    /// <summary>Direction toward the sun with +Z up. It is normalized before use; a zero vector uses +Z.</summary>
    public Vector3 DirectionToSun { get; init; } = ProceduralSkyUtility.GetDirectionToSun(10.0f);

    /// <summary>Linear RGB tint multiplied by atmospheric sun transmittance.</summary>
    public Vector3 SunColor { get; init; } = Vector3.One;

    /// <summary>Direct sunlight intensity before twilight fading.</summary>
    public float SunIntensity { get; init; } = 8.0f;

    /// <summary>Rayleigh scattering density multiplier.</summary>
    public float RayleighScale { get; init; } = 1.0f;

    /// <summary>Mie scattering density multiplier.</summary>
    public float MieScale { get; init; } = 0.3f;

    /// <summary>Visible sky Mie phase anisotropy; zero is isotropic.</summary>
    public float MieAnisotropy { get; init; } = 0.9f;

    /// <summary>Linear exposure of atmospheric scattering and the night radiance floor.</summary>
    public float SkyExposure { get; init; } = 1.0f;

    /// <summary>Brightness of visible stars after sunset.</summary>
    public float StarIntensity { get; init; } = 1.0f;

    /// <summary>Baseline sky radiance at night.</summary>
    public float NightFloor { get; init; } = 0.05f;

    /// <summary>Solar radiance driving atmospheric scattering, independent of direct lighting intensity.</summary>
    public float SunRadianceScale { get; init; } = 20.0f;

    /// <summary>Strength of the diffuse ground and sky bounce floor.</summary>
    public float AmbientFloor { get; init; } = 0.25f;

    /// <summary>Filtered sky saturation for diffuse ambient light, from zero (gray) to one (full color).</summary>
    public float AmbientSaturation { get; init; } = 0.6f;

    /// <summary>Cosine of the visible sun disc's angular radius.</summary>
    public float SunDiscCosRadius { get; init; } = 0.9995f;

    /// <summary>HDR brightness of the visible sun disc. Zero hides it without affecting direct lighting.</summary>
    public float SunDiscBrightness { get; init; } = 18.0f;
}

public sealed partial class World3DScene
{
    private RGNode_AutoExposure? _autoExposureNode;

    /// <summary>Gets or sets the sun and sky settings used on the next full render frame.</summary>
    public World3DLighting Lighting { get; set; } = new();

    /// <summary>Gets the histogram-based automatic exposure configuration for this scene.</summary>
    public World3DAutoExposureSettings AutoExposure { get; } = new();

    /// <summary>Gets the recorded automatic exposure status and latest async telemetry for the latest frame.</summary>
    public World3DAutoExposureStats LastRecordedAutoExposure { get; private set; }

    /// <summary>Gets the latest async 256-bin log2 luminance histogram weights when telemetry is enabled.</summary>
    public ReadOnlySpan<uint> LastAutoExposureHistogram =>
        _autoExposureNode != null ? _autoExposureNode.LastHistogram : ReadOnlySpan<uint>.Empty;

    /// <summary>Forces the next rendered frame to snap immediately to the metered exposure without temporal lag.</summary>
    public void ResetAutoExposure() => _autoExposureNode?.Reset();

    /// <summary>Synchronously reads the latest GPU automatic exposure state from the last submitted world frame.</summary>
    /// <param name="state">Receives the GPU exposure state on success.</param>
    /// <returns>True when a submitted GPU world frame with initialized exposure state was available.</returns>
    public bool TryReadAutoExposureState(out AutoExposureState state)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        state = default;
        if (_context == null || _context.IsDisposed || _context.CurrentFrameId != 0 || _system.ActiveBackend == GraphicsBackend.NoGPU)
            return false;
        ReconcileFrame();
        if (!_submittedWorld || _autoExposureNode == null || !_autoExposureNode.TryReadState(out state))
            return false;
        LastRecordedAutoExposure = new World3DAutoExposureStats(
            AutoExposure.Enabled && LightingDebugView == World3DLightingDebugView.None,
            true,
            state.Exposure,
            state.AdaptedEV,
            state.TargetEV,
            state.AverageLuminance);
        return true;
    }

    private void UpdateAutoExposureStats(bool active)
    {
        if (_autoExposureNode != null && _autoExposureNode.HasTelemetry)
        {
            AutoExposureState s = _autoExposureNode.LastState;
            LastRecordedAutoExposure = new World3DAutoExposureStats(
                active,
                true,
                s.Exposure,
                s.AdaptedEV,
                s.TargetEV,
                s.AverageLuminance);
        }
        else
        {
            LastRecordedAutoExposure = new World3DAutoExposureStats(active, false, 1f, 0f, 0f, 0f);
        }
    }

    private void AddLightingPipeline()
    {
        const string atmosphereModule = "World3DAtmosphere";
        const string lightingModule = "World3DDeferredLighting";
        ShaderSystem shaders = _system.Shaders;
        shaders.Compiler.RegisterVirtualModule(atmosphereModule, atmosphereModule + ".slang", ReadWorldShader(atmosphereModule));
        IReadOnlyList<ShaderTypeLayout> shadowTypes = shaders.Compiler.LoadModuleFromSource(
            "World3DShadowSample", "World3DShadowSample.slang", ReadWorldShader("World3DShadowSample")).GetTypes();
        GpuStore.ValidateLayout<World3DCascadeGpu>(shadowTypes.First(t => t.TypeName == nameof(World3DCascadeGpu)));
        GpuStore.ValidateLayout<World3DShadowFrameGpu>(shadowTypes.First(t => t.TypeName == nameof(World3DShadowFrameGpu)));
        Shader lightingShader = shaders.GetShaderFromSource(lightingModule, lightingModule + ".slang",
            ReadWorldShader(lightingModule), ShaderKind.Compute);
        ValidateRoot<DeferredLightingRoot>(lightingShader.Reflection.Root
            ?? throw new InvalidOperationException($"{lightingModule} did not reflect a root layout."));

        RenderChain chain = new();
        RGNode_AutoExposure autoExposure = new(_system, chain, name: "World3D.AutoExposure");
        _autoExposureNode = autoExposure;
        RGNode_Bloom bloom = new(_system, chain, name: "World3D.Bloom")
        {
            Threshold = 1.0f,
            ExposureProvider = _ => AutoExposure.ExposureAwareBloom ? autoExposure.OutputBuffer : default,
        };
        RGNode_Tonemap tonemap = new(_system, chain, name: "World3D.Tonemap")
        {
            Operator = TonemapType.ACES,
            ExposureProvider = _ => autoExposure.OutputBuffer,
        };
        RGNode_FXAA fxaa = new(_system, _ => chain.Current, name: "World3D.FXAA");
        _graph.Use(new DeferredLightingNode(this, chain, lightingShader, autoExposure, bloom, tonemap, fxaa))
            .Use(new VolumetricCloudsPostNode(this, chain))
            .Use(new VolumetricLightNode(this, chain))
            .Use(new SsrPasses(this, chain))
            .Use(new ForwardPasses(this, chain))
            .Use(autoExposure).Use(bloom).Use(tonemap).Use(fxaa);
    }

    [StructLayout(LayoutKind.Sequential, Size = 176)]
    private struct DeferredLightingRoot
    {
        public GPUBufferView Views;
        public GPUBufferView ShadowFrame;
        public GPUTextureId Albedo;
        public GPUTextureId Normal;
        public GPUTextureId Material;
        public GPUTextureId Emissive;
        public GPUTextureId Depth;
        public GPUTextureId Hdr;
        public GPUTextureId GiDiffuse;
        public GPUTextureId GiSpecular;
        public Vector4 SunDirectionAndIntensity;
        public Vector4 SunColorAndDiscCosRadius;
        public Vector4 Atmosphere;
        public Vector4 Sky;
        public Vector4 SkyHorizonAndDiscBrightness;
        public Vector4 SkyZenith;
        public GPUBufferView PointLights;
        public GPUTextureId GiRawDiffuse;
        public uint PointLightCount;
        public uint GiFlags;
        public float GiDiffuseScale;
        public float GiSpecularScale;
        public uint Reserved0;
    }

    private sealed class DeferredLightingNode(World3DScene scene, RenderChain chain, Shader shader,
        RGNode_AutoExposure autoExposure, RGNode_Bloom bloom, RGNode_Tonemap tonemap, RGNode_FXAA fxaa) : IRenderGraphNode
    {
        private const uint GroupSize = 8;

        private readonly World3DScene _scene = scene;
        private readonly RenderChain _chain = chain;
        private readonly Shader _shader = shader;
        private readonly RGNode_AutoExposure _autoExposure = autoExposure;
        private readonly RGNode_Bloom _bloom = bloom;
        private readonly RGNode_Tonemap _tonemap = tonemap;
        private readonly RGNode_FXAA _fxaa = fxaa;
        private RGTexture _hdr;
        private World3DLighting? _cachedLighting;
        private DeferredLightingRoot _cachedEnvironment;
        private bool _wasAutoExposureActive;

        void IRenderGraphNode.Setup(RenderGraphBuilder builder)
        {
            GBufferTargets gbuffer = _scene._gbufferTargets;
            BufferSlice views = _scene._frameViews;
            bool active = builder.HasOutput && gbuffer.Depth.IsValid && !views.IsEmpty;
            // Setup runs in registration order, before the post nodes inspect IsEnabled.
            // A skipped frame must not let them read the previous frame's chain handles.
            bool autoExposureActive = active && _scene.AutoExposure.Enabled && _scene.LightingDebugView == World3DLightingDebugView.None;
            if (autoExposureActive && !_wasAutoExposureActive)
                _autoExposure.Reset();
            _wasAutoExposureActive = autoExposureActive;

            _autoExposure.IsEnabled = autoExposureActive;
            if (autoExposureActive)
            {
                World3DAutoExposureSettings ae = _scene.AutoExposure;
                _autoExposure.Settings = new AutoExposureSettings
                {
                    MinEV = ae.MinEV,
                    MaxEV = ae.MaxEV,
                    ExposureCompensation = ae.ExposureCompensation,
                    KeyValue = ae.KeyValue,
                    LowPercent = ae.LowPercent,
                    HighPercent = ae.HighPercent,
                    MinLogLuminance = ae.MinLogLuminance,
                    MaxLogLuminance = ae.MaxLogLuminance,
                    SpeedUp = ae.SpeedUp,
                    SpeedDown = ae.SpeedDown,
                    MeteringMode = ae.MeteringMode,
                    CenterWeightRadius = ae.CenterWeightRadius,
                    IgnoreDarkFloor = ae.IgnoreDarkFloor,
                    HalfResolution = ae.HalfResolution,
                };
                _autoExposure.TelemetryEnabled = ae.TelemetryEnabled;
            }
            else
            {
                _autoExposure.TelemetryEnabled = false;
            }

            _bloom.IsEnabled = active;
            _tonemap.IsEnabled = active;
            _fxaa.IsEnabled = active;
            if (!active)
                return;

            _hdr = builder.CreateTexture(new RGTextureDesc
            {
                Name = "World3D.HDR",
                Extent = builder.GetExtent(gbuffer.Depth),
                Format = Format.Rgba16Float,
            });
            _chain.Reset(_hdr);

            // FXAA writes directly to the graph output. An sRGB attachment encodes on write.
            bool srgbOutput = builder.GetFormat(builder.Output) is Format.Rgba8Srgb or Format.Bgra8Srgb;
            _tonemap.ACESData = ACESTonemapData.Default with { Gamma = srgbOutput ? 1.0f : 2.2f };
            _tonemap.OutputFormat = srgbOutput ? Format.Rgba16Float : Format.Rgba8Unorm;
            _tonemap.DitherEnabled = !srgbOutput;
            _fxaa.ColorSpace = srgbOutput ? FXAAColorSpace.Linear : FXAAColorSpace.Gamma;

            RenderGraphPassBuilder pass = builder.AddPass("World3D.DeferredLighting");
            pass.Read(builder.ImportBuffer(views.Buffer!, "World3D.Views"), RGUsage.ComputeRead);
            if (!_scene._frameShadowData.IsEmpty)
                pass.Read(builder.ImportBuffer(_scene._frameShadowData.Buffer!, "World3D.ShadowFrame"), RGUsage.ComputeRead);
            if (_scene._frameShadowArrayRG.IsValid)
                pass.Read(_scene._frameShadowArrayRG, RGUsage.ComputeRead);
            if (_scene._frameShadowMinMaxRG.IsValid)
                pass.Read(_scene._frameShadowMinMaxRG, RGUsage.ComputeRead);
            if (_scene._frameFarHeightRG.IsValid)
                pass.Read(_scene._frameFarHeightRG, RGUsage.ComputeRead);
            if (_scene._frameCloudShadowRG.IsValid)
                pass.Read(_scene._frameCloudShadowRG, RGUsage.ComputeRead);
            if (_scene._frameRsmFluxRG.IsValid)
                pass.Read(_scene._frameRsmFluxRG, RGUsage.ComputeRead);
            if (_scene._frameCurrHiZ.IsValid)
                pass.Read(_scene._frameCurrHiZ, RGUsage.ComputeRead);
            if (_scene._frameGiOutputDiffuseRG.IsValid)
                pass.Read(_scene._frameGiOutputDiffuseRG, RGUsage.ComputeRead);
            if (_scene._frameGiOutputSpecularRG.IsValid)
                pass.Read(_scene._frameGiOutputSpecularRG, RGUsage.ComputeRead);
            if (_scene._frameGiRawDiffuseRG.IsValid)
                pass.Read(_scene._frameGiRawDiffuseRG, RGUsage.ComputeRead);
            pass.Read(gbuffer.Albedo, RGUsage.ComputeRead);
            pass.Read(gbuffer.Normal, RGUsage.ComputeRead);
            pass.Read(gbuffer.Material, RGUsage.ComputeRead);
            pass.Read(gbuffer.Emissive, RGUsage.ComputeRead);
            pass.Read(gbuffer.Depth, RGUsage.ComputeRead);
            pass.Write(_hdr, RGUsage.ComputeWrite);
        }

        void IRenderGraphNode.Execute(in RenderGraphContext context, int passId)
        {
            World3DLighting lighting = _scene.Lighting;
            if (_cachedLighting != lighting)
            {
                _cachedEnvironment = EvaluateEnvironment(in lighting);
                _cachedLighting = lighting;
            }

            GBufferTargets gbuffer = _scene._gbufferTargets;
            DeferredLightingRoot root = _cachedEnvironment;
            root.Views = _scene._frameViews.View;
            root.ShadowFrame = _scene._frameShadowData.View;
            root.Albedo = GPUTextureId.FromSampled(context.GetTexture(gbuffer.Albedo));
            root.Normal = GPUTextureId.FromSampled(context.GetTexture(gbuffer.Normal));
            root.Material = GPUTextureId.FromSampled(context.GetTexture(gbuffer.Material));
            root.Emissive = GPUTextureId.FromSampled(context.GetTexture(gbuffer.Emissive));
            root.Depth = GPUTextureId.FromSampled(context.GetTexture(gbuffer.Depth));
            root.Hdr = GPUTextureId.FromStorage(context.GetTexture(_hdr));
            root.PointLights = _scene._framePointLights.IsEmpty ? default : _scene._framePointLights.View;
            root.PointLightCount = (uint)_scene._recordedPointLightCount;

            if (_scene._recordedVoxelGiEnabled && _scene._frameGiOutputDiffuseRG.IsValid && _scene._frameGiOutputSpecularRG.IsValid)
            {
                root.GiFlags = 1u;
                root.GiDiffuse = GPUTextureId.FromSampled(context.GetTexture(_scene._frameGiOutputDiffuseRG));
                root.GiSpecular = GPUTextureId.FromSampled(context.GetTexture(_scene._frameGiOutputSpecularRG));
                root.GiRawDiffuse = _scene._frameGiRawDiffuseRG.IsValid
                    ? GPUTextureId.FromSampled(context.GetTexture(_scene._frameGiRawDiffuseRG))
                    : GPUTextureId.Invalid;
                root.GiDiffuseScale = MathF.Max(0f, _scene.VoxelGi.IndirectDiffuseIntensity);
                root.GiSpecularScale = _scene.Ssr.Enabled && _scene.Ssr.DisableVoxelSpecularFallback
                    ? 0f
                    : MathF.Max(0f, _scene.VoxelGi.IndirectSpecularIntensity);
            }
            else
            {
                root.GiFlags = 0u;
                root.GiDiffuse = GPUTextureId.Invalid;
                root.GiSpecular = GPUTextureId.Invalid;
                root.GiRawDiffuse = GPUTextureId.Invalid;
                root.GiDiffuseScale = 0f;
                root.GiSpecularScale = 0f;
            }

            Extent3D extent = context.GetExtent(_hdr);
            Extent3D groups = new((extent.X + GroupSize - 1) / GroupSize, (extent.Y + GroupSize - 1) / GroupSize, 1);
            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            compute.Dispatch(_shader, in root, groups);
        }

        private static DeferredLightingRoot EvaluateEnvironment(in World3DLighting lighting)
        {
            EvaluateFilteredSkyAndSun(in lighting, out Vector3 directionToSun, out Vector3 sunColor,
                out float sunIntensity, out Vector3 horizon, out Vector3 zenith);

            return new DeferredLightingRoot
            {
                SunDirectionAndIntensity = new Vector4(directionToSun, sunIntensity),
                SunColorAndDiscCosRadius = new Vector4(sunColor, Math.Clamp(lighting.SunDiscCosRadius, 0.0f, 0.9999999f)),
                Atmosphere = new Vector4(lighting.RayleighScale, lighting.MieScale,
                    Math.Clamp(lighting.MieAnisotropy, -0.99f, 0.99f), lighting.SkyExposure),
                Sky = new Vector4(lighting.StarIntensity, lighting.NightFloor, lighting.SunRadianceScale, lighting.AmbientFloor),
                SkyHorizonAndDiscBrightness = new Vector4(horizon, lighting.SunDiscBrightness),
                SkyZenith = new Vector4(zenith, 0.0f),
            };
        }
    }

    private static void EvaluateFilteredSkyAndSun(
        in World3DLighting lighting,
        out Vector3 directionToSun,
        out Vector3 sunColor,
        out float sunIntensity,
        out Vector3 horizon,
        out Vector3 zenith)
    {
        float lengthSquared = lighting.DirectionToSun.LengthSquared();
        directionToSun = lengthSquared > 0.0f && float.IsFinite(lengthSquared)
            ? lighting.DirectionToSun / MathF.Sqrt(lengthSquared)
            : Vector3.UnitZ;
        sunColor = ProceduralSkyUtility.GetSunColor(directionToSun) * lighting.SunColor;
        sunIntensity = lighting.SunIntensity * ProceduralSkyUtility.GetSunLightScale(directionToSun);
        ProceduralSkyUtility.GetSkyRadianceGradient(directionToSun, lighting.RayleighScale, lighting.MieScale,
            lighting.SkyExposure, lighting.NightFloor, lighting.SunRadianceScale, out Vector3 rawHorizon, out Vector3 rawZenith);

        Vector3 luminanceWeights = new(0.2126f, 0.7152f, 0.0722f);
        float saturation = Math.Clamp(lighting.AmbientSaturation, 0.0f, 1.0f);
        horizon = Vector3.Lerp(new Vector3(Vector3.Dot(rawHorizon, luminanceWeights)), rawHorizon, saturation);
        zenith = Vector3.Lerp(new Vector3(Vector3.Dot(rawZenith, luminanceWeights)), rawZenith, saturation);
    }
}
