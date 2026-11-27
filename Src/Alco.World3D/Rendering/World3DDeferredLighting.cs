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
    /// <summary>Gets or sets the sun and sky settings used on the next full render frame.</summary>
    public World3DLighting Lighting { get; set; } = new();

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
        RGNode_Bloom bloom = new(_system, chain, name: "World3D.Bloom") { Threshold = 1.0f };
        RGNode_Tonemap tonemap = new(_system, chain, name: "World3D.Tonemap") { Operator = TonemapType.ACES };
        RGNode_FXAA fxaa = new(_system, _ => chain.Current, name: "World3D.FXAA");
        _graph.Use(new DeferredLightingNode(this, chain, lightingShader, bloom, tonemap, fxaa))
            .Use(bloom).Use(tonemap).Use(fxaa);
    }

    [StructLayout(LayoutKind.Sequential, Size = 144)]
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
        public uint Reserved0;
        public uint Reserved1;
        public Vector4 SunDirectionAndIntensity;
        public Vector4 SunColorAndDiscCosRadius;
        public Vector4 Atmosphere;
        public Vector4 Sky;
        public Vector4 SkyHorizonAndDiscBrightness;
        public Vector4 SkyZenith;
    }

    private sealed class DeferredLightingNode(World3DScene scene, RenderChain chain, Shader shader,
        RGNode_Bloom bloom, RGNode_Tonemap tonemap, RGNode_FXAA fxaa) : IRenderGraphNode
    {
        private const uint GroupSize = 8;
        private static readonly Vector3 LuminanceWeights = new(0.2126f, 0.7152f, 0.0722f);

        private readonly World3DScene _scene = scene;
        private readonly RenderChain _chain = chain;
        private readonly Shader _shader = shader;
        private readonly RGNode_Bloom _bloom = bloom;
        private readonly RGNode_Tonemap _tonemap = tonemap;
        private readonly RGNode_FXAA _fxaa = fxaa;
        private RGTexture _hdr;
        private World3DLighting? _cachedLighting;
        private DeferredLightingRoot _cachedEnvironment;

        void IRenderGraphNode.Setup(RenderGraphBuilder builder)
        {
            GBufferTargets gbuffer = _scene._gbufferTargets;
            BufferSlice views = _scene._frameViews;
            bool active = builder.HasOutput && gbuffer.Depth.IsValid && !views.IsEmpty;
            // Setup runs in registration order, before the post nodes inspect IsEnabled.
            // A skipped frame must not let them read the previous frame's chain handles.
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
            if (_scene._frameRsmFluxRG.IsValid)
                pass.Read(_scene._frameRsmFluxRG, RGUsage.ComputeRead);
            if (_scene._frameCurrHiZ.IsValid)
                pass.Read(_scene._frameCurrHiZ, RGUsage.ComputeRead);
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

            Extent3D extent = context.GetExtent(_hdr);
            Extent3D groups = new((extent.X + GroupSize - 1) / GroupSize, (extent.Y + GroupSize - 1) / GroupSize, 1);
            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            compute.Dispatch(_shader, in root, groups);
        }

        private static DeferredLightingRoot EvaluateEnvironment(in World3DLighting lighting)
        {
            float lengthSquared = lighting.DirectionToSun.LengthSquared();
            Vector3 directionToSun = lengthSquared > 0.0f && float.IsFinite(lengthSquared)
                ? lighting.DirectionToSun / MathF.Sqrt(lengthSquared)
                : Vector3.UnitZ;
            Vector3 sunColor = ProceduralSkyUtility.GetSunColor(directionToSun) * lighting.SunColor;
            float sunIntensity = lighting.SunIntensity * ProceduralSkyUtility.GetSunLightScale(directionToSun);
            ProceduralSkyUtility.GetSkyRadianceGradient(directionToSun, lighting.RayleighScale, lighting.MieScale,
                lighting.SkyExposure, lighting.NightFloor, lighting.SunRadianceScale, out Vector3 horizon, out Vector3 zenith);

            float saturation = Math.Clamp(lighting.AmbientSaturation, 0.0f, 1.0f);
            return new DeferredLightingRoot
            {
                SunDirectionAndIntensity = new Vector4(directionToSun, sunIntensity),
                SunColorAndDiscCosRadius = new Vector4(sunColor, Math.Clamp(lighting.SunDiscCosRadius, 0.0f, 0.9999999f)),
                Atmosphere = new Vector4(lighting.RayleighScale, lighting.MieScale,
                    Math.Clamp(lighting.MieAnisotropy, -0.99f, 0.99f), lighting.SkyExposure),
                Sky = new Vector4(lighting.StarIntensity, lighting.NightFloor, lighting.SunRadianceScale, lighting.AmbientFloor),
                SkyHorizonAndDiscBrightness = new Vector4(Desaturate(horizon, saturation), lighting.SunDiscBrightness),
                SkyZenith = new Vector4(Desaturate(zenith, saturation), 0.0f),
            };
        }

        private static Vector3 Desaturate(Vector3 color, float saturation) =>
            Vector3.Lerp(new Vector3(Vector3.Dot(color, LuminanceWeights)), color, saturation);
    }
}
