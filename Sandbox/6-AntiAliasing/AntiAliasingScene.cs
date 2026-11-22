using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.IO;
using Alco.Rendering;

/// <summary>
/// Procedural 3D showcase scene encapsulating crisp silhouettes, shallow-angle beams,
/// low-contrast overlapping monoliths, curved surfaces, and surface patterns for anti-aliasing evaluation.
/// </summary>
public sealed class AntiAliasingScene
{
    private const string Scene3DShaderSource = """
        #language slang 2025
        module SandboxFXAAScene3D;

        import Core;

        public struct SceneVertex
        {
            public float4 PositionUvU;
            public float4 NormalUvV;
        };

        public struct SceneInstance
        {
            public float4x4 WorldMatrix;
            public float4 BaseColor;
            public float4 SecondaryColor;
            public float4 PatternParams; // x: type (0 solid, 1 checker, 2 stripes, 3 grid), y: scale, z: specStrength, w: shininess
        };

        [GPURoot]
        public struct Scene3DRoot
        {
            public float4x4 ViewProjection;
            public GPUBufferView Vertices;
            public GPUBufferView Instances;
            public float4 CameraPos;
            public float4 SunDirIntensity; // xyz: direction toward light, w: intensity
            public float4 SunColor;        // rgb: color, a: ambient intensity
            public uint VertexOffset;
            public uint InstanceOffset;
            public uint ShowPatternOverlay;
            public uint Pad0;
        };

        public struct V2F
        {
            public float4 Position : SV_Position;
            public float3 WorldPos : TEXCOORD0;
            public float3 WorldNormal : TEXCOORD1;
            public float2 Uv : TEXCOORD2;
            public float4 BaseColor : COLOR0;
            public float4 SecondaryColor : COLOR1;
            public float4 PatternParams : TEXCOORD3;
        };

        [shader("vertex")]
        public V2F MainVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
        {
            Scene3DRoot root = gpu_load_root<Scene3DRoot>();
            SceneVertex v = gpu_load<SceneVertex>(root.Vertices, root.VertexOffset + vertexId);
            SceneInstance inst = gpu_load<SceneInstance>(root.Instances, root.InstanceOffset + instanceId);

            float4 worldPos = mul(float4(v.PositionUvU.xyz, 1.0f), inst.WorldMatrix);

            // Transform normals by the adjugate (cofactor matrix) so non-uniformly scaled struts stay accurate.
            float3 r0 = inst.WorldMatrix[0].xyz;
            float3 r1 = inst.WorldMatrix[1].xyz;
            float3 r2 = inst.WorldMatrix[2].xyz;
            float3 n = v.NormalUvV.xyz;
            float3 worldNrm = n.x * cross(r1, r2) + n.y * cross(r2, r0) + n.z * cross(r0, r1);

            V2F output;
            output.Position = mul(worldPos, root.ViewProjection);
            output.WorldPos = worldPos.xyz;
            output.WorldNormal = worldNrm;
            output.Uv = float2(v.PositionUvU.w, v.NormalUvV.w);
            output.BaseColor = inst.BaseColor;
            output.SecondaryColor = inst.SecondaryColor;
            output.PatternParams = inst.PatternParams;
            return output;
        }

        [shader("pixel")]
        public float4 MainPS(V2F input) : SV_Target0
        {
            Scene3DRoot root = gpu_load_root<Scene3DRoot>();

            float3 n = normalize(input.WorldNormal);
            float3 v = normalize(root.CameraPos.xyz - input.WorldPos);
            float3 l = normalize(root.SunDirIntensity.xyz);
            float3 h = normalize(l + v);

            float3 albedo = input.BaseColor.rgb;
            if (root.ShowPatternOverlay != 0u)
            {
                int patternType = int(input.PatternParams.x + 0.5f);
                float scale = input.PatternParams.y;
                if (patternType == 1)
                {
                    // Deliberately hard-stepped checker so surface aliasing differences stay clear.
                    float2 cell = floor(input.Uv * scale);
                    float parity = fmod(abs(cell.x + cell.y), 2.0f);
                    albedo = lerp(input.BaseColor.rgb, input.SecondaryColor.rgb, parity);
                }
                else if (patternType == 2)
                {
                    float stripeCoord = (input.Uv.x + input.Uv.y * 0.45f) * scale;
                    float band = step(0.5f, frac(stripeCoord));
                    albedo = lerp(input.BaseColor.rgb, input.SecondaryColor.rgb, band);
                }
                else if (patternType == 3)
                {
                    float2 g = frac(input.Uv * scale);
                    float lineMask = max(step(0.90f, g.x), step(0.90f, g.y));
                    albedo = lerp(input.BaseColor.rgb, input.SecondaryColor.rgb, lineMask);
                }
            }

            float ndl = max(dot(n, l), 0.0f);
            float hemi = 0.5f + 0.5f * n.z;
            float3 skyBounce = float3(0.42f, 0.58f, 0.85f);
            float3 groundBounce = float3(0.18f, 0.16f, 0.14f);
            float3 ambient = lerp(groundBounce, skyBounce, hemi) * root.SunColor.a;

            float specPower = max(input.PatternParams.w, 4.0f);
            float spec = pow(max(dot(n, h), 0.0f), specPower) * input.PatternParams.z;
            float fresnel = pow(1.0f - max(dot(n, v), 0.0f), 4.0f) * 0.14f;

            float3 diffuse = albedo * (ambient + root.SunColor.rgb * (ndl * root.SunDirIntensity.w));
            float3 linearColor = diffuse + root.SunColor.rgb * spec + skyBounce * fresnel;

            // Simple shoulder compression + sRGB gamma encode for Rgba8Unorm FXAA input.
            float3 mapped = linearColor / (1.0f + linearColor * 0.25f);
            float3 srgb = pow(max(mapped, 0.0f), 1.0f / 2.2f);
            return float4(srgb, 1.0f);
        }
        """;

    private const float RawMouseCountScale = 5f;
    private static readonly Vector4 SkyClearColor = new(0.42f, 0.58f, 0.80f, 1.0f);

    private static readonly CameraPreset OverviewPreset = new(
        "Overview",
        new Vector3(1.2f, -1.35f, 1.50f),
        Yaw: 0.22f,
        Pitch: 0.23f,
        Distance: 13.8f);

    private static readonly CameraPreset StrutRackPreset = new(
        "Shallow Struts",
        new Vector3(-0.6f, -3.9f, 2.05f),
        Yaw: 0.05f,
        Pitch: 0.08f,
        Distance: 5.4f);

    private static readonly CameraPreset LatticePreset = new(
        "Lattice Cube",
        new Vector3(0.0f, 0.0f, 2.2f),
        Yaw: 0.34f,
        Pitch: 0.20f,
        Distance: 4.8f);

    private static readonly CameraPreset LowContrastPreset = new(
        "Low-Contrast Depth",
        new Vector3(5.6f, -2.8f, 2.1f),
        Yaw: -0.22f,
        Pitch: 0.12f,
        Distance: 6.2f);

    private static readonly CameraPreset StripeBoardPreset = new(
        "Stripe Panel",
        new Vector3(4.1f, 3.0f, 1.85f),
        Yaw: 0.36f,
        Pitch: 0.16f,
        Distance: 5.2f);

    private readonly RenderChain _chain;
    private readonly CameraPerspective _camera;
    private readonly Material _sceneMaterial;
    private readonly Alco.Graphics.Buffer _vertexBuffer;
    private readonly Alco.Graphics.Buffer _instanceBuffer;
    private readonly MeshBatch[] _batches;
    private readonly SceneInstance[] _instances;
    private readonly AnimatedInstanceSpec[] _animatedSpecs;
    private readonly SceneRenderNode _sceneNode;

    private Vector3 _lookAt;
    private float _yaw;
    private float _pitch;
    private float _distance;
    private bool _isOrbiting;
    private float _animTime;

    /// <summary>Gets or sets whether showcase shapes automatically rotate.</summary>
    public bool AutoRotate { get; set; } = true;

    /// <summary>Gets or sets the continuous rotation speed multiplier.</summary>
    public float RotateSpeed { get; set; } = 0.35f;

    /// <summary>Gets or sets whether hard-stepped surface checker and stripe patterns are rendered.</summary>
    public bool ShowSurfacePatterns { get; set; } = true;

    /// <summary>Gets the RenderGraph node that renders the 3D scene color and depth buffers.</summary>
    public RenderGraphNode SceneNode => _sceneNode;

    /// <summary>Gets the <see cref="Format.D32Float"/> depth target written by <see cref="SceneNode"/>.</summary>
    public RGTexture DepthTarget => _sceneNode.DepthTarget;

    /// <summary>Creates the procedural 3D test geometry, GPU buffers, camera, and RenderGraph pass.</summary>
    /// <param name="rendering">Rendering system owning device resources.</param>
    /// <param name="shaders">Shader system used to compile the scene shader.</param>
    /// <param name="chain">Render chain reset by the scene pass each frame.</param>
    public AntiAliasingScene(RenderingSystem rendering, ShaderSystem shaders, RenderChain chain)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentNullException.ThrowIfNull(chain);

        _chain = chain;
        _camera = new CameraPerspective
        {
            FieldOfView = 0.85f,
            Near = 0.25f,
            Far = 140f,
            ReverseInfiniteDepth = false,
        };
        ApplyCameraPreset(OverviewPreset);

        Shader sceneShader = shaders.GetShaderFromSource(
            "SandboxFXAAScene3D", "SandboxFXAAScene3D.slang", Scene3DShaderSource, ShaderKind.Graphics);
        _sceneMaterial = sceneShader.CreateMaterial("sandbox_fxaa_scene3d");
        _sceneMaterial.Rasterization = RasterizationState.CullNone;
        _sceneMaterial.Blend = BlendState.Opaque;

        (SceneVertex[] vertices, MeshBatch[] batches, SceneInstance[] instances, AnimatedInstanceSpec[] animSpecs) =
            BuildSceneAssets();

        _batches = batches;
        _instances = instances;
        _animatedSpecs = animSpecs;

        _vertexBuffer = rendering.CreateBuffer((ulong)(vertices.Length * Unsafe.SizeOf<SceneVertex>()));
        _vertexBuffer.SetData<SceneVertex>(vertices);

        _instanceBuffer = rendering.CreateBuffer((ulong)(instances.Length * Unsafe.SizeOf<SceneInstance>()));
        _instanceBuffer.SetData<SceneInstance>(instances);

        _sceneNode = new SceneRenderNode(this);
    }

    /// <summary>Updates camera projection aspect ratio to match the current viewport extent.</summary>
    /// <param name="extent">Viewport pixel dimensions.</param>
    public void SetViewportExtent(Extent2D extent)
    {
        if (extent.Y > 0)
        {
            _camera.AspectRatio = (float)extent.X / extent.Y;
        }
    }

    /// <summary>Advances animations and handles orbit/zoom camera controls.</summary>
    /// <param name="delta">Frame delta time in seconds.</param>
    /// <param name="input">Engine input state.</param>
    /// <param name="view">Main window view for focus checks.</param>
    /// <param name="captureMouse">True when ImGui is capturing mouse input.</param>
    /// <param name="captureKeyboard">True when ImGui is capturing keyboard input.</param>
    public void Update(float delta, Input input, View view, bool captureMouse, bool captureKeyboard)
    {
        if (!captureKeyboard && input.IsKeyDown(KeyCode.R))
        {
            ApplyCameraPreset(OverviewPreset);
        }

        if (AutoRotate)
        {
            _animTime += delta * RotateSpeed;
            UpdateAnimatedInstances();
        }

        UpdateCamera(input, view, captureMouse);
    }

    /// <summary>Draws ImGui widgets controlling scene animation, surface patterns, and camera presets.</summary>
    public void DrawImGuiControls()
    {
        ImGui.Text("Scene & Camera");

        bool autoRotate = AutoRotate;
        if (ImGui.Checkbox("Auto Rotate", ref autoRotate))
        {
            AutoRotate = autoRotate;
        }

        if (AutoRotate)
        {
            float speed = RotateSpeed;
            if (ImGui.SliderFloat("Rotate Speed", ref speed, 0.05f, 1.5f))
            {
                RotateSpeed = speed;
            }
        }

        bool showPatterns = ShowSurfacePatterns;
        if (ImGui.Checkbox("Surface Checker / Stripes", ref showPatterns))
        {
            ShowSurfacePatterns = showPatterns;
        }

        if (ImGui.Button(OverviewPreset.Name))
        {
            ApplyCameraPreset(OverviewPreset);
        }
        ImGui.SameLine();
        if (ImGui.Button(StrutRackPreset.Name))
        {
            ApplyCameraPreset(StrutRackPreset);
        }
        ImGui.SameLine();
        if (ImGui.Button(LatticePreset.Name))
        {
            ApplyCameraPreset(LatticePreset);
        }

        if (ImGui.Button(LowContrastPreset.Name))
        {
            ApplyCameraPreset(LowContrastPreset);
        }
        ImGui.SameLine();
        if (ImGui.Button(StripeBoardPreset.Name))
        {
            ApplyCameraPreset(StripeBoardPreset);
        }

        ImGui.Text("Drag LMB/MMB: Orbit | Wheel: Zoom | R: Reset");
    }

    /// <summary>Releases GPU vertex and instance buffers created for the scene.</summary>
    public void Destroy()
    {
        _instanceBuffer.Destroy();
        _vertexBuffer.Destroy();
    }

    private void UpdateAnimatedInstances()
    {
        for (int i = 0; i < _animatedSpecs.Length; i++)
        {
            ref readonly AnimatedInstanceSpec spec = ref _animatedSpecs[i];
            float angle = spec.BaseYaw + _animTime * spec.SpeedMultiplier;
            Quaternion q = Quaternion.CreateFromYawPitchRoll(angle, spec.BasePitch, spec.BaseRoll);
            _instances[spec.InstanceIndex].WorldMatrix =
                Matrix4x4.CreateScale(spec.Scale) *
                Matrix4x4.CreateFromQuaternion(q) *
                Matrix4x4.CreateTranslation(spec.Position);
        }

        _instanceBuffer.SetData<SceneInstance>(_instances);
    }

    private void UpdateCamera(Input input, View view, bool captureMouse)
    {
        bool mouseDown =
            input.IsMouseDown(Mouse.Left) ||
            input.IsMouseDown(Mouse.Middle) ||
            input.IsMouseDown(Mouse.Right);
        bool mouseHeld =
            input.IsMousePressing(Mouse.Left) ||
            input.IsMousePressing(Mouse.Middle) ||
            input.IsMousePressing(Mouse.Right);

        if (!captureMouse && mouseDown && view.IsFocused)
        {
            _isOrbiting = true;
        }
        else if (!mouseHeld || !view.IsFocused)
        {
            _isOrbiting = false;
        }

        input.IsMouseRelativeMode = _isOrbiting;
        if (_isOrbiting)
        {
            Vector2 delta = input.MouseDelta / RawMouseCountScale;
            _yaw += delta.X * 0.007f;
            _pitch = Math.Clamp(_pitch + delta.Y * 0.007f, -1.35f, 1.35f);
        }

        if (!captureMouse && input.IsMouseScrolling(out Vector2 wheel) && wheel.Y != 0f)
        {
            _distance = Math.Clamp(_distance * MathF.Pow(0.90f, wheel.Y), 2.2f, 36.0f);
        }

        Vector3 offset = new(
            -MathF.Cos(_pitch) * MathF.Cos(_yaw),
            -MathF.Cos(_pitch) * MathF.Sin(_yaw),
            MathF.Sin(_pitch));
        Vector3 position = _lookAt + offset * _distance;
        _camera.Transform = new Transform3D(position, LookRotation(_lookAt - position, Vector3.UnitZ));
    }

    private void ApplyCameraPreset(in CameraPreset preset)
    {
        _lookAt = preset.LookAt;
        _yaw = preset.Yaw;
        _pitch = preset.Pitch;
        _distance = preset.Distance;
    }

    private static (SceneVertex[] Vertices, MeshBatch[] Batches, SceneInstance[] Instances, AnimatedInstanceSpec[] AnimSpecs)
        BuildSceneAssets()
    {
        List<SceneVertex> vertices = new(8192);
        List<MeshBatch> batches = new(8);
        List<SceneInstance> instances = new(32);
        List<AnimatedInstanceSpec> animSpecs = new(4);

        // 1. Box mesh (floor, pedestals, thin strut rack, low-contrast pillars, stripe panel, sloped wedge).
        uint boxOffset = (uint)vertices.Count;
        AddBox(vertices, Vector3.Zero, new Vector3(0.5f, 0.5f, 0.5f));
        uint boxCount = (uint)vertices.Count - boxOffset;

        int boxInstanceStart = instances.Count;

        // Ground plane with sharp checker pattern (contrasts Luma vs DepthOnly FXAA).
        instances.Add(MakeStaticInstance(
            new Vector3(2.0f, 0.0f, -0.2f),
            new Vector3(42f, 42f, 0.4f),
            Quaternion.Identity,
            new Vector4(0.24f, 0.27f, 0.32f, 1f),
            new Vector4(0.14f, 0.16f, 0.20f, 1f),
            patternType: 1f,
            patternScale: 26f,
            specStrength: 0.08f,
            shininess: 16f));

        // Central stepped pedestal.
        instances.Add(MakeStaticInstance(
            new Vector3(0.0f, 0.0f, 0.14f),
            new Vector3(2.8f, 2.8f, 0.28f),
            Quaternion.Identity,
            new Vector4(0.76f, 0.78f, 0.82f, 1f),
            new Vector4(0.54f, 0.57f, 0.63f, 1f),
            patternType: 3f,
            patternScale: 6f,
            specStrength: 0.2f,
            shininess: 32f));
        instances.Add(MakeStaticInstance(
            new Vector3(0.0f, 0.0f, 0.36f),
            new Vector3(1.9f, 1.9f, 0.16f),
            Quaternion.Identity,
            new Vector4(0.64f, 0.67f, 0.72f, 1f),
            Vector4.Zero,
            patternType: 0f,
            patternScale: 1f,
            specStrength: 0.25f,
            shininess: 32f));

        // Front-left shallow-angle strut rack (posts + 7 thin tilted dark bars against bright sky).
        instances.Add(MakeStaticInstance(
            new Vector3(-0.6f, -3.9f, 0.12f),
            new Vector3(0.9f, 4.4f, 0.24f),
            Quaternion.Identity,
            new Vector4(0.62f, 0.65f, 0.70f, 1f),
            Vector4.Zero,
            0f, 1f, 0.2f, 24f));
        instances.Add(MakeStaticInstance(
            new Vector3(-0.6f, -5.85f, 2.25f),
            new Vector3(0.18f, 0.18f, 4.3f),
            Quaternion.Identity,
            new Vector4(0.16f, 0.18f, 0.21f, 1f),
            Vector4.Zero,
            0f, 1f, 0.35f, 48f));
        instances.Add(MakeStaticInstance(
            new Vector3(-0.6f, -1.95f, 2.25f),
            new Vector3(0.18f, 0.18f, 4.3f),
            Quaternion.Identity,
            new Vector4(0.16f, 0.18f, 0.21f, 1f),
            Vector4.Zero,
            0f, 1f, 0.35f, 48f));

        ReadOnlySpan<float> strutAnglesDeg = [2f, 5f, 9f, 14f, 21f, 30f, 40f];
        for (int i = 0; i < strutAnglesDeg.Length; i++)
        {
            float z = 0.72f + i * 0.42f;
            float rad = strutAnglesDeg[i] * (MathF.PI / 180f);
            Quaternion tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitX, rad);
            instances.Add(MakeStaticInstance(
                new Vector3(-0.6f, -3.9f, z),
                new Vector3(0.10f, 3.2f, 0.09f),
                tilt,
                new Vector4(0.08f, 0.09f, 0.11f, 1f),
                Vector4.Zero,
                0f, 1f, 0.45f, 64f));
        }

        // Back-left overlapping low-contrast monoliths (highlights DepthAssisted / DepthOnly).
        instances.Add(MakeStaticInstance(
            new Vector3(4.2f, -2.5f, 1.9f),
            new Vector3(0.55f, 1.85f, 3.8f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.18f),
            new Vector4(0.11f, 0.12f, 0.14f, 1f),
            Vector4.Zero,
            0f, 1f, 0.08f, 12f));
        instances.Add(MakeStaticInstance(
            new Vector3(7.0f, -3.2f, 2.2f),
            new Vector3(0.55f, 2.15f, 4.4f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.14f),
            new Vector4(0.135f, 0.145f, 0.165f, 1f),
            Vector4.Zero,
            0f, 1f, 0.08f, 12f));

        // Back-right slanted diagonal stripe panel (shader/texture aliasing vs silhouette AA).
        instances.Add(MakeStaticInstance(
            new Vector3(4.1f, 3.0f, 1.85f),
            new Vector3(0.14f, 3.4f, 2.2f),
            Quaternion.CreateFromYawPitchRoll(-0.28f, 0.12f, 0.22f),
            new Vector4(0.92f, 0.94f, 0.96f, 1f),
            new Vector4(0.11f, 0.14f, 0.20f, 1f),
            patternType: 2f,
            patternScale: 12f,
            specStrength: 0.2f,
            shininess: 32f));

        // Mid-center smoothly sloped wedge beam (tests continuous slope depth threshold).
        instances.Add(MakeStaticInstance(
            new Vector3(5.4f, 0.3f, 0.58f),
            new Vector3(3.2f, 1.25f, 0.22f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.34f),
            new Vector4(0.50f, 0.56f, 0.64f, 1f),
            new Vector4(0.36f, 0.42f, 0.50f, 1f),
            patternType: 3f,
            patternScale: 8f,
            specStrength: 0.30f,
            shininess: 36f));

        batches.Add(new MeshBatch(boxOffset, boxCount, (uint)boxInstanceStart, (uint)(instances.Count - boxInstanceStart)));

        // 2. Wireframe Lattice Cube (12 thin 3D beams).
        uint latticeOffset = (uint)vertices.Count;
        AddLatticeCube(vertices, halfSize: 1.0f, strutHalfThickness: 0.055f);
        uint latticeCount = (uint)vertices.Count - latticeOffset;

        int latticeInstanceIdx = instances.Count;
        AnimatedInstanceSpec latticeSpec = new(
            latticeInstanceIdx,
            new Vector3(0.0f, 0.0f, 2.25f),
            new Vector3(1.12f, 1.12f, 1.12f),
            BaseYaw: 0.48f,
            BasePitch: 0.32f,
            BaseRoll: 0.22f,
            SpeedMultiplier: 1.0f);
        instances.Add(MakeStaticInstance(
            latticeSpec.Position,
            latticeSpec.Scale,
            Quaternion.CreateFromYawPitchRoll(latticeSpec.BaseYaw, latticeSpec.BasePitch, latticeSpec.BaseRoll),
            new Vector4(0.94f, 0.90f, 0.80f, 1f),
            Vector4.Zero,
            0f, 1f, 0.55f, 64f));
        animSpecs.Add(latticeSpec);
        batches.Add(new MeshBatch(latticeOffset, latticeCount, (uint)latticeInstanceIdx, 1u));

        // 3. Smooth Torus (concave + convex rim curvature).
        uint torusOffset = (uint)vertices.Count;
        AddTorus(vertices, majorRadius: 1.0f, minorRadius: 0.27f, majorSegments: 48, minorSegments: 24);
        uint torusCount = (uint)vertices.Count - torusOffset;

        int torusInstanceIdx = instances.Count;
        AnimatedInstanceSpec torusSpec = new(
            torusInstanceIdx,
            new Vector3(2.4f, -2.5f, 1.85f),
            new Vector3(1.05f, 1.05f, 1.05f),
            BaseYaw: 0.35f,
            BasePitch: 0.85f,
            BaseRoll: 0.20f,
            SpeedMultiplier: -0.75f);
        instances.Add(MakeStaticInstance(
            torusSpec.Position,
            torusSpec.Scale,
            Quaternion.CreateFromYawPitchRoll(torusSpec.BaseYaw, torusSpec.BasePitch, torusSpec.BaseRoll),
            new Vector4(0.94f, 0.48f, 0.16f, 1f),
            Vector4.Zero,
            0f, 1f, 0.65f, 72f));
        animSpecs.Add(torusSpec);
        batches.Add(new MeshBatch(torusOffset, torusCount, (uint)torusInstanceIdx, 1u));

        // 4. Smooth UV Sphere (outer silhouette + inner lattice orb).
        uint sphereOffset = (uint)vertices.Count;
        AddSphere(vertices, radius: 1.0f, slices: 36, stacks: 18);
        uint sphereCount = (uint)vertices.Count - sphereOffset;

        int sphereStart = instances.Count;
        instances.Add(MakeStaticInstance(
            new Vector3(-0.3f, 3.6f, 1.15f),
            new Vector3(1.05f, 1.05f, 1.05f),
            Quaternion.Identity,
            new Vector4(0.20f, 0.56f, 0.88f, 1f),
            Vector4.Zero,
            0f, 1f, 0.75f, 96f));
        instances.Add(MakeStaticInstance(
            new Vector3(0.0f, 0.0f, 2.25f),
            new Vector3(0.46f, 0.46f, 0.46f),
            Quaternion.Identity,
            new Vector4(0.86f, 0.24f, 0.28f, 1f),
            Vector4.Zero,
            0f, 1f, 0.80f, 96f));
        batches.Add(new MeshBatch(sphereOffset, sphereCount, (uint)sphereStart, 2u));

        // 5. Crisp Faceted Pyramid & Slender Spire (sharp apex triangles).
        uint pyramidOffset = (uint)vertices.Count;
        AddCone(vertices, radius: 1.0f, height: 2.2f, segments: 4, smoothNormals: false);
        uint pyramidCount = (uint)vertices.Count - pyramidOffset;

        int pyramidStart = instances.Count;
        instances.Add(MakeStaticInstance(
            new Vector3(1.1f, 2.1f, 1.1f),
            new Vector3(0.95f, 0.95f, 1.0f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.42f),
            new Vector4(0.24f, 0.72f, 0.54f, 1f),
            Vector4.Zero,
            0f, 1f, 0.40f, 48f));
        instances.Add(MakeStaticInstance(
            new Vector3(2.8f, 4.2f, 1.65f),
            new Vector3(0.28f, 0.28f, 1.55f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.14f),
            new Vector4(0.85f, 0.76f, 0.24f, 1f),
            Vector4.Zero,
            0f, 1f, 0.50f, 64f));
        batches.Add(new MeshBatch(pyramidOffset, pyramidCount, (uint)pyramidStart, 2u));

        return (vertices.ToArray(), batches.ToArray(), instances.ToArray(), animSpecs.ToArray());
    }

    private static SceneInstance MakeStaticInstance(
        Vector3 position,
        Vector3 scale,
        Quaternion rotation,
        Vector4 baseColor,
        Vector4 secondaryColor,
        float patternType,
        float patternScale,
        float specStrength,
        float shininess)
    {
        return new SceneInstance
        {
            WorldMatrix =
                Matrix4x4.CreateScale(scale) *
                Matrix4x4.CreateFromQuaternion(rotation) *
                Matrix4x4.CreateTranslation(position),
            BaseColor = baseColor,
            SecondaryColor = secondaryColor,
            PatternParams = new Vector4(patternType, patternScale, specStrength, shininess),
        };
    }

    private static void AddLatticeCube(List<SceneVertex> vertices, float halfSize, float strutHalfThickness)
    {
        float len = halfSize + strutHalfThickness;
        for (int s0 = -1; s0 <= 1; s0 += 2)
        {
            for (int s1 = -1; s1 <= 1; s1 += 2)
            {
                float c0 = s0 * halfSize;
                float c1 = s1 * halfSize;
                AddBox(vertices, new Vector3(0f, c0, c1), new Vector3(len, strutHalfThickness, strutHalfThickness));
                AddBox(vertices, new Vector3(c0, 0f, c1), new Vector3(strutHalfThickness, len, strutHalfThickness));
                AddBox(vertices, new Vector3(c0, c1, 0f), new Vector3(strutHalfThickness, strutHalfThickness, len));
            }
        }
    }

    private static void AddBox(List<SceneVertex> vertices, Vector3 center, Vector3 halfExtents)
    {
        float x = halfExtents.X;
        float y = halfExtents.Y;
        float z = halfExtents.Z;

        AddBoxFace(vertices, center, new(x, -y, -z), new(x, y, -z), new(x, y, z), new(x, -y, z), Vector3.UnitX);
        AddBoxFace(vertices, center, new(-x, y, -z), new(-x, -y, -z), new(-x, -y, z), new(-x, y, z), -Vector3.UnitX);
        AddBoxFace(vertices, center, new(x, y, -z), new(-x, y, -z), new(-x, y, z), new(x, y, z), Vector3.UnitY);
        AddBoxFace(vertices, center, new(-x, -y, -z), new(x, -y, -z), new(x, -y, z), new(-x, -y, z), -Vector3.UnitY);
        AddBoxFace(vertices, center, new(-x, -y, z), new(x, -y, z), new(x, y, z), new(-x, y, z), Vector3.UnitZ);
        AddBoxFace(vertices, center, new(-x, y, -z), new(x, y, -z), new(x, -y, -z), new(-x, -y, -z), -Vector3.UnitZ);
    }

    private static void AddBoxFace(
        List<SceneVertex> vertices,
        Vector3 center,
        Vector3 p0,
        Vector3 p1,
        Vector3 p2,
        Vector3 p3,
        Vector3 normal)
    {
        AddQuad(
            vertices,
            center + p0,
            center + p1,
            center + p2,
            center + p3,
            normal, normal, normal, normal,
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f));
    }

    private static void AddSphere(List<SceneVertex> vertices, float radius, int slices, int stacks)
    {
        for (int i = 0; i < stacks; i++)
        {
            float v0 = (float)i / stacks;
            float v1 = (float)(i + 1) / stacks;
            float phi0 = MathF.PI * v0;
            float phi1 = MathF.PI * v1;

            for (int j = 0; j < slices; j++)
            {
                float u0 = (float)j / slices;
                float u1 = (float)(j + 1) / slices;
                float theta0 = 2f * MathF.PI * u0;
                float theta1 = 2f * MathF.PI * u1;

                Vector3 n00 = SphericalNormal(phi0, theta0);
                Vector3 n10 = SphericalNormal(phi0, theta1);
                Vector3 n11 = SphericalNormal(phi1, theta1);
                Vector3 n01 = SphericalNormal(phi1, theta0);

                AddQuad(
                    vertices,
                    n00 * radius, n10 * radius, n11 * radius, n01 * radius,
                    n00, n10, n11, n01,
                    new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
            }
        }
    }

    private static Vector3 SphericalNormal(float phi, float theta)
    {
        float sinPhi = MathF.Sin(phi);
        return new Vector3(sinPhi * MathF.Cos(theta), sinPhi * MathF.Sin(theta), MathF.Cos(phi));
    }

    private static void AddTorus(
        List<SceneVertex> vertices,
        float majorRadius,
        float minorRadius,
        int majorSegments,
        int minorSegments)
    {
        for (int i = 0; i < majorSegments; i++)
        {
            float u0 = (float)i / majorSegments;
            float u1 = (float)(i + 1) / majorSegments;
            float phi0 = 2f * MathF.PI * u0;
            float phi1 = 2f * MathF.PI * u1;

            for (int j = 0; j < minorSegments; j++)
            {
                float v0 = (float)j / minorSegments;
                float v1 = (float)(j + 1) / minorSegments;
                float theta0 = 2f * MathF.PI * v0;
                float theta1 = 2f * MathF.PI * v1;

                (Vector3 p00, Vector3 n00) = TorusPoint(majorRadius, minorRadius, phi0, theta0);
                (Vector3 p10, Vector3 n10) = TorusPoint(majorRadius, minorRadius, phi1, theta0);
                (Vector3 p11, Vector3 n11) = TorusPoint(majorRadius, minorRadius, phi1, theta1);
                (Vector3 p01, Vector3 n01) = TorusPoint(majorRadius, minorRadius, phi0, theta1);

                AddQuad(
                    vertices,
                    p00, p10, p11, p01,
                    n00, n10, n11, n01,
                    new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
            }
        }
    }

    private static (Vector3 Pos, Vector3 Normal) TorusPoint(float majorRadius, float minorRadius, float phi, float theta)
    {
        Vector3 radial = new(MathF.Cos(phi), MathF.Sin(phi), 0f);
        Vector3 normal = radial * MathF.Cos(theta) + Vector3.UnitZ * MathF.Sin(theta);
        return (radial * majorRadius + normal * minorRadius, normal);
    }

    private static void AddCone(List<SceneVertex> vertices, float radius, float height, int segments, bool smoothNormals)
    {
        float halfH = height * 0.5f;
        Vector3 apex = new(0f, 0f, halfH);
        Vector3 baseCenter = new(0f, 0f, -halfH);

        for (int i = 0; i < segments; i++)
        {
            float a0 = 2f * MathF.PI * i / segments;
            float a1 = 2f * MathF.PI * (i + 1) / segments;
            Vector3 p0 = new(radius * MathF.Cos(a0), radius * MathF.Sin(a0), -halfH);
            Vector3 p1 = new(radius * MathF.Cos(a1), radius * MathF.Sin(a1), -halfH);

            Vector3 flatNormal = Vector3.Normalize(Vector3.Cross(p1 - apex, p0 - apex));
            if (Vector3.Dot(flatNormal, p0 + p1) < 0f)
            {
                flatNormal = -flatNormal;
            }

            if (smoothNormals)
            {
                float midA = 0.5f * (a0 + a1);
                Vector3 nApex = Vector3.Normalize(new Vector3(MathF.Cos(midA) * height, MathF.Sin(midA) * height, radius));
                Vector3 n0 = Vector3.Normalize(new Vector3(MathF.Cos(a0) * height, MathF.Sin(a0) * height, radius));
                Vector3 n1 = Vector3.Normalize(new Vector3(MathF.Cos(a1) * height, MathF.Sin(a1) * height, radius));
                AddTriangle(vertices, p0, p1, apex, n0, n1, nApex, new(0f, 0f), new(1f, 0f), new(0.5f, 1f));
            }
            else
            {
                AddTriangle(vertices, p0, p1, apex, flatNormal, flatNormal, flatNormal, new(0f, 0f), new(1f, 0f), new(0.5f, 1f));
            }

            AddTriangle(vertices, baseCenter, p1, p0, -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ, new(0.5f, 0.5f), new(1f, 0f), new(0f, 0f));
        }
    }

    private static void AddQuad(
        List<SceneVertex> vertices,
        Vector3 p0,
        Vector3 p1,
        Vector3 p2,
        Vector3 p3,
        Vector3 n0,
        Vector3 n1,
        Vector3 n2,
        Vector3 n3,
        Vector2 uv0,
        Vector2 uv1,
        Vector2 uv2,
        Vector2 uv3)
    {
        AddTriangle(vertices, p0, p1, p2, n0, n1, n2, uv0, uv1, uv2);
        AddTriangle(vertices, p0, p2, p3, n0, n2, n3, uv0, uv2, uv3);
    }

    private static void AddTriangle(
        List<SceneVertex> vertices,
        Vector3 p0,
        Vector3 p1,
        Vector3 p2,
        Vector3 n0,
        Vector3 n1,
        Vector3 n2,
        Vector2 uv0,
        Vector2 uv1,
        Vector2 uv2)
    {
        vertices.Add(new SceneVertex { PositionUvU = new Vector4(p0, uv0.X), NormalUvV = new Vector4(n0, uv0.Y) });
        vertices.Add(new SceneVertex { PositionUvU = new Vector4(p1, uv1.X), NormalUvV = new Vector4(n1, uv1.Y) });
        vertices.Add(new SceneVertex { PositionUvU = new Vector4(p2, uv2.X), NormalUvV = new Vector4(n2, uv2.Y) });
    }

    private static Quaternion LookRotation(Vector3 forward, Vector3 worldUp)
    {
        forward = Vector3.Normalize(forward);
        Vector3 up = Vector3.Normalize(worldUp - forward * Vector3.Dot(forward, worldUp));
        Vector3 right = Vector3.Cross(up, forward);
        Matrix4x4 m = new(
            forward.X, forward.Y, forward.Z, 0f,
            right.X, right.Y, right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            0f, 0f, 0f, 1f);
        return Quaternion.CreateFromRotationMatrix(m);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SceneVertex
    {
        public Vector4 PositionUvU;
        public Vector4 NormalUvV;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SceneInstance
    {
        public Matrix4x4 WorldMatrix;
        public Vector4 BaseColor;
        public Vector4 SecondaryColor;
        public Vector4 PatternParams;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Scene3DRoot
    {
        public Matrix4x4 ViewProjection;
        public GPUBufferView Vertices;
        public GPUBufferView Instances;
        public Vector4 CameraPos;
        public Vector4 SunDirIntensity;
        public Vector4 SunColor;
        public uint VertexOffset;
        public uint InstanceOffset;
        public uint ShowPatternOverlay;
        public uint Pad0;
    }

    private readonly record struct MeshBatch(uint VertexOffset, uint VertexCount, uint InstanceOffset, uint InstanceCount);

    private readonly record struct AnimatedInstanceSpec(
        int InstanceIndex,
        Vector3 Position,
        Vector3 Scale,
        float BaseYaw,
        float BasePitch,
        float BaseRoll,
        float SpeedMultiplier);

    private readonly record struct CameraPreset(string Name, Vector3 LookAt, float Yaw, float Pitch, float Distance);

    private sealed class SceneRenderNode(AntiAliasingScene scene) : RenderGraphNode("Scene3D")
    {
        private RGTexture _colorTarget;
        public RGTexture DepthTarget { get; private set; }

        protected override void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass)
        {
            _colorTarget = builder.CreateTexture(new RGTextureDesc
            {
                Name = "Scene3DColor",
                Format = Format.Rgba8Unorm,
            });
            DepthTarget = builder.CreateTexture(new RGTextureDesc
            {
                Name = "Scene3DDepth",
                Format = Format.D32Float,
            });

            pass.Write(_colorTarget, RGUsage.ColorAttachmentWrite);
            pass.Write(DepthTarget, RGUsage.DepthStencil);
            scene._chain.Reset(_colorTarget);
        }

        protected override void ExecutePass(in RenderGraphContext context)
        {
            RenderContext rc = context.RenderContext;
            Extent2D vp = context.Viewport;

            ColorAttachment colorAttachment = new()
            {
                RenderView = context.GetRenderView(_colorTarget),
                Load = LoadOp.Clear,
                Store = StoreOp.Store,
                Clear = SkyClearColor,
            };
            DepthAttachment depthAttachment = new()
            {
                RenderView = context.GetRenderView(DepthTarget),
                Load = LoadOp.Clear,
                Store = StoreOp.Store,
                Clear = 1.0f,
            };
            RenderingDesc passDesc = new()
            {
                Colors = MemoryMarshal.CreateReadOnlySpan(ref colorAttachment, 1),
                Depth = depthAttachment,
            };

            using RenderContext.RenderScope render = rc.BeginRenderPass(passDesc);
            render.SetViewport(new Viewport { Width = vp.X, Height = vp.Y });
            render.SetScissor(new Scissor { Width = vp.X, Height = vp.Y });

            Vector3 sunDir = Vector3.Normalize(new Vector3(-0.55f, 0.35f, 0.80f));
            Scene3DRoot rootBase = new()
            {
                ViewProjection = scene._camera.ViewProjectionMatrix,
                Vertices = scene._vertexBuffer.View,
                Instances = scene._instanceBuffer.View,
                CameraPos = new Vector4(scene._camera.Transform.Position, 1.0f),
                SunDirIntensity = new Vector4(sunDir, 1.35f),
                SunColor = new Vector4(1.0f, 0.96f, 0.90f, 0.55f),
                ShowPatternOverlay = scene.ShowSurfacePatterns ? 1u : 0u,
            };

            for (int i = 0; i < scene._batches.Length; i++)
            {
                ref readonly MeshBatch batch = ref scene._batches[i];
                Scene3DRoot root = rootBase with
                {
                    VertexOffset = batch.VertexOffset,
                    InstanceOffset = batch.InstanceOffset,
                };
                GPURange rootRange = rc.AllocateRoot(in root);
                render.Draw(
                    scene._sceneMaterial,
                    rootRange,
                    DepthStencilState.Write,
                    vertexCount: batch.VertexCount,
                    instanceCount: batch.InstanceCount);
            }
        }
    }
}
