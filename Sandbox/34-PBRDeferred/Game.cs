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
/// clipmap with compute voxelization, light injection and cone tracing).
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
/// <br/>CLI: --screenshot=&lt;path.png&gt; [--frames=N] [--interior] [--cascade-debug] [--sun=x,y,z] [--time=H] [--time-speed=S] [--no-hbao] [--hbao-debug] [--no-gi] [--gi-debug=N]
/// </summary>
public class Game : GameEngine
{
    /// <summary>A PBR scene object: mesh, transform and surface parameters.</summary>
    private sealed class SceneObject
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

        /// <summary>The static voxel registration handle (-1 when not registered static).</summary>
        public int VoxelStaticHandle = -1;
        /// <summary>The dynamic voxel mesh handle shared by instances of this mesh (-1 when unregistered).</summary>
        public int VoxelDynamicHandle = -1;

        public Matrix4x4 WorldMatrix => Transform.Matrix;
    }

    private readonly PBRDeferredPipeline _pipeline;
    private readonly CameraPerspectiveBuffer _camera;
    private readonly PrimitiveMesh? _cubeMesh;
    private readonly PrimitiveMesh? _sphereMesh;
    private readonly PrimitiveMesh? _groundMesh;
    private readonly Texture2D _checkerTexture;

    private readonly List<SceneObject> _objects = new();
    private PBRDeferredPipeline.DeferredLightingData _lightingData = new();

    // Scene materials owned by the game (created via the pipeline's material factory).
    private GraphicsMaterial? _proceduralMaterial;
    private GraphicsMaterial[]? _bistroMaterials;

    // Static geometry recorded once into render bundles and replayed every frame
    // (re-recorded while Bistro textures stream in, or when baked parameters change).
    private readonly SubRenderContext[] _staticShadowBundles = new SubRenderContext[PBRDeferredPipeline.ShadowCascadeCount];
    private SubRenderContext? _staticGBufferBundle;
    private bool _staticBundlesDirty;
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
    // the atmosphere transmittance tint from ProceduralSkyUtility.
    private readonly Vector3? _sunDirectionOverride;
    private float _sunIntensity = 8.0f;
    private bool _shadowEnabled = true;
    private bool _sunDiscEnabled = true;

    // Cascaded shadow map state.
    private readonly float _cameraNear;
    private float _shadowDistance;
    private bool _cascadeDebug;
    private bool _shadowDebug;
    private readonly Matrix4x4[] _cascadeViewProjections = new Matrix4x4[PBRDeferredPipeline.ShadowCascadeCount];
    private readonly float[] _cascadeSplits = new float[PBRDeferredPipeline.ShadowCascadeCount];
    private readonly float[] _cascadeTexelSizes = new float[PBRDeferredPipeline.ShadowCascadeCount];

    // Time of day and physically-based sky (atmosphere parameters are packed
    // into DeferredLightingData.SkyParams/SkyParams2, see Atmosphere.hlsli).
    private float _timeOfDay = 10.0f;
    private float _timeSpeed = 0.5f;
    private float _skyExposure = 1.0f;
    private float _rayleighScale = 1.0f;
    private float _mieScale = 1.0f;
    private float _miePhaseG = 0.8f;
    private float _starIntensity = 1.0f;
    private float _sunRadianceScale = 20.0f;
    private float _nightFloor = 0.003f;

    // HBAO+ screen-space ambient occlusion (computed from the G-buffer).
    private const float HBAOMaxStepPixels = 64.0f;
    private bool _hbaoEnabled = true;
    private bool _hbaoDebugView;
    private float _hbaoRadius = 1.0f;
    private float _hbaoStrength = 1.0f;
    private float _hbaoIntensity = 1.2f;
    private float _hbaoBias = 0.02f;
    private HbaoRenderer.HbaoData _hbaoData = new();

    // Voxel global illumination (cascaded voxel clipmap + cone tracing).
    private readonly VoxelGiRenderer? _voxelGI;
    private VoxelGiRenderer.VoxelGiData _voxelData = new();
    private bool _giEnabled = true;
    private float _giDiffuseStrength = 1.0f;
    private float _giSpecularStrength = 1.0f;
    private float _giMaxTraceDistance = 20.0f;
    private int _giDebugView;

    // Material tweak panel.
    private int _selectedObject;
    private bool _animateObjects = true;

    // Bloom post-processing (engine built-in) and the emissive boost feeding it.
    // The Bistro emissive factors are all 1.0 and its emissive textures are LDR,
    // so without a boost nothing crosses the bloom threshold next to the sun.
    private BloomSystem? _bloom;
    private float _emissiveBoost = 4.0f;

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
        _cascadeDebug = args.Contains("--cascade-debug");
        _shadowDebug = args.Contains("--shadow-debug");
        _hbaoEnabled = !args.Contains("--no-hbao");
        _hbaoDebugView = args.Contains("--hbao-debug");
        _giEnabled = !args.Contains("--no-gi");
        if (int.TryParse(GetArgValue(args, "--gi-debug="), out int giDebug))
        {
            _giDebugView = giDebug;
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
        _fixedCameraPosition = ParseVector3(GetArgValue(args, "--pos="));
        _fixedCameraLook = ParseVector3(GetArgValue(args, "--look="));
        bool interior = args.Contains("--interior");

        // Load the Bistro scene; fall back to the procedural scene when absent.
        string bistroFile = interior ? "Bistro/BistroInterior.gltf" : "Bistro/BistroExterior.gltf";
        if (AssetSystem.TryLoad(bistroFile, out ModelScene? bistro, out string failedReason))
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

        string lightingShaderText = AssetSystem.Load<string>(BuiltInAssetsPath.Shader_PBRDeferredLighting);
        _pipeline = new PBRDeferredPipeline(
            RenderingSystem,
            BuiltInAssets.Shader_PBRGBuffer,
            BuiltInAssets.Shader_PBRShadowDepth,
            lightingShaderText,
            BuiltInAssetsPath.Shader_PBRDeferredLighting,
            shadowMapSize: 2048,
            width: (uint)MainView.Size.X,
            height: (uint)MainView.Size.Y,
            gbufferTangentShader: AssetSystem.Load<Shader>("Shaders/Pipelines/Rendering/PBR/GBufferTangent.hlsl"),
            shadowTangentShader: AssetSystem.Load<Shader>("Shaders/Pipelines/Rendering/PBR/ShadowDepthTangent.hlsl"),
            hbaoShader: AssetSystem.Load<Shader>("Shaders/Pipelines/Rendering/PBR/HBAO.hlsl"),
            hbaoBlurShader: AssetSystem.Load<Shader>("Shaders/Pipelines/Rendering/PBR/HBAOBlur.hlsl"));

        // Materials created by the pipeline factory bind this camera; the sandbox
        // drives its own camera (RenderingSystem.MainCamera is not set by sandboxes).
        _pipeline.SetCamera(_camera);

        if (_bistro != null)
        {
            // One game-owned G-buffer material per glTF material. Textures still
            // streaming in start as the fallbacks and are synced in RenderBistroFrame.
            _bistroMaterials = new GraphicsMaterial[_bistro.Materials.Count];
            for (int i = 0; i < _bistroMaterials.Length; i++)
            {
                ModelMaterial material = _bistro.Materials[i];
                _bistroMaterials[i] = _pipeline.CreateGBufferTangentMaterial(
                    material.AlbedoTexture, material.NormalTexture, material.MetallicRoughnessTexture,
                    material.EmissiveTexture, material.DoubleSided, $"bistro_{material.Name}");
            }
            _bistroStreaming = !_bistro.LoadingCompletion.IsCompleted;
        }
        else
        {
            _cubeMesh = CreateCubeMesh();
            _sphereMesh = CreateSphereMesh(48, 24);
            _groundMesh = CreateGroundMesh(40, 10);
            BuildScene();
            _proceduralMaterial = _pipeline.CreateGBufferMaterial(_checkerTexture, name: "checker");
        }

        // Record the static geometry once; the bundles are replayed every frame.
        for (int i = 0; i < _staticShadowBundles.Length; i++)
        {
            _staticShadowBundles[i] = RenderingSystem.CreateSubRenderContext($"pbr_shadow_static_{i}");
        }
        _staticGBufferBundle = RenderingSystem.CreateSubRenderContext("pbr_gbuffer_static");
        RecordStaticBundles();

        // Voxel global illumination: a 4-level clipmap whose coarsest level
        // covers roughly 4x the scene radius (128^3 voxels per level).
        if (_giEnabled)
        {
            float baseVoxelSize = MathF.Max(_sceneRadius * 4.0f / 1024.0f, 0.02f);
            _voxelGI = new VoxelGiRenderer(
                RenderingSystem,
                AssetSystem.Load<Shader>("Shaders/Pipelines/Rendering/PBR/VoxelClear.hlsl"),
                AssetSystem.Load<Shader>("Shaders/Pipelines/Rendering/PBR/Voxelize.hlsl"),
                AssetSystem.Load<Shader>("Shaders/Pipelines/Rendering/PBR/VoxelInject.hlsl"),
                AssetSystem.Load<Shader>("Shaders/Pipelines/Rendering/PBR/VoxelMip.hlsl"),
                AssetSystem.Load<Shader>("Shaders/Pipelines/Rendering/PBR/VoxelTrace.hlsl"),
                width: (uint)MainView.Size.X,
                height: (uint)MainView.Size.Y,
                resolution: 128,
                baseVoxelSize: baseVoxelSize);
            _giMaxTraceDistance = _sceneRadius;
            RegisterVoxelMeshes();
            _pipeline.SetGlobalIllumination(_voxelGI.IndirectTexture);
        }

        // Bloom blits into the HDR target in OnPostUpdate, before PluginHDR's
        // tonemapped present, so boosted emissive surfaces get a natural glow.
        _bloom = new BloomSystem(this, MainRenderTarget) { Threshold = 2.0f, Intensity = 1.0f };
        AddSystem(_bloom);

        MainView.OnResize += OnMainWindowResize;
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
        // Match the game's tonemapping operator.
        if (TryGetPlugin<PluginHDR>(out var pluginHDR))
        {
            pluginHDR.Tonemap = PluginHDR.TonemapType.Neutral;
        }
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        _time += delta;
        _timeOfDay = (_timeOfDay + delta * _timeSpeed) % 24.0f;

        UpdateCamera(delta);
        if (_animateObjects)
        {
            AnimateObjects(delta);
        }

        UpdateLightingData();

        RenderFrame();

        DrawImGuiPanel();

        DebugStats.Text(FrameRate);
        DebugStats.Text(_flyMode
            ? "RMB drag: look | WASD: move | E/Q: up/down | Shift: fast | wheel: speed | C: orbit | ESC: exit"
            : "LMB drag: orbit | wheel: zoom | C: fly | ESC: exit");

        _frameCount++;
    }

    protected override void OnEndFrame()
    {
        // Capture here, after OnSystemPostUpdate, so the shot includes bloom:
        // bloom blits into the HDR target after OnUpdate, and ViewRenderTarget
        // clears that target again at the start of the next frame. With
        // --wait-load the capture is held back until the Bistro scene's
        // asynchronously streaming textures have all arrived.
        if (_screenshotPath != null && _frameCount >= _screenshotFrames &&
            (!_waitForStreaming || _bistro == null || _bistro.LoadingCompletion.IsCompleted))
        {
            CaptureScreenshot(_screenshotPath);
            Stop();
        }
    }

    protected override void OnStop()
    {
    }

    protected void OnMainWindowResize(uint2 size)
    {
        _camera.AspectRatio = (float)size.X / size.Y;
        _pipeline.Resize(size.X, size.Y);
        if (_voxelGI != null)
        {
            // The indirect textures are recreated by the resize: rebind them.
            _voxelGI.Resize(size.X, size.Y);
            _pipeline.SetGlobalIllumination(_voxelGI.IndirectTexture);
        }
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

    private void UpdateLightingData()
    {
        // The sun orbits with the time of day (the CLI override fixes the
        // direction the light travels for debugging); its color and intensity
        // follow from the atmosphere transmittance and elevation.
        Vector3 directionToSun = _sunDirectionOverride.HasValue
            ? -_sunDirectionOverride.Value
            : ProceduralSkyUtility.GetDirectionToSun(_timeOfDay);
        Vector3 sunDirection = -directionToSun;
        Vector3 sunTint = ProceduralSkyUtility.GetSunColor(directionToSun);
        float sunScale = ProceduralSkyUtility.GetSunLightScale(directionToSun);

        Matrix4x4.Invert(_camera.Data.ViewProjectionMatrix, out Matrix4x4 invViewProjection);

        // Fit the shadow distance to the view: when the camera is far from the
        // scene (e.g. aerial views), extend past the configured base so visible
        // geometry never crosses the shadow range boundary — shadows would
        // otherwise fade/pop out at _shadowDistance while still on screen.
        float shadowDistance = Math.Max(_shadowDistance,
            Vector3.Distance(_camera.Transform.Position, _sceneCenter) + _sceneRadius);

        // Fit the shadow cascades to the camera frustum (PSSM splits).
        PBRDeferredPipeline.ComputeShadowCascades(
            invViewProjection,
            _camera.Transform.Position,
            _cameraNear,
            shadowDistance,
            sunDirection,
            _sceneRadius,
            0.6f,
            _pipeline.ShadowMapSize,
            _cascadeViewProjections,
            _cascadeSplits,
            _cascadeTexelSizes);

        _lightingData.InvViewProjection = invViewProjection;
        _lightingData.SunViewProjection0 = _cascadeViewProjections[0];
        _lightingData.SunViewProjection1 = _cascadeViewProjections[1];
        _lightingData.SunViewProjection2 = _cascadeViewProjections[2];
        _lightingData.SunViewProjection3 = _cascadeViewProjections[3];
        _lightingData.CameraPosition = new Vector4(_camera.Transform.Position, 1.0f);
        _lightingData.SunDirection = new Vector4(sunDirection, 0);
        _lightingData.SunColorAndIntensity = new Vector4(sunTint, _sunIntensity * sunScale);
        _lightingData.SkyParams = new Vector4(_rayleighScale, _mieScale, _miePhaseG, _skyExposure);
        _lightingData.SkyParams2 = new Vector4(_starIntensity, _nightFloor, _sunRadianceScale, 0.0f);
        _lightingData.Params = new Vector4(
            _shadowEnabled ? 1.0f : 0.0f,
            1.0f,
            _pipeline.ShadowMapSize,
            _sunDiscEnabled ? 1.0f : 0.0f);
        _lightingData.CascadeSplits = new Vector4(
            _cascadeSplits[0], _cascadeSplits[1], _cascadeSplits[2], _cascadeSplits[3]);
        _lightingData.CascadeTexelSizes = new Vector4(
            _cascadeTexelSizes[0], _cascadeTexelSizes[1], _cascadeTexelSizes[2], _cascadeTexelSizes[3]);
        _lightingData.Params2 = new Vector4(
            _cascadeDebug ? 1.0f : 0.0f,
            _shadowDebug ? 1.0f : 0.0f,
            0.0f,
            _hbaoDebugView ? 1.0f : 0.0f);
        _lightingData.Params3 = new Vector4(
            _giEnabled && _voxelGI != null ? 1.0f : 0.0f,
            _giDiffuseStrength,
            _giSpecularStrength,
            _giDebugView);

        // Voxel GI per-frame data (the clipmap and resolution fields are filled by the renderer).
        if (_voxelGI != null)
        {
            _voxelData.InvViewProjection = invViewProjection;
            _voxelData.SunViewProjection0 = _cascadeViewProjections[0];
            _voxelData.SunViewProjection1 = _cascadeViewProjections[1];
            _voxelData.SunViewProjection2 = _cascadeViewProjections[2];
            _voxelData.SunViewProjection3 = _cascadeViewProjections[3];
            _voxelData.CameraPosition = _lightingData.CameraPosition;
            _voxelData.SunDirection = _lightingData.SunDirection;
            _voxelData.SunColorAndIntensity = _lightingData.SunColorAndIntensity;
            _voxelData.SkyParams = _lightingData.SkyParams;
            _voxelData.SkyParams2 = _lightingData.SkyParams2;
            _voxelData.CascadeSplits = _lightingData.CascadeSplits;
            _voxelData.CascadeTexelSizes = _lightingData.CascadeTexelSizes;
            _voxelData.LightingParams = new Vector4(
                _shadowEnabled ? 1.0f : 0.0f,
                1.0f,
                _pipeline.ShadowMapSize,
                0.0f);
            _voxelData.GiParams = new Vector4(_emissiveBoost, _giMaxTraceDistance, 0.0f, 0.0f);
            _voxelData.GiParams2 = new Vector4(_giDebugView, 0.0f, 0.0f, 0.0f);
        }

        // HBAO+ per-frame data (the viewport components of Params2 are filled by the renderer).
        Quaternion cameraRotation = _camera.Transform.Rotation;
        _hbaoData.InvViewProjection = invViewProjection;
        _hbaoData.CameraPosition = _lightingData.CameraPosition;
        _hbaoData.CameraRight = new Vector4(Vector3.Transform(Vector3.UnitY, cameraRotation), 0.0f);
        _hbaoData.CameraUp = new Vector4(Vector3.Transform(Vector3.UnitZ, cameraRotation), 0.0f);
        _hbaoData.CameraForward = new Vector4(Vector3.Transform(Vector3.UnitX, cameraRotation), 0.0f);
        _hbaoData.Params = new Vector4(_hbaoRadius, _hbaoIntensity, _hbaoBias, 1.0f / (_hbaoRadius * _hbaoRadius));
        float projScale = 0.5f * MainView.Size.Y * _camera.Data.ProjectionMatrix.M22;
        _hbaoData.Params2 = new Vector4(projScale, 0.0f, 0.0f, HBAOMaxStepPixels);
        float hbaoStrength = _hbaoEnabled && _pipeline.HBAO != null ? _hbaoStrength : 0.0f;
        _hbaoData.Params3 = new Vector4(hbaoStrength, 0.0f, 0.0f, 0.0f);
    }

    private void RenderFrame()
    {
        if (_bistro != null)
        {
            RenderBistroFrame();
            return;
        }

        if (_staticBundlesDirty)
        {
            RecordStaticBundles();
        }

        // 1. Shadow map pass (only objects that cast shadows), one pass per cascade:
        // replay the recorded static casters, then draw the animated ones immediately.
        for (int cascade = 0; cascade < PBRDeferredPipeline.ShadowCascadeCount; cascade++)
        {
            _pipeline.BeginShadowPass(cascade, _cascadeViewProjections[cascade]);
            _pipeline.ExecuteShadowSubContext(_staticShadowBundles[cascade]);
            for (int i = 0; i < _objects.Count; i++)
            {
                SceneObject sceneObject = _objects[i];
                if (sceneObject.CastsShadow && !IsStatic(sceneObject))
                {
                    _pipeline.DrawShadow(_pipeline.ShadowContext, sceneObject.Mesh, sceneObject.WorldMatrix, cascade);
                }
            }
            _pipeline.EndShadowPass();
        }

        // 2. G-buffer pass: recorded static geometry first, then the animated objects.
        _pipeline.BeginGBufferPass();
        _pipeline.ExecuteGBufferSubContext(_staticGBufferBundle!);
        for (int i = 0; i < _objects.Count; i++)
        {
            SceneObject sceneObject = _objects[i];
            if (!IsStatic(sceneObject))
            {
                _pipeline.DrawGBuffer(_pipeline.GBufferContext, sceneObject.Mesh, _proceduralMaterial!,
                    sceneObject.WorldMatrix,
                    new Vector4(sceneObject.BaseColor, 1.0f),
                    new Vector4(sceneObject.Metallic, sceneObject.Roughness, sceneObject.AmbientOcclusion, 1.0f));
            }
        }
        _pipeline.EndGBufferPass();

        // 3. Screen-space ambient occlusion (HBAO+) from the G-buffer.
        RenderHBAOPass();

        // 4. Voxel global illumination (clipmap voxelization + cone tracing).
        RenderVoxelGIPass();

        // 5. Deferred lighting into the engine's HDR main target.
        _pipeline.RenderLighting(MainFrameBuffer, ref _lightingData);
    }

    private void RenderBistroFrame()
    {
        // Textures stream in asynchronously: refresh the materials and re-record the
        // bundles until everything arrived (equivalent to drawing every frame), then
        // the bundles stay frozen for the rest of the session.
        if (_bistroStreaming)
        {
            SyncBistroMaterials();
            RecordStaticBundles();
            _bistroStreaming = !_bistro!.LoadingCompletion.IsCompleted;
            if (!_bistroStreaming && _voxelGI != null)
            {
                // Texture instances may have been swapped while streaming:
                // re-register against the final textures.
                _voxelGI.ClearStaticMeshes();
                RegisterVoxelMeshes();
            }
        }
        else if (_staticBundlesDirty)
        {
            RecordStaticBundles();
        }

        // The Bistro scene is fully static: every pass is a pure bundle replay.
        for (int cascade = 0; cascade < PBRDeferredPipeline.ShadowCascadeCount; cascade++)
        {
            _pipeline.BeginShadowPass(cascade, _cascadeViewProjections[cascade]);
            _pipeline.ExecuteShadowSubContext(_staticShadowBundles[cascade]);
            _pipeline.EndShadowPass();
        }

        _pipeline.BeginGBufferPass();
        _pipeline.ExecuteGBufferSubContext(_staticGBufferBundle!);
        _pipeline.EndGBufferPass();

        RenderHBAOPass();

        RenderVoxelGIPass();

        _pipeline.RenderLighting(MainFrameBuffer, ref _lightingData);
    }

    /// <summary>An object is static (baked into the render bundles) when it neither spins nor floats.</summary>
    private static bool IsStatic(SceneObject sceneObject)
    {
        return sceneObject.SpinSpeed == 0 && sceneObject.FloatSpeed == 0;
    }

    /// <summary>Record all static geometry into the shadow and G-buffer render bundles.</summary>
    private void RecordStaticBundles()
    {
        for (int cascade = 0; cascade < PBRDeferredPipeline.ShadowCascadeCount; cascade++)
        {
            SubRenderContext bundle = _staticShadowBundles[cascade];
            bundle.Begin(_pipeline.ShadowLayout);
            RecordStaticShadowCasters(bundle, cascade);
            bundle.End();
        }

        _staticGBufferBundle!.Begin(_pipeline.GBufferLayout);
        RecordStaticGBufferGeometry(_staticGBufferBundle);
        _staticGBufferBundle.End();

        _staticBundlesDirty = false;
    }

    private void RecordStaticShadowCasters(IRenderContext target, int cascade)
    {
        if (_bistro != null)
        {
            IReadOnlyList<ModelDrawItem> drawItems = _bistro.DrawItems;
            for (int i = 0; i < drawItems.Count; i++)
            {
                ModelDrawItem item = drawItems[i];
                _pipeline.DrawShadowTangent(target, item.Mesh, item.World, cascade);
            }
            return;
        }

        for (int i = 0; i < _objects.Count; i++)
        {
            SceneObject sceneObject = _objects[i];
            if (sceneObject.CastsShadow && IsStatic(sceneObject))
            {
                _pipeline.DrawShadow(target, sceneObject.Mesh, sceneObject.WorldMatrix, cascade);
            }
        }
    }

    private void RecordStaticGBufferGeometry(IRenderContext target)
    {
        if (_bistro != null)
        {
            IReadOnlyList<ModelDrawItem> drawItems = _bistro.DrawItems;
            IReadOnlyList<ModelMaterial> materials = _bistro.Materials;
            for (int i = 0; i < drawItems.Count; i++)
            {
                ModelDrawItem item = drawItems[i];
                ModelMaterial material = materials[item.MaterialIndex];
                _pipeline.DrawGBuffer(target, item.Mesh, _bistroMaterials![item.MaterialIndex], item.World,
                    material.BaseColorFactor,
                    new Vector4(material.MetallicFactor, material.RoughnessFactor, 1.0f, 0.0f),
                    material.EmissiveFactor * _emissiveBoost,
                    GetAlphaCutoff(material));
            }
            return;
        }

        for (int i = 0; i < _objects.Count; i++)
        {
            SceneObject sceneObject = _objects[i];
            if (IsStatic(sceneObject))
            {
                _pipeline.DrawGBuffer(target, sceneObject.Mesh, _proceduralMaterial!, sceneObject.WorldMatrix,
                    new Vector4(sceneObject.BaseColor, 1.0f),
                    new Vector4(sceneObject.Metallic, sceneObject.Roughness, sceneObject.AmbientOcclusion, 1.0f));
            }
        }
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
                _voxelGI.RegisterStaticMesh(item.Mesh, (uint)VertexPositionNormalTextureTangent.SizeInBytes,
                    material.AlbedoTexture, material.EmissiveTexture,
                    material.BaseColorFactor, material.EmissiveFactor, GetAlphaCutoff(material), item.World);
            }
            return;
        }

        int dynamicSphereHandle = -1;
        int dynamicCubeHandle = -1;
        for (int i = 0; i < _objects.Count; i++)
        {
            SceneObject sceneObject = _objects[i];
            if (IsStatic(sceneObject))
            {
                sceneObject.VoxelStaticHandle = _voxelGI.RegisterStaticMesh(sceneObject.Mesh,
                    (uint)VertexPositionNormalTexture.SizeInBytes, _checkerTexture, null,
                    new Vector4(sceneObject.BaseColor, 1.0f), Vector3.Zero, 0.0f, sceneObject.WorldMatrix);
            }
            else if (sceneObject.Mesh == _sphereMesh)
            {
                if (dynamicSphereHandle < 0)
                {
                    dynamicSphereHandle = _voxelGI.RegisterDynamicMesh(sceneObject.Mesh,
                        (uint)VertexPositionNormalTexture.SizeInBytes, _checkerTexture, null);
                }
                sceneObject.VoxelDynamicHandle = dynamicSphereHandle;
            }
            else
            {
                if (dynamicCubeHandle < 0)
                {
                    dynamicCubeHandle = _voxelGI.RegisterDynamicMesh(sceneObject.Mesh,
                        (uint)VertexPositionNormalTexture.SizeInBytes, _checkerTexture, null);
                }
                sceneObject.VoxelDynamicHandle = dynamicCubeHandle;
            }
        }
    }

    /// <summary>Run the voxel GI passes between the G-buffer/HBAO and lighting passes.</summary>
    private void RenderVoxelGIPass()
    {
        if (!_giEnabled || _voxelGI == null)
        {
            return;
        }

        for (int i = 0; i < _objects.Count; i++)
        {
            SceneObject sceneObject = _objects[i];
            if (sceneObject.VoxelDynamicHandle >= 0)
            {
                _voxelGI.SubmitDynamicInstance(sceneObject.VoxelDynamicHandle, sceneObject.WorldMatrix,
                    new Vector4(sceneObject.BaseColor, 1.0f), Vector3.Zero, 0.0f);
            }
        }
        _voxelGI.Render(_pipeline.GBuffer, _pipeline.ShadowMap, ref _voxelData, _camera.Transform.Position);
    }

    /// <summary>Copy the current (possibly still streaming) Bistro textures into the materials.</summary>
    private void SyncBistroMaterials()
    {
        IReadOnlyList<ModelMaterial> materials = _bistro!.Materials;
        for (int i = 0; i < materials.Count; i++)
        {
            ModelMaterial material = materials[i];
            _pipeline.SetGBufferTangentMaterialTextures(_bistroMaterials![i],
                material.AlbedoTexture, material.NormalTexture,
                material.MetallicRoughnessTexture, material.EmissiveTexture);
        }
    }

    /// <summary>Run the HBAO+ compute passes between the G-buffer and lighting passes.</summary>
    private void RenderHBAOPass()
    {
        if (_hbaoEnabled && _pipeline.HBAO != null)
        {
            _pipeline.HBAO.Render(_pipeline.GBuffer, ref _hbaoData);
        }
    }

    /// <summary>
    /// Read back the HDR main target, tonemap and save it as a PNG screenshot.
    /// </summary>
    private unsafe void CaptureScreenshot(string path)
    {
        Texture2D color = MainRenderTarget.RenderTexture.ColorTextures[0];
        int width = (int)color.Width;
        int height = (int)color.Height;
        int pixelCount = width * height;

        // The HDR main target is RGBA16Float.
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
    }

    private void DrawImGuiPanel()
    {
        ImGui.Begin("PBR Deferred");

        if (ImGui.CollapsingHeader("Sun Light"))
        {
            ImGui.SliderFloat("Intensity", ref _sunIntensity, 0.0f, 30.0f);
            ImGui.Checkbox("Shadows", ref _shadowEnabled);
            ImGui.SliderFloat("Shadow Distance", ref _shadowDistance, _sceneRadius * 0.5f, _sceneRadius * 8.0f);
            ImGui.Checkbox("Cascade Debug", ref _cascadeDebug);
            ImGui.Checkbox("Shadow Debug", ref _shadowDebug);
            ImGui.Checkbox("Sun disc", ref _sunDiscEnabled);
        }

        if (ImGui.CollapsingHeader("Sky & Time"))
        {
            ImGui.SliderFloat("Time of Day", ref _timeOfDay, 0.0f, 24.0f);
            ImGui.SliderFloat("Time Speed", ref _timeSpeed, 0.0f, 4.0f);
            ImGui.SliderFloat("Sky Exposure", ref _skyExposure, 0.1f, 4.0f);
            ImGui.SliderFloat("Rayleigh Scale", ref _rayleighScale, 0.1f, 4.0f);
            ImGui.SliderFloat("Mie Scale (Haze)", ref _mieScale, 0.0f, 8.0f);
            ImGui.SliderFloat("Mie Phase G", ref _miePhaseG, 0.0f, 0.95f);
            ImGui.SliderFloat("Sun Radiance", ref _sunRadianceScale, 1.0f, 60.0f);
            ImGui.SliderFloat("Star Intensity", ref _starIntensity, 0.0f, 4.0f);
        }

        if (_pipeline.HBAO != null && ImGui.CollapsingHeader("Ambient Occlusion (HBAO+)"))
        {
            ImGui.Checkbox("AO Enabled", ref _hbaoEnabled);
            ImGui.SliderFloat("AO Radius", ref _hbaoRadius, 0.1f, MathF.Max(4.0f, _sceneRadius * 0.05f));
            ImGui.SliderFloat("AO Strength", ref _hbaoStrength, 0.0f, 1.0f);
            ImGui.SliderFloat("AO Power", ref _hbaoIntensity, 0.5f, 3.0f);
            ImGui.SliderFloat("AO Bias", ref _hbaoBias, 0.0f, 0.2f);
            ImGui.Checkbox("AO Debug View", ref _hbaoDebugView);
        }

        if (_voxelGI != null && ImGui.CollapsingHeader("Global Illumination (Voxel)"))
        {
            ImGui.Checkbox("GI Enabled", ref _giEnabled);
            ImGui.SliderFloat("GI Diffuse Strength", ref _giDiffuseStrength, 0.0f, 4.0f);
            ImGui.SliderFloat("GI Specular Strength", ref _giSpecularStrength, 0.0f, 4.0f);
            ImGui.SliderFloat("GI Max Trace Distance", ref _giMaxTraceDistance, 1.0f, MathF.Max(4.0f, _sceneRadius * 2.0f));
            string[] giDebugModes = ["Off", "Indirect Diffuse", "Indirect Specular"];
            ImGui.Combo("GI Debug", ref _giDebugView, giDebugModes, giDebugModes.Length);
        }

        if (_bloom != null && ImGui.CollapsingHeader("Emissive & Bloom"))
        {
            // The boosted emissive factor is baked into the static G-buffer bundle.
            if (ImGui.SliderFloat("Emissive Boost", ref _emissiveBoost, 0.0f, 20.0f))
            {
                _staticBundlesDirty = true;
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

        if (TryGetSystem<FXAASystem>(out FXAASystem? fxaaSystem))
        {
            if (ImGui.CollapsingHeader("FXAA"))
            {
                bool fxaaEnabled = fxaaSystem.IsEnabled;
                if (ImGui.Checkbox("FXAA Enabled", ref fxaaEnabled))
                {
                    fxaaSystem.IsEnabled = fxaaEnabled;
                }
                float fxaaThreshold = fxaaSystem.Threshold;
                if (ImGui.SliderFloat("Edge Threshold", ref fxaaThreshold, 0.063f, 0.333f))
                {
                    fxaaSystem.Threshold = fxaaThreshold;
                }
            }
        }

        if (_bistro != null)
        {
            if (ImGui.CollapsingHeader("Bistro Scene"))
            {
                ImGui.Text($"{_bistro.DrawItems.Count} draw items, {_bistro.Materials.Count} materials");
                ImGui.Text($"bounds min {_bistro.BoundsMin}");
                ImGui.Text($"bounds max {_bistro.BoundsMax}");
            }
        }
        else
        {
            if (ImGui.CollapsingHeader("Material"))
            {
                string[] objectNames = _objects.Select(o => o.Mesh.Name).ToArray();
                ImGui.Combo("Object", ref _selectedObject, objectNames, objectNames.Length);
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
                    _staticBundlesDirty = true;
                    if (sceneObject.VoxelStaticHandle >= 0)
                    {
                        _voxelGI?.UpdateStaticMesh(sceneObject.VoxelStaticHandle,
                            new Vector4(sceneObject.BaseColor, 1.0f), Vector3.Zero, 0.0f, sceneObject.WorldMatrix);
                    }
                }
            }

            ImGui.Separator();
            ImGui.Checkbox("Animate Objects", ref _animateObjects);
        }

        ImGui.End();
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

    private PrimitiveMesh CreateCubeMesh()
    {
        // 24 vertices, one quad per face, outward normals, CCW winding.
        Span<VertexPositionNormalTexture> vertices = stackalloc VertexPositionNormalTexture[24];
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
                vertices[vertexIndex] = new VertexPositionNormalTexture(corners[i], normal, uvs[i]);
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
        VertexPositionNormalTexture[] vertices = new VertexPositionNormalTexture[vertexCount];
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
                vertices[vertexIndex++] = new VertexPositionNormalTexture(position, position, new Vector2((float)u / segmentsU, (float)v / segmentsV));
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
        VertexPositionNormalTexture[] vertices = new VertexPositionNormalTexture[vertexCount];
        ushort[] indices = new ushort[indexCount];

        const float uvTiles = 4.0f;
        int vertexIndex = 0;
        for (int j = 0; j <= segments; j++)
        {
            for (int i = 0; i <= segments; i++)
            {
                float x = (float)i / segments * size - size * 0.5f;
                float y = (float)j / segments * size - size * 0.5f;
                vertices[vertexIndex++] = new VertexPositionNormalTexture(
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
