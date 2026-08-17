using System.Numerics;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.GUI;
using Alco.ImGUI;
using Alco.IO;
using Alco.Rendering;
using SandboxUtils;

/// <summary>
/// Sandbox demonstrating 3D PBR rendering with a deferred pipeline:
/// G-buffer pass, deferred lighting (GGX BRDF), a directional sun with
/// cascaded shadow maps (4 cascades in a 2x2 atlas), up to four point
/// lights, emissive surfaces with HDR bloom, a physically-based procedural
/// sky (single-scattering atmosphere driven by the time of day, with a sun
/// disc and a star field) and voxel global illumination (a camera-following
/// sparse brick clipmap with compute voxelization, rotation-balanced diffuse
/// tracing and hybrid reflections).
/// <br/>Static geometry (the whole Bistro scene, or the non-animated primitives)
/// is recorded once into render bundles (one per shadow cascade plus one for the
/// G-buffer pass) and replayed every frame; the game owns the scene materials
/// created via the pipeline's material factory. Only animated objects are drawn
/// immediately each frame.
/// <br/>Loads the Amazon Lumberyard Bistro scene (glTF) when present in
/// Assets/Bistro; otherwise falls back to a procedural primitive scene.
/// <br/>Controls: in fly mode hold the right mouse button to look around,
/// WASD to move; in orbit mode drag with the left mouse button to orbit,
/// mouse wheel to zoom, ESC to exit.
/// <br/>CLI: --screenshot=&lt;path.png&gt; [--frames=N] [--interior] [--procedural] [--cascade-debug] [--sun=x,y,z] [--time=H] [--time-speed=S] [--no-hbao] [--hbao-debug] [--no-gi] [--gi-debug=N] [--gi-resolution=50|75|100] [--no-bloom] [--bloom-threshold=N] [--bloom-intensity=N]
/// </summary>
public class Game : GameEngine
{
    /// <summary>A PBR scene object: mesh, transform and surface parameters.</summary>
    private sealed class SceneObject : IGBufferRenderable, IShadowRenderable
    {
        public required PrimitiveMesh Mesh;
        public Transform3D Transform = Transform3D.Identity;
        public Vector3 BaseColor;
        public float Metallic;
        public float Roughness;
        public float AmbientOcclusion = 1.0f;
        public bool CastsShadow = true;
        public float SpinSpeed;
        public float FloatSpeed;
        public float FloatPhase;

        /// <summary>The shared voxel mesh-material handle.</summary>
        public int VoxelMeshHandle = -1;
        /// <summary>The persistent structural voxel instance handle.</summary>
        public int VoxelStaticInstanceHandle = -1;

        // ── IGBufferRenderable (GBuffer renderer reads these) ──
        public GraphicsMaterial? GBufferMaterial { get; set; }
        public bool IsStatic => SpinSpeed == 0 && FloatSpeed == 0;
        Mesh IGBufferRenderable.Mesh => Mesh;
        GraphicsMaterial IGBufferRenderable.Material => GBufferMaterial!;
        Matrix4x4 IGBufferRenderable.WorldMatrix => Transform.Matrix;
        Vector4 IGBufferRenderable.BaseColor => new(BaseColor, 1.0f);
        Vector4 IGBufferRenderable.MetallicRoughnessAO => new(Metallic, Roughness, AmbientOcclusion, 1.0f);
        Vector3 IGBufferRenderable.EmissiveFactor => Vector3.Zero;
        float IGBufferRenderable.AlphaCutoff => 0.0f;

        public Matrix4x4 WorldMatrix => Transform.Matrix;

        // ── IShadowRenderable (Shadow renderer reads these) ──
        public GraphicsMaterial? ShadowMaterial { get; set; }
        bool IShadowRenderable.CastsShadow => CastsShadow;
        Mesh IShadowRenderable.Mesh => Mesh;
        GraphicsMaterial IShadowRenderable.Material => ShadowMaterial!;
        Matrix4x4 IShadowRenderable.WorldMatrix => Transform.Matrix;
        float IShadowRenderable.AlphaCutoff => 0.0f;
        float IShadowRenderable.BaseColorAlpha => 1.0f;
    }

    /// <summary>
    /// Adapter that wraps a Bistro <see cref="ModelDrawItem"/> + its material as an
    /// <see cref="IGBufferRenderable"/> for the GBufferRenderer registry.
    /// </summary>
    private sealed class BistroRenderable : IGBufferRenderable
    {
        private readonly ModelDrawItem _item;
        private readonly ModelMaterial _material;
        private readonly GraphicsMaterial _gbufferMaterial;
        private readonly Func<Vector3> _getEmissiveFactor;

        public BistroRenderable(ModelDrawItem item, ModelMaterial material, GraphicsMaterial gbufferMaterial,
            Func<Vector3> getEmissiveFactor)
        {
            _item = item;
            _material = material;
            _gbufferMaterial = gbufferMaterial;
            _getEmissiveFactor = getEmissiveFactor;
        }

        public bool IsStatic => true;
        Mesh IGBufferRenderable.Mesh => _item.Mesh;
        GraphicsMaterial IGBufferRenderable.Material => _gbufferMaterial;
        Matrix4x4 IGBufferRenderable.WorldMatrix => _item.World;
        Vector4 IGBufferRenderable.BaseColor => _material.BaseColorFactor;
        Vector4 IGBufferRenderable.MetallicRoughnessAO => new(_material.MetallicFactor, _material.RoughnessFactor, 1.0f, 0.0f);
        Vector3 IGBufferRenderable.EmissiveFactor => _getEmissiveFactor();
        float IGBufferRenderable.AlphaCutoff => GetAlphaCutoff(_material);
    }

    /// <summary>
    /// Adapter that wraps a Bistro <see cref="ModelDrawItem"/> + its shadow material as an
    /// <see cref="IShadowRenderable"/> for the ShadowRenderer registry.
    /// </summary>
    private sealed class BistroShadowRenderable : IShadowRenderable
    {
        private readonly ModelDrawItem _item;
        private readonly ModelMaterial _material;
        private readonly GraphicsMaterial _shadowMaterial;

        public BistroShadowRenderable(ModelDrawItem item, ModelMaterial material, GraphicsMaterial shadowMaterial)
        {
            _item = item;
            _material = material;
            _shadowMaterial = shadowMaterial;
        }

        public bool IsStatic => true;
        public bool CastsShadow => true;
        Mesh IShadowRenderable.Mesh => _item.Mesh;
        GraphicsMaterial IShadowRenderable.Material => _shadowMaterial;
        Matrix4x4 IShadowRenderable.WorldMatrix => _item.World;
        float IShadowRenderable.AlphaCutoff => GetAlphaCutoff(_material);
        float IShadowRenderable.BaseColorAlpha => _material.BaseColorFactor.W;
    }

    /// <summary>
    /// Adapter that wraps a Bistro <see cref="ModelDrawItem"/> + its glass material
    /// as an <see cref="IForwardRenderable"/> for the RGNode_Forward registry.
    /// </summary>
    private sealed class BistroGlassRenderable : IForwardRenderable
    {
        private readonly ModelDrawItem _item;
        private readonly ModelMaterial _material;
        private readonly GraphicsMaterial _glassMaterial;
        private readonly Func<float> _getTransmission;

        public BistroGlassRenderable(ModelDrawItem item, ModelMaterial material, GraphicsMaterial glassMaterial,
            Func<float> getTransmission)
        {
            _item = item;
            _material = material;
            _glassMaterial = glassMaterial;
            _getTransmission = getTransmission;
        }

        public bool IsStatic => true;
        Mesh IForwardRenderable.Mesh => _item.Mesh;
        GraphicsMaterial IForwardRenderable.Material => _glassMaterial;
        Matrix4x4 IForwardRenderable.WorldMatrix => _item.World;
        Vector4 IForwardRenderable.BaseColor => _material.BaseColorFactor;
        Vector4 IForwardRenderable.MetallicRoughnessAO => new(_material.MetallicFactor, _material.RoughnessFactor, 1.0f, 0.0f);
        Vector3 IForwardRenderable.EmissiveFactor => Vector3.Zero;
        float IForwardRenderable.TransmissionFactor => _getTransmission();
    }

    private readonly PBRDeferredPreset _preset;
    private readonly PBRSceneEnvironment _environment;
    private readonly GBufferRenderer _gbufferRenderer;
    private readonly ShadowRenderer _shadowRenderer;
    private readonly CameraPerspectiveBuffer _camera;
    private readonly PrimitiveMesh? _cubeMesh;
    private readonly PrimitiveMesh? _sphereMesh;
    private readonly PrimitiveMesh? _groundMesh;
    private readonly Texture2D _checkerTexture;

    private readonly List<SceneObject> _objects = new();

    // Scene materials owned by the game (created via the renderer's material factory).
    private GraphicsMaterial? _proceduralMaterial;
    private GraphicsMaterial? _proceduralShadowMaterial;
    private GraphicsMaterial[]? _bistroMaterials;
    private GraphicsMaterial[]? _bistroShadowMaterials;
    private GraphicsMaterial[]? _bistroGlassMaterials;

    // Forward transparency renderer for glass materials.
    private RGNode_Forward? _forwardRenderer;
    private bool _glassEnabled = true;
    private float _glassTransmission = 0.85f;

    // Volumetric clouds (ray-marched slab composited over the HDR scene,
    // with a coverage bake that shadows the direct sun).
    private readonly RGNode_VolumetricClouds? _clouds;
    private int _cloudStepsPreset = 0;
    private static readonly int[] CloudStepPresets = [64, 112, 160];
    private static readonly string[] CloudStepModes =
        ["Performance (64)", "Balanced (112)", "Quality (160)"];

    private bool _staticShadowBundlesDirty;
    private bool _bistroStreaming;

    // The loaded Bistro scene (null when the assets are missing).
    private readonly ModelScene? _bistro;

    // Camera orbit state.
    private float _yaw = 0.8f;
    private float _pitch = 0.35f;
    private float _distance = 15f;
    private float _minDistance = 4f;
    private float _maxDistance = 60f;
    private Vector3 _sceneCenter = Vector3.Zero;
    private float _sceneRadius = 20f;

    // Fly camera state (default mode when the Bistro scene is loaded; C toggles).
    private bool _flyMode;
    private Vector3 _flyPosition;
    private float _flySpeed = 3f;

    // Sun light. The direction comes from the time of day (see below) unless
    // overridden with --sun=x,y,z (direction the light travels); the color is
    // the atmosphere transmittance tint from ProceduralSkyUtility, shifted warm
    // by _sunWarmth for a more pleasing art direction.
    private readonly Vector3? _sunDirectionOverride;
    private float _sunIntensity = 8.0f;

    // Artistic warm tint for direct sunlight (0 = physical neutral, 1 = fully warm).
    private float _sunWarmth = 0.4f;

    // Sun orbit: max elevation at noon simulates latitude (lower = polar,
    // higher = tropical); azimuth controls the sunrise/sunset direction.
    private float _sunMaxElevationDeg = 50.0f;
    private float _sunAzimuthDeg = 180f;

    // Cascaded shadow map state.
    private readonly float _cameraNear;
    private float _shadowDistance;

    // Time of day and physically-based sky (atmosphere parameters are packed
    // into DeferredLightingData.SkyParams/SkyParams2, see Atmosphere.hlsli).
    private float _timeOfDay = 10.0f;
    private float _timeSpeed = 0.5f;
    private float _skyExposure = 1.0f;
    private float _rayleighScale = 1.0f;
    // A narrower, lower-energy Mie lobe keeps the sun visible without washing
    // the daylight sky toward white.  These values are intentionally paired:
    // raising g alone would make the forward peak much brighter.
    private float _mieScale = 0.3f;
    private float _miePhaseG = 0.9f;
    private float _starIntensity = 1.0f;
    private float _sunRadianceScale = 20.0f;
    private float _nightFloor = 0.05f;
    private float _ambientFloor = 0.25f;

    // HBAO+ screen-space ambient occlusion (computed from the G-buffer).
    private bool _hbaoEnabled = true;
    private float _hbaoRadius = 1.0f;
    private float _hbaoStrength = 1.0f;
    private RGNode_HBAO? _hbaoRenderer;

    // Voxel global illumination (sparse clipmap + cone tracing).
    private readonly RGNode_VoxelGI? _voxelGI;
    private readonly RGNode_SSR? _ssrRenderer;
    private bool _giEnabled = true;
    private float _giDiffuseStrength = 1.0f;
    private float _giSpecularStrength = 1f;
    private float _giSsaoAmount = 1f;
    private int _giResolutionPreset = 0;
    private int _ssrResolutionPreset = 0;
    private static readonly float[] GiTraceResolutionScales = [0.5f, 0.75f, 1.0f];
    private static readonly string[] GiTraceResolutionModes =
        ["Performance (50%)", "Balanced (75%)", "Quality (100%)"];

    // Material tweak panel.
    private int _selectedObject;
    private bool _animateObjects = true;

    // Bloom post-processing (a content processor node on the pipeline's forward
    // chain) and the emissive boost feeding it.
    // The Bistro emissive factors are all 1.0 and its emissive textures are LDR,
    // so without a boost nothing crosses the bloom threshold next to the sun.
    private RGNode_Bloom? _bloom;
    private float _emissiveBoost = 4.0f;

    // Point lights auto-generated from Bistro emissive surfaces.
    private bool _pointLightsEnabled = false;
    private float _pointLightIntensity = 0.5f;   // global multiplier on per-light base intensity
    private float _pointLightRangeScale = 3.0f;   // global multiplier on per-light range
    private PBRSceneEnvironment.PointLight[]? _bistroPointLights;         // base lights (unscaled)
    private PBRSceneEnvironment.PointLight[]? _pointLightUploadBuffer;    // scratch for per-frame scaling

    // HDR tone mapping node: switchable operator with per-type parameters.
    private RGNode_Tonemap? _tonemapStage;
    private TonemapType _tonemapType;

    // Shader hot-reload notification (brief on-screen message).
    private string? _shaderReloadNotice;
    private float _shaderReloadNoticeTimer;

    // Reusable zero-allocation string builder for ImGui text display.
    private readonly SpanStringBuilder _textBuilder = new(256);

    // Cached combo item arrays (avoid per-frame allocation).
    private static readonly string[] GiDebugModes =
        ["Off", "Diffuse Irradiance", "Indirect Specular", "GI Visibility",
         "Raw Diffuse Trace", "SSR Hit Confidence"];
    private string[]? _objectNames;

    // Screenshot mode.
    private readonly string? _screenshotPath;
    private readonly int _screenshotFrames;
    private readonly bool _waitForStreaming;
    private readonly Vector3? _fixedCameraPosition;
    private readonly Vector3? _fixedCameraLook;
    private int _frameCount;

    private float _time;

    public Game(GameEngineSetting setting, string[] args) : base(setting)
    {
        _screenshotPath = GetArgValue(args, "--screenshot=");
        _screenshotFrames = int.TryParse(GetArgValue(args, "--frames="), out int frames) ? frames : 60;
        _waitForStreaming = args.Contains("--wait-load");
        bool cascadeDebug = args.Contains("--cascade-debug");
        bool shadowDebug = args.Contains("--shadow-debug");
        _hbaoEnabled = !args.Contains("--no-hbao");
        bool hbaoDebugView = args.Contains("--hbao-debug");
        _giEnabled = !args.Contains("--no-gi");
        VoxelGiDebugMode giDebugView = default;
        if (Enum.TryParse<VoxelGiDebugMode>(GetArgValue(args, "--gi-debug="), ignoreCase: true, out var parsedDebug))
        {
            giDebugView = parsedDebug;
        }
        if (int.TryParse(GetArgValue(args, "--gi-resolution="), out int giResolutionPercent))
        {
            _giResolutionPreset = giResolutionPercent switch
            {
                50 => 0,
                75 => 1,
                100 => 2,
                _ => _giResolutionPreset,
            };
        }
        Vector3? sunOverride = ParseVector3(GetArgValue(args, "--sun="));
        if (sunOverride.HasValue)
        {
            _sunDirectionOverride = Vector3.Normalize(sunOverride.Value);
        }
        if (float.TryParse(GetArgValue(args, "--time="), out float timeOfDay))
        {
            _timeOfDay = timeOfDay;
        }
        if (float.TryParse(GetArgValue(args, "--time-speed="), out float timeSpeed))
        {
            _timeSpeed = timeSpeed;
        }
        if (float.TryParse(GetArgValue(args, "--sky-exposure="), out float skyExposure))
        {
            _skyExposure = skyExposure;
        }
        if (float.TryParse(GetArgValue(args, "--gi-diffuse="), out float giDiffuse))
        {
            _giDiffuseStrength = giDiffuse;
        }
        if (float.TryParse(GetArgValue(args, "--gi-specular="), out float giSpecular))
        {
            _giSpecularStrength = giSpecular;
        }

        _fixedCameraPosition = ParseVector3(GetArgValue(args, "--pos="));
        _fixedCameraLook = ParseVector3(GetArgValue(args, "--look="));
        bool interior = args.Contains("--interior");
        bool procedural = args.Contains("--procedural");

        // Load the Bistro scene; fall back to the procedural scene when absent.
        string bistroFile = interior ? "Bistro/BistroInterior.gltf" : "Bistro/BistroExterior.gltf";
        string failedReason = "the procedural scene was requested";
        if (!procedural && AssetSystem.TryLoad(bistroFile, out ModelScene? bistro, out failedReason))
        {
            _bistro = bistro;
            _sceneCenter = (bistro.BoundsMin + bistro.BoundsMax) * 0.5f;
            _sceneRadius = MathF.Max(Vector3.Distance(bistro.BoundsMax, bistro.BoundsMin) * 0.5f, 1.0f);
            _distance = _sceneRadius * 0.6f;
            _minDistance = _sceneRadius * 0.01f;
            _maxDistance = _sceneRadius * 5.0f;
            _yaw = 0.6f;
            _pitch = 0.12f;
            // Start in fly mode looking at the same view the orbit camera would give.
            _flySpeed = _sceneRadius * 0.25f;
            OrbitToFly();
            Console.WriteLine($"Loaded {bistroFile}: {bistro.DrawItems.Count} draw items, " +
                $"{bistro.Materials.Count} materials, bounds {bistro.BoundsMin} .. {bistro.BoundsMax}");
        }
        else
        {
            Console.WriteLine($"Bistro scene not loaded ({failedReason}); using procedural scene.");
        }

        _checkerTexture = CreateCheckerTexture(256);
        _cameraNear = MathF.Max(_sceneRadius * 0.002f, 0.1f);
        _camera = RenderingSystem.CreateCameraPerspective(0.83f, 16f / 9,
            _cameraNear, _sceneRadius * 10.0f);
        _shadowDistance = _sceneRadius * 3.0f;

        // Create the PBR deferred pipeline preset that drives the whole frame.
        _preset = RenderPipelines.CreatePBRDeferred(
            RenderingSystem,
            AssetSystem.Load<Shader>(BuiltInAssetsPath.Shader_PBRDeferredLighting),
            BuiltInAssets.Shader_Blit,
            shadowMapSize: 2048,
            width: (uint)MainView.Size.X,
            height: (uint)MainView.Size.Y,
            volumetricLightShader: BuiltInAssets.Shader_PBRVolumetricLight);
        _environment = _preset.Environment;
        _environment.VolumetricLightEnabled = true;

        _gbufferRenderer = new GBufferRenderer(
            RenderingSystem,
            BuiltInAssets.Shader_PBRGBuffer);

        _shadowRenderer = new ShadowRenderer(
            RenderingSystem,
            BuiltInAssets.Shader_PBRShadowDepth,
            _preset.ShadowLayout,
            _environment.ShadowDataBuffer);

        // Materials created by the renderer bind this camera; the sandbox
        // drives its own camera (RenderingSystem.MainCamera is not set by sandboxes).
        _environment.Camera = _camera;
        _gbufferRenderer.SetCamera(_camera);
        _preset.GBufferPass.Content.Add(_gbufferRenderer);
        _preset.ShadowPass.Content.Add(_shadowRenderer);
        _environment.ShadowCasterExtension = _sceneRadius;
        _environment.CascadeDebug = cascadeDebug;
        _environment.ShadowDebug = shadowDebug;
        _environment.AoDebugView = hbaoDebugView;

        // HBAO+ as a render plugin (decoupled from the pipeline): Attach wires its
        // graph node and the lighting AO input itself.
        if (_hbaoEnabled)
        {
            _hbaoRenderer = new RGNode_HBAO(
                RenderingSystem,
                AssetSystem.Load<Shader>("Shaders/Pipelines/Rendering/PBR/HBAO.hlsl"),
                AssetSystem.Load<Shader>("Shaders/Pipelines/Rendering/PBR/HBAOBlur.hlsl"));
            _hbaoRenderer.Attach(_preset.Graph, _preset.Lighting, _preset.GBufferResource, _environment);
        }

        // Forward transparency renderer for glass materials (after deferred lighting).
        _forwardRenderer = new RGNode_Forward(
            RenderingSystem,
            _preset.Graph,
            _preset.PostChain,
            AssetSystem.Load<Shader>("Shaders/Pipelines/Rendering/PBR/ForwardGlass.hlsl"),
            _environment.LightingDataBuffer,
            _environment.PointLightBuffer,
            _preset.ShadowMap);
        _forwardRenderer.SetCamera(_camera);
        _preset.Pipeline.Use(_forwardRenderer);

        // Volumetric clouds: a ray-marched Perlin-Worley cloud slab rendered at
        // half resolution and composited depth-aware over the HDR scene color
        // right after deferred lighting (before the volumetric light overlay,
        // whose near-camera shafts correctly add over the clouds). The lighting
        // pass also dims the direct sun from the plugin's cloud coverage bake,
        // so cloud shadows drift across the scene.
        if (!args.Contains("--no-clouds"))
        {
            float cloudResolutionScale = float.TryParse(GetArgValue(args, "--cloud-res="), out float parsedCloudRes)
                ? Math.Clamp(parsedCloudRes, 0.25f, 1.0f)
                : 0.5f;
            string shaderDir = "Shaders/Pipelines/Rendering/PBR/";
            _clouds = new RGNode_VolumetricClouds(
                RenderingSystem,
                AssetSystem.Load<Shader>(shaderDir + "VolumetricClouds.hlsl"),
                AssetSystem.Load<Shader>(shaderDir + "VolumetricCloudsComposite.hlsl"),
                AssetSystem.Load<Shader>(shaderDir + "VolumetricCloudNoise.hlsl"),
                AssetSystem.Load<Shader>(shaderDir + "VolumetricCloudShadow.hlsl"))
            {
                MarchResolutionScale = cloudResolutionScale,
            };
            if (float.TryParse(GetArgValue(args, "--cloud-coverage="), out float cloudCoverage))
            {
                _clouds.Coverage = Math.Clamp(cloudCoverage, 0.0f, 1.0f);
            }
            if (float.TryParse(GetArgValue(args, "--cloud-bottom="), out float cloudBottom))
            {
                _clouds.BottomAltitudeKm = cloudBottom;
            }
            if (float.TryParse(GetArgValue(args, "--cloud-thickness="), out float cloudThickness))
            {
                _clouds.ThicknessKm = cloudThickness;
            }
            if (int.TryParse(GetArgValue(args, "--cloud-steps="), out int cloudSteps))
            {
                _clouds.MaxMarchSteps = Math.Clamp(cloudSteps, 24, 200);
                _cloudStepsPreset = cloudSteps <= 64 ? 0 : cloudSteps <= 112 ? 1 : 2;
            }
            _clouds.Attach(
                _preset.Graph,
                _preset.PostChain,
                _preset.Lighting,
                _preset.GBufferResource,
                _preset.ShadowMapResource,
                _environment);
        }

        // Per-frame logic that runs between the G-buffer pass and the plugin pass
        // (HBAO/GI) is wired into the pipeline via AfterGBufferCallback so that
        // Render() drives the full frame internally.
        _preset.AfterGBuffer += () =>
        {
            SubmitDynamicInstances();
            SyncHbaoParams();
        };

        if (_bistro != null)
        {
            // One game-owned G-buffer material per glTF material. Textures still
            // streaming in start as the fallbacks and are synced in PrepareBistroFrame.
            _bistroMaterials = new GraphicsMaterial[_bistro.Materials.Count];
            // One cutout shadow material per glTF material so alpha-tested meshes
            // (foliage, fences, etc.) cast correctly shaped shadows.
            _bistroShadowMaterials = new GraphicsMaterial[_bistro.Materials.Count];
            // Glass materials for transparent BLEND glass (rendered in forward pass).
            _bistroGlassMaterials = new GraphicsMaterial[_bistro.Materials.Count];
            for (int i = 0; i < _bistroMaterials.Length; i++)
            {
                ModelMaterial material = _bistro.Materials[i];
                _bistroMaterials[i] = _gbufferRenderer.CreateMaterial(
                    material.AlbedoTexture, material.NormalTexture, material.MetallicRoughnessTexture,
                    material.EmissiveTexture, material.DoubleSided, $"bistro_{material.Name}");
                _bistroShadowMaterials[i] = _shadowRenderer.CreateShadowCutoutMaterial(
                    material.AlbedoTexture, material.DoubleSided, $"bistro_shadow_{material.Name}");
                _bistroGlassMaterials[i] = _forwardRenderer.CreateGlassMaterial(
                    material.AlbedoTexture, material.NormalTexture, material.MetallicRoughnessTexture,
                    material.EmissiveTexture, material.DoubleSided, $"bistro_glass_{material.Name}");
            }
            _bistroStreaming = !_bistro.LoadingCompletion.IsCompleted;

            // Register Bistro draw items: glass → forward renderer, everything else → GBuffer + shadow.
            {
                IReadOnlyList<ModelDrawItem> drawItems = _bistro.DrawItems;
                IReadOnlyList<ModelMaterial> materials = _bistro.Materials;
                for (int i = 0; i < drawItems.Count; i++)
                {
                    ModelDrawItem item = drawItems[i];
                    ModelMaterial material = materials[item.MaterialIndex];
                    if (IsGlassMaterial(material))
                    {
                        _forwardRenderer.Add(new BistroGlassRenderable(
                            item, material, _bistroGlassMaterials![item.MaterialIndex],
                            () => _glassTransmission));
                    }
                    else
                    {
                        // The emissive boost is resolved at bundle record time so
                        // the Point Lights toggle / Emissive Boost slider take
                        // effect on the next re-record (MarkStaticBundleDirty).
                        _gbufferRenderer.Add(new BistroRenderable(item, material, _bistroMaterials![item.MaterialIndex],
                            () => material.EmissiveFactor * (_pointLightsEnabled ? _emissiveBoost : 0.0f)));
                        _shadowRenderer.Add(new BistroShadowRenderable(item, material, _bistroShadowMaterials![item.MaterialIndex]));
                    }
                }
            }
            BuildBistroPointLights();
        }
        else
        {
            _cubeMesh = CreateCubeMesh();
            _sphereMesh = CreateSphereMesh(48, 24);
            _groundMesh = CreateGroundMesh(40, 10);
            BuildScene();
            _objectNames = _objects.Select(o => o.Mesh.Name).ToArray();
            _proceduralMaterial = _gbufferRenderer.CreateMaterial(_checkerTexture, null, null, null, name: "checker");
            _proceduralShadowMaterial = _shadowRenderer.CreateShadowMaterial(name: "checker_shadow");
            // Register all procedural objects with the GBufferRenderer and ShadowRenderer.
            foreach (SceneObject obj in _objects)
            {
                obj.GBufferMaterial = _proceduralMaterial;
                obj.ShadowMaterial = _proceduralShadowMaterial;
                _gbufferRenderer.Add(obj);
                _shadowRenderer.Add(obj);
            }
        }

        // Voxel global illumination: a 4-level clipmap whose coarsest level
        // covers roughly 4x the scene radius (128^3 voxels per level).
        if (_giEnabled)
        {
            float baseVoxelSize = MathF.Max(_sceneRadius * 4.0f / 1024.0f, 0.02f);
            string shaderDir = "Shaders/Pipelines/Rendering/PBR/";
            _voxelGI = new RGNode_VoxelGI(
                RenderingSystem,
                new VoxelGiShaders
                {
                    Clear = AssetSystem.Load<Shader>(shaderDir + "VoxelClear.hlsl"),
                    Voxelize = AssetSystem.Load<Shader>(shaderDir + "Voxelize.hlsl"),
                    Inject = AssetSystem.Load<Shader>(shaderDir + "VoxelInject.hlsl"),
                    Mip = AssetSystem.Load<Shader>(shaderDir + "VoxelMip.hlsl"),
                    MipChain = AssetSystem.Load<Shader>(shaderDir + "VoxelMipChain.hlsl"),
                    Propagate = AssetSystem.Load<Shader>(shaderDir + "VoxelPropagate.hlsl"),
                    Trace = AssetSystem.Load<Shader>(shaderDir + "VoxelTrace.hlsl"),
                    Demosaic = AssetSystem.Load<Shader>(shaderDir + "VoxelDemosaic.hlsl"),
                    Upsample = AssetSystem.Load<Shader>(shaderDir + "VoxelGiUpsample.hlsl"),
                },
                width: (uint)MainView.Size.X,
                height: (uint)MainView.Size.Y,
                resolution: 128,
                baseVoxelSize: baseVoxelSize,
                traceResolutionScale: GiTraceResolutionScales[_giResolutionPreset]);
            _voxelGI.DebugView = giDebugView;
            _voxelGI.SsrOnly = args.Contains("--ssr-only");
            RegisterVoxelMeshes();
            _voxelGI.Attach(_preset.Graph, _preset.Lighting, _preset.GBufferResource, _preset.ShadowMapResource, _environment);

            // Complementary-style SSR runs after deferred lighting and forward
            // transparency, so its hit color is the actual completed HDR scene.
            // The trace pass draws its stochastic samples from a blue-noise
            // tile baked once at runtime by ScreenSpaceReflectionBlueNoise.hlsl
            // (Heitz Owen-scrambled Sobol over an optimized scrambling table).
            _ssrRenderer = new RGNode_SSR(
                RenderingSystem,
                _preset.Graph,
                _preset.PostChain,
                _preset.GBufferResource,
                _preset.SceneColorResource,
                _voxelGI,
                _camera,
                _environment,
                AssetSystem.Load<Shader>(shaderDir + "ScreenSpaceReflectionTrace.hlsl"),
                AssetSystem.Load<Shader>(shaderDir + "ScreenSpaceReflectionResolve.hlsl"),
                AssetSystem.Load<Shader>(shaderDir + "ScreenSpaceReflectionComposite.hlsl"),
                BuiltInAssets.Shader_Blit,
                AssetSystem.Load<Shader>(shaderDir + "ScreenSpaceReflectionBlueNoise.hlsl"),
                (uint)MainView.Size.X,
                (uint)MainView.Size.Y,
                traceResolutionScale: GiTraceResolutionScales[_ssrResolutionPreset]);
            _ssrRenderer.Attach(_preset.FinalBlit);
        }

        // Bloom is a chain transform node on the pipeline's post chain;
        // registered before FXAA and tonemap, so boosted emissive surfaces get
        // a natural glow.
        float bloomThreshold = float.TryParse(GetArgValue(args, "--bloom-threshold="), out float parsedBloomThreshold)
            ? parsedBloomThreshold
            : 1.5f;
        float bloomIntensity = float.TryParse(GetArgValue(args, "--bloom-intensity="), out float parsedBloomIntensity)
            ? parsedBloomIntensity
            : 0.35f;
        _bloom = new RGNode_Bloom(
            RenderingSystem,
            _preset.Graph,
            _preset.PostChain,
            _preset.PostProcessLayout,
            RenderingSystem.CreateBloom(
                BuiltInAssets.Shader_BloomBlit,
                BuiltInAssets.Shader_BloomClamp,
                BuiltInAssets.Shader_BloomDownSample,
                BuiltInAssets.Shader_BloomUpSample,
                11),
            BuiltInAssets.Shader_Blit)
        {
            IsEnabled = !args.Contains("--no-bloom"),
            Threshold = bloomThreshold,
            Intensity = bloomIntensity,
        };
        _preset.Pipeline.Use(_bloom);

        // FXAA anti-aliasing node (registered between bloom and tonemap).
        _preset.Pipeline.Use(new RGNode_FXAA(
            _preset.Graph,
            _preset.PostChain,
            _preset.PostProcessLayout,
            RenderingSystem.CreateFXAA(
                BuiltInAssets.Shader_FXAA,
                BuiltInAssets.Shader_Blit)));

        // HDR tone mapping node (registered last, after bloom and FXAA).
        _tonemapStage = new RGNode_Tonemap(
            RenderingSystem,
            _preset.Graph,
            _preset.PostChain,
            _preset.PostProcessLayout,
            BuiltInAssets.Shader_Blit,
            BuiltInAssets.Shader_ReinhardLuminanceTonemap,
            BuiltInAssets.Shader_Uncharted2Tonemap,
            BuiltInAssets.Shader_FilmicTonemap,
            BuiltInAssets.Shader_ACESTonemap,
            BuiltInAssets.Shader_NeutralTonemap,
            BuiltInAssets.Shader_AgXTonemap);
        _preset.Pipeline.Use(_tonemapStage);

        MainPresenter.OnResize += OnMainWindowResize;

        AssetSystem.OnHotReload += OnShaderHotReload;
    }

    public override IEnumerable<IAssetLoader> CreateDefaultAssetLoaders()
    {
        foreach (var loader in base.CreateDefaultAssetLoaders())
        {
            yield return loader;
        }
        yield return new AssetLoaderModelGltf(RenderingSystem);
    }

    public override IEnumerable<IFileSource> CreateDefaultFileSources()
    {
        foreach (var fileSource in base.CreateDefaultFileSources())
        {
            yield return fileSource;
        }
        yield return new DirectoryWatcherFileSource(Utils.GetBuiltInAssetsPath(), AssetSystem);
        yield return new DirectoryWatcherFileSource(Utils.GetProjectAssetsPath(), AssetSystem);
    }

    protected override void OnStart()
    {
        AddSystem(new ImGUISystem(this));

        // Use ACES tone mapping with gamma 2.2.
        if (_tonemapStage != null)
        {
            _tonemapStage.Operator = TonemapType.ACES;
            _tonemapType = TonemapType.ACES;
            var acesData = _tonemapStage.ACESData;
            acesData.Gamma = 2.2f;
            _tonemapStage.ACESData = acesData;
        }
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        _time += delta;
        if (_shaderReloadNoticeTimer > 0)
        {
            _shaderReloadNoticeTimer -= delta;
        }
        _timeOfDay = (_timeOfDay + delta * _timeSpeed) % 24.0f;

        UpdateCamera(delta);
        if (_animateObjects)
        {
            AnimateObjects(delta);
        }

        UpdateLightingData();

        PrepareFrame();

        DrawImGuiPanel();

        DebugStats.Text(FrameRate);

        _frameCount++;

        // Disable the forward renderer when no glass is registered, so the
        // pipeline skips the forward pass depth-tested against the shared
        // G-buffer depth attachment.
        _forwardRenderer!.IsEnabled = _forwardRenderer.HasContent;

        // Render the frame and resolve it through the forward chain into the swapchain.
        _preset.Pipeline.Render(MainPresenter.FrameBuffer);

        // Capture here: after Render the forward render texture still holds the last
        // completed frame's HDR image. Bloom is composited into the swapchain by the
        // chain and is not part of the capture. With --wait-load the capture is held
        // back until the Bistro scene's asynchronously streaming textures have all arrived.
        if (_screenshotPath != null && _frameCount >= _screenshotFrames &&
            (!_waitForStreaming || _bistro == null || _bistro.LoadingCompletion.IsCompleted))
        {
            CaptureScreenshot(_screenshotPath);
            Stop();
        }
    }

    protected override void OnStop()
    {
        AssetSystem.OnHotReload -= OnShaderHotReload;
        // Pass content providers are not owned by the graph (see the Content
        // ownership note on RGNode_GeometryPass/RGNode_ShadowPass): dispose them here.
        _gbufferRenderer.Dispose();
        _shadowRenderer.Dispose();
        _preset.Dispose();
    }

    /// <summary>
    /// Called when any asset is hot-reloaded. For shaders, marks the static
    /// render bundles dirty so G-buffer / shadow / forward passes re-record
    /// with the freshly compiled pipeline on the next frame. Without this,
    /// only dynamic draws pick up the new shader (static bundles replay the
    /// old pipeline captured at record time).
    /// </summary>
    private void OnShaderHotReload(string filename, object cachedAsset)
    {
        if (cachedAsset is not Shader)
        {
            return;
        }

        _gbufferRenderer.MarkStaticBundleDirty();
        _shadowRenderer.MarkStaticBundleDirty();
        _forwardRenderer?.MarkStaticBundleDirty();

        string shaderName = Path.GetFileName(filename);
        _shaderReloadNotice = $"Shader reloaded: {shaderName}";
        _shaderReloadNoticeTimer = 3.0f;
        Console.WriteLine($"[Hot Reload] {shaderName}");
    }

    protected void OnMainWindowResize(uint2 size)
    {
        _camera.AspectRatio = (float)size.X / size.Y;
        // The pipeline resizes its own targets and its plugins (including VoxelGI).
        _preset.Pipeline.Resize(size.X, size.Y);
    }

    private static string? GetArgValue(string[] args, string prefix)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
            {
                return arg[prefix.Length..];
            }
        }
        return null;
    }

    private static Vector3? ParseVector3(string? value)
    {
        if (value == null)
        {
            return null;
        }
        string[] parts = value.Split(',');
        if (parts.Length != 3 ||
            !float.TryParse(parts[0], out float x) ||
            !float.TryParse(parts[1], out float y) ||
            !float.TryParse(parts[2], out float z))
        {
            return null;
        }
        return new Vector3(x, y, z);
    }

    private void UpdateCamera(float delta)
    {
        // Fixed camera from CLI args (--pos / --look) bypasses orbiting.
        if (_fixedCameraPosition.HasValue && _fixedCameraLook.HasValue)
        {
            Vector3 fixedLook = Vector3.Normalize(_fixedCameraLook.Value - _fixedCameraPosition.Value);
            _camera.Transform = new Transform3D(_fixedCameraPosition.Value, LookRotation(fixedLook, Vector3.UnitZ));
            _camera.UpdateMatrixToGPU();
            return;
        }

        if (Input.IsKeyDown(KeyCode.C))
        {
            if (_flyMode)
            {
                FlyToOrbit();
            }
            else
            {
                OrbitToFly();
            }
        }

        if (_flyMode)
        {
            UpdateFlyCamera(delta);
            return;
        }

        Input.IsCursorVisible = true;

        // Do not orbit/zoom while the mouse is over an ImGui window: dragging a
        // slider or scrolling the panel must not move the camera.
        bool mouseOverImGui = ImGUIInputHandler.IsCapturingMouse;

        if (!mouseOverImGui && Input.IsMousePressing(Mouse.Left))
        {
            Vector2 mouseDelta = Input.MouseDelta;
            _yaw -= mouseDelta.X * 0.008f;
            _pitch -= mouseDelta.Y * 0.008f;
        }

        if (!mouseOverImGui && Input.IsMouseScrolling(out Vector2 wheel))
        {
            _distance = Math.Clamp(_distance - wheel.Y * 0.8f * (_distance * 0.1f), _minDistance, _maxDistance);
        }

        // Keep the camera above the ground plane (z = 0): the pitch floor depends
        // on the orbit distance so the camera can still get low at far zoom but
        // never dips under the ground.
        float minPitch = MathF.Asin(MathF.Min(0.5f / _distance, 1.0f));
        _pitch = Math.Clamp(_pitch, minPitch, 1.45f);

        Vector3 cameraDirection = Direction(_pitch, _yaw);
        Vector3 cameraPosition = _sceneCenter + cameraDirection * _distance;
        Vector3 lookDirection = Vector3.Normalize(-cameraDirection);

        _camera.Transform = new Transform3D(cameraPosition, LookRotation(lookDirection, Vector3.UnitZ));
        _camera.UpdateMatrixToGPU();
    }

    /// <summary>Free-fly camera: hold the right mouse button to look around
    /// (the cursor hides and pins to the window center while held, release it
    /// to free the cursor for ImGui interaction), WASD moves along the view,
    /// E/Q or Space/Ctrl moves vertically, Shift speeds up, the wheel tunes
    /// the fly speed while looking.</summary>
    private void UpdateFlyCamera(float delta)
    {
        // Looking only happens while the right mouse button is held; otherwise
        // the cursor stays visible and free so ImGui can be operated normally.
        bool looking = MainView.IsFocused && Input.IsMousePressing(Mouse.Right);
        Input.IsCursorVisible = !looking;

        if (looking)
        {
            Vector2 mouseDelta = Input.MouseDelta;
            _yaw += mouseDelta.X * 0.008f;
            _pitch = Math.Clamp(_pitch - mouseDelta.Y * 0.008f, -1.55f, 1.55f);

            // Keep the OS cursor pinned at the window center so looking never hits
            // the screen edge; the input system keeps MouseDelta accurate across warps.
            int2 windowPosition = MainView.Position;
            uint2 windowSize = MainView.Size;
            Input.WarpMousePreservingDelta(new Vector2(
                windowPosition.X + windowSize.X * 0.5f,
                windowPosition.Y + windowSize.Y * 0.5f));
        }

        if (looking && Input.IsMouseScrolling(out Vector2 wheel))
        {
            _flySpeed = Math.Clamp(_flySpeed * MathF.Pow(1.2f, wheel.Y), _sceneRadius * 0.005f, _sceneRadius * 2.0f);
        }

        float speed = _flySpeed;
        if (Input.IsKeyPressing(KeyCode.ShiftLeft) || Input.IsKeyPressing(KeyCode.ShiftRight))
        {
            speed *= 4.0f;
        }

        Vector3 forward = Direction(_pitch, _yaw);
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, forward));
        Vector3 move = Vector3.Zero;
        if (Input.IsKeyPressing(KeyCode.W)) move += forward;
        if (Input.IsKeyPressing(KeyCode.S)) move -= forward;
        if (Input.IsKeyPressing(KeyCode.D)) move += right;
        if (Input.IsKeyPressing(KeyCode.A)) move -= right;
        if (Input.IsKeyPressing(KeyCode.E) || Input.IsKeyPressing(KeyCode.Space)) move += Vector3.UnitZ;
        if (Input.IsKeyPressing(KeyCode.Q) || Input.IsKeyPressing(KeyCode.ControlLeft)) move -= Vector3.UnitZ;
        if (move != Vector3.Zero)
        {
            _flyPosition += Vector3.Normalize(move) * speed * delta;
        }

        _camera.Transform = new Transform3D(_flyPosition, LookRotation(forward, Vector3.UnitZ));
        _camera.UpdateMatrixToGPU();
    }

    /// <summary>Switch from orbit to fly mode, keeping the current view.</summary>
    private void OrbitToFly()
    {
        _flyPosition = _sceneCenter + Direction(_pitch, _yaw) * _distance;
        _yaw += MathF.PI;
        _pitch = -_pitch;
        _flyMode = true;
    }

    /// <summary>Switch from fly to orbit mode, orbiting the point ahead of the camera.</summary>
    private void FlyToOrbit()
    {
        _sceneCenter = _flyPosition + Direction(_pitch, _yaw) * _distance;
        _distance = Math.Clamp(_distance, _minDistance, _maxDistance);
        _yaw += MathF.PI;
        _pitch = -_pitch;
        _flyMode = false;
    }

    /// <summary>Direction on the unit sphere: yaw around +Z, pitch above the XY plane.</summary>
    private static Vector3 Direction(float pitch, float yaw)
        => new(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch));

    private void AnimateObjects(float delta)
    {
        for (int i = 0; i < _objects.Count; i++)
        {
            SceneObject sceneObject = _objects[i];
            if (sceneObject.SpinSpeed != 0)
            {
                Vector3 axis = i % 2 == 0 ? Vector3.UnitZ : Vector3.UnitX;
                sceneObject.Transform.Rotation = Quaternion.CreateFromAxisAngle(axis, sceneObject.SpinSpeed * delta) * sceneObject.Transform.Rotation;
            }
            if (sceneObject.FloatSpeed != 0)
            {
                sceneObject.Transform.Position.Z = sceneObject.FloatPhase
                    + MathF.Sin(_time * sceneObject.FloatSpeed) * 0.6f;
            }
        }
    }

    /// <summary>
    /// Scan Bistro draw items for emissive materials and build point lights at
    /// their world-space centers. Light color, range and intensity are matched
    /// to the emissive material name (street lights, string lights, shop signs,
    /// ceiling lamps, etc.). Called once during initialization.
    /// </summary>
    private void BuildBistroPointLights()
    {
        if (_bistro == null)
        {
            return;
        }

        var lights = new List<PBRSceneEnvironment.PointLight>();
        IReadOnlyList<ModelDrawItem> drawItems = _bistro.DrawItems;
        IReadOnlyList<ModelMaterial> materials = _bistro.Materials;

        for (int i = 0; i < drawItems.Count; i++)
        {
            ModelDrawItem item = drawItems[i];
            ModelMaterial mat = materials[item.MaterialIndex];
            bool hasEmissive = mat.EmissiveFactor != Vector3.Zero || mat.EmissiveTexture != null;
            if (!hasEmissive)
            {
                continue;
            }

            Vector3 localCenter = (item.LocalBoundsMin + item.LocalBoundsMax) * 0.5f;
            Vector3 worldCenter = Vector3.Transform(localCenter, item.World);

            GetEmissiveLightParams(mat.Name, out Vector3 color, out float range, out float intensity);
            lights.Add(new PBRSceneEnvironment.PointLight(worldCenter, color, intensity, range));

            if (lights.Count >= PBRSceneEnvironment.MaxPointLights)
            {
                break;
            }
        }

        _bistroPointLights = lights.ToArray();
        _pointLightUploadBuffer = new PBRSceneEnvironment.PointLight[lights.Count];
    }

    /// <summary>
    /// Match a Bistro emissive material name to point light parameters.
    /// </summary>
    private static void GetEmissiveLightParams(string name, out Vector3 color, out float range, out float intensity)
    {
        // Large warm-white fixtures: street lights, spotlights, lanterns, interior lamps.
        if (name.Contains("StreetLight", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Spotlight", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Lantern", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Ceiling_Lamp", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("CeilingFan", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Wall_Light", StringComparison.OrdinalIgnoreCase))
        {
            color = new Vector3(1.0f, 0.85f, 0.6f);
            range = 6.0f;
            intensity = 15.0f;
        }
        else if (name.Contains("Orange", StringComparison.OrdinalIgnoreCase))
        {
            color = new Vector3(1.0f, 0.5f, 0.15f);
            range = 3.0f;
            intensity = 5.0f;
        }
        else if (name.Contains("Blue", StringComparison.OrdinalIgnoreCase))
        {
            color = new Vector3(0.3f, 0.55f, 1.0f);
            range = 3.0f;
            intensity = 5.0f;
        }
        else if (name.Contains("White", StringComparison.OrdinalIgnoreCase))
        {
            color = new Vector3(1.0f, 1.0f, 1.0f);
            range = 3.0f;
            intensity = 5.0f;
        }
        else if (name.Contains("Pink", StringComparison.OrdinalIgnoreCase))
        {
            color = new Vector3(1.0f, 0.4f, 0.6f);
            range = 3.0f;
            intensity = 5.0f;
        }
        else if (name.Contains("Red", StringComparison.OrdinalIgnoreCase))
        {
            color = new Vector3(1.0f, 0.2f, 0.15f);
            range = 3.0f;
            intensity = 5.0f;
        }
        else if (name.Contains("Green", StringComparison.OrdinalIgnoreCase))
        {
            color = new Vector3(0.2f, 1.0f, 0.3f);
            range = 3.0f;
            intensity = 5.0f;
        }
        else if (name.Contains("Shopsign", StringComparison.OrdinalIgnoreCase))
        {
            color = new Vector3(1.0f, 0.9f, 0.7f);
            range = 4.0f;
            intensity = 8.0f;
        }
        else
        {
            // Default: warm white.
            color = new Vector3(1.0f, 0.85f, 0.6f);
            range = 4.0f;
            intensity = 8.0f;
        }
    }

    private void UpdateLightingData()
    {
        // The sun orbits with the time of day (the CLI override fixes the
        // direction the light travels for debugging); its color and intensity
        // follow from the atmosphere transmittance and elevation.
        Vector3 directionToSun = _sunDirectionOverride.HasValue
            ? -_sunDirectionOverride.Value
            : ProceduralSkyUtility.GetDirectionToSun(_timeOfDay, _sunMaxElevationDeg, _sunAzimuthDeg * MathF.PI / 180.0f);
        Vector3 sunDirection = -directionToSun;
        Vector3 sunTint = ProceduralSkyUtility.GetSunColor(directionToSun);
        // Artistic warm tint: blend the physical transmittance toward a warm
        // sunlight color so noon light reads golden instead of neutral white.
        sunTint = Vector3.Lerp(sunTint, sunTint * new Vector3(1.0f, 0.80f, 0.55f), _sunWarmth);
        float sunScale = ProceduralSkyUtility.GetSunLightScale(directionToSun);
        ProceduralSkyUtility.GetSkyRadianceGradient(
            directionToSun,
            _rayleighScale,
            _mieScale,
            _skyExposure,
            _nightFloor,
            _sunRadianceScale,
            out Vector3 skyHorizonColor,
            out Vector3 skyZenithColor);

        // Scene-level lighting properties on the pipeline.
        _environment.SunDirection = sunDirection;
        _environment.SunColor = sunTint;
        _environment.SunIntensity = _sunIntensity * sunScale;
        _environment.SkyHorizonColor = skyHorizonColor;
        _environment.SkyZenithColor = skyZenithColor;
        _environment.SkyParams = new Vector4(_rayleighScale, _mieScale, _miePhaseG, _skyExposure);
        _environment.SkyParams2 = new Vector4(_starIntensity, _nightFloor, _sunRadianceScale, _ambientFloor);

        // Fit the shadow distance to the view: when the camera is far from the
        // scene (e.g. aerial views), extend past the configured base so visible
        // geometry never crosses the shadow range boundary — shadows would
        // otherwise fade/pop out at _shadowDistance while still on screen.
        _environment.ShadowDistance = Math.Max(_shadowDistance,
            Vector3.Distance(_camera.Transform.Position, _sceneCenter) + _sceneRadius);

        // Fit the shadow cascades to the camera frustum (PSSM splits).
        _environment.ComputeShadowCascades(_cameraNear);

        // Scale and upload point lights generated from Bistro emissive surfaces.
        int pointLightCount = 0;
        if (_pointLightsEnabled && _bistroPointLights != null && _bistroPointLights.Length > 0)
        {
            for (int i = 0; i < _bistroPointLights.Length; i++)
            {
                Vector4 ci = _bistroPointLights[i].ColorAndIntensity;
                _pointLightUploadBuffer![i] = _bistroPointLights[i];
                _pointLightUploadBuffer[i].ColorAndIntensity =
                    new Vector4(ci.X, ci.Y, ci.Z, ci.W * _pointLightIntensity);
                _pointLightUploadBuffer[i].Position.W *= _pointLightRangeScale;
            }
            pointLightCount = _bistroPointLights.Length;
        }
        _environment.UpdatePointLights(
            _pointLightUploadBuffer != null
                ? _pointLightUploadBuffer.AsSpan(0, pointLightCount)
                : ReadOnlySpan<PBRSceneEnvironment.PointLight>.Empty);

        // GI state on the pipeline.
        if (_voxelGI != null)
        {
            _environment.GiEnabled = _giEnabled;
            _environment.GiDiffuseStrength = _giDiffuseStrength;
            _environment.GiSpecularStrength = _giSpecularStrength;
            // Post-lighting SSR needs the normally shaded scene as its source.
            // Its own two debug modes are therefore resolved by the SSR node,
            // while the pre-lighting deferred debug mode stays disabled.
            _environment.GiDebugView = _voxelGI.DebugView is
                VoxelGiDebugMode.IndirectSpecular or VoxelGiDebugMode.SsrConfidence
                ? 0
                : (int)_voxelGI.DebugView;
            _voxelGI.EmissiveScale = _pointLightsEnabled ? _emissiveBoost : 0.0f;
            if (_ssrRenderer != null)
            {
                _ssrRenderer.IsEnabled = _giEnabled;
            }
        }
    }

    /// <summary>
    /// Per-frame render bookkeeping (texture streaming sync, stale bundle
    /// re-records). The passes themselves are driven by the engine through the
    /// main pipeline after OnUpdate: shadow → G-buffer → callback → plugins →
    /// lighting → forward transparency.
    /// </summary>
    private void PrepareFrame()
    {
        if (_bistro != null)
        {
            PrepareBistroFrame();
            return;
        }

        if (_staticShadowBundlesDirty)
        {
            _shadowRenderer.MarkStaticBundleDirty();
            _staticShadowBundlesDirty = false;
        }
    }

    /// <summary>
    /// The Bistro scene is fully static: every pass the pipeline runs is a pure
    /// bundle replay; only streaming and dirty bookkeeping happens here.
    /// </summary>
    private void PrepareBistroFrame()
    {
        // Textures stream in asynchronously: refresh the materials and re-record the
        // bundles until everything arrived (equivalent to drawing every frame), then
        // the bundles stay frozen for the rest of the session.
        if (_bistroStreaming)
        {
            SyncBistroMaterials();
            _shadowRenderer.MarkStaticBundleDirty();
            _gbufferRenderer.MarkStaticBundleDirty();
            _bistroStreaming = !_bistro!.LoadingCompletion.IsCompleted;
            if (!_bistroStreaming && _voxelGI != null)
            {
                // Texture instances may have been swapped while streaming:
                // re-register against the final textures.
                _voxelGI.ClearStaticInstances();
                RegisterVoxelMeshes();
            }
        }
        else if (_staticShadowBundlesDirty)
        {
            _shadowRenderer.MarkStaticBundleDirty();
            _staticShadowBundlesDirty = false;
        }
    }

    /// <summary>An object is static (baked into the render bundles) when it neither spins nor floats.</summary>
    private static bool IsStatic(SceneObject sceneObject)
    {
        return sceneObject.SpinSpeed == 0 && sceneObject.FloatSpeed == 0;
    }

    private static float GetAlphaCutoff(ModelMaterial material)
    {
        return material.AlphaMode switch
        {
            GltfAlphaMode.Mask => material.AlphaCutoff,
            GltfAlphaMode.Blend => 0.5f,
            _ => 0.0f,
        };
    }

    /// <summary>
    /// Identify whether a glTF material should be rendered as transparent glass
    /// (forward pass) rather than opaque/cutout (G-buffer pass). Only BLEND-mode
    /// materials whose name contains "Glass" are treated as glass.
    /// </summary>
    private static bool IsGlassMaterial(ModelMaterial material)
    {
        return material.AlphaMode == GltfAlphaMode.Blend &&
               material.Name.Contains("Glass", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Register the scene geometry into the voxel GI clipmap.</summary>
    private void RegisterVoxelMeshes()
    {
        if (_voxelGI == null)
        {
            return;
        }

        if (_bistro != null)
        {
            IReadOnlyList<ModelDrawItem> drawItems = _bistro.DrawItems;
            IReadOnlyList<ModelMaterial> materials = _bistro.Materials;
            for (int i = 0; i < drawItems.Count; i++)
            {
                ModelDrawItem item = drawItems[i];
                ModelMaterial material = materials[item.MaterialIndex];
                // The emissive factor is registered unboosted; the boost is a
                // runtime cbuffer scale at injection time.
                int meshHandle = _voxelGI.RegisterMesh(
                    item.Mesh,
                    (uint)VertexPBR.SizeInBytes,
                    new VoxelGiBounds(item.LocalBoundsMin, item.LocalBoundsMax),
                    material.AlbedoTexture,
                    material.EmissiveTexture);
                _voxelGI.AddStaticInstance(
                    meshHandle,
                    item.World,
                    material.BaseColorFactor,
                    material.EmissiveFactor,
                    GetAlphaCutoff(material));
            }
            return;
        }

        int sphereHandle = -1;
        int cubeHandle = -1;
        int groundHandle = -1;
        for (int i = 0; i < _objects.Count; i++)
        {
            SceneObject sceneObject = _objects[i];
            int meshHandle;
            if (sceneObject.Mesh == _sphereMesh)
            {
                sphereHandle = RegisterProceduralMeshOnce(sceneObject.Mesh, sphereHandle);
                meshHandle = sphereHandle;
            }
            else if (sceneObject.Mesh == _cubeMesh)
            {
                cubeHandle = RegisterProceduralMeshOnce(sceneObject.Mesh, cubeHandle);
                meshHandle = cubeHandle;
            }
            else
            {
                groundHandle = RegisterProceduralMeshOnce(sceneObject.Mesh, groundHandle);
                meshHandle = groundHandle;
            }
            sceneObject.VoxelMeshHandle = meshHandle;

            if (IsStatic(sceneObject))
            {
                sceneObject.VoxelStaticInstanceHandle = _voxelGI.AddStaticInstance(
                    meshHandle,
                    sceneObject.WorldMatrix,
                    new Vector4(sceneObject.BaseColor, 1.0f),
                    Vector3.Zero,
                    0.0f);
            }
        }

        int RegisterProceduralMeshOnce(PrimitiveMesh mesh, int existingHandle)
        {
            return existingHandle >= 0
                ? existingHandle
                : _voxelGI.RegisterMesh(
                    mesh,
                    (uint)VertexPBR.SizeInBytes,
                    GetProceduralBounds(mesh),
                    _checkerTexture,
                    null);
        }
    }

    /// <summary>Submit dynamic object instances to the voxel GI before plugin execution.</summary>
    private void SubmitDynamicInstances()
    {
        if (!_giEnabled || _voxelGI == null)
        {
            return;
        }
        for (int i = 0; i < _objects.Count; i++)
        {
            SceneObject sceneObject = _objects[i];
            if (!IsStatic(sceneObject) && sceneObject.VoxelMeshHandle >= 0)
            {
                _voxelGI.SubmitDynamicInstance(sceneObject.VoxelMeshHandle, sceneObject.WorldMatrix,
                    new Vector4(sceneObject.BaseColor, 1.0f), Vector3.Zero, 0.0f);
            }
        }
    }

    /// <summary>
    /// Sync HBAO user-tunable parameters (camera data is read automatically
    /// from the pipeline).
    /// </summary>
    private void SyncHbaoParams()
    {
        if (_hbaoRenderer != null)
        {
            float ssaoAmount = _giEnabled && _voxelGI != null ? _giSsaoAmount : 1.0f;
            _hbaoRenderer.Radius = MathF.Max(_hbaoRadius * ssaoAmount, 0.001f);
            _hbaoRenderer.Strength = (_hbaoEnabled ? _hbaoStrength : 0.0f) * ssaoAmount;
        }
    }

    /// <summary>Copy the current (possibly still streaming) Bistro textures into the materials.</summary>
    private void SyncBistroMaterials()
    {
        IReadOnlyList<ModelMaterial> materials = _bistro!.Materials;
        for (int i = 0; i < materials.Count; i++)
        {
            ModelMaterial material = materials[i];
            _gbufferRenderer.SetMaterialTextures(_bistroMaterials![i],
                material.AlbedoTexture, material.NormalTexture,
                material.MetallicRoughnessTexture, material.EmissiveTexture);
            _shadowRenderer.SetShadowCutoutMaterialTextures(_bistroShadowMaterials![i], material.AlbedoTexture);
            if (_bistroGlassMaterials != null)
            {
                _forwardRenderer?.SetGlassMaterialTextures(_bistroGlassMaterials[i],
                    material.AlbedoTexture, material.NormalTexture,
                    material.MetallicRoughnessTexture, material.EmissiveTexture);
            }
        }
        _forwardRenderer?.MarkStaticBundleDirty();
    }

    /// <summary>
    /// Read back the HDR scene texture, tonemap and save it as a PNG screenshot.
    /// </summary>
    private unsafe void CaptureScreenshot(string path)
    {
        Texture2D color = _preset.ForwardRenderTexture.ColorTextures[0];
        int width = (int)color.Width;
        int height = (int)color.Height;
        int pixelCount = width * height;

        // The HDR scene texture is RGBA16Float.
        var raw = new byte[pixelCount * 8];
        fixed (byte* rawPointer = raw)
        {
            GraphicsDevice.ReadTexture(color.NativeTexture, rawPointer, (uint)raw.Length);
        }

        var rgba = new byte[pixelCount * 4];
        for (int i = 0; i < pixelCount; i++)
        {
            for (int c = 0; c < 4; c++)
            {
                ushort bits = BitConverter.ToUInt16(raw, (i * 4 + c) * 2);
                float value = (float)BitConverter.UInt16BitsToHalf(bits);
                // Reinhard tonemap + sRGB encode, matching what the eye expects.
                value = value / (1.0f + value);
                value = value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1.0f / 2.4f) - 0.055f;
                rgba[i * 4 + c] = (byte)Math.Clamp(value * 255.0f + 0.5f, 0, 255);
            }
        }

        byte[] png = ImageEncodeUtility.EncodePng(rgba, width, height);
        File.WriteAllBytes(path, png);
        Console.WriteLine($"Screenshot saved to {path}");
        if (_voxelGI != null)
        {
            VoxelGiStatistics statistics = _voxelGI.Statistics;
            Console.WriteLine(
                $"GI stats: static={statistics.StaticResidentBricks}/{statistics.StaticCapacityBricks}, " +
                $"dynamic={statistics.DynamicResidentBricks}/{statistics.DynamicCapacityBricks}, " +
                $"queued={statistics.PendingStaticBricks}, dropped={statistics.DroppedBricks}, " +
                $"attribute={statistics.AttributeMemoryBytes / (1024.0 * 1024.0):F1}MiB, " +
                $"radiance={statistics.RadianceMemoryBytes / (1024.0 * 1024.0):F1}MiB");
        }
    }

    private void DrawImGuiPanel()
    {
        ImGui.Begin("PBR Deferred");

        if (_shaderReloadNoticeTimer > 0 && _shaderReloadNotice != null)
        {
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), _shaderReloadNotice);
            ImGui.Separator();
        }

        // Keep an exact camera description visible so visual GI regressions can
        // be reproduced with the existing --pos / --look screenshot arguments.
        // Transform3D cameras look along local +X in this sandbox.
        Vector3 cameraPosition = _camera.Transform.Position;
        Vector3 cameraForward = Vector3.Normalize(Vector3.Transform(
            Vector3.UnitX,
            _camera.Transform.Rotation));
        Vector3 cameraLookTarget = cameraPosition + cameraForward;
        ImGui.Text("Camera Repro (--pos / --look)");
        ImGui.Text(_textBuilder.Clear().Append("Position: ").Append(cameraPosition.X, "F4").Append(", ").Append(cameraPosition.Y, "F4").Append(", ").Append(cameraPosition.Z, "F4").AsReadOnlySpan());
        ImGui.Text(_textBuilder.Clear().Append("Forward:  ").Append(cameraForward.X, "F4").Append(", ").Append(cameraForward.Y, "F4").Append(", ").Append(cameraForward.Z, "F4").AsReadOnlySpan());
        ImGui.Text(_textBuilder.Clear().Append("Look:     ").Append(cameraLookTarget.X, "F4").Append(", ").Append(cameraLookTarget.Y, "F4").Append(", ").Append(cameraLookTarget.Z, "F4").AsReadOnlySpan());
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Sun Light"))
        {
            ImGui.SliderFloat("Intensity", ref _sunIntensity, 0.0f, 30.0f);
            ImGui.SliderFloat("Sun Warmth", ref _sunWarmth, 0.0f, 1.0f);
            bool shadowEnabled = _environment.ShadowEnabled;
            if (ImGui.Checkbox("Shadows", ref shadowEnabled))
                _environment.ShadowEnabled = shadowEnabled;
            ImGui.SliderFloat("Shadow Distance", ref _shadowDistance, _sceneRadius * 0.5f, _sceneRadius * 8.0f);
            bool cascadeDebug = _environment.CascadeDebug;
            if (ImGui.Checkbox("Cascade Debug", ref cascadeDebug))
                _environment.CascadeDebug = cascadeDebug;
            bool shadowDebug = _environment.ShadowDebug;
            if (ImGui.Checkbox("Shadow Debug", ref shadowDebug))
                _environment.ShadowDebug = shadowDebug;
            bool sunDiscEnabled = _environment.SunDiscEnabled;
            if (ImGui.Checkbox("Sun disc", ref sunDiscEnabled))
                _environment.SunDiscEnabled = sunDiscEnabled;
            float sunDiscSize = _environment.SunDiscSize;
            if (ImGui.SliderFloat("Sun Disc Size", ref sunDiscSize, 0.9990f, 0.99999f, "%.5f"))
                _environment.SunDiscSize = sunDiscSize;
            float sunDiscBrightness = _environment.SunDiscBrightness;
            if (ImGui.SliderFloat("Sun Disc Brightness", ref sunDiscBrightness, 0.0f, 60.0f))
                _environment.SunDiscBrightness = sunDiscBrightness;
        }

        if (ImGui.CollapsingHeader("Sky & Time"))
        {
            ImGui.SliderFloat("Time of Day", ref _timeOfDay, 0.0f, 24.0f);
            ImGui.SliderFloat("Time Speed", ref _timeSpeed, 0.0f, 4.0f);
            ImGui.SliderFloat("Sun Max Elevation (Latitude)", ref _sunMaxElevationDeg, 5.0f, 90.0f);
            ImGui.SliderFloat("Sun Azimuth (Sunrise Dir)", ref _sunAzimuthDeg, -180.0f, 180.0f);
            ImGui.SliderFloat("Sky Exposure", ref _skyExposure, 0.1f, 4.0f);
            ImGui.SliderFloat("Rayleigh Scale", ref _rayleighScale, 0.1f, 4.0f);
            ImGui.SliderFloat("Mie Scale (Haze)", ref _mieScale, 0.0f, 8.0f);
            ImGui.SliderFloat("Mie Phase G", ref _miePhaseG, 0.0f, 0.95f);
            ImGui.SliderFloat("Sun Radiance", ref _sunRadianceScale, 1.0f, 60.0f);
            ImGui.SliderFloat("Star Intensity", ref _starIntensity, 0.0f, 4.0f);
            ImGui.SliderFloat("Night Floor", ref _nightFloor, 0.0f, 0.5f, "%.4f");
            ImGui.SliderFloat("Ambient Floor", ref _ambientFloor, 0.0f, 10.0f);
        }

        if (ImGui.CollapsingHeader("Volumetric Light"))
        {
            bool vlEnabled = _environment.VolumetricLightEnabled;
            if (ImGui.Checkbox("Enabled", ref vlEnabled))
                _environment.VolumetricLightEnabled = vlEnabled;

            float vlIntensity = _environment.VolumetricLightIntensity;
            if (ImGui.SliderFloat("Intensity", ref vlIntensity, 0.0f, 4.0f))
                _environment.VolumetricLightIntensity = vlIntensity;
            float vlDensity = _environment.VolumetricLightDensity;
            if (ImGui.SliderFloat("Fog Density", ref vlDensity, 0.0f, 0.2f, "%.4f"))
                _environment.VolumetricLightDensity = vlDensity;
            float vlHeightScale = _environment.VolumetricLightHeightScale;
            if (ImGui.SliderFloat("Height Scale", ref vlHeightScale, 5.0f, 500.0f, "%.0f"))
                _environment.VolumetricLightHeightScale = vlHeightScale;
            float vlPhaseG = _environment.VolumetricLightPhaseG;
            if (ImGui.SliderFloat("Phase G", ref vlPhaseG, 0.0f, 0.95f))
                _environment.VolumetricLightPhaseG = vlPhaseG;
        }

        if (_clouds != null && ImGui.CollapsingHeader("Volumetric Clouds"))
        {
            bool cloudsEnabled = _clouds.IsEnabled;
            if (ImGui.Checkbox("Enabled", ref cloudsEnabled))
                _clouds.IsEnabled = cloudsEnabled;

            float coverage = _clouds.Coverage;
            if (ImGui.SliderFloat("Coverage", ref coverage, 0.0f, 1.0f))
                _clouds.Coverage = coverage;
            float cloudDensity = _clouds.Density;
            if (ImGui.SliderFloat("Density", ref cloudDensity, 0.1f, 3.0f))
                _clouds.Density = cloudDensity;
            float cloudBottom = _clouds.BottomAltitudeKm;
            if (ImGui.SliderFloat("Bottom Altitude (km)", ref cloudBottom, 0.4f, 6.0f))
                _clouds.BottomAltitudeKm = cloudBottom;
            float cloudThickness = _clouds.ThicknessKm;
            if (ImGui.SliderFloat("Thickness (km)", ref cloudThickness, 0.5f, 8.0f))
                _clouds.ThicknessKm = cloudThickness;
            float cloudDetail = _clouds.DetailStrength;
            if (ImGui.SliderFloat("Detail Erosion", ref cloudDetail, 0.0f, 1.0f))
                _clouds.DetailStrength = cloudDetail;
            float cloudExtinction = _clouds.ExtinctionPerKm;
            if (ImGui.SliderFloat("Extinction (1/km)", ref cloudExtinction, 2.0f, 48.0f))
                _clouds.ExtinctionPerKm = cloudExtinction;
            float cloudAmbient = _clouds.AmbientStrength;
            if (ImGui.SliderFloat("Ambient Strength", ref cloudAmbient, 0.0f, 3.0f))
                _clouds.AmbientStrength = cloudAmbient;
            float cloudSun = _clouds.SunStrength;
            if (ImGui.SliderFloat("Sun Strength", ref cloudSun, 0.0f, 2.5f))
                _clouds.SunStrength = cloudSun;
            float cloudWindDir = _clouds.WindDirectionDeg;
            if (ImGui.SliderFloat("Wind Heading (deg)", ref cloudWindDir, 0.0f, 360.0f))
                _clouds.WindDirectionDeg = cloudWindDir;
            float cloudWindSpeed = _clouds.WindSpeed;
            if (ImGui.SliderFloat("Wind Speed (m/s)", ref cloudWindSpeed, 0.0f, 40.0f))
                _clouds.WindSpeed = cloudWindSpeed;
            float cloudFadeStart = _clouds.AerialFadeStartKm;
            if (ImGui.SliderFloat("Aerial Fade Start (km)", ref cloudFadeStart, 4.0f, 30.0f))
                _clouds.AerialFadeStartKm = cloudFadeStart;
            float cloudFadeEnd = _clouds.AerialFadeEndKm;
            if (ImGui.SliderFloat("Aerial Fade End (km)", ref cloudFadeEnd, 10.0f, 42.0f))
                _clouds.AerialFadeEndKm = cloudFadeEnd;
            if (ImGui.Combo("March Steps", ref _cloudStepsPreset, CloudStepModes, CloudStepModes.Length))
                _clouds.MaxMarchSteps = CloudStepPresets[_cloudStepsPreset];
            float cloudShadow = _clouds.ShadowStrength;
            if (ImGui.SliderFloat("Cloud Shadow Strength", ref cloudShadow, 0.0f, 1.0f))
                _clouds.ShadowStrength = cloudShadow;
            float cloudShadowExtent = _clouds.ShadowExtentKm;
            if (ImGui.SliderFloat("Shadow Extent (km)", ref cloudShadowExtent, 4.0f, 40.0f))
                _clouds.ShadowExtentKm = cloudShadowExtent;
            bool cloudDebugOpacity = _clouds.DebugOpacityView;
            if (ImGui.Checkbox("Debug Opacity View", ref cloudDebugOpacity))
                _clouds.DebugOpacityView = cloudDebugOpacity;
        }

        if (_hbaoRenderer != null && ImGui.CollapsingHeader("Ambient Occlusion (HBAO+)"))
        {
            ImGui.Checkbox("AO Enabled", ref _hbaoEnabled);
            ImGui.SliderFloat("AO Radius", ref _hbaoRadius, 0.1f, MathF.Max(4.0f, _sceneRadius * 0.05f));
            ImGui.SliderFloat("AO Strength", ref _hbaoStrength, 0.0f, 1.0f);
            float intensity = _hbaoRenderer.Intensity;
            if (ImGui.SliderFloat("AO Power", ref intensity, 0.5f, 3.0f))
                _hbaoRenderer.Intensity = intensity;
            float bias = _hbaoRenderer.Bias;
            if (ImGui.SliderFloat("AO Bias", ref bias, 0.0f, 0.2f))
                _hbaoRenderer.Bias = bias;
            ImGui.SliderFloat("SSAO Amount With GI", ref _giSsaoAmount, 0.0f, 1.0f);
            bool aoDebugView = _environment.AoDebugView;
            if (ImGui.Checkbox("AO Debug View", ref aoDebugView))
                _environment.AoDebugView = aoDebugView;
        }

        if (_voxelGI != null && ImGui.CollapsingHeader("Global Illumination (Sparse Voxel Cone Tracing)"))
        {
            ImGui.Checkbox("GI Enabled", ref _giEnabled);
            ImGui.SliderFloat("GI Diffuse Strength", ref _giDiffuseStrength, 0.0f, 4.0f);
            ImGui.SliderFloat("GI Specular Strength", ref _giSpecularStrength, 0.0f, 4.0f);
            bool giSsrOnly = _voxelGI.SsrOnly;
            if (ImGui.Checkbox("SSR Only", ref giSsrOnly))
                _voxelGI.SsrOnly = giSsrOnly;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Disable the voxel specular-cone fallback so indirect specular contains only screen-space reflections.");
            if (_ssrRenderer != null)
            {
                float ssrMaxDistance = _ssrRenderer.MaxTraceDistance;
                if (ImGui.SliderFloat("SSR Max Trace Distance", ref ssrMaxDistance, 1.0f, 300.0f))
                    _ssrRenderer.MaxTraceDistance = ssrMaxDistance;
                float ssrRoughnessCutoff = _ssrRenderer.RoughnessCutoff;
                if (ImGui.SliderFloat("SSR Roughness Cutoff", ref ssrRoughnessCutoff, 0.05f, 1.0f))
                    _ssrRenderer.RoughnessCutoff = ssrRoughnessCutoff;
                if (ImGui.Combo(
                    "SSR Resolution",
                    ref _ssrResolutionPreset,
                    GiTraceResolutionModes,
                    GiTraceResolutionModes.Length))
                {
                    _ssrRenderer.TraceResolutionScale =
                        GiTraceResolutionScales[_ssrResolutionPreset];
                }
                ImGui.Text(_textBuilder.Clear().Append("SSR trace resolution: ").Append(_ssrRenderer.TraceWidth).Append('x').Append(_ssrRenderer.TraceHeight).AsReadOnlySpan());
            }
            float giSkyIntensity = _voxelGI.SkyIntensity;
            if (ImGui.SliderFloat("GI Sky Intensity", ref giSkyIntensity, 0.0f, 10.0f))
                _voxelGI.SkyIntensity = giSkyIntensity;
            float giMaxTraceDistance = _voxelGI.TraceMaxDistance;
            if (ImGui.SliderFloat("GI Max Trace Distance", ref giMaxTraceDistance, 1.0f, MathF.Max(4.0f, _sceneRadius * 2.0f)))
                _voxelGI.TraceMaxDistance = giMaxTraceDistance;
            float giDiffuseSpreading = _voxelGI.DiffuseSpreading;
            if (ImGui.SliderFloat("GI Diffuse Spreading", ref giDiffuseSpreading, 0.0f, 0.5f, "%.3f"))
                _voxelGI.DiffuseSpreading = giDiffuseSpreading;
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Diffuse spreading amount for the dual-kernel opacity bias.\nLowers cone elevation toward the tangent for stronger contact AO.");
            }
            if (ImGui.Combo(
                "GI Resolution",
                ref _giResolutionPreset,
                GiTraceResolutionModes,
                GiTraceResolutionModes.Length))
            {
                _voxelGI.TraceResolutionScale = GiTraceResolutionScales[_giResolutionPreset];
            }
            ImGui.Text(_textBuilder.Clear().Append("GI trace resolution: ").Append(_voxelGI.IndirectTexture.Width / 3).Append('x').Append(_voxelGI.IndirectTexture.Height).AsReadOnlySpan());
            int giDebugInt = (int)_voxelGI.DebugView;
            if (ImGui.Combo("GI Debug", ref giDebugInt, GiDebugModes, GiDebugModes.Length))
            {
                _voxelGI.DebugView = (VoxelGiDebugMode)giDebugInt;
            }
            float giRefreshRate = _voxelGI.VolumeRefreshRate;
            if (ImGui.SliderFloat("GI Refresh Rate", ref giRefreshRate, 0.0f, 240.0f, "%.0f Hz (0 = every frame)"))
            {
                _voxelGI.VolumeRefreshRate = giRefreshRate;
            }
            for (int giLevel = 0; giLevel < 4; giLevel++)
            {
                int giStaticBrickBudget = _voxelGI.GetStaticBrickBudget(giLevel);
                ReadOnlySpan<char> budgetLabel;
                if (giLevel == 0)
                    budgetLabel = "GI Brick Budget L0 (finest)";
                else if (giLevel == 3)
                    budgetLabel = "GI Brick Budget L3 (coarsest)";
                else
                    budgetLabel = _textBuilder.Clear().Append("GI Brick Budget L").Append(giLevel).AsReadOnlySpan();
                if (ImGui.SliderInt(budgetLabel, ref giStaticBrickBudget, 0, 256))
                {
                    _voxelGI.SetStaticBrickBudget(giLevel, giStaticBrickBudget);
                }
            }
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Max structural bricks voxelized per frame, per clipmap level.\n" +
                    "Dirty bricks beyond the budget stay queued and are amortized over\n" +
                    "subsequent frames (watch 'queued/updated' in the stats below).\n" +
                    "Lower values smooth frame spikes when the camera crosses brick\n" +
                    "boundaries; higher values voxelize newly-exposed geometry sooner.\n" +
                    "Coarser levels cover much more world space per brick, so they\n" +
                    "usually want a smaller budget than the fine levels.\n" +
                    "0 pauses structural voxelization for that level (its queue keeps growing).");
            }
            VoxelGiStatistics statistics = _voxelGI.Statistics;
            ImGui.Text(_textBuilder.Clear()
                .Append("Static bricks: ").Append(statistics.StaticResidentBricks).Append('/')
                .Append(statistics.StaticCapacityBricks).Append(" (")
                .Append(statistics.PendingStaticBricks).Append(" queued, ")
                .Append(statistics.StaticBricksUpdated).Append(" updated)").AsReadOnlySpan());
            ImGui.Text(_textBuilder.Clear()
                .Append("Dynamic bricks: ").Append(statistics.DynamicResidentBricks).Append('/')
                .Append(statistics.DynamicCapacityBricks).Append(" (")
                .Append(statistics.DynamicBricksUpdated).Append(" updated)").AsReadOnlySpan());
            ImGui.Text(_textBuilder.Clear().Append("Dropped bricks: ").Append(statistics.DroppedBricks).AsReadOnlySpan());
            float sparseRatio = statistics.DenseBrickTotal > 0
                ? 100.0f * statistics.SparseBrickTotal / statistics.DenseBrickTotal
                : 0.0f;
            ImGui.Text(_textBuilder.Clear()
                .Append("Sparse dispatch: ").Append(statistics.SparseBrickTotal).Append('/')
                .Append(statistics.DenseBrickTotal).Append(" bricks (")
                .Append(sparseRatio, "F1").Append("% of dense)").AsReadOnlySpan());
            ImGui.Text(_textBuilder.Clear()
                .Append("GI memory: attributes ").Append(statistics.AttributeMemoryBytes / (1024.0 * 1024.0), "F1").Append(" MiB, ")
                .Append("radiance ").Append(statistics.RadianceMemoryBytes / (1024.0 * 1024.0), "F1").Append(" MiB").AsReadOnlySpan());
        }

        if (_bloom != null && ImGui.CollapsingHeader("Emissive & Bloom"))
        {
            // The boosted emissive factor is baked into the static G-buffer bundle.
            if (ImGui.SliderFloat("Emissive Boost", ref _emissiveBoost, 0.0f, 20.0f))
            {
                _gbufferRenderer.MarkStaticBundleDirty();
            }
            bool bloomEnabled = _bloom.IsEnabled;
            if (ImGui.Checkbox("Bloom", ref bloomEnabled))
            {
                _bloom.IsEnabled = bloomEnabled;
            }
            float bloomThreshold = _bloom.Threshold;
            if (ImGui.SliderFloat("Bloom Threshold", ref bloomThreshold, 0.0f, 10.0f))
            {
                _bloom.Threshold = bloomThreshold;
            }
            float bloomIntensity = _bloom.Intensity;
            if (ImGui.SliderFloat("Bloom Intensity", ref bloomIntensity, 0.0f, 4.0f))
            {
                _bloom.Intensity = bloomIntensity;
            }
        }

        if (_bistroPointLights != null && ImGui.CollapsingHeader("Point Lights"))
        {
            if (ImGui.Checkbox("Enabled", ref _pointLightsEnabled))
            {
                _gbufferRenderer.MarkStaticBundleDirty();
            }
            ImGui.SliderFloat("Light Intensity", ref _pointLightIntensity, 0.0f, 5.0f);
            ImGui.SliderFloat("Light Range", ref _pointLightRangeScale, 0.1f, 3.0f);
            ImGui.Text(_textBuilder.Clear().Append("Lights: ").Append(_bistroPointLights.Length).Append(" / ").Append(PBRSceneEnvironment.MaxPointLights).AsReadOnlySpan());
        }

        if (_tonemapStage != null && ImGui.CollapsingHeader("Tone Mapping"))
        {
            if (ImGui.Combo("Tone Map Type", ref _tonemapType))
            {
                _tonemapStage.Operator = _tonemapType;
            }

            // Optional parameter controls depending on type
            switch (_tonemapType)
            {
                case TonemapType.Reinhard:
                    {
                        var d = _tonemapStage.ReinhardData;
                        if (ImGui.SliderFloat("Max Luminance", ref d.MaxLuminance, 0.1f, 10f) |
                            ImGui.SliderFloat("Gamma", ref d.Gamma, 0.5f, 3.0f))
                        {
                            _tonemapStage.ReinhardData = d;
                        }
                        break;
                    }
                case TonemapType.Uncharted2:
                    {
                        var d2 = _tonemapStage.Uncharted2Data;
                        if (ImGui.SliderFloat("Exposure", ref d2.Exposure, 0.1f, 4f) |
                            ImGui.SliderFloat("Gamma", ref d2.Gamma, 0.5f, 3.0f))
                        {
                            _tonemapStage.Uncharted2Data = d2;
                        }
                        break;
                    }
                case TonemapType.Filmic:
                    {
                        var df = _tonemapStage.FilmicData;
                        if (ImGui.SliderFloat("Exposure", ref df.Exposure, 0.1f, 4f) |
                            ImGui.SliderFloat("Gamma", ref df.Gamma, 0.5f, 3.0f))
                        {
                            _tonemapStage.FilmicData = df;
                        }
                        break;
                    }
                case TonemapType.ACES:
                    {
                        var da = _tonemapStage.ACESData;
                        if (ImGui.SliderFloat("Exposure", ref da.Exposure, 0.1f, 4f) |
                            ImGui.SliderFloat("Gamma", ref da.Gamma, 0.5f, 3.0f))
                        {
                            _tonemapStage.ACESData = da;
                        }
                        break;
                    }
                case TonemapType.Neutral:
                    {
                        var dn = _tonemapStage.NeutralData;
                        if (ImGui.SliderFloat("Exposure", ref dn.Exposure, 0.1f, 4f) |
                            ImGui.SliderFloat("Gamma", ref dn.Gamma, 0.5f, 3.0f) |
                            ImGui.SliderFloat("StartCompression", ref dn.StartCompression, 0.5f, 1f) |
                            ImGui.SliderFloat("Desaturation", ref dn.Desaturation, 0.0f, 4f))
                        {
                            _tonemapStage.NeutralData = dn;
                        }
                        break;
                    }
                case TonemapType.AgX:
                    {
                        var dag = _tonemapStage.AgXData;
                        int look = (int)dag.Look;
                        if (ImGui.SliderFloat("Exposure", ref dag.Exposure, 0.1f, 4f) |
                            ImGui.SliderFloat("Gamma", ref dag.Gamma, 0.5f, 3.0f) |
                            ImGui.Combo("Look", ref look, "Default\0Golden\0Punchy\0"))
                        {
                            dag.Look = look;
                            _tonemapStage.AgXData = dag;
                        }
                        break;
                    }
            }
        }

        RGNode_FXAA? fxaaStage = _preset.Pipeline.Get<RGNode_FXAA>();
        if (fxaaStage != null && ImGui.CollapsingHeader("FXAA"))
        {
            bool fxaaEnabled = fxaaStage.IsEnabled;
            if (ImGui.Checkbox("FXAA Enabled", ref fxaaEnabled))
            {
                fxaaStage.IsEnabled = fxaaEnabled;
            }
            float fxaaThreshold = fxaaStage.Threshold;
            if (ImGui.SliderFloat("Edge Threshold", ref fxaaThreshold, 0.063f, 0.333f))
            {
                fxaaStage.Threshold = fxaaThreshold;
            }
        }

        if (_bistro != null)
        {
            if (ImGui.CollapsingHeader("Bistro Scene"))
            {
                ImGui.Text(_textBuilder.Clear().Append(_bistro.DrawItems.Count).Append(" draw items, ").Append(_bistro.Materials.Count).Append(" materials").AsReadOnlySpan());
                ImGui.Text(_textBuilder.Clear().Append("bounds min ").Append(_bistro.BoundsMin).AsReadOnlySpan());
                ImGui.Text(_textBuilder.Clear().Append("bounds max ").Append(_bistro.BoundsMax).AsReadOnlySpan());
            }

            if (ImGui.CollapsingHeader("Glass Transparency"))
            {
                if (ImGui.Checkbox("Glass Enabled", ref _glassEnabled))
                {
                    _forwardRenderer?.MarkStaticBundleDirty();
                }
                if (ImGui.SliderFloat("Transmission", ref _glassTransmission, 0.0f, 1.0f))
                {
                    _forwardRenderer?.MarkStaticBundleDirty();
                }
            }
        }
        else
        {
            if (ImGui.CollapsingHeader("Material"))
            {
                ImGui.Combo("Object", ref _selectedObject, _objectNames!, _objectNames!.Length);
                _selectedObject = Math.Clamp(_selectedObject, 0, _objects.Count - 1);

                SceneObject sceneObject = _objects[_selectedObject];
                Vector3 baseColor = sceneObject.BaseColor;
                bool bakedChanged = false;
                if (ImGui.ColorEdit3("Base Color", ref baseColor))
                {
                    sceneObject.BaseColor = baseColor;
                    bakedChanged = true;
                }
                bakedChanged |= ImGui.SliderFloat("Metallic", ref sceneObject.Metallic, 0.0f, 1.0f);
                bakedChanged |= ImGui.SliderFloat("Roughness", ref sceneObject.Roughness, 0.0f, 1.0f);
                bakedChanged |= ImGui.SliderFloat("AO", ref sceneObject.AmbientOcclusion, 0.0f, 1.0f);
                bakedChanged |= ImGui.Checkbox("Cast Shadow", ref sceneObject.CastsShadow);
                // Static objects are baked into the render bundles: schedule a re-record.
                if (bakedChanged && IsStatic(sceneObject))
                {
                    _staticShadowBundlesDirty = true;
                    _gbufferRenderer.MarkStaticBundleDirty();
                    if (sceneObject.VoxelStaticInstanceHandle >= 0)
                    {
                        _voxelGI?.UpdateStaticInstance(
                            sceneObject.VoxelStaticInstanceHandle,
                            sceneObject.WorldMatrix,
                            new Vector4(sceneObject.BaseColor, 1.0f),
                            Vector3.Zero,
                            0.0f);
                    }
                }
            }

            ImGui.Separator();
            ImGui.Checkbox("Animate Objects", ref _animateObjects);
        }

        if (ImGui.CollapsingHeader("Render Profiler"))
        {
            ref readonly RenderProfileSnapshot snapshot = ref _preset.Profiler.GetSnapshot();
            if (snapshot.Count == 0)
            {
                ImGui.TextDisabled("No profiling data yet.");
            }
            else
            {
                if (ImGui.Button("Copy to Clipboard"))
                {
                    ImGui.SetClipboardText(BuildProfilerClipboardText(in snapshot));
                }

                // Group counters by their Group label, rendering each group as
                // a sub-section. The snapshot arrays are pre-sorted by registration
                // order (pipeline first, then plugins), so same-group entries are
                // already contiguous.
                string currentGroup = null!;
                for (int i = 0; i < snapshot.Count; i++)
                {
                    string group = snapshot.Groups[i];
                    if (group != currentGroup)
                    {
                        if (i > 0)
                        {
                            ImGui.Spacing();
                        }
                        ImGui.TextDisabled(group);
                        currentGroup = group;
                    }

                    double ms = snapshot.Values[i];
                    ImGui.Text(_textBuilder.Clear()
                        .Append("  ").Append(snapshot.Names[i]).Append("  ")
                        .Append(ms, "F3").Append(" ms").AsReadOnlySpan());
                }
            }
        }

        ImGui.End();
    }

    /// <summary>
    /// Formats a profiler snapshot as grouped plain text (one counter per line,
    /// "Group / Name / milliseconds") for the clipboard.
    /// </summary>
    /// <param name="snapshot">The published snapshot to format.</param>
    /// <returns>The clipboard text.</returns>
    private string BuildProfilerClipboardText(in RenderProfileSnapshot snapshot)
    {
        _textBuilder.Clear();
        _textBuilder.Append("Render Profiler @ frame ").Append(_frameCount).AppendLine();
        string currentGroup = null!;
        for (int i = 0; i < snapshot.Count; i++)
        {
            string group = snapshot.Groups[i];
            if (group != currentGroup)
            {
                _textBuilder.Append('[').Append(group).AppendLine("]");
                currentGroup = group;
            }
            _textBuilder.Append("  ").Append(snapshot.Names[i]).Append(" = ")
                .Append(snapshot.Values[i], "F3").AppendLine(" ms");
        }
        return _textBuilder.ToString();
    }

    private void BuildScene()
    {
        // Ground.
        _objects.Add(new SceneObject
        {
            Mesh = _groundMesh!,
            BaseColor = Vector3.One,
            Metallic = 0.0f,
            Roughness = 0.85f,
            AmbientOcclusion = 1.0f,
        });
        // Material variety: gold / mirror / rough red / plastic / copper / dark metal / ceramic / green.
        _objects.Add(new SceneObject
        {
            Mesh = _sphereMesh!,
            Transform = new Transform3D(new Vector3(-3.5f, -2.5f, 1.6f), Quaternion.Identity, new Vector3(1.6f)),
            BaseColor = new Vector3(1.0f, 0.766f, 0.336f),
            Metallic = 1.0f,
            Roughness = 0.25f,
            SpinSpeed = 0.5f,
        });
        _objects.Add(new SceneObject
        {
            Mesh = _sphereMesh!,
            Transform = new Transform3D(new Vector3(3.5f, -2.5f, 1.6f), Quaternion.Identity, new Vector3(1.6f)),
            BaseColor = new Vector3(0.92f, 0.93f, 0.95f),
            Metallic = 1.0f,
            Roughness = 0.05f,
            SpinSpeed = -0.4f,
        });
        _objects.Add(new SceneObject
        {
            Mesh = _sphereMesh!,
            Transform = new Transform3D(new Vector3(3.5f, 2.5f, 1.6f), Quaternion.Identity, new Vector3(1.6f)),
            BaseColor = new Vector3(0.75f, 0.1f, 0.12f),
            Metallic = 0.05f,
            Roughness = 0.85f,
            FloatSpeed = 1.2f,
            FloatPhase = 1.6f,
        });
        _objects.Add(new SceneObject
        {
            Mesh = _sphereMesh!,
            Transform = new Transform3D(new Vector3(-3.5f, 2.5f, 1.6f), Quaternion.Identity, new Vector3(1.6f)),
            BaseColor = new Vector3(0.9f, 0.9f, 0.9f),
            Metallic = 0.0f,
            Roughness = 0.55f,
            FloatSpeed = 0.9f,
            FloatPhase = 1.6f,
        });
        _objects.Add(new SceneObject
        {
            Mesh = _cubeMesh!,
            Transform = new Transform3D(new Vector3(-5.5f, 0, 0.9f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.4f), new Vector3(1.8f, 1.8f, 1.8f)),
            BaseColor = new Vector3(0.1f, 0.35f, 0.8f),
            Metallic = 0.0f,
            Roughness = 0.35f,
            SpinSpeed = 0.6f,
        });
        _objects.Add(new SceneObject
        {
            Mesh = _cubeMesh!,
            Transform = new Transform3D(new Vector3(5.5f, 0, 0.9f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f), new Vector3(1.8f, 1.8f, 1.8f)),
            BaseColor = new Vector3(0.85f, 0.45f, 0.2f),
            Metallic = 0.95f,
            Roughness = 0.3f,
            SpinSpeed = -0.5f,
        });
        _objects.Add(new SceneObject
        {
            Mesh = _cubeMesh!,
            Transform = new Transform3D(new Vector3(-1.8f, -5.5f, 0.9f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f), new Vector3(1.8f, 1.8f, 1.8f)),
            BaseColor = new Vector3(0.2f, 0.6f, 0.2f),
            Metallic = 0.0f,
            Roughness = 0.9f,
        });
        _objects.Add(new SceneObject
        {
            Mesh = _cubeMesh!,
            Transform = new Transform3D(new Vector3(-1.8f, 5.5f, 0.9f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.4f), new Vector3(1.8f, 1.8f, 1.8f)),
            BaseColor = new Vector3(0.4f, 0.4f, 0.45f),
            Metallic = 0.9f,
            Roughness = 0.7f,
        });
        _objects.Add(new SceneObject
        {
            Mesh = _sphereMesh!,
            Transform = new Transform3D(new Vector3(0, 0, 2.6f), Quaternion.Identity, new Vector3(2.6f)),
            BaseColor = new Vector3(0.25f, 0.3f, 0.35f),
            Metallic = 0.85f,
            Roughness = 0.45f,
            CastsShadow = false,
        });
    }

    private Texture2D CreateCheckerTexture(int size)
    {
        const int tileSize = 16;
        byte[] data = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool light = ((x / tileSize) + (y / tileSize)) % 2 == 0;
                byte value = light ? (byte)210 : (byte)105;
                int index = (y * size + x) * 4;
                data[index] = value;
                data[index + 1] = value;
                data[index + 2] = value;
                data[index + 3] = 255;
            }
        }
        return RenderingSystem.CreateTexture2D(data, (uint)size, (uint)size,
            new ImageLoadOption(format: PixelFormat.RGBA8UnormSrgb, addressMode: AddressMode.Repeat, filterMode: FilterMode.Linear, name: "checker_albedo"));
    }

    private VoxelGiBounds GetProceduralBounds(PrimitiveMesh mesh)
    {
        if (mesh == _cubeMesh)
        {
            return new VoxelGiBounds(new Vector3(-0.5f), new Vector3(0.5f));
        }
        if (mesh == _sphereMesh)
        {
            return new VoxelGiBounds(new Vector3(-1.0f), new Vector3(1.0f));
        }
        return new VoxelGiBounds(new Vector3(-20.0f, -20.0f, 0.0f), new Vector3(20.0f, 20.0f, 0.0f));
    }

    private PrimitiveMesh CreateCubeMesh()
    {
        // 24 vertices, one quad per face, outward normals, CCW winding.
        Span<VertexPBR> vertices = stackalloc VertexPBR[24];
        Span<ushort> indices = stackalloc ushort[36];

        // face data: normal, tangent axis a, tangent axis b (a x b == normal)
        Span<Vector3> normals = stackalloc Vector3[]
        {
            new(0, 0, 1), new(0, 0, -1),
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
        };
        Span<Vector3> aAxes = stackalloc Vector3[]
        {
            new(1, 0, 0), new(1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
            new(0, 0, 1), new(0, 0, -1),
        };
        Span<Vector3> bAxes = stackalloc Vector3[]
        {
            new(0, 1, 0), new(0, -1, 0),
            new(0, 0, 1), new(0, 0, 1),
            new(1, 0, 0), new(1, 0, 0),
        };

        int vertexIndex = 0;
        int indexIndex = 0;
        for (int face = 0; face < 6; face++)
        {
            Vector3 normal = normals[face];
            Vector3 a = aAxes[face];
            Vector3 b = bAxes[face];
            Vector3 center = normal * 0.5f;
            Vector3 aHalf = a * 0.5f;
            Vector3 bHalf = b * 0.5f;

            Span<Vector2> uvs = stackalloc Vector2[]
            {
                new(0, 0), new(1, 0), new(1, 1), new(0, 1),
            };
            Span<Vector3> corners = stackalloc Vector3[]
            {
                center - aHalf - bHalf, center + aHalf - bHalf, center + aHalf + bHalf, center - aHalf + bHalf,
            };

            for (int i = 0; i < 4; i++)
            {
                vertices[vertexIndex] = new VertexPBR(corners[i], normal, uvs[i], new Vector4(a, 1));
                vertexIndex++;
            }

            indices[indexIndex++] = (ushort)(face * 4 + 0);
            indices[indexIndex++] = (ushort)(face * 4 + 1);
            indices[indexIndex++] = (ushort)(face * 4 + 2);
            indices[indexIndex++] = (ushort)(face * 4 + 0);
            indices[indexIndex++] = (ushort)(face * 4 + 2);
            indices[indexIndex++] = (ushort)(face * 4 + 3);
        }

        return RenderingSystem.CreatePrimitiveMesh(vertices, indices, "cube");
    }

    private PrimitiveMesh CreateSphereMesh(int segmentsU, int segmentsV)
    {
        int vertexCount = (segmentsU + 1) * (segmentsV + 1);
        int indexCount = segmentsU * segmentsV * 6;
        VertexPBR[] vertices = new VertexPBR[vertexCount];
        ushort[] indices = new ushort[indexCount];

        int vertexIndex = 0;
        for (int v = 0; v <= segmentsV; v++)
        {
            float phi = v * MathF.PI / segmentsV; // 0 at +Z pole to PI at -Z pole
            float sinPhi = MathF.Sin(phi);
            float cosPhi = MathF.Cos(phi);
            for (int u = 0; u <= segmentsU; u++)
            {
                float theta = u * (MathF.PI * 2.0f) / segmentsU;
                Vector3 position = new(
                    sinPhi * MathF.Cos(theta),
                    sinPhi * MathF.Sin(theta),
                    cosPhi);
                // Tangent follows the latitude line (∂P/∂θ), w=1 for standard handedness.
                Vector3 tangent = new(-sinPhi * MathF.Sin(theta), sinPhi * MathF.Cos(theta), 0.0f);
                vertices[vertexIndex++] = new VertexPBR(position, position, new Vector2((float)u / segmentsU, (float)v / segmentsV), new Vector4(tangent, 1));
            }
        }

        int indexIndex = 0;
        for (int v = 0; v < segmentsV; v++)
        {
            for (int u = 0; u < segmentsU; u++)
            {
                int i0 = v * (segmentsU + 1) + u;
                int i1 = i0 + 1;
                int i2 = i0 + (segmentsU + 1);
                int i3 = i2 + 1;
                // CCW when viewed from outside.
                indices[indexIndex++] = (ushort)i0;
                indices[indexIndex++] = (ushort)i2;
                indices[indexIndex++] = (ushort)i1;
                indices[indexIndex++] = (ushort)i1;
                indices[indexIndex++] = (ushort)i2;
                indices[indexIndex++] = (ushort)i3;
            }
        }

        return RenderingSystem.CreatePrimitiveMesh(vertices, indices, "sphere");
    }

    private PrimitiveMesh CreateGroundMesh(float size, int segments)
    {
        int vertexCount = (segments + 1) * (segments + 1);
        int indexCount = segments * segments * 6;
        VertexPBR[] vertices = new VertexPBR[vertexCount];
        ushort[] indices = new ushort[indexCount];

        const float uvTiles = 4.0f;
        int vertexIndex = 0;
        for (int j = 0; j <= segments; j++)
        {
            for (int i = 0; i <= segments; i++)
            {
                float x = (float)i / segments * size - size * 0.5f;
                float y = (float)j / segments * size - size * 0.5f;
                vertices[vertexIndex++] = new VertexPBR(
                    new Vector3(x, y, 0),
                    Vector3.UnitZ,
                    new Vector2((float)i / segments * uvTiles, (float)j / segments * uvTiles));
            }
        }

        int indexIndex = 0;
        for (int j = 0; j < segments; j++)
        {
            for (int i = 0; i < segments; i++)
            {
                int i0 = j * (segments + 1) + i;
                int i1 = i0 + 1;
                int i2 = i0 + (segments + 1);
                int i3 = i2 + 1;
                // CCW when viewed from +Z.
                indices[indexIndex++] = (ushort)i0;
                indices[indexIndex++] = (ushort)i1;
                indices[indexIndex++] = (ushort)i2;
                indices[indexIndex++] = (ushort)i1;
                indices[indexIndex++] = (ushort)i3;
                indices[indexIndex++] = (ushort)i2;
            }
        }

        return RenderingSystem.CreatePrimitiveMesh(vertices, indices, "ground");
    }

    // Build a quaternion so that Transform(UnitX, q) == forward and the up axis
    // stays as close to worldUp as the forward direction allows. This is the
    // engine's equivalent of a LookAt rotation for a +X-forward / +Z-up camera.
    private static Quaternion LookRotation(Vector3 forward, Vector3 worldUp)
    {
        forward = Vector3.Normalize(forward);
        Vector3 up = Vector3.Normalize(worldUp - forward * Vector3.Dot(forward, worldUp));
        Vector3 right = Vector3.Cross(up, forward);
        Matrix4x4 m = new(
            forward.X, forward.Y, forward.Z, 0,
            right.X, right.Y, right.Z, 0,
            up.X, up.Y, up.Z, 0,
            0, 0, 0, 1);
        return Quaternion.CreateFromRotationMatrix(m);
    }
}
